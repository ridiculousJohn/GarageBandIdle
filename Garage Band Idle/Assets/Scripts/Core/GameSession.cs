using System;
using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Events;
using RidiculousGaming.GarageBandIdle.Meta;

namespace RidiculousGaming.GarageBandIdle
{
    // The session's phases (design doc 12.9). Launch and backgrounding are
    // NoChapter; AwaitingIdleClaim never ticks and is the one phase the claim
    // settles from - the chapter is live only after the claim settles; Live is
    // the one phase that ticks and sweeps.
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
    // explicit window [stamp, windowEndUtc], held by the session, dead with the
    // process. The lines hold what settlement pays - a Pass owner's are computed
    // doubled at entry, and the ad callback doubles them before it settles. THE
    // STAMP IS THE PENDING CLAIM - a kill with the dialog up saves nothing, and
    // the next entry recomputes from the stamp it never advanced.
    public class IdleOffer
    {
        public DateTime windowEndUtc;
        public List<IdleOfferLine> lines = new();
    }

    // The transient execution context (design doc 12.9): plain C#, never
    // serialized, holding only orchestration - the foreground chapter, the
    // phase, the outstanding offer, and the reentrancy guard. Durable facts
    // live in the tree. The session owns the transaction pipeline; the wrapped
    // systems stay public and unchanged, and tests keep calling them directly.
    public class GameSession
    {
        public readonly RootScopeState Root;
        private readonly GameConfig config;

        public ChapterScopeState ForegroundChapter { get; private set; }
        public SessionPhase Phase { get; private set; } = SessionPhase.NoChapter;

        // The outstanding idle offer - non-null exactly while the phase is
        // AwaitingIdleClaim. The dialog renders it; the ad callback doubles its
        // lines and settles them in one transaction (12.9), and a Pass owner's
        // is computed doubled.
        public IdleOffer CurrentOffer { get; private set; }

        // The last tick's realized movement, held out for interpolation (12.11)
        // and never serialized. The tick is its only writer: a command owns its
        // mutation and the flush before it, and the flush IS a tick, so the
        // report a command's refresh renders was measured against the state the
        // command found. Null only before the first tick.
        public TickReport LastTick { get; private set; }

        // The 12.11 hook, one per completed transaction and none on a refusal;
        // step 9's widgets subscribe. Unconditional where the sweep is not,
        // which is what repaints the claim dialog when an entitlement written
        // under it changes only the button set (12.9).
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
        // with an offer up settles it on a chapter-to-chapter switch (an exit
        // path, section 9) and DROPS it on backgrounding - the stamp
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
                            SettleOffer(outgoing);
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
        // anything: the stored lines deposit at their held homes as they stand,
        // and the stamp advances in the same transaction, which is the whole
        // exactly-once mechanism (12.9) - the save is the tree, so a kill keeps
        // both writes or neither. This transaction's sweep - root plus the
        // now-live foreground - is the deferred one: a threshold crossed while
        // away, by the switch's own settle-out, or by this deposit fires here,
        // root triggers included.
        public bool ClaimIdle(DateTime nowUtc) => Claim(nowUtc, doubled: false);

        // The rewarded ad's callback: doubles the offer's lines and settles them
        // in the SAME transaction (12.9), so a doubled offer is never left
        // standing. Refused like ClaimIdle when no offer stands - a kill mid-ad
        // takes the callback with the process, and the unmoved stamp re-offers
        // the window on the next launch.
        public bool DoubleAndClaimIdle(DateTime nowUtc) => Claim(nowUtc, doubled: true);

        // The pipeline the OK button and the ad callback share: the doubling, if
        // any, is inside the transaction with the settlement, so no phase and
        // no exit can ever see a doubled offer standing.
        private bool Claim(DateTime nowUtc, bool doubled)
        {
            GuardReentrancy();
            if (Phase != SessionPhase.AwaitingIdleClaim)
                return false;
            commandInProgress = true;
            try
            {
                if (doubled)
                    DoubleOffer();
                SettleOffer(ForegroundChapter);
                Phase = SessionPhase.Live;
                CloseTransaction(nowUtc);
                return true;
            }
            finally
            {
                commandInProgress = false;
            }
        }

        // The x2 written INTO the lines, because the lines are what settlement
        // pays and what the dialog shows - one amount, no second reader to keep
        // in step.
        private void DoubleOffer()
        {
            foreach (var line in CurrentOffer.lines)
                line.amount *= 2;
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

            // Two of the Pass's three benefits are the entitlement read at
            // COMPUTATION (section 9): the raised cap, and an offer computed
            // already doubled - the screen enters as if the ad had been
            // watched. The third, permanent Encore, is content - the first leg
            // of encore's own appliesWhen - so no code here knows about it.
            var owner = BackstagePass.Owned(Root);
            var paidSeconds = Math.Min(elapsed, owner ? config.backstagePassIdleCapSeconds : config.idleCapSeconds);
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
                {
                    currency = pairs[i].currency,
                    home = pairs[i].home,
                    amount = owner ? amounts[i] * 2 : amounts[i]
                });
            }
            if (offer.lines.Count == 0)
                return SessionPhase.Live;

            CurrentOffer = offer;
            return SessionPhase.AwaitingIdleClaim;
        }

        // Settlement pays the stored lines as they stand, through their held
        // references - nothing resolves a name here - and advances the stamp to
        // the window actually paid, never the settlement moment. Time past the
        // window's end is foreground presence, never idle: the next live exit
        // stamps over it. Then the offer dies.
        //
        // Both doublings - the Pass at computation, the ad callback before it
        // settles - are already in the amounts, so an exit and OK pay the same
        // number the dialog showed, which is what section 9 requires.
        private void SettleOffer(ChapterScopeState chapter)
        {
            var offer = CurrentOffer;
            foreach (var line in offer.lines)
            {
                // Resolved: the line's currency was judged active by the gather
                // that built the offer, under the claim's own circumstance. A
                // re-ask here would run under a live context instead and could
                // refuse a line the offer already promised, mid-settlement.
                new GameContext(line.home, offer.windowEndUtc).DepositResolved(line.currency.Id, line.amount);
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
        // system takes plus nothing new. A callback command arrives with its
        // step and builds the context for the scope it writes.

        public bool TryRung(GameContext ctx) =>
            RunCommand(ctx, c => c.Scope.Definition is InteriorDefinition interior
                && interior.rung != null && interior.rung.TryExecute(c));

        public bool TryBuy(GameContext ctx, GeneratorDefinition generator) =>
            RunCommand(ctx, c => Purchasing.TryBuy(c, generator));

        public bool TryBuy(GameContext ctx, UpgradeDefinition upgrade) =>
            RunCommand(ctx, c => Purchasing.TryBuy(c, upgrade));

        // Firing has no gate of its own - it always happens, so the pipeline
        // always runs.
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
        // modifier. The record write is the whole mutation, and the sweep stays
        // conditional on the resulting phase, so a grant that lands while a
        // claim awaits presentation repaints without sweeping the unpaid window
        // away (12.9).
        public void ExtendBuff(ScopeState scope, ModifierDefinition modifier, double seconds, DateTime nowUtc)
        {
            // A grant only ever moves an expiry LATER, so a nonpositive or
            // non-finite duration is a caller bug and not a shorter buff.
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
                throw new InvalidOperationException(
                    $"ExtendBuff for '{modifier.Id}': {seconds} is not a finite positive number of seconds.");
            RunCommand(new GameContext(scope, nowUtc), c =>
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
                return true;
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

        // The store callback's write for a restored or otherwise granted
        // entitlement: under the claim dialog it sweeps nothing and repaints
        // only the button set - the offer stays what was computed and is what
        // OK pays (12.9).
        public void GrantEntitlement(string entitlementId, DateTime nowUtc)
        {
            RunCommand(new GameContext(Root, nowUtc), c =>
            {
                WriteEntitlement(entitlementId);
                return true;
            });
        }

        // The store callback for a Pass purchase (12.9): the entitlement write,
        // and when an offer stands - bought FROM the dialog - the offer doubled
        // and settled in the same transaction, ending Live so the dialog closes
        // with the phase. Bought anywhere else, the write is the whole command.
        public void PurchasePassFromDialog(DateTime nowUtc)
        {
            RunCommand(new GameContext(Root, nowUtc), c =>
            {
                WriteEntitlement(BackstagePass.EntitlementId);
                if (Phase != SessionPhase.AwaitingIdleClaim)
                    return true;
                DoubleOffer();
                SettleOffer(ForegroundChapter);
                Phase = SessionPhase.Live;
                return true;
            });
        }

        // Writes at root, and only an id root declares: the save filter would
        // drop an undeclared one on the next load, so a write of one is a
        // silent loss - the SetFlag rule (12.3), a throw.
        private void WriteEntitlement(string entitlementId)
        {
            if (!Root.DefinitionAs<RootDefinition>().DeclaresEntitlement(entitlementId))
                throw new InvalidOperationException(
                    $"GrantEntitlement for '{entitlementId}': root declares no such entitlement (12.3).");
            Root.entitlements.Add(entitlementId);
        }

        // The store's bundle grant: a root-context deposit into roadies through
        // the AUTHORED write, so the currency's activeWhen is honored (12.2). A
        // nonpositive count is a caller bug, not a smaller bundle -
        // GameConfig.Require refuses one before any store can report success.
        public void GrantRoadies(int count, DateTime nowUtc)
        {
            if (count <= 0)
                throw new InvalidOperationException($"GrantRoadies: {count} is not a positive count.");
            RunCommand(new GameContext(Root, nowUtc), c =>
            {
                c.Deposit(Roadies.CurrencyId, count);
                return true;
            });
        }

        // ---- the pipeline ----

        // The one pipeline every command runs: guard - flush - mutation -
        // conditional sweep - commit - one refresh (12.9/12.11). The flush
        // precedes the command, so the mutation runs against settled state; a
        // command that returns false commits no transaction and triggers no
        // refresh, and commit is a seam rather than machinery - the point after
        // the sweep where the transaction's state is what refresh reads.
        private bool RunCommand(GameContext ctx, Func<GameContext, bool> command)
        {
            GuardReentrancy();
            // A command owns its mutation and the flush before it. Outside Live
            // the flush is a no-op of its own accord, since the session banks
            // time only while it ticks.
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

        private void GuardReentrancy()
        {
            if (commandInProgress)
                throw new InvalidOperationException(
                    "A session command was issued from inside a running transaction (design doc 12.9).");
        }
    }
}
