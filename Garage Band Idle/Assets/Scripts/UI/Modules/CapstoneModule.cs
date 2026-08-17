using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // Module: the chapter capstone offer ("Play the Backyard Party"). The label
    // advertises what completing right now would bank - the capstone implicitly
    // cuts an album, so the preview is Scope.PendingReleaseRecords,
    // the same one pending-payout home the Release button reads, which is what
    // keeps the two offers from ever disagreeing about what the run is worth.
    // Pressability is the capstone's own authored unlock, asked through the one
    // evaluator like every gate; after completion the button stays dark - the
    // celebration is the story beat card, revealed by the completion flag its
    // section gates on (slice 10).
    public class CapstoneModule : MonoBehaviour, IChapterModule
    {
        [SerializeField] private Button _completeButton;
        [SerializeField] private TextMeshProUGUI _completeLabel;

        private ChapterContext _context;
        private CurrencyDefinition _recordsDefinition;

        // presents the chapter's capstone config, not a definition asset, so
        // its section entry names none
        public ModuleDefinitionKind RequiredDefinition => ModuleDefinitionKind.None;

        public void Initialize(ChapterContext context, string definitionId)
        {
            _context = context;
            _recordsDefinition = context.Economy.Currencies.GetDefinition(
                context.Economy.Conditions.RecordsCurrencyId);
            RefreshLabel();
            context.Economy.Conditions.Settled += RefreshLabel;
            _completeButton.onClick.AddListener(HandleCompleteClicked);
        }

        private void OnDestroy()
        {
            if (_context != null)
                _context.Economy.Conditions.Settled -= RefreshLabel;
            _completeButton.onClick.RemoveListener(HandleCompleteClicked);
        }

        private void RefreshLabel()
        {
            // a section can hand this module a chapter that authors no
            // capstone; disarmed rather than armed-and-erroring, because an
            // unauthored capstone has a null unlock and a null gate means
            // "always met" to the evaluator
            var capstone = _context.Chapter.Capstone;
            if (capstone == null || !capstone.IsAuthored)
            {
                _completeButton.interactable = false;
                return;
            }

            var pending = _context.Economy.PendingReleaseRecords();
            _completeLabel.text = $"{capstone.DisplayName.ToUpperInvariant()}\n<size=44>+{NumberFormatter.Format(pending, _recordsDefinition)} Records</size>";

            // the offer's gate is the capstone's authored unlock, re-asked on
            // every settle; a completed capstone stays visible but disarmed,
            // since the region outlives the one press it exists for while the
            // completion flag is what reveals what comes next
            _completeButton.interactable = !_context.Economy.Capstone.IsCompleted
                && ConditionEvaluator.IsMet(capstone.Unlock, _context.Economy.Conditions);
        }

        private void HandleCompleteClicked()
        {
            // the economy this module displays is the one it completes - never
            // "whatever has focus", which could be a different context entirely
            _context.Economy.CompleteCapstone();
        }
    }
}
