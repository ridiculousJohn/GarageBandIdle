using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Economy;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // Module: a tap button. Pressing it FIRES the contributor its section entry
    // names, and every currency that contributor feeds a yield to pays out. The
    // label advertises the cash half of that - cash's composed yield, which is the
    // base line plus anything else contributing to it (stage_presence) times the
    // multipliers reaching it - and refreshes whenever that yield moves (buff
    // bought, reward applied, run reset).
    //
    // "Tap" is this module's word and stops here: below the button the economy
    // knows only that something fired (design doc section 12, rule 13), which is
    // what lets an automation or a story beat fire the same surface later without
    // production learning a second trigger.
    //
    // Which contributor it fires comes from the section's module entry
    // (definitionId), not from a constant here. Chapter 1 authors one surface, so
    // nothing observable changes; what changes is that a second one - a Merch/Sell
    // button - is a producer asset and a module entry rather than a rewrite of what
    // firing means.
    public class TapModule : MonoBehaviour, IChapterModule
    {
        [SerializeField] private Button _jamButton;
        [SerializeField] private TextMeshProUGUI _jamLabel;

        private ChapterContext _context;
        private string _contributorId;

        // the currencies firing this contributor pays into, asked once at bind: the
        // set changes only with the chapter's content, never with a purchase or a
        // gate. Held with their definitions so the label formats each in its own
        // currency rather than assuming one.
        private readonly List<CurrencyDefinition> _paid = new();

        // a producer that can actually be fired: naming the passive band producer
        // would resolve and still pay nothing, so the requirement is the narrower one
        public ModuleDefinitionKind RequiredDefinition => ModuleDefinitionKind.FireableContributor;

        public void Initialize(ChapterContext context, string definitionId)
        {
            _context = context;
            _contributorId = definitionId;

            // A button with nothing named would fire nothing and advertise zero,
            // which reads as a tuning problem rather than a missing module entry -
            // so it reports here, where the section and the address are still in
            // hand.
            if (string.IsNullOrEmpty(_contributorId))
                Debug.LogError("TapModule: no producer id on this module's section entry - the button would pay nothing. Author it as the module entry's definitionId.");
            else if (!context.Economy.Production.CanFire(_contributorId))
                Debug.LogError($"TapModule: producer '{_contributorId}' authors no yield lines in chapter '{context.Chapter?.Id}'. The button would pay nothing.");

            foreach (var currencyId in context.Economy.Production.FiredCurrencies(_contributorId))
                _paid.Add(context.Economy.Currencies.GetDefinition(currencyId));

            RefreshLabel();
            context.Economy.Production.YieldChanged += HandleYieldChanged;
            _jamButton.onClick.AddListener(HandleJamClicked);
        }

        private void OnDestroy()
        {
            if (_context != null)
                _context.Economy.Production.YieldChanged -= HandleYieldChanged;
            _jamButton.onClick.RemoveListener(HandleJamClicked);
        }

        // the event carries which CURRENCY's yield moved, so a yield this button
        // does not pay never redraws it
        private void HandleYieldChanged(string currencyId)
        {
            foreach (var currency in _paid)
            {
                if (currency != null && currency.Id == currencyId)
                {
                    RefreshLabel();
                    return;
                }
            }
        }

        // What a press is worth: the composed yield of each currency this button
        // fires, which is the CURRENCY's yield and not this contributor's share of
        // it - so a bonus another fact contributes (stage_presence's +1 Cash) shows
        // up here with nothing having to know it exists.
        //
        // A currency whose yield is currently dormant is left out rather than shown
        // as zero: the rehearsal press pays nothing before the `covers` flag, and
        // advertising "+0" reads as authored rather than gated.
        private void RefreshLabel()
        {
            var parts = new List<string>();
            foreach (var currency in _paid)
            {
                if (currency == null)
                    continue;

                var value = _context.Economy.Production.YieldOf(currency.Id);
                if (value > BigNumber.Zero)
                    parts.Add($"+{NumberFormatter.Format(value, currency)}");
            }

            var advertised = parts.Count == 0 ? "+0" : string.Join(", ", parts);
            _jamLabel.text = $"JAM\n<size=44>{advertised} per tap</size>";
        }

        private void HandleJamClicked()
        {
            _context.Economy.Fire(_contributorId);
        }
    }
}
