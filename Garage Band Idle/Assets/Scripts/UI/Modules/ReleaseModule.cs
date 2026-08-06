using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // Module: the Cut a Demo (album release) button. The label advertises what
    // releasing right now would bank - EconomyContext.PendingReleaseRecords,
    // the payout formula over the current fans balance - and refreshes when
    // conditions settle, since fans accrue every tick. Reading the context's
    // one pending-payout home is what keeps this preview and the release
    // itself from ever disagreeing about what a demo is worth.
    public class ReleaseModule : MonoBehaviour, IChapterModule
    {
        [SerializeField] private Button _releaseButton;
        [SerializeField] private TextMeshProUGUI _releaseLabel;

        private ChapterContext _context;
        private CurrencyDefinition _recordsDefinition;

        public void Initialize(ChapterContext context)
        {
            _context = context;
            _recordsDefinition = context.Economy.Currencies.GetDefinition(
                context.Economy.Conditions.RecordsCurrencyId);
            RefreshLabel();
            context.Economy.Conditions.Settled += RefreshLabel;
            _releaseButton.onClick.AddListener(HandleReleaseClicked);
        }

        private void OnDestroy()
        {
            if (_context != null)
                _context.Economy.Conditions.Settled -= RefreshLabel;
            _releaseButton.onClick.RemoveListener(HandleReleaseClicked);
        }

        private void RefreshLabel()
        {
            var pending = _context.Economy.PendingReleaseRecords();
            _releaseLabel.text = $"CUT A DEMO\n<size=44>+{NumberFormatter.Format(pending, _recordsDefinition)} Records</size>";

            // The offer's gate (design doc section 5): pressable only while the
            // album's unlock holds, asked through the one evaluator like every
            // gate - none authored means always offered. Since the inputs are
            // run facts, this re-arms each run; the REGION meanwhile follows
            // the permanent album flag its section gates on. The release
            // OPERATION is deliberately ungated - the capstone cuts an album
            // regardless of any offer.
            _releaseButton.interactable = ConditionEvaluator.IsMet(
                _context.Chapter.Album.ReleaseWhen, _context.Economy.Conditions);
        }

        private void HandleReleaseClicked()
        {
            // the economy this module displays is the one it releases - never
            // "whatever has focus", which could be a different context entirely
            _context.Economy.ReleaseAlbum();
        }
    }
}
