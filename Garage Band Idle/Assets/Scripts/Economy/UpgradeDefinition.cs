using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // One level of a levelled upgrade (design doc 4): its price in the
    // upgrade's costCurrency, the effects that apply while the upgrade stands
    // at exactly this level, the text saying what it does, and the actions run
    // at the moment it is bought.
    [Serializable]
    public class UpgradeLevel
    {
        public BigNumber cost;                  // zero is legal content
        public List<Effect> effects = new();
        public string description;
        [SerializeReference, SubclassPicker] public List<GameAction> actions = new();
    }

    // A purchase whose fact is the latch in purchasedUpgrades at the declaring
    // scope (design doc 12.6): the effects apply for as long as the latch
    // exists, and the actions run once, at the moment of purchase. A pure
    // latch is legal - Chapter 1's stage_presence carries no effects and no
    // actions, and tap_producer's conditioned entry reads it.
    //
    // A LEVELLED upgrade authors `levels` instead of a cost, effects, a
    // description and actions of its own (design doc 4): each buy buys the next
    // level, the owned level is the upgradeLevels fact beside the latch, and
    // exactly the owned level's effects apply - a level replaces the one below
    // it, never stacks on it. The latch stands from the first level on, so
    // UpgradePurchased holds at level 1 and above; the upgrade is owned at its
    // last level.
    [CreateAssetMenu(menuName = "Garage Band Idle/Upgrade")]
    public class UpgradeDefinition : Definition
    {
        // Null refuses the buy, exactly like a generator's availableWhen.
        [SerializeReference, SubclassPicker] public Condition gate;

        // Optional; null is absent. While it holds the upgrade is owned with
        // no latch: a purchase priced in a tier's currency whose payload sets a
        // chapter flag declares ownedWhen on that flag, so it reads as owned
        // after the tier resets and is never bought again (design doc 4).
        // Never authored on a levelled upgrade (12.12).
        [SerializeReference, SubclassPicker] public Condition ownedWhen;

        public CurrencyDefinition costCurrency;
        public BigNumber cost;                  // zero is legal content
        public List<Effect> effects = new();
        [SerializeReference, SubclassPicker] public List<GameAction> actions = new();

        // Empty for a one-purchase upgrade. When authored, the top-level cost,
        // effects, description, actions and ownedWhen are not (12.12).
        public List<UpgradeLevel> levels = new();

        // Optional; null is absent. The glyph the upgrade's tile draws in
        // place of the shared guitar pick (12.11); the importer resolves the
        // authored file name stem under UI/Icons to this reference.
        public Texture2D icon;

        public bool IsLevelled => levels.Count > 0;

        public bool IsOffered(GameContext declaringCtx) =>
            gate != null && gate.Evaluate(declaringCtx);

        // The ownership rule, the one place it is stated (12.6): the purchase
        // latch at the declaring scope, or ownedWhen holding there - the
        // context the gate is asked in. A levelled upgrade is owned at its last
        // level, since only then is there nothing left to buy. Every reader of
        // "owned" comes through here, by Purchasing.IsOwned from any scope on
        // the chain; the effects alone read the bare facts (EffectLink).
        public bool IsOwned(GameContext declaringCtx)
        {
            if (IsLevelled)
                return LevelAt(declaringCtx.Scope) >= levels.Count;
            return declaringCtx.Scope.purchasedUpgrades.Contains(Id)
                || (ownedWhen != null && ownedWhen.Evaluate(declaringCtx));
        }

        // The level owned at the declaring scope: zero before the first buy,
        // and always zero for an upgrade with no levels.
        public int LevelAt(ScopeState declaring) =>
            declaring.upgradeLevels.TryGetValue(Id, out var level) ? level : 0;

        // The level the next purchase buys at the declaring scope. Asking at
        // the last level, or of an upgrade with no levels, is a caller bug:
        // there is nothing next to price or run.
        public UpgradeLevel NextLevel(ScopeState declaring)
        {
            var owned = LevelAt(declaring);
            if (!IsLevelled || owned >= levels.Count)
                throw new InvalidOperationException(
                    $"Upgrade '{Id}' has no next level at '{declaring.ScopeId}' (owned {owned} of {levels.Count}).");
            return levels[owned];
        }

        // The authored price of the next purchase, before the cost factor: the
        // next level's cost, or the upgrade's own.
        public BigNumber NextBaseCost(ScopeState declaring) =>
            IsLevelled ? NextLevel(declaring).cost : cost;

        // What the next purchase runs: the next level's actions, or the
        // upgrade's own.
        public List<GameAction> NextActions(ScopeState declaring) =>
            IsLevelled ? NextLevel(declaring).actions : actions;
    }
}
