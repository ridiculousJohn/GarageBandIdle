using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Loop;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // Composes the chapter's screen from data: for each SectionDefinition the
    // chapter names, instantiates its module prefabs (addressable, by address)
    // under the canvas root and initializes them through IChapterModule.
    // Sections start hidden until their visibleWhen condition holds, then latch
    // visible - the design doc's progressive reveal (section 2), driven by flags
    // and the shared Condition language.
    public class ChapterScreen : MonoBehaviour
    {
        [SerializeField] private RectTransform _sectionsRoot;

        private class SectionInstance
        {
            public SectionDefinition Definition;
            public readonly List<GameObject> Modules = new();
            public bool Revealed;
        }

        private GameManager _game;
        private ChapterContext _context;
        private readonly List<SectionInstance> _sections = new();

        private void Start()
        {
            _game = GameManager.Instance;
            if (_game == null || _game.CurrentChapter == null)
            {
                // GameManager already logged the missing-content error
                return;
            }

            _context = new ChapterContext(_game, _game.CurrentChapter, _game.Flags);

            foreach (var section in _game.Sections)
                _sections.Add(BuildSection(section));

            if (_sections.Count == 0)
                Debug.LogError($"ChapterScreen: chapter '{_game.CurrentChapter.Id}' has no sections - nothing to show. Re-run the chapter import.");

            // One subscription for every condition input there is: the context
            // holds the individual signals (balances, flags, owned counts,
            // completed bars) and publishes once the drain has settled them, so
            // a visibleWhen gate is never asked about half-applied state and this
            // screen has no list of inputs to keep in step with the Condition
            // vocabulary.
            _game.Conditions.Settled += HandleConditionsSettled;

            // no drain has run yet, so the opening reveal is asked for directly
            EvaluateSections();
        }

        private void OnDestroy()
        {
            if (_game == null)
                return;

            if (_game.Conditions != null)
                _game.Conditions.Settled -= HandleConditionsSettled;

            foreach (var section in _sections)
            {
                foreach (var module in section.Modules)
                {
                    if (module != null)
                        Addressables.ReleaseInstance(module);
                }
            }
        }

        private SectionInstance BuildSection(SectionDefinition definition)
        {
            var section = new SectionInstance { Definition = definition };

            foreach (var address in definition.ModuleAddresses)
            {
                GameObject instance;
                try
                {
                    instance = Addressables.InstantiateAsync(address, _sectionsRoot).WaitForCompletion();
                }
                catch (System.Exception exception)
                {
                    Debug.LogError($"ChapterScreen: section '{definition.Id}' failed to instantiate module '{address}' - is the prefab marked addressable with that address? ({exception.Message})");
                    continue;
                }

                // initialize while still hidden so event subscriptions are live
                // before the section reveals
                if (instance.TryGetComponent<IChapterModule>(out var module))
                    module.Initialize(_context);
                else
                    Debug.LogError($"ChapterScreen: module '{address}' has no IChapterModule component on its root.");

                instance.SetActive(false);
                section.Modules.Add(instance);
            }

            return section;
        }

        private void HandleConditionsSettled() => EvaluateSections();

        private void EvaluateSections()
        {
            foreach (var section in _sections)
            {
                if (section.Revealed)
                    continue;
                if (!ConditionEvaluator.IsMet(section.Definition.VisibleWhen, _game.Conditions))
                    continue;

                section.Revealed = true;
                foreach (var module in section.Modules)
                    module.SetActive(true);
            }
        }
    }
}
