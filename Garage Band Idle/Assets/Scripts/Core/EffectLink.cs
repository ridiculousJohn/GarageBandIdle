using System;
using RidiculousGaming.GarageBandIdle.Economy;

namespace RidiculousGaming.GarageBandIdle
{
    // One (carrier, effect, node) a coordinate plan holds (design doc 12.6).
    // WHICH effects can ever apply to a number is authored and validated, so it
    // is compiled when the tree is built; what stays a fact is whether each one
    // is LIVE - purchased, stacked, filled, recorded, its appliesWhen holding -
    // and that is the whole of what this reads.
    //
    // Two readings of that one fact: Live, whether it exists at my node, and
    // Factor, my contribution to the product - the fact's own arithmetic, or
    // One. The liveness kind is the LINK's own, so no reader ever switches on a
    // carrier kind and nothing but a link reads a carrier's fact.
    public sealed class EffectLink
    {
        // The node whose fact decides this link. Not the gather's origin: the
        // stack, the latch, the fill count, and the record all live where the
        // carrier is applied, which is where the chain walk found it.
        public ScopeState Node { get; }

        public LivenessKind Liveness { get; }

        // The upgrade, modifier, bar, or event the fact belongs to.
        public Definition Carrier { get; }

        public Effect Effect { get; }

        // Cascade entries only - a modifier scales through its own stacking
        // kind, read off the carrier.
        public GrowthKind Growth { get; }

        internal EffectLink(in CarrierEffect carrier, ScopeState node)
        {
            Liveness = carrier.Liveness;
            Carrier = carrier.Carrier;
            Effect = carrier.Effect;
            Growth = carrier.Growth;
            Node = node;
        }

        // This link's contribution to the product, against the state the caller
        // found. One when the fact is absent - that is what "contributes 1x
        // until its fact exists" means (12.6) - and otherwise the effect's
        // factor, constant or formula, scaled by the stored count through this
        // kind's own arithmetic.
        //
        // The formula is computed against the ORIGIN context, never one rebased
        // here: a multiplier is addressed to a NUMBER, and the number's identity
        // includes the chain it resolves on (12.6). appliesWhen is the opposite
        // and deliberately so - it is judged at THIS node, the one holding the
        // stack or the membership, which is the site validation judges it from.
        public BigNumber Factor(GameContext origin)
        {
            if (!Live(origin))
                return BigNumber.One;
            switch (Liveness)
            {
                case LivenessKind.Permanent:
                {
                    // An implicit application count of 1, MERGED with this
                    // node's stored stacks for the same modifier and resolved
                    // through its own stacking kind: Replace means
                    // permanent-plus-granted is still one application (12.5).
                    var permanent = (ModifierDefinition)Carrier;
                    Node.modifierStacks.TryGetValue(permanent.Id, out var stacks);
                    return Producer.Stacked(Producer.FactorOf(Effect, origin), 1 + stacks, permanent.stacking);
                }

                case LivenessKind.Granted:
                {
                    var granted = (ModifierDefinition)Carrier;
                    Node.modifierStacks.TryGetValue(granted.Id, out var count);
                    return Producer.Stacked(Producer.FactorOf(Effect, origin), count, granted.stacking);
                }

                case LivenessKind.Cascade:
                    // A completed fill applies the carrying entry's effect
                    // again, scaled by the entry's own growth kind (12.6/12.7).
                    Node.fillCounts.TryGetValue(Carrier.Id, out var fills);
                    return Producer.Grown(Producer.FactorOf(Effect, origin), fills, Growth);

                default:
                    // An upgrade's latch and an event's record scale nothing:
                    // there is one of each, so the live fact IS the factor
                    // (12.6/12.8). A kind with no liveness never reaches here -
                    // Live throws on one.
                    return Producer.FactorOf(Effect, origin);
            }
        }

        // Whether this link's fact exists at its node: the latch, the membership
        // with its gate holding, a stack, a fill, the record. Factor is this
        // answer times the effect's scaled factor, and the autobuy switch reads
        // this answer alone (12.2).
        public bool Live(GameContext origin)
        {
            switch (Liveness)
            {
                case LivenessKind.Upgrade:
                    // The effects apply for as long as the latch exists (12.6)
                    // and the upgrade is ON: an upgrade a group holds off
                    // gathers nothing, which is the membership seam for this
                    // kind (12.7).
                    return Node.purchasedUpgrades.Contains(Carrier.Id)
                        && origin.Rebase(Node).IsOn(Carrier);

                case LivenessKind.Permanent:
                    // The membership is the declaration itself, so the gate is
                    // the whole of what can withdraw it (12.5).
                    return Applies((ModifierDefinition)Carrier, origin);

                case LivenessKind.Granted:
                    return Node.modifierStacks.ContainsKey(Carrier.Id)
                        && Applies((ModifierDefinition)Carrier, origin);

                case LivenessKind.Cascade:
                    return Node.fillCounts.TryGetValue(Carrier.Id, out var fills) && fills > 0;

                case LivenessKind.Handicap:
                {
                    // Handicaps ride on the record EXISTING - no expiry check,
                    // because a failed attempt sits one tap from a reset and
                    // briefly lifting the handicap there would be the worse
                    // state (12.8).
                    var record = ((InteriorScopeState)Node).activeEvent;
                    return record != null && record.eventId == Carrier.Id;
                }

                default:
                    throw new InvalidOperationException(
                        $"EffectLink for '{Carrier?.Id}' at '{Node.ScopeId}' carries no liveness kind.");
            }
        }

        // Whether a modifier applies under this gather's circumstance, judged at
        // the node the modifier is APPLIED to - this link's own, which holds the
        // stack or the permanent membership. That is the site validation judges
        // the gate from (FinalizeModifierChecks), so execution and the load pass
        // agree; the circumstance and the clock ride the rebase (12.5/12.6).
        // Absent means always.
        private bool Applies(ModifierDefinition modifier, GameContext origin) =>
            modifier.appliesWhen == null || modifier.appliesWhen.Evaluate(origin.Rebase(Node));
    }
}
