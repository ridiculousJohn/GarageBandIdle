using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // A module-held production source (design doc section 12, rule 13): the
    // authored data behind an engagement surface like the Jam button - the
    // production configs it fires. Only generators idle-pay (section 9), and they
    // hold their config as produces/baseOutput; a producer's output never
    // idle-pays, by construction - a tap cannot fire while nobody taps, and its
    // tick configs run only while the economy is focused. Discovered by
    // Addressables label like every definition.
    //
    // It deliberately does NOT name a module. It used to carry a ModuleAddress,
    // which was validated and read by nothing - the binding ran the other way, from
    // a section's module entry naming the producer, and 6.5 made that entry the
    // thing the runtime actually fires. Keeping both would be two declarations of
    // one relationship, able to disagree with no way to tell which was meant.
    // "Nothing presents this producer" is now derived: no section entry names it
    // (boot validation reports that for a TAP producer, which cannot fire without
    // a surface, and allows it for a passive one like the band).
    [CreateAssetMenu(
        fileName = "NewProducer",
        menuName = "GarageBandIdle/Producer")]
    public class ProducerDefinition : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Stable string id. Never rename once saves exist.")]
        private string _id;

        [SerializeField]
        private List<ProductionConfig> _production = new();

        public string Id => _id;
        public IReadOnlyList<ProductionConfig> Production => _production;

        // Whether this producer authors any tap-triggered config. Asked by boot
        // validation, which requires a tap producer to be presented by some section
        // module entry - a tap surface nobody can press is dead content - while a
        // purely passive producer (the band's fan accrual) needs no surface at all.
        public bool HasTapConfigs
        {
            get
            {
                foreach (var config in _production)
                {
                    if (config != null && config.Trigger == ProductionTrigger.Tap)
                        return true;
                }
                return false;
            }
        }

#if UNITY_EDITOR
        // importer-only: producer assets are generated from chapter JSON
        public void EditorInitialize(string id, List<ProductionConfig> production)
        {
            _id = id;
            _production = production;
        }
#endif
    }
}
