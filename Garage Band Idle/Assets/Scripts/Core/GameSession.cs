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

        // The 12.11 hook, one per completed transaction and none on a refusal;
        // step 9's widgets subscribe. Unconditional where the sweep is not,
        // which is what repaints the claim dialog when a callback marks it
        // doubled without sweeping.
        public event Action Refreshed;

        // The reentrancy guard: a command issued from inside a transaction (a
        // trigger action, a refresh handler) is a code bug and throws. The
        // callback queue 12.9 describes is this flag's future consumer.
        private bool commandInProgress;

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
        // once over the explicit window [stamp, nowUtc] at current state, so
        // Records earned while away boost it, under the idle-accumulation
        // circumstance - the authored root base joins the gather and live-only
        // modifiers excuse themselves - and skipped entirely when the away
        // time is under the minimum, a blocking record holds, or every line
        // computes zero.
        private SessionPhase EnterChapter(ChapterScopeState chapter, DateTime nowUtc)
        {
            // The 12.10 clamp: a backwards clock claims nothing.
            var elapsed = Math.Max(0, (nowUtc - chapter.lastActiveUtc).TotalSeconds);
            if (elapsed < config.minimumAwaySeconds || BlockedByEvent(chapter))
                return SessionPhase.Live;

            var seconds = Math.Min(elapsed, config.idleCapSeconds);
            var idleCtx = new GameContext(chapter, nowUtc, idleAccumulation: true);
            var offer = new IdleOffer { windowEndUtc = nowUtc };
            foreach (var (currency, home) in Producer.RatePairs(chapter))
            {
                var amount = Producer.GetRate(idleCtx, currency) * seconds;
                if (amount == BigNumber.Zero)
                    continue;
                offer.lines.Add(new IdleOfferLine { currency = currency, home = home, amount = amount });
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

        // Live only; TickSystem.Tick inside the same pipeline. Nonpositive dt
        // is what a backwards mid-session clock produces when the driver diffs
        // DateTimes, and it no-ops like a refusal - nothing mutated, nothing
        // to sweep or repaint.
        public void Tick(double realSeconds, DateTime nowUtc)
        {
            GuardReentrancy();
            if (Phase != SessionPhase.Live || realSeconds <= 0)
                return;
            commandInProgress = true;
            try
            {
                TickSystem.Tick(Root, ForegroundChapter, config, realSeconds, nowUtc);
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
