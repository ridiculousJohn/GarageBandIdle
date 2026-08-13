using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Runtime home of a chapter's upgrades. Implements the general content-unlock
    // mechanism (design doc sections 2 and 4): a contentUnlock upgrade whose gate
    // is met applies its payload - setFlag latches the named flag in the single
    // reveal registry, which sections and other gates observe. Buffs are never
    // auto-applied: they are bought through TryBuy, which charges the declared
    // cost currency and grants the payload. Gate conditions are validated by the
    // boot validation pass (ContentValidator), not here.
    public class UpgradeSystem : IModifierFactSource
    {
        private readonly List<Upgrade> _upgrades = new();
        private readonly Dictionary<string, Upgrade> _byId = new();
        private readonly ICurrencies _currencies;
        private readonly EffectContext _effectContext;

        // fires once per upgrade when its payload is applied
        public event Action<Upgrade> UpgradeApplied;

        // fires once per upgrade when a run reset clears its purchase latch, the
        // counterpart to UpgradeApplied: a row that hid itself when bought offers
        // the buff again. Kept separate because "acquired" and "available to buy
        // again" are different facts, and a subscriber may care about only one.
        public event Action<Upgrade> UpgradeCleared;

        public IReadOnlyList<Upgrade> All => _upgrades;

        public UpgradeSystem(IEnumerable<UpgradeDefinition> definitions, ICurrencies currencies,
            FlagSystem flags, ModifierSystem modifiers)
        {
            _currencies = currencies;
            _effectContext = new EffectContext(currencies, flags, modifiers);

            foreach (var definition in definitions)
            {
                if (definition == null)
                {
                    Debug.LogError("UpgradeSystem: chapter upgrade list contains a null entry. Skipping it.");
                    continue;
                }
                if (string.IsNullOrEmpty(definition.Id))
                {
                    Debug.LogError($"UpgradeSystem: UpgradeDefinition asset '{definition.name}' has an empty id. Skipping it.");
                    continue;
                }
                if (_byId.TryGetValue(definition.Id, out var existing))
                {
                    Debug.LogError($"UpgradeSystem: duplicate upgrade id '{definition.Id}' on assets '{definition.name}' and '{existing.Definition.name}'. Keeping '{existing.Definition.name}'.");
                    continue;
                }

                if (!string.IsNullOrEmpty(definition.CostCurrencyId))
                    _currencies.ValidateReference(definition.CostCurrencyId, $"Upgrade '{definition.Id}' (cost)");
                ValidatePayload(definition);

                var upgrade = new Upgrade(definition, modifiers);
                _upgrades.Add(upgrade);
                _byId.Add(definition.Id, upgrade);
            }
        }

        public Upgrade Get(string id)
        {
            if (_byId.TryGetValue(id, out var upgrade))
                return upgrade;

            Debug.LogError($"UpgradeSystem: unknown upgrade id '{id}'.");
            return null;
        }

        // applies any content unlock whose gate now holds; called on tick and
        // after purchases (an ownedCount gate can trip mid-tick)
        public void EvaluateContentUnlocks(ConditionContext context)
        {
            foreach (var upgrade in _upgrades)
            {
                if (upgrade.Applied)
                    continue;
                if (upgrade.Definition.Type != UpgradeType.ContentUnlock)
                    continue;
                if (!ConditionEvaluator.IsMet(upgrade.Definition.Gate, context))
                    continue;

                Apply(upgrade);
            }
        }

        // Whether the UI should offer this buff: its gate holds and it has not
        // been bought yet. Affordability is deliberately separate so a row can
        // show a priced, disabled button rather than vanishing.
        public bool IsAvailable(Upgrade upgrade, ConditionContext context)
            => upgrade != null
               && upgrade.Definition.Type == UpgradeType.Buff
               && !upgrade.Applied
               && ConditionEvaluator.IsMet(upgrade.Definition.Gate, context);

        public bool CanAfford(Upgrade upgrade)
            => upgrade != null
               && upgrade.Definition.CostAmount > 0
               && !string.IsNullOrEmpty(upgrade.Definition.CostCurrencyId)
               && _currencies.Get(upgrade.Definition.CostCurrencyId) >= (BigNumber)upgrade.Definition.CostAmount;

        // Buys one buff: charges the declared cost currency and grants the
        // payload. Every refusal is silent except the ones that mean broken
        // content, because the UI calls this on a button press.
        public bool TryBuy(Upgrade upgrade, ConditionContext context)
        {
            if (upgrade == null)
                return false;

            // content unlocks are free and apply on their gate; buying one is a
            // caller mistake, not a rejected purchase
            if (upgrade.Definition.Type != UpgradeType.Buff)
            {
                Debug.LogError($"UpgradeSystem: TryBuy on '{upgrade.Definition.Id}', which is a {upgrade.Definition.Type} - only buffs are bought.");
                return false;
            }

            if (upgrade.Applied)
                return false;
            if (!ConditionEvaluator.IsMet(upgrade.Definition.Gate, context))
                return false;

            // Fail closed on broken content (boot validation reports all of it):
            // never charge for a purchase that would grant nothing, and never let
            // a missing price or currency become an endless free purchase. A buff
            // may coherently be all-payload, all-contributions, all-actions or any
            // mix - but an action ENTRY is not a grant: a null slot or an award of
            // nothing must not become a charged no-op, so each action is asked
            // whether it would actually execute, before any state moves.
            var payload = upgrade.Definition.Payload;
            var anyExecutableAction = false;
            foreach (var action in upgrade.Definition.Actions)
                anyExecutableAction |= action != null && action.CanExecute(_effectContext);
            if (!upgrade.Definition.GrantsAnything && !anyExecutableAction)
            {
                Debug.LogError($"UpgradeSystem: upgrade '{upgrade.Definition.Id}' has no payload and no executable action. Refusing the purchase rather than charging for nothing.");
                return false;
            }

            var cost = (BigNumber)upgrade.Definition.CostAmount;
            if (cost <= BigNumber.Zero)
                return false;

            var currencyId = upgrade.Definition.CostCurrencyId;
            if (string.IsNullOrEmpty(currencyId))
                return false;
            if (_currencies.Get(currencyId) < cost)
                return false;

            // state, then notify: the latch and the effect settle before the
            // spend fires BalanceChanged, so no condition evaluator or UI
            // subscriber observes the money gone with the buff not yet granted
            upgrade.MarkApplied();
            payload?.Apply(_effectContext, upgrade.Definition.Scope);

            // The one-shot awards, and this is the ONLY line in the system that
            // runs them: a purchase is a player action, re-buying re-pays because
            // TryBuy re-charged. The auto-apply path (Apply below) never reads
            // Actions, so a payout on a content unlock cannot run - there is no
            // code on that path to run it.
            foreach (var action in upgrade.Definition.Actions)
                action?.Execute(_effectContext);

            _currencies.Add(currencyId, -cost);
            UpgradeApplied?.Invoke(upgrade);
            return true;
        }

        // Run reset (album release, design doc section 5): a run-scoped buff is
        // re-bought each run, so its purchase latch clears; a content unlock is
        // permanent within the chapter and keeps its latch, which is what leaves
        // flags set and content revealed across demos. Scope decides, never the
        // type and never a name list, so an upgrade added later resets according
        // to what it declares.
        //
        // This clears the LATCH only, and the latch is the fact. The effects
        // those purchases granted are rebuilt by re-projecting from the latches
        // that survive (EconomyContext.ProjectModifiers), never edited in place,
        // so the release still does not have to know what any payload did - it
        // resets facts and asks for the projection again.
        //
        // Every latch settles before any notification fires, so no subscriber sees
        // one buff on offer again while another still reads as bought (state, then
        // notify). Returns whether anything changed, so a no-op reset stays silent.
        public bool ResetRunScoped()
        {
            List<Upgrade> cleared = null;

            foreach (var upgrade in _upgrades)
            {
                if (upgrade.Definition.Scope != ContentScope.Run)
                    continue;
                if (!upgrade.ClearApplied())
                    continue;

                cleared ??= new List<Upgrade>();
                cleared.Add(upgrade);
            }

            if (cleared == null)
                return false;

            foreach (var upgrade in cleared)
                UpgradeCleared?.Invoke(upgrade);
            return true;
        }

        // Restore (save load, event-sandbox seeding): REPLACES the whole latch set.
        // Every upgrade named ends up applied and every currently-applied upgrade
        // the snapshot omits ends up cleared, for the same reason FlagSystem.Restore
        // replaces rather than merges - a merge leaves a previous restore's latches
        // standing under a different snapshot, which is two routes to one state.
        //
        // This restores FACTS only. Nothing here re-applies a payload: the caller
        // re-projects afterwards (EconomyContext.Restore), which is what turns
        // these latches back into effects. A load can never re-pay an award,
        // because awards are GameActions on the purchase moment - no restore or
        // projection path executes one.
        //
        // Notifications are deliberately absent even when notify is true: a restored
        // latch is not an acquisition and not a loss of one, exactly as
        // ProjectModifiers refuses to re-fire UpgradeApplied. The parameter exists so
        // the signature matches the other primitives and so a caller reading the
        // restore sequence sees the same shape everywhere; the UI re-reads on the
        // settled signal.
        public void RestoreApplied(IReadOnlyCollection<string> appliedIds, bool notify = true)
        {
            if (appliedIds == null)
            {
                Debug.LogError("UpgradeSystem: RestoreApplied with no saved latches. Ignoring - clearing every latch was more likely a missing snapshot than an authored empty one.");
                return;
            }

            var wanted = new HashSet<string>();
            foreach (var id in appliedIds)
            {
                if (string.IsNullOrEmpty(id))
                    continue;
                if (!_byId.ContainsKey(id))
                {
                    Debug.LogError($"UpgradeSystem: RestoreApplied names unknown upgrade id '{id}'. Skipping it - stale saved state naming an upgrade this chapter does not list.");
                    continue;
                }
                wanted.Add(id);
            }

            foreach (var upgrade in _upgrades)
            {
                if (wanted.Contains(upgrade.Definition.Id))
                    upgrade.MarkApplied();
                else
                    upgrade.ClearApplied();
            }
        }

        // The latched upgrade ids, for a capture. Ordered by the chapter's
        // declaration order like every other projection input, so two captures of
        // one state are identical.
        public IReadOnlyCollection<string> CaptureApplied()
        {
            var applied = new List<string>();
            foreach (var upgrade in _upgrades)
            {
                if (upgrade.Applied)
                    applied.Add(upgrade.Definition.Id);
            }
            return applied;
        }

        // The declared lifetime of an upgrade's latch, for the snapshot filter that
        // builds an event sandbox's seed - the same question FlagSystem.IsRunScoped
        // answers, and re-derived from content for the same reason.
        public ContentScope ScopeOf(string upgradeId)
            => _byId.TryGetValue(upgradeId ?? "", out var upgrade) ? upgrade.Definition.Scope : ContentScope.None;

        public string FactSourceName => "upgrade purchase latches";

        // The projection (design doc section 12, rule 6): every latched upgrade
        // re-applies its payload, so the modifier store is rebuilt from the
        // latches rather than from a memory of what was granted. Notifications
        // are deliberately NOT re-fired - UpgradeApplied means "just acquired",
        // and a projection is not an acquisition; a row that hid itself when
        // bought is already hidden. Nothing latches or unlatches here, which is
        // what makes this safe to run at any boundary.
        //
        // Re-running Apply is safe for ANY payload, by construction: an effect is
        // re-applicable state by definition of being a GameEffect, and the awards
        // a purchase paid are GameActions this method cannot see - so a latch
        // that survives a release rebuilds its buffs and can never pay again.
        public void ProjectModifiers()
        {
            foreach (var upgrade in _upgrades)
            {
                if (!upgrade.Applied)
                    continue;

                // a missing payload was already reported when the latch was set;
                // a projection repeating it every boundary would be noise
                upgrade.Definition.Payload?.Apply(_effectContext, upgrade.Definition.Scope);
            }
        }

        private void Apply(Upgrade upgrade)
        {
            // State, then notify - the same order TryBuy uses, and for a sharper
            // reason here: a setFlag payload fires FlagSet from inside Apply, so
            // anything that re-evaluates content unlocks on that signal would
            // otherwise observe this upgrade as unapplied and grant it twice.
            upgrade.MarkApplied();

            var payload = upgrade.Definition.Payload;
            if (payload == null)
            {
                // Already latched, so a content mistake reports once, not per tick.
                // Contributions alone are a complete grant - the latch is what makes
                // them live - so only an upgrade granting neither is broken.
                if (!upgrade.Definition.GrantsAnything)
                    Debug.LogError($"UpgradeSystem: upgrade '{upgrade.Definition.Id}' has no payload and no contributions. Nothing to apply.");
            }
            else
            {
                // The upgrade's declared scope travels with the grant, so the
                // effect's lifetime is never a second declaration. Actions are
                // deliberately NOT executed here: this path re-fires whenever the
                // gate holds and the latch is absent (a release or restore can
                // clear it), so a one-shot award run from here would pay again -
                // awards run only from TryBuy, the moment that charges for them.
                payload.Apply(_effectContext, upgrade.Definition.Scope);
            }

            UpgradeApplied?.Invoke(upgrade);
        }

        private void ValidatePayload(UpgradeDefinition definition)
        {
            if (definition.Type != UpgradeType.ContentUnlock)
                return;

            // content unlocks apply on their gate and are never bought, so a
            // price on one would never be charged - only buffs go through TryBuy
            if (definition.CostAmount > 0)
                Debug.LogError($"UpgradeSystem: content unlock '{definition.Id}' has a non-zero cost, but content unlocks are applied automatically. Its cost will be ignored.");
        }
    }
}
