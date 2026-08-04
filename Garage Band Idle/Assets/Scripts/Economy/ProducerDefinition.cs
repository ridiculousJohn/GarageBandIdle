using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // A module-held production source (design doc section 12, rule 13): the
    // authored data behind an engagement surface like the Jam button - which
    // module prefab presents it, and the production configs it fires. Only
    // generators idle-pay (section 9), and they hold their config as
    // produces/baseOutput; a producer's output never idle-pays, by
    // construction - a tap cannot fire while nobody taps, and its tick
    // configs run only while the economy is focused. Discovered by
    // Addressables label like every definition.
    [CreateAssetMenu(
        fileName = "NewProducer",
        menuName = "GarageBandIdle/Producer")]
    public class ProducerDefinition : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Stable string id. Never rename once saves exist.")]
        private string _id;

        [SerializeField]
        [Tooltip("Addressable address of the module prefab presenting this producer, e.g. module/tap.")]
        private string _moduleAddress;

        [SerializeField]
        private List<ProductionConfig> _production = new();

        public string Id => _id;
        public string ModuleAddress => _moduleAddress;
        public IReadOnlyList<ProductionConfig> Production => _production;

#if UNITY_EDITOR
        // importer-only: producer assets are generated from chapter JSON
        public void EditorInitialize(string id, string moduleAddress, List<ProductionConfig> production)
        {
            _id = id;
            _moduleAddress = moduleAddress;
            _production = production;
        }
#endif
    }
}
