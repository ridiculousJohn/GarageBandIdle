using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // An authored bundle of production contributions (design doc section 12, rule
    // 13): the data behind an engagement surface like the Jam button, plus the
    // passive sources nothing presents at all. Its lines feed the currency
    // producers they name, exactly as a generator's do; what differs is only that
    // these amounts are flat, scaled by no owned count. Discovered by Addressables
    // label like every definition.
    //
    // It deliberately does NOT name a module. It used to carry a ModuleAddress,
    // which was validated and read by nothing - the binding ran the other way, from
    // a section's module entry naming the producer, and 6.5 made that entry the
    // thing the runtime actually fires. Keeping both would be two declarations of
    // one relationship, able to disagree with no way to tell which was meant.
    // "Nothing presents this producer" is now derived: no section entry names it
    // (boot validation reports that for one holding a YIELD line, which cannot pay
    // without a surface to fire it, and allows it for a purely passive one like the
    // band).
    //
    // The name is now a half-truth worth flagging: under rule 13 the PRODUCER is
    // the per-currency thing (CurrencyProducer), and this is one of its
    // contributors. Renaming the asset family is a mechanical pass over the
    // Addressables label, the folder and the chapter's id list, deliberately not
    // folded into this changeset.
    [CreateAssetMenu(
        fileName = "NewProducer",
        menuName = "GarageBandIdle/Producer")]
    public class ProducerDefinition : Definition
    {
        [SerializeField]
        [Tooltip("Flat lines this authors. Amounts are per second for a rate, per firing for a yield.")]
        private List<ProductionContribution> _contributions = new();

        public IReadOnlyList<ProductionContribution> Contributions => _contributions;

        // Whether this authors anything a firing would pay. Asked by boot
        // validation, which requires such a producer to be presented by some
        // section module entry - a surface nobody can press is dead content - while
        // a purely passive one (the band's fan accrual) needs no surface at all.
        //
        // It reads the QUANTITY rather than a trigger, which is the whole of rule
        // 13's correction: a yield is per firing and a rate is per second, and
        // nothing here asks what did the firing.
        public bool HasYieldContributions
        {
            get
            {
                foreach (var contribution in _contributions)
                {
                    if (contribution != null && contribution.Feeds == ProductionFeed.Yield)
                        return true;
                }
                return false;
            }
        }

#if UNITY_EDITOR
        // importer-only: producer assets are generated from chapter JSON
        public void EditorInitialize(string id, List<ProductionContribution> contributions)
        {
            SetIdentity(id);
            _contributions = contributions ?? new List<ProductionContribution>();
        }
#endif
    }
}
