using System;
using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Events;

namespace RidiculousGaming.GarageBandIdle
{
    // The session's phases (design doc 12.9). Launch and backgrounding are
    // NoChapter; AwaitingIdleClaim admits only the claim and switch commands
    // and never ticks - the chapter is live only after the claim settles;
    // Live admits everything.
    public enum SessionPhase
    {
        NoChapter,
        AwaitingIdleClaim,
        Live
    }

    // One line of the idle offer: the currency, its home, and the amount - all
    // references, born from the same (currency, home) enumeration GetRate sums,
    // because nothing about an offer ever crosses a save boundary.
    public class IdleOfferLine
    {
        public CurrencyDefinition currency;
        public ScopeState home;
        public BigNumber amount;
    }

    // The transient idle offer (design doc 12.9): computed once over the
    // explicit window [stamp, windowEndUtc], held by the session, marked by the
    // ad callback, paid by settlement, dead with the process. THE STAMP IS THE
    // PENDING CLAIM - a kill with the dialog up saves nothing, and the next
    // entry recomputes from the stamp it never advanced.
    public class IdleOffer
    {
        public DateTime windowEndUtc;
        public List<IdleOfferLine> lines = new();
        public bool doubled;
    }

    // The transient execution context (design doc 12.9): plain C#, never
    // serialized, holding only orchestration - the foreground chapter, the
    // phase, the outstanding offer, and the reentrancy guard. Durable facts
    // live in the tree. The session draws the command boundary and owns the
    // transaction pipeline; the wrapped systems stay public and unchanged, and
    // tests keep calling them directly.
    public class GameSession
    {
        public readonly RootScopeState Root;
        private readonly GameConfig config;

        public ChapterScopeState ForegroundChapter { get; private set; }
        public SessionPhase Phase { get; private set; } = SessionPhase.NoChapter;

        // The outstanding idle offer - non-null exactly while the phase is
        // AwaitingIdleClaim. Step 9's dialog renders it; step 10's ad callback
        // doubles AND settles it in one transaction (12.9), so a doubled offer
        // is never left exposed to an exit's undoubled settle.
        public IdleOffer CurrentOffer { get; private set; }

        // The last tick's realized movement, held out for interpolation (12.11)
        // and never serialized. The tick is its only writer: a command owns its
        // mutation and the flush before it, and the flush IS a tick, so the
        // report a command's refresh renders was measured against the state the
        // command found. Null only before the first tick.
        public TickReport LastTick { get; private set; }

        // The 12.11 hook, one per completed transaction and none on a refusal;
        // step 9's widgets subscribe. Unconditional where the sweep is not,
        // which is what repaints the claim dialog when a callback marks it
        // doubled without sweeping.
        public event Action Refreshed;

        // The reentrancy guard: a command issued from inside a transaction (a
        // trigger action, a refresh handler) is a code bug and throws. The
        // callback queue 12.9 describes is this flag's future consumer.
        private bool commandInProgress;

        // The pacing state (12.9): the frame's clock sample and the live time
        // banked since the last tick. The FRAME is the only reader of the clock;
        // a player action settles the bank and measures nothing. The sample is
        // set at every chapter switch, which is where a window starts, and
        // boot switches before the first frame.
        private DateTime lastSampleUtc;
        private double pendingSeconds;

        public GameSession(RootScopeState root, GameConfig config)
        {
            GameConfig.Require(config);     // fail-loud at construction (requirement 7)
            Root = root;
            this.config = config;
        }

        // ---- the session commands ----

        // Legal in every phase, one transaction (12.9). Switching to the
        // CURRENT chapter (or to null while already NoChapter) is a no-op
        // success that runs no pipeline: the stamp is old during a live
        // session, and recomputing here would mint an offer covering time the
        // player spent playing. A live outgoing chapter stamps at now; one
        // with an offer up settles it undoubled on a chapter-to-chapter switch
        // (an exit path, section 9) and DROPS it on backgrounding - the stamp
        // stays, so the unpaid window recomputes on return and backgrounding
        // and an app kill behave identically. Entering Live directly makes
        // this transaction's closing sweep the deferred "first live sweep
        // after switch-in" (12.8); entering AwaitingIdleClaim sweeps nothing,
        // because even a root trigger can legally reset a descendant chapter,
        // re-stamping the unpaid window away before it is presented.
        public void SwitchChapter(ChapterScopeState chapter, DateTime nowUtc)
        {
            GuardReentrancy();
            if (chapter == ForegroundChapter)
                return;
            // The outgoing chapter's banked foreground time settles into the
            // outgoing subtree, never the incoming one - and the incoming
            // window starts here, with nothing banked.
            FlushPending(nowUtc);
            lastSampleUtc = nowUtc;
            pendingSeconds = 0;
            commandInProgress = true;
            try
            {
                var outgoing = ForegroundChapter;
                if (outgoing != null)
                {
                    if (CurrentOffer != null)
                    {
                        if (chapter != null)
                            SettleOffer(outgoing, honorDoubled: false);
                        else
                            CurrentOffer = null;
                    }
                    else
                    {
                        outgoing.StampActive(nowUtc);
                    }
                }

                ForegroundChapter = chapter;
                if (chapter == null)
                {
                    Phase = SessionPhase.NoChapter;
                }
                else
                {
                    Root.currentChapterId = chapter.ScopeId;   // the durable root fact boot returns to
                    Phase = EnterChapter(chapter, nowUtc);
                }
                CloseTransaction(nowUtc);
            }
            finally
            {
                commandInProgress = false;
            }
        }

        // AwaitingIdleClaim only. Pure settlement - the claim never computes
        // anything: the stored lines deposit at their held homes, x2 when the
        // ad callback marked the offer doubled, and the stamp advances in the
        // same transaction, which is the whole exactly-once mechanism (12.9) -
        // the save is the tree, so a kill keeps both writes or neither. This
        // transaction's sweep - root plus the now-live foreground - is the
        // deferred one: a threshold crossed while away, by the switch's own
        // settle-out, or by this deposit fires here, root triggers included.
        public bool ClaimIdle(DateTime nowUtc)
        {
            GuardReentrancy();
            if (Phase != SessionPhase.AwaitingIdleClaim)
                return false;
            commandInProgress = true;
            try
            {
                SettleOffer(ForegroundChapter, honorDoubled: true);
                Phase = SessionPhase.Live;
                CloseTransaction(nowUtc);
                return true;
            }
            finally
            {
                commandInProgress = false;
            }
        }

        // The incoming chapter's phase (12.9's point 4). The offer is computed
        // once over the paid window [stamp, stamp + min(elapsed, cap)] at
        // current state, so Records earned while away boost it, under the
        // idle-accumulation circumstance - the authored root base joins the
        // gather and live-only modifiers excuse themselves. The window is
        // segmented at the buff expiries inside it exactly as the tick segments,
        // each segment paying its own length at the rate and speed live in it:
        // the cap bounds REAL seconds and speed multiplies what they pay.
        // Skipped entirely when the away time is under the minimum, a blocking
        // record holds, every line computes zero, or the chapter has never been
        // left.
        private SessionPhase EnterChapter(ChapterScopeState chapter, DateTime nowUtc)
        {
            // A chapter never LEFT owes no idle (12.3). The stamp means "when I
            // last stopped playing this one" - written by the exit above,
            // re-written by a reset - so its default is the ABSENCE of a moment
            // rather than year one, and a window measured from it would bill the
            // player for two millennia. Answered here because entry is the only
            // place the question arises: a stamp written at construction cannot
            // cover a chapter authored after the save was written, nor a first
            // switch into a dormant one hours into a session.
            if (chapter.lastActiveUtc == default)
            {
                chapter.StampActive(nowUtc);
                return SessionPhase.Live;
            }

            // The 12.10 clamp: a backwards clock claims nothing.
            var elapsed = Math.Max(0, (nowUtc - chapter.lastActiveUtc).TotalSeconds);
            if (elapsed < config.minimumAwaySeconds || BlockedByEvent(chapter))
                return SessionPhase.Live;

            var paidSeconds = Math.Min(elapsed, config.idleCapSeconds);
            var windowStartUtc = chapter.lastActiveUtc;
            var windowEndUtc = windowStartUtc.AddSeconds(paidSeconds);
            var pairs = Producer.RatePairs(chapter);
            var amounts = new BigNumber[pairs.Count];
            for (var i = 0; i < amounts.Length; i++)
                amounts[i] = BigNumber.Zero;

            // The tick's own segmentation, over the tick's own code: each
            // segment is stamped at its start, so a record that expires inside
            // the window is live before its edge and dead after it. Nothing is
            // removed from any timedBuffs list here - a record removed before
            // the walk would delete the boundary and pay the whole window at 1x
            // - and the next tick's end collects the expired one.
            foreach (var (start, end) in TickSystem.Segments(Root, chapter, windowStartUtc, windowEndUtc))
            {
                var segCtx = new GameContext(chapter, start, idleAccumulation: true);
                var effSeconds = (end - start).TotalSeconds * TickSystem.GameSpeed(segCtx, chapter, config);
                for (var i = 0; i < pairs.Count; i++)
                    amounts[i] += Producer.GetRate(segCtx, pairs[i].currency) * effSeconds;
            }

            // The offer's window ends at NOW even though the payment covers the
            // capped window: the stamp advances to the window PRESENTED, and
            // time past the cap is a lost window either way (12.9's settled-
            // window rule), so nothing about settlement changes.
            var offer = new IdleOffer { windowEndUtc = nowUtc };
            for (var i = 0; i < pairs.Count; i++)
            {
                if (amounts[i] == BigNumber.Zero)
                    continue;
                offer.lines.Add(new IdleOfferLine
                    { currency = pairs[i].currency, home = pairs[i].home, amount = amounts[i] });
            }
            if (offer.lines.Count == 0)
                return SessionPhase.Live;

            CurrentOffer = offer;
            return SessionPhase.AwaitingIdleClaim;
        }

        // Settlement pays the stored lines through their held references -
        // nothing resolves a name here - and advances the stamp to the window
        // actually paid, never the settlement moment. Time past the window's
        // end is foreground presence, never idle: the next live exit stamps
        // over it. Then the offer dies.
        private void SettleOffer(ChapterScopeState chapter, bool honorDoubled)
        {
            var offer = CurrentOffer;
            foreach (var line in offer.lines)
            {
                var amount = honorDoubled && offer.doubled ? line.amount * 2 : line.amount;
                // Resolved: the line's currency was judged active by the gather
                // that built the offer, under the claim's own circumstance. A
                // re-ask here would run under a live context instead and could
                // refuse a line the offer already promised, mid-settlement.
                new GameContext(line.home, offer.windowEndUtc).DepositResolved(line.currency.Id, amount);
            }
            chapter.StampActive(offer.windowEndUtc);
            CurrentOffer = null;
        }

        // Skipped entirely while any record in the chapter's subtree is for an
        // event that blocks idle (6.1) - the idle path asks the event, never
        // inspects a timer. Read through the declaration list, like every
        // record read, so a stray record id blocks nothing.
        private static bool BlockedByEvent(ScopeState node)
        {
            if (node is InteriorScopeState host && host.activeEvent != null)
            {
                foreach (var evt in ((InteriorDefinition)node.Definition).events)
                    if (evt != null && evt.Id == host.activeEvent.eventId && evt.BlocksIdle)
                        return true;
            }
            foreach (var child in node.Children)
                if (BlockedByEvent(child))
                    return true;
            return false;
        }

        // ---- the cadence ----

        // The driver's per-frame call (12.9): it holds no pacing state and only
        // passes the clock's real time. The sample advances every frame and
        // only the bank is conditional, so time under a dialog never pools up
        // and dumps into the first live tick. One tick carries the WHOLE
        // accumulation ending at the sample, which is what keeps the simulated
        // windows contiguous and their timestamps exact - TickSystem already
        // segments internally, so a hitch is one correct call.
        public void Accumulate(DateTime nowUtc)
        {
            GuardReentrancy();
            var elapsed = (nowUtc - lastSampleUtc).TotalSeconds;
            lastSampleUtc = nowUtc;
            // A backwards wall clock clears the bank with the discontinuity:
            // Tick(dt, now) promises [now - dt, now] is one contiguous interval,
            // and a dt spanning a rollback would judge absolute stamps against
            // wall positions that never held. Zero elapsed is just a frame that
            // read the same time.
            if (Phase != SessionPhase.Live || elapsed < 0)
            {
                pendingSeconds = 0;
                return;
            }
            pendingSeconds += elapsed;
            if (pendingSeconds < config.tickIntervalSeconds)
                return;
            var dt = pendingSeconds;
            pendingSeconds = 0;
            Tick(dt, nowUtc);
        }

        // A player action settles the bank before it mutates: whatever the
        // frames have banked since the last tick is simulated as a preceding
        // tick transaction, so the mutation runs against settled state - a
        // generator bought mid-window earns nothing for the time before it
        // existed. The action reads no clock; the frame already did. Flush,
        // never clear: FireProducer is a command, and clearing per Jam tap
        // would starve the rate production the rates exist for.
        private void FlushPending(DateTime nowUtc)
        {
            if (Phase != SessionPhase.Live || pendingSeconds <= 0)
                return;
            var dt = pendingSeconds;
            pendingSeconds = 0;
            Tick(dt, nowUtc);
        }

        // Live only; TickSystem.Tick inside the same pipeline. Nonpositive dt
        // no-ops like a refusal - nothing mutated, nothing to sweep or repaint.
        // A tick that runs SETTLES the sample at its end with nothing pending:
        // the world is simulated through nowUtc, so a command issued at that
        // moment flushes nothing, rather than re-simulating the window a caller
        // ticked by hand.
        public void Tick(double realSeconds, DateTime nowUtc)
        {
            GuardReentrancy();
            if (Phase != SessionPhase.Live || realSeconds <= 0)
                return;
            commandInProgress = true;
            try
            {
                lastSampleUtc = nowUtc;
                pendingSeconds = 0;
                LastTick = TickSystem.Tick(Root, ForegroundChapter, config, realSeconds, nowUtc);
                CloseTransaction(nowUtc);
            }
            finally
            {
                commandInProgress = false;
            }
        }

        // ---- the command surface ----
        // One wrapper per entry point, taking the same GameContext the wrapped
        // system takes plus nothing new. Root-owned commands take the
        // exception path 12.9 names and arrive with their step.

        public bool TryRung(GameContext ctx) =>
            RunCommand(ctx, c => c.Scope.Definition is InteriorDefinition interior
                && interior.rung != null && interior.rung.TryExecute(c));

        public bool TryBuy(GameContext ctx, GeneratorDefinition generator) =>
            RunCommand(ctx, c => Purchasing.TryBuy(c, generator));

        public bool TryBuy(GameContext ctx, UpgradeDefinition upgrade) =>
            RunCommand(ctx, c => Purchasing.TryBuy(c, upgrade));

        // Firing has no gate of its own - past the session's guards it always
        // happens, so the pipeline always runs.
        public bool FireProducer(GameContext ctx, ProducerDefinition producer) =>
            RunCommand(ctx, c => { Producer.FireProducer(c, producer); return true; });

        public bool SetActiveBars(GameContext ctx, BarGroupDefinition group, IReadOnlyList<BarDefinition> bars) =>
            RunCommand(ctx, c => BarSystem.SetActiveBars(c, group, bars));

        public bool TryStartEvent(GameContext ctx, EventDefinition evt) =>
            RunCommand(ctx, c => EventSystem.TryStart(c, evt));

        public bool TryDismissEvent(GameContext ctx, EventDefinition evt) =>
            RunCommand(ctx, c => EventSystem.TryDismiss(c, evt));

        // Extends the timer of the modifier whose record lives at `scope` - root
        // and encore for the ad callback, as AddModifier takes a target and a
        // modifier. Legal in EVERY phase (12.9: an authenticated callback is
        // always phase-eligible). The dialog's refusal of ordinary commands
        // exists so a sweep cannot reset away an unpaid window, and a root
        // record write sweeps nothing there, since the sweep is conditional on
        // the resulting phase; refusing would discard a watched ad whenever the
        // app resumed into the dialog before the callback landed.
        public void ExtendBuff(ScopeState scope, ModifierDefinition modifier, double seconds, DateTime nowUtc)
        {
            // A grant only ever moves an expiry LATER, so a nonpositive or
            // non-finite duration is a caller bug and not a shorter buff.
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
                throw new InvalidOperationException(
                    $"ExtendBuff for '{modifier.Id}': {seconds} is not a finite positive number of seconds.");
            RunRootCommand(nowUtc, () =>
            {
                var record = FindBuff(scope, modifier.Id);
                if (record == null)
                {
                    record = new TimedBuff { buffId = modifier.Id, expiresAtUtc = nowUtc.AddSeconds(seconds) };
                    scope.timedBuffs.Add(record);
                }
                else
                {
                    // From the later of the expiry and now: a record already
                    // dead is no credit toward the next grant.
                    record.expiresAtUtc =
                        (record.expiresAtUtc > nowUtc ? record.expiresAtUtc : nowUtc).AddSeconds(seconds);
                }
                // The cap bounds REMAINING time, so it is measured from now and
                // clamps the grant rather than refusing it.
                var ceiling = nowUtc.AddSeconds(config.encoreCapSeconds);
                if (record.expiresAtUtc > ceiling)
                    record.expiresAtUtc = ceiling;
            });
        }

        // One record per modifier id per scope, so the first match IS the
        // record; null means the scope holds none for that modifier yet.
        private static TimedBuff FindBuff(ScopeState scope, string modifierId)
        {
            foreach (var buff in scope.timedBuffs)
                if (buff != null && buff.buffId == modifierId)
                    return buff;
            return null;
        }

        // ---- the pipeline ----

        // Guards - mutation - conditional sweep - commit - one refresh
        // (12.9/12.11). A refused command runs no pipeline: every refusal
        // precedes any mutation, so there is nothing to sweep or repaint, and
        // commit is a seam rather than machinery - the point after the sweep
        // where the transaction's state is what refresh reads.
        private bool RunCommand(GameContext ctx, Func<GameContext, bool> command)
        {
            GuardReentrancy();
            if (Phase != SessionPhase.Live || !InForeground(ctx))
                return false;
            // Past the refusals, so a refused command runs no pipeline and
            // flushes nothing; the mutation below sees settled state.
            FlushPending(ctx.NowUtc);
            commandInProgress = true;
            try
            {
                if (!command(ctx))
                    return false;
                CloseTransaction(ctx.NowUtc);
                return true;
            }
            finally
            {
                commandInProgress = false;
            }
        }

        // The root-owned pipeline: RunCommand without the phase test and the
        // foreground test, which are the chapter-local boundary a root command
        // is 12.9's exception to. The flush stays - a command owns its mutation
        // and the flush before it - and outside Live it is a no-op of its own
        // accord, since the session banks time only while it ticks. No phase
        // logic is added anywhere: CloseTransaction still sweeps only when the
        // resulting phase is Live.
        private void RunRootCommand(DateTime nowUtc, Action command)
        {
            GuardReentrancy();
            FlushPending(nowUtc);
            commandInProgress = true;
            try
            {
                command();
                CloseTransaction(nowUtc);
            }
            finally
            {
                commandInProgress = false;
            }
        }

        // The sweep is conditional on the transaction's RESULTING phase: only
        // one ending in Live sweeps. Ending in AwaitingIdleClaim or NoChapter
        // commits and refreshes without sweeping - a stored claim awaits
        // presentation, and any sweep (root included, since a root trigger may
        // legally reset a descendant chapter) could destroy it. The refresh IS
        // unconditional.
        private void CloseTransaction(DateTime nowUtc)
        {
            if (Phase == SessionPhase.Live)
                Sweep.Run(Root, ForegroundChapter, nowUtc);
            Refreshed?.Invoke();
        }

        // The command boundary (12.9): a chapter-local mutation is rejected
        // when its acting scope lies outside the foreground chapter's live
        // subtree - ids are unique tree-wide, but reachable is not the same as
        // mutable. The chain test IS the subtree test: a scope inside the
        // subtree has the foreground chapter on its outward chain, and the
        // identity comparison keeps a same-definition node from another tree
        // out.
        private bool InForeground(GameContext ctx) =>
            ctx.Scope.FindOnChain(ForegroundChapter.Definition) == ForegroundChapter;

        private void GuardReentrancy()
        {
            if (commandInProgress)
                throw new InvalidOperationException(
                    "A session command was issued from inside a running transaction (design doc 12.9).");
        }
    }
}
