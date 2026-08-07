using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // Module: a tap button. The label advertises what a tap actually grants -
    // ProductionSystem.TapValue for THIS module's producer, its cash config times
    // the multiplier stacks - and refreshes whenever that producer's tap value
    // moves (reward applied, run reset).
    //
    // Which producer it fires comes from the section's module entry
    // (definitionId), not from a constant here and not from "every tap config in
    // the chapter". Chapter 1 authors one tap surface, so nothing observable
    // changes; what changes is that a second one - a Merch/Sell button - is a
    // producer asset and a module entry rather than a rewrite of what a tap means.
    public class TapModule : MonoBehaviour, IChapterModule
    {
        [SerializeField] private Button _jamButton;
        [SerializeField] private TextMeshProUGUI _jamLabel;

        private ChapterContext _context;
        private CurrencyDefinition _cashDefinition;
        private string _producerId;

        // a producer that can actually be tapped: naming the passive band producer
        // would resolve and still pay nothing, so the requirement is the narrower one
        public ModuleDefinitionKind RequiredDefinition => ModuleDefinitionKind.TapProducer;

        public void Initialize(ChapterContext context, string definitionId)
        {
            _context = context;
            _producerId = definitionId;

            // A tap button with no producer named would fire nothing and advertise
            // zero, which reads as a tuning problem rather than a missing module
            // entry - so it reports here, where the section and the address are
            // still in hand.
            if (string.IsNullOrEmpty(_producerId))
                Debug.LogError("TapModule: no producer id on this module's section entry - the button would pay nothing. Author it as the module entry's definitionId.");
            else if (!context.Economy.Production.HasTapProducer(_producerId))
                Debug.LogError($"TapModule: producer '{_producerId}' authors no tap configs in chapter '{context.Chapter?.Id}'. The button would pay nothing.");

            _cashDefinition = context.Economy.Currencies.GetDefinition(GameManager.CashCurrencyId);
            RefreshLabel();
            context.Economy.Production.TapValueChanged += HandleTapValueChanged;
            _jamButton.onClick.AddListener(HandleJamClicked);
        }

        private void OnDestroy()
        {
            if (_context != null)
                _context.Economy.Production.TapValueChanged -= HandleTapValueChanged;
            _jamButton.onClick.RemoveListener(HandleJamClicked);
        }

        // the event carries which producer moved, so a chapter with two tap
        // surfaces never redraws one because the other changed
        private void HandleTapValueChanged(string producerId)
        {
            if (producerId == _producerId)
                RefreshLabel();
        }

        private void RefreshLabel()
        {
            var value = _context.Economy.Production.TapValue(_producerId);
            _jamLabel.text = $"JAM\n<size=44>+{NumberFormatter.Format(value, _cashDefinition)} per tap</size>";
        }

        private void HandleJamClicked()
        {
            _context.Economy.Jam(_producerId);
        }
    }
}
