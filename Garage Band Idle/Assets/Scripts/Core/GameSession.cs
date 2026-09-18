using System;
using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Events;
using RidiculousGaming.GarageBandIdle.Meta;
using RidiculousGaming.GarageBandIdle.Story;

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

    // One line of the idle offer: the target, its home, and the amount - all
    // references, born from the same (target, home) enumeration GetRate sums,
    // because nothing about an offer ever crosses a save boundary. Idle pays
    // every rate target the tick would, one line per target (12.9), plus what
    // a window's completions fired. The same shape carries a draw, where the
    // amount is what a currency LOST rather than gained; both are positive, and
    // which list an entry sits in is which write it takes.
    public class IdleOfferLine
    {
        public Definition target;
        public ScopeState home;
        public BigNumber amount;
    }

    // One bar the window moved (section 9): the bar, its declaring node, the
    // progress the window leaves it at, and how many thresholds it crossed.
    // References like every other line, because nothing about an offer ever
    // crosses a save boundary; the claim is where any of it is written.
    public class IdleBarLine
    {
        public BarDefinition bar;
        public ScopeState home;
        public BigNumber progress;      // carried progress after the window's completions
        public int completions;

        // The manual team that completed: it leaves the active set of every
        // group listing it, and the claim is where that write lands (12.7).
        public bool off;
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

        // What the window's bars drew from each currency, the fact the tick
        // records at the same moment (TickReport.RecordDraw). A draw is a SPEND
        // and a line is a deposit, so they are two lists and never one netted
        // number: an earned total counts what was earned, and a spend never
        // touches it (12.3).
        public List<IdleOfferLine> draws = new();

        // What the window's bars did, beside what it paid and what it drank:
        // these are the writes that the fill earned (section 9).
        public List<IdleBarLine> bars = new();

        // What the claim CHANGES each balance by: a line less the draw on the
        // same (target, home), signed, zero dropped, and a draw nothing paid
        // into as its whole negative. This is the dialog's row list and the
        // session's test for whether there is anything to show at all (section
        // 9): bar progress is a write, never a report, so a window that changed
        // no balance raises no dialog. Nothing about the economy is computed
        // here - the two numbers are already stored, and this subtracts them.
        public List<IdleOfferLine> Changes()
        {
            var changes = new List<IdleOfferLine>();
            foreach (var line in lines)
            {
                var net = line.amount;
                foreach (var draw in draws)
                    if (draw.target == line.target && draw.home == line.home)
                        net -= draw.amount;
                if (net != BigNumber.Zero)
                    changes.Add(new IdleOfferLine { target = line.target, home = line.home, amount = net });
            }
            foreach (var draw in draws)
            {
                var paidInto = false;
                foreach (var line in lines)
                    if (line.target == draw.target && line.home == draw.home)
                        paidInto = true;
                if (!paidInto)
                    changes.Add(new IdleOfferLine { target = draw.target, home = draw.home, amount = -draw.amount });
            }
            return changes;
        }
    }

    // The transient execution context (design doc 12.9): plain C#, never
    // serialized, holding only orchestration - the foreground chapter, the
    // phase, the outstanding offer, and the command queue. Durable facts
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

        // The knobs a widget reads off the session it already holds (12.11).
        public GameConfig Config => config;

        // The 12.11 hook, one per completed transaction and none on a refusal,
        // fired from inside the transaction that completes. A handler that
        // issues a command submits it, and it runs at the next drain as its own
        // transaction. Unconditional where the sweep is not, which is what
        // repaints the claim dialog when an entitlement written under it
        // changes only the button set (12.9).
        public event Action Refreshed;

        // The command queue (12.9). Every transaction the session runs is
        // submitted here and executed from the front; an entry stays at the
        // front until it returns, so the queue is non-empty for the whole of
        // its run and "empty" is the complete test for running a submission at
        // once. A transaction submitted while one is executing - from a refresh
        // handler, from a trigger action - lands behind it and runs at the
        // frame's drain, as its own transaction with its own refresh, never
        // nested. Nothing can reenter, so nothing guards.
        private readonly Queue<Action> queue = new();

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

        public bool IsChapterUnlocked(ChapterScopeState chapter, DateTime nowUtc) =>
            chapter != null && chapter.Parent == Root && chapter.IsUnlocked(nowUtc);

        // Legal in every phase, one transaction (12.9). Switching to the
        // CURRENT chapter (or to null while already NoChapter) is a no-op
        // that runs no pipeline: the stamp is old during a live
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
            Submit(() =>
            {
                // Read when the transaction runs, which is the state the switch
                // would act on.
                if (chapter == ForegroundChapter)
                    return;
                if (chapter != null && !IsChapterUnlocked(chapter, nowUtc))
                    return;
                // The outgoing chapter's banked foreground time settles into
                // the outgoing subtree, never the incoming one - and the
                // incoming window starts here, with nothing banked.
                FlushPending(nowUtc);
                lastSampleUtc = nowUtc;
                pendingSeconds = 0;

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
            });
        }

        // AwaitingIdleClaim only. Pure settlement - the claim never computes
        // anything: the stored lines deposit at their held homes as they stand,
        // and the stamp advances in the same transaction, which is the whole
        // exactly-once mechanism (12.9) - the save is the tree, so a kill keeps
        // both writes or neither. This transaction's sweep - root plus the
        // now-live foreground - is the deferred one: a threshold crossed while
        // away, by the switch's own settle-out, or by this deposit fires here,
        // root triggers included.
        public void ClaimIdle(DateTime nowUtc, Action<bool> completed = null) =>
            Claim(nowUtc, doubled: false, completed);

        // The rewarded ad's callback: doubles the offer's lines and settles them
        // in the SAME transaction (12.9), so a doubled offer is never left
        // standing. Refused like ClaimIdle when no offer stands - a kill mid-ad
        // takes the callback with the process, and the unmoved stamp re-offers
        // the window on the next launch.
        public void DoubleAndClaimIdle(DateTime nowUtc, Action<bool> completed = null) =>
            Claim(nowUtc, doubled: true, completed);

        // The pipeline the OK button and the ad callback share, as one queued
        // transaction: the doubling, if any, is inside the transaction with the
        // settlement, so no phase and no exit can ever see a doubled offer
        // standing. The phase is read when the transaction runs, and the
        // completed callback is told whether it settled.
        private void Claim(DateTime nowUtc, bool doubled, Action<bool> completed)
        {
            Submit(() =>
            {
                if (Phase != SessionPhase.AwaitingIdleClaim)
                {
                    completed?.Invoke(false);
                    return;
                }
                if (doubled)
                    DoubleOffer();
                SettleOffer(ForegroundChapter);
                Phase = SessionPhase.Live;
                CloseTransaction(nowUtc);
                completed?.Invoke(true);
            });
        }

        // The x2 written INTO the lines, because the lines are what settlement
        // pays and what the dialog shows - one amount, no second reader to keep
        // in step. The LINES alone: a draw is what the window's bars drank, and
        // doubling it would charge twice for one fill, and a bar entry is never
        // doubled either - twice the Cash, never twice the cover (section 9).
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
        // Every bar of the subtree fills over the same window, capped by what it
        // consumes, and what its completions fire joins the lines (section 9).
        // Skipped entirely when the away time is under the minimum, a blocking
        // record holds, nothing at all moved, or the chapter has never been
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

            // The accumulation, keyed by target AND home over the whole window.
            // The rate pairs seed it, so the lines keep the tick's own order,
            // and a completion's payout into anything else lands where it was
            // first seen. Only the seeded ones take a rate: a target appended by
            // a payout is one no source in this subtree pays per second.
            var targets = Producer.RatePairs(chapter);
            var amounts = new List<BigNumber>(targets.Count);
            var drawn = new List<BigNumber>(targets.Count);
            for (var i = 0; i < targets.Count; i++)
            {
                amounts.Add(BigNumber.Zero);
                drawn.Add(BigNumber.Zero);
            }
            var rateCount = targets.Count;
            var barLines = new List<IdleBarLine>();

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
                for (var i = 0; i < rateCount; i++)
                    amounts[i] += Producer.GetRate(segCtx, targets[i].target) * effSeconds;
                // AFTER the segment's rate amounts, so a bar drinks what the
                // same segment produced - the tick's own phase order (12.9).
                FillBars(segCtx, effSeconds);
            }

            // The offer's window ends at NOW even though the payment covers the
            // capped window: the stamp advances to the window PRESENTED, and
            // time past the cap is a lost window either way (12.9's settled-
            // window rule), so nothing about settlement changes.
            var offer = new IdleOffer { windowEndUtc = nowUtc };
            for (var i = 0; i < targets.Count; i++)
            {
                if (amounts[i] == BigNumber.Zero)
                    continue;
                offer.lines.Add(new IdleOfferLine
                {
                    target = targets[i].target,
                    home = targets[i].home,
                    // A Pass owner's offer is computed already doubled - the
                    // screen enters as if the ad had been watched (section 9).
                    amount = owner ? amounts[i] * 2 : amounts[i]
                });
            }
            // The draws in the same order, as their own writes: what the bars
            // drank is spent at the claim, never netted off a deposit (12.3).
            for (var i = 0; i < targets.Count; i++)
            {
                if (drawn[i] == BigNumber.Zero)
                    continue;
                offer.draws.Add(new IdleOfferLine
                {
                    target = targets[i].target,
                    home = targets[i].home,
                    amount = drawn[i]
                });
            }
            offer.bars.AddRange(barLines);
            if (offer.lines.Count == 0 && offer.draws.Count == 0 && offer.bars.Count == 0)
                return SessionPhase.Live;

            // The dialog exists for a balance the player is owed. A window that
            // changed none - a bar part-way through a cycle, or a bar that drank
            // exactly what came in - has nothing to show, so it settles on entry:
            // the same claim, the progress and the stamp written, no screen
            // (section 9). Never an empty dialog.
            CurrentOffer = offer;
            if (offer.Changes().Count == 0)
            {
                SettleOffer(chapter);
                return SessionPhase.Live;
            }
            return SessionPhase.AwaitingIdleClaim;

            // Every bar of the chapter's subtree, in the tick's settlement
            // order, for one segment: the fill the tick would have moved, capped
            // by what the bar consumes, then the completions that fill crossed
            // and what each one fires. Nothing is written - the whole of it
            // lands in the offer (section 9).
            void FillBars(GameContext segCtx, double effSeconds)
            {
                foreach (var plan in ContributorPlan.At(chapter).Bars)
                {
                    var line = BarLine(plan.Bar);
                    // The manual team that completed ran its one cycle and is
                    // off for the rest of the window (12.7).
                    if (line != null && line.off)
                        continue;
                    var barCtx = segCtx.Rebase(plan.Node);
                    var progress = line != null ? line.progress : Progress(plan);
                    if (!BarSystem.Drawing(barCtx, plan.Bar, progress))
                        continue;

                    // idle_base's rate x0.5 halves the fill through the ordinary
                    // gather, exactly as it halves every rate (section 9).
                    var fill = BarSystem.FillRate(segCtx, plan) * effSeconds;

                    // What each consumed currency can give: its balance at the
                    // stamp, plus this window's inflow into it, minus what
                    // earlier bars already drew - so the settlement order is the
                    // tick's - divided by the per-unit price under its factor.
                    var costs = new List<(int slot, BigNumber perUnit)>(plan.Consumes.Count);
                    foreach (var consumes in plan.Consumes)
                    {
                        var perUnit = consumes.Entry.amount * Producer.GetMultiplier(barCtx, consumes.Factor);
                        if (perUnit <= BigNumber.Zero)
                            continue;       // free covers unboundedly
                        var slot = Slot(consumes.Entry.currency, consumes.Home);
                        var available = new GameContext(consumes.Home, segCtx.NowUtc)
                            .GetBalance(consumes.Entry.currency.Id) + amounts[slot] - drawn[slot];
                        costs.Add((slot, perUnit));
                        fill = BigNumber.Min(fill, available / perUnit);
                    }
                    // Nothing to drink moves nothing, and a cover rounded a hair
                    // below zero is the same answer.
                    if (fill < BigNumber.Zero)
                        fill = BigNumber.Zero;

                    var completions = 0;
                    var off = false;
                    if (plan.Bar.repeatWhen == null)
                    {
                        // Fill once and stay full: the fill past the threshold is
                        // never drawn, so the take is sized to what fits, and the
                        // completed bar leaves its groups at the claim as it does
                        // live (12.7).
                        if (progress + fill >= plan.Bar.fillAmount)
                        {
                            fill = plan.Bar.fillAmount - progress;
                            completions = 1;
                            progress = plan.Bar.fillAmount;
                            off = true;
                        }
                        else
                        {
                            progress += fill;
                        }
                    }
                    else if (plan.Bar.repeatWhen.Evaluate(barCtx))
                    {
                        // Every threshold the window crosses pays and the
                        // residual is kept - the same arithmetic the tick's own
                        // settlement takes, over the counts this window adds.
                        progress += fill;
                        plan.Node.fillCounts.TryGetValue(plan.Bar.Id, out var recorded);
                        completions = BarSystem.Crossings(plan.Bar, progress,
                            recorded + (line == null ? 0 : line.completions));
                        progress -= plan.Bar.fillAmount * completions;
                    }
                    else if (progress + fill >= plan.Bar.fillAmount)
                    {
                        // The manual team: one cycle, then zero, then off. The
                        // excess past the threshold is discarded as it is live.
                        completions = 1;
                        progress = BigNumber.Zero;
                        off = true;
                    }
                    else
                    {
                        progress += fill;
                    }

                    foreach (var (slot, perUnit) in costs)
                        drawn[slot] += fill * perUnit;

                    // What the completions fired, resolved once under the segment
                    // context and multiplied - the resolution the live completion
                    // uses, so the window pays what presence would have.
                    for (var i = 0; completions > 0 && i < plan.Bar.onComplete.Count; i++)
                    {
                        if (plan.Bar.onComplete[i] is not FireGeneratorYield fire)
                            continue;
                        foreach (var (target, amount) in Producer.ResolveGeneratorYield(barCtx, fire.generator))
                        {
                            if (amount == BigNumber.Zero)
                                continue;
                            var home = Producer.DeclaringScope<ScopeState>(plan.Node, target);
                            amounts[Slot(target, home)] += amount * completions;
                        }
                    }

                    // Progress is carried whether or not anything crossed: a bar
                    // that pays very slowly still moves while the player is away
                    // (section 9).
                    if (line == null)
                    {
                        line = new IdleBarLine { bar = plan.Bar, home = plan.Node };
                        barLines.Add(line);
                    }
                    line.progress = progress;
                    line.completions += completions;
                    line.off = off;
                }
            }

            // Where one (target, home) keeps its running inflow and its running
            // draw, appended when this window is the first thing to move it. A
            // slot the rate pairs did not seed takes no rate: nothing in this
            // subtree pays it per second, and a payout or a draw found it.
            int Slot(Definition target, ScopeState home)
            {
                for (var i = 0; i < targets.Count; i++)
                    if (targets[i].target == target && targets[i].home == home)
                        return i;
                targets.Add((target, home));
                amounts.Add(BigNumber.Zero);
                drawn.Add(BigNumber.Zero);
                return targets.Count - 1;
            }

            IdleBarLine BarLine(BarDefinition bar)
            {
                for (var i = 0; i < barLines.Count; i++)
                    if (barLines[i].bar == bar)
                        return barLines[i];
                return null;
            }

            BigNumber Progress(BarPlan plan) =>
                plan.Node.barProgress.TryGetValue(plan.Bar.Id, out var stored) ? stored : BigNumber.Zero;
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
                // Resolved: the line's target was judged active by the gather
                // that built the offer, under the claim's own circumstance. A
                // re-ask here would run under a live context instead and could
                // refuse a line the offer already promised, mid-settlement.
                Producer.PayResolved(new GameContext(line.home, offer.windowEndUtc), line.target, line.amount);
            }

            // Then what the bars drank, as the spend the tick makes: only a
            // currency is ever drunk (requirement 7 on anything else). Clamped at
            // the balance the production above landed in, as the live draw is:
            // a cover sized as a quotient can put the product a last digit over
            // it, and Spend throws on that.
            foreach (var draw in offer.draws)
            {
                if (draw.target is not CurrencyDefinition consumed)
                    throw new InvalidOperationException(
                        $"Idle draw names '{draw.target.Id}', and only a currency is consumed (12.7).");
                var ctx = new GameContext(draw.home, offer.windowEndUtc);
                ctx.Spend(consumed.Id, BigNumber.Min(draw.amount, ctx.GetBalance(consumed.Id)));
            }

            // Then what the window's bars did: the progress, the fill counts the
            // crossings added, the manual team leaving its groups, and each
            // completion's own actions - with the payment ones skipped, because
            // those were the lines (section 9).
            foreach (var line in offer.bars)
            {
                var ctx = new GameContext(line.home, offer.windowEndUtc);
                line.home.barProgress[line.bar.Id] = line.progress;
                if (line.bar.repeatWhen != null && line.completions > 0)
                {
                    line.home.fillCounts.TryGetValue(line.bar.Id, out var recorded);
                    line.home.fillCounts[line.bar.Id] = recorded + line.completions;
                }
                if (line.off)
                    BarSystem.Deselect(line.home, line.bar);
                for (var i = 0; i < line.completions; i++)
                    ActionList.Run(line.bar.onComplete, ctx, skip: a => a is FireGeneratorYield);
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
        // only the bank is conditional, so time under a dialog never banks up
        // and dumps into the first live tick. One tick carries the WHOLE
        // accumulation ending at the sample, which is what keeps the simulated
        // windows contiguous and their timestamps exact - TickSystem already
        // segments internally, so a hitch is one correct call.
        public void Accumulate(DateTime nowUtc)
        {
            // The frame drains what the last frame's refreshes submitted, then
            // banks time (12.9).
            Drain();
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
            // A submission like every other transaction (12.9): with the queue
            // empty it runs here, and behind entries the drain above left
            // standing it runs at the next drain - either way a command its
            // refresh submits lands behind it and never inside it.
            Submit(() => TickNow(dt, nowUtc));
        }

        // A player action settles the bank before it mutates: whatever the
        // frames have banked since the last tick is simulated as a preceding
        // tick transaction, so the mutation runs against settled state - a
        // generator bought mid-window earns nothing for the time before it
        // existed. The action reads no clock; the frame already did. Flush,
        // never clear: FireProducer is a command, and clearing per Jam tap
        // would starve the rate production the rates exist for. The tick BODY,
        // never a submission: the flush is part of the transaction running
        // around it, and a submitted flush would queue behind the command it
        // has to precede.
        private void FlushPending(DateTime nowUtc)
        {
            if (Phase != SessionPhase.Live || pendingSeconds <= 0)
                return;
            var dt = pendingSeconds;
            pendingSeconds = 0;
            TickNow(dt, nowUtc);
        }

        // A tick asked for by name: one submitted transaction like every other
        // (12.9), so one issued from inside a running transaction lands behind
        // it and runs at the drain. The frame's own tick is Accumulate's.
        public void Tick(double realSeconds, DateTime nowUtc) =>
            Submit(() => TickNow(realSeconds, nowUtc));

        // Live only; TickSystem.Tick inside the same pipeline. Nonpositive dt
        // no-ops like a refusal - nothing mutated, nothing to sweep or repaint.
        // A tick that runs SETTLES the sample at its end with nothing pending:
        // the world is simulated through nowUtc, so a command issued at that
        // moment flushes nothing, rather than re-simulating the window a caller
        // ticked by hand.
        private void TickNow(double realSeconds, DateTime nowUtc)
        {
            if (Phase != SessionPhase.Live || realSeconds <= 0)
                return;
            lastSampleUtc = nowUtc;
            pendingSeconds = 0;
            LastTick = TickSystem.Tick(Root, ForegroundChapter, config, realSeconds, nowUtc);
            CloseTransaction(nowUtc);
        }

        // ---- the command surface ----
        // One wrapper per entry point, taking the same GameContext the wrapped
        // system takes plus nothing new. A callback command arrives with its
        // step and builds the context for the scope it writes. Every wrapper
        // submits and returns; a caller that needs the outcome passes a
        // completed callback and is told when the transaction has run.

        public void TryRung(GameContext ctx, Action<bool> completed = null) =>
            RunCommand(ctx, c => c.Scope.Definition is InteriorDefinition interior
                && interior.rung != null && interior.rung.TryExecute(c), completed);

        public void TryBuy(GameContext ctx, GeneratorDefinition generator, int count, Action<bool> completed = null) =>
            RunCommand(ctx, c => Purchasing.TryBuy(c, generator, count), completed);

        public void TryBuy(GameContext ctx, UpgradeDefinition upgrade, Action<bool> completed = null) =>
            RunCommand(ctx, c => Purchasing.TryBuy(c, upgrade), completed);

        // Firing has no gate of its own - it always happens, so the pipeline
        // always runs.
        public void FireProducer(GameContext ctx, ProducerDefinition producer, Action<bool> completed = null) =>
            RunCommand(ctx, c => { Producer.FireProducer(c, producer); return true; }, completed);

        public void SetActiveMembers(GameContext ctx, GroupDefinition group, IReadOnlyList<Definition> members,
                                     Action<bool> completed = null) =>
            RunCommand(ctx, c => BarSystem.SetActiveMembers(c, group, members), completed);

        public void TryStartEvent(GameContext ctx, EventDefinition evt, Action<bool> completed = null) =>
            RunCommand(ctx, c => EventSystem.TryStart(c, evt), completed);

        public void TryDismissEvent(GameContext ctx, EventDefinition evt, Action<bool> completed = null) =>
            RunCommand(ctx, c => EventSystem.TryDismiss(c, evt), completed);

        // Opening a beat's card marks it read (section 10): the seen flag is
        // written at its home through the same outward walk SetFlag uses. A
        // session command like every other, on RunCommand; it checks nothing of
        // its own - the host opens a card only for a beat whose button is live
        // or whose mark pops it, and setting a flag already set changes nothing.
        public void AcknowledgeStory(GameContext ctx, StoryBeatDefinition beat, Action<bool> completed = null) =>
            RunCommand(ctx, c => { c.SetFlag(beat.seenFlag); return true; }, completed);

        // The timer extend as a command (section 9): the record write is the whole
        // mutation, and the sweep stays conditional on the resulting phase, so a grant
        // that lands while a claim awaits presentation repaints without sweeping the
        // unpaid window away (12.9). The completed callback is told when the write is
        // on the tree.
        public void ExtendBuff(ScopeState scope, string timerId, double seconds, double capSeconds, DateTime nowUtc,
                               Action<bool> completed = null) =>
            RunCommand(new GameContext(scope, nowUtc), c => { c.ExtendTimer(timerId, seconds, capSeconds); return true; }, completed);

        // An authored reward list run at root's context as ONE transaction (section 9,
        // 12.5): the ad callback hands it root's encoreAdReward. Run rather than TryRun:
        // a reward list is authored on root, which no reset can refuse, and forcing
        // past a refusal throws (requirement 7).
        public void RunReward(IReadOnlyList<GameAction> actions, DateTime nowUtc, Action<bool> completed = null) =>
            RunCommand(new GameContext(Root, nowUtc), c => { ActionList.Run(actions, c); return true; }, completed);

        // The store callback's write for a restored or otherwise granted
        // entitlement: under the claim dialog it sweeps nothing and repaints
        // only the button set - the offer stays what was computed and is what
        // OK pays (12.9). The completed callback is told when the write is on
        // the tree.
        public void GrantEntitlement(string entitlementId, DateTime nowUtc, Action<bool> completed = null)
        {
            RunCommand(new GameContext(Root, nowUtc), c =>
            {
                WriteEntitlement(entitlementId);
                return true;
            }, completed);
        }

        // The store callback for a Pass purchase (12.9): the entitlement write,
        // and when an offer stands - bought FROM the dialog - the offer doubled
        // and settled in the same transaction, ending Live so the dialog closes
        // with the phase. Bought anywhere else, the write is the whole command.
        // The completed callback is told when the write is on the tree.
        public void PurchasePassFromDialog(DateTime nowUtc, Action<bool> completed = null)
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
            }, completed);
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
        // The completed callback is told when the write is on the tree.
        public void GrantRoadies(int count, DateTime nowUtc, Action<bool> completed = null)
        {
            if (count <= 0)
                throw new InvalidOperationException($"GrantRoadies: {count} is not a positive count.");
            RunCommand(new GameContext(Root, nowUtc), c =>
            {
                c.Deposit(Roadies.CurrencyId, count);
                return true;
            }, completed);
        }

        // Replaces the whole allocation through the root command pipeline. The
        // caller's draft is copied before submission because a command issued
        // during a refresh waits for the next drain; later UI edits must not
        // change the transaction that is already queued.
        public void SetRoadieAllocation(IReadOnlyDictionary<string, int> allocation, DateTime nowUtc,
                                        Action<bool> completed = null)
        {
            if (allocation == null)
                throw new ArgumentNullException(nameof(allocation));
            var requested = new Dictionary<string, int>(allocation.Count);
            foreach (var pair in allocation)
                requested.Add(pair.Key, pair.Value);
            RunCommand(new GameContext(Root, nowUtc),
                c => Meta.RoadieAllocation.TrySet(c, requested), completed);
        }

        // ---- the pipeline ----

        // Submission: append, and when the entry is the only one, run it now.
        // Running now is an optimization no call site relies on - a caller that
        // needs the outcome passes a completed callback and is told when it
        // happens.
        private void Submit(Action transaction)
        {
            queue.Enqueue(transaction);
            if (queue.Count == 1)
                RunFront();
        }

        // The front entry runs and is then removed - in a finally, so a
        // transaction that throws leaves the queue moving rather than parked
        // behind it.
        private void RunFront()
        {
            try { queue.Peek()(); }
            finally { queue.Dequeue(); }
        }

        // The frame's drain (12.9): the entries present when it starts, in
        // order, and no more - one enqueued by a refresh inside this drain
        // waits for the next frame.
        public void Drain()
        {
            var pending = queue.Count;
            for (var i = 0; i < pending; i++)
                RunFront();
        }

        // A refresh with nothing to commit, as its own queued entry: the first
        // render of a bound screen, which no transaction precedes (12.11). It
        // is an entry so that a command the render submits lands behind it and
        // runs at the drain, exactly as one submitted from any other refresh.
        public void Refresh() => Submit(() => Refreshed?.Invoke());

        // The one pipeline every command runs (12.9/12.11), as one queued
        // transaction: the flush, the command's own check and mutation, the
        // sweep when the resulting phase is Live, one refresh. The flush
        // precedes the command, so the mutation runs against settled state; a
        // command that returns false commits no transaction and triggers no
        // refresh, and commit is a seam rather than machinery - the point after
        // the sweep where the transaction's state is what refresh reads. The
        // completed callback, when a caller passes one, is told the command's
        // own answer after the transaction has run.
        private void RunCommand(GameContext ctx, Func<GameContext, bool> command, Action<bool> completed = null)
        {
            Submit(() =>
            {
                // A command owns its mutation and the flush before it. Outside
                // Live the flush is a no-op of its own accord, since the
                // session banks time only while it ticks.
                FlushPending(ctx.NowUtc);
                var ran = command(ctx);
                if (ran)
                    CloseTransaction(ctx.NowUtc);
                completed?.Invoke(ran);
            });
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
    }
}
