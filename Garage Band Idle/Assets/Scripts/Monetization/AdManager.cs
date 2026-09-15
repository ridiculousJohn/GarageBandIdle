using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RidiculousGaming.GarageBandIdle.Monetization
{
    // The rewarded-ad side of the callback surface (design doc 12.11): plain
    // C#, owned by GameManager, and the only caller of the ad commands. A
    // button REQUESTS; the reward is this class's own transaction, delivered
    // from the driver's Update so it never interleaves with a running command.
    public sealed class AdManager
    {
        // One ad in flight: what it was for, the SDK's completion, and - for an
        // IdleDouble - the chapter the request was made against, since that is
        // the only thing that can invalidate a watched ad.
        private sealed class Request
        {
            public AdPlacement Placement;
            public Task<AdResult> Result;
            public ChapterScopeState Chapter;

            // What the requester wants told once the reward is on the tree and
            // saved - the Encore window closes on it. Nothing is told on a
            // failed or aborted result, since the player watched no ad.
            public Action Granted;
        }

        private readonly GameSession session;
        private readonly IAdService ads;

        // The driver's one save site. A watched ad is never replayed by an ad
        // network, so a crash after the grant would take the reward off disk.
        private readonly Action save;

        private readonly List<Request> pending = new();

        public AdManager(GameSession session, IAdService ads, Action save)
        {
            this.session = session;
            this.ads = ads;
            this.save = save;
        }

        public void RequestEncoreExtension(Action granted = null) =>
            pending.Add(new Request
            {
                Placement = AdPlacement.EncoreExtension,
                Result = ads.ShowRewarded(AdPlacement.EncoreExtension),
                Granted = granted,
            });

        // Records the chapter the offer belongs to: a switch under the dialog
        // settles that offer on the way out, and the result must not double a
        // different chapter's (design doc 9).
        public void RequestIdleDouble() =>
            pending.Add(new Request
            {
                Placement = AdPlacement.IdleDouble,
                Result = ads.ShowRewarded(AdPlacement.IdleDouble),
                Chapter = session.ForegroundChapter,
            });

        // Called by the driver each frame after the session accumulates, so a
        // result always lands between transactions. A request is removed before
        // it is delivered: a throw out of the command must not leave it to be
        // paid again next frame. An IdleDouble result waits while no chapter is
        // in the foreground: backgrounding drops the offer and keeps the stamp
        // (12.9), the resume re-enters the recorded chapter and recomputes it,
        // and only then can the chapter test tell a switch from a pause.
        public void Update(DateTime nowUtc)
        {
            for (var i = 0; i < pending.Count;)
            {
                var request = pending[i];
                if (!request.Result.IsCompleted
                    || (request.Placement == AdPlacement.IdleDouble && session.ForegroundChapter == null))
                {
                    i++;
                    continue;
                }
                pending.RemoveAt(i);
                Deliver(request, nowUtc);
            }
        }

        // Aborted, Failed, and a faulted task alike pay nothing and save
        // nothing - the player watched no ad.
        private void Deliver(Request request, DateTime nowUtc)
        {
            var result = request.Result.Status == TaskStatus.RanToCompletion
                ? request.Result.Result
                : AdResult.Failed;
            if (result != AdResult.Rewarded)
                return;

            if (request.Placement == AdPlacement.EncoreExtension)
            {
                // Always, after a backgrounding or a chapter change alike: the
                // command is legal in every phase and there is no offer to
                // lose, so a watched ad is never discarded. What the placement
                // pays is root's authored reward list, run as one command; the
                // save waits for that transaction, since the grant is submitted
                // (12.9) and may run at the frame's drain rather than at the call.
                session.RunReward(session.Root.DefinitionAs<RootDefinition>().encoreAdReward, nowUtc, _ =>
                {
                    save();
                    request.Granted?.Invoke();
                });
                return;
            }

            // The drop rule is IdleDouble's alone: the player switched under
            // the dialog, and that switch already settled the offer this ad was
            // watched for.
            if (session.ForegroundChapter != request.Chapter)
                return;
            // The save waits for the transaction that settled: the claim is a
            // submitted command (12.9), and the completed callback is what says
            // an offer was actually paid and is on the tree to be written.
            session.DoubleAndClaimIdle(nowUtc, settled =>
            {
                if (settled)
                    save();
            });
        }
    }
}
