using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // Module: one prestige rung's button - the Cut a Demo release and the
    // capstone offer are the same module parameterized by rung id (the section
    // entry's definitionId), which is what being two instances of one shape
    // means for presentation. The label advertises what pressing right now
    // would bank - Scope.PendingRungGrant, the press's own resolved plan - so
    // the preview and the payout cannot disagree, and a capstone-shaped rung's
    // preview includes the album payout it implicitly banks.
    //
    // Pressability is the rung's authored offer, asked through the one
    // evaluator like every gate, and completion disarms it for good - the
    // celebration is whatever content gates on the latched flag, not this
    // button. The press itself re-asks the operation gate fail-closed, so a
    // stale row can never complete anything.
    //
    // The serialized fields carry both retired modules' names
    // (FormerlySerializedAs), so the release and capstone prefabs keep their
    // references across the script swap without either being re-authored.
    public class PrestigeModule : MonoBehaviour, IChapterModule
    {
        [SerializeField]
        [FormerlySerializedAs("_releaseButton")]
        [FormerlySerializedAs("_completeButton")]
        private Button _button;

        [SerializeField]
        [FormerlySerializedAs("_releaseLabel")]
        [FormerlySerializedAs("_completeLabel")]
        private TextMeshProUGUI _label;

        private ChapterContext _context;
        private string _rungId;

        // presents one rung, named by its section entry - a dead button is a
        // content mistake boot validation reports by this kind
        public ModuleDefinitionKind RequiredDefinition => ModuleDefinitionKind.PrestigeRung;

        public void Initialize(ChapterContext context, string definitionId)
        {
            _context = context;
            _rungId = definitionId;
            RefreshLabel();
            context.Economy.Conditions.Settled += RefreshLabel;
            _button.onClick.AddListener(HandlePressed);
        }

        private void OnDestroy()
        {
            if (_context != null)
                _context.Economy.Conditions.Settled -= RefreshLabel;
            _button.onClick.RemoveListener(HandlePressed);
        }

        private void RefreshLabel()
        {
            // a section can hand this module a rung its scope does not file;
            // disarmed rather than armed-and-erroring, boot validation names it
            if (!_context.Economy.Prestige.TryGet(_rungId, out var rung))
            {
                _button.interactable = false;
                return;
            }

            // Every currency the press's plan would pay, named by its own
            // definition - the module hardcodes no currency, because a rung
            // banks whatever its ladder authors (an intermediate rung pays an
            // intermediate currency, and "+0 Records" on it would be a lie).
            var parts = new List<string>();
            foreach (var grant in _context.Economy.PendingRungGrants(_rungId))
            {
                var definition = _context.Economy.Currencies.GetDefinition(grant.CurrencyId);
                parts.Add($"+{NumberFormatter.Format(grant.Amount, definition)} {definition?.DisplayName ?? grant.CurrencyId}");
            }
            _label.text = $"{rung.DisplayName.ToUpperInvariant()}\n<size=44>{string.Join("  ", parts)}</size>";

            // the offer's gate, re-asked on every settle - none authored means
            // always offered. A completed rung stays visible but disarmed: the
            // region outlives the one press it exists for, while the latched
            // flag is what reveals what comes next.
            _button.interactable = !_context.Economy.Prestige.IsCompleted(rung)
                && ConditionEvaluator.IsMet(rung.Offer, _context.Economy.Conditions);
        }

        private void HandlePressed()
        {
            // the economy this module displays is the one it presses - never
            // "whatever has focus", which could be a different context entirely
            _context.Economy.CompleteRung(_rungId);
        }
    }
}
