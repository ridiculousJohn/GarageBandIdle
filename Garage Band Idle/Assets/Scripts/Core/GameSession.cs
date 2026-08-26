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

    // The transient execution context (design doc 12.9): plain C#, never
    // serialized, holding only orchestration - the foreground chapter, the
    // phase, and the reentrancy guard. Durable facts live in the tree. The
    // session draws the command boundary and owns the transaction pipeline;
    // the wrapped systems stay public and unchanged, and tests keep calling
    // them directly.
    public class GameSession
    {
        public readonly RootScopeState Root;
        private readonly GameConfig config;

        public ChapterScopeState ForegroundChapter { get; private set; }
        public SessionPhase Phase { get; private set; } = SessionPhase.NoChapter;

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

        // Legal in every phase. What lands here is the transition skeleton:
        // switching to the CURRENT chapter (or to null while already
        // NoChapter) is a no-op success that runs no pipeline; a null incoming
        // chapter is backgrounding; an incoming chapter holding an unsettled
        // claim re-offers it, and otherwise the switch enters Live, whose
        // closing sweep is the deferred "first live sweep after switch-in"
        // (12.8). The idle half of the command - the monotonic stamps, the
        // settle-out, the claim computation and its skip rules, the current-
        // chapter root fact - is the idle changeset's.
        public void SwitchChapter(ChapterScopeState chapter, DateTime nowUtc)
        {
            GuardReentrancy();
            if (chapter == ForegroundChapter)
                return;
            commandInProgress = true;
            try
            {
                ForegroundChapter = chapter;
                if (chapter == null)
                    Phase = SessionPhase.NoChapter;
                else if (chapter.pendingClaim != null && !chapter.pendingClaim.settled)
                    Phase = SessionPhase.AwaitingIdleClaim;
                else
                    Phase = SessionPhase.Live;
                CloseTransaction(nowUtc);
            }
            finally
            {
                commandInProgress = false;
            }
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
