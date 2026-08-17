using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Loop;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // Composes the chapter's screen from data: for each SectionDefinition the
    // chapter names, instantiates its module prefabs (addressable, by address)
    // under the canvas root and initializes them through IChapterModule.
    //
    // A section is visible exactly while its visibleWhen holds, re-evaluated
    // each settle - the same live shape BarListModule uses for bar groups. No
    // latch lives here (one briefly did, twice): "stays once earned" is a
    // property of the STATE a condition reads, so it is authored by gating on
    // a fact with that lifetime - a flag, whose declaration says whether a
    // release clears it, or a monotonic earned-total - never remembered by
    // the UI. That is what keeps visibility derivable: a release resets facts
    // and this screen just reads the new answers.
    public class ChapterScreen : MonoBehaviour
    {
        [SerializeField] private RectTransform _sectionsRoot;

        private class SectionInstance
        {
            public SectionDefinition Definition;
            public readonly List<GameObject> Modules = new();
        }

        private GameManager _game;
        private Scope _economy;
        private ChapterContext _context;
        private readonly List<SectionInstance> _sections = new();

        private void Start()
        {
            _game = GameManager.Instance;

            // the screen shows the chapter being played forward, so it binds the
            // frontier economy specifically rather than "whatever has focus" -
            // an event sandbox taking focus (slice 8) does not repaint this
            _economy = _game?.Frontier;
            if (_economy == null)
            {
                // GameManager already logged the missing-content error
                return;
            }

            _context = new ChapterContext(_economy);

            foreach (var section in _economy.Sections)
                _sections.Add(BuildSection(section));

            if (_sections.Count == 0)
                Debug.LogError($"ChapterScreen: chapter '{_economy.Chapter.Id}' has no sections - nothing to show. Re-run the chapter import.");

            // One subscription for every condition input there is: the context
            // publishes once a drain has settled the mutation, so a visibleWhen
            // is never asked about half-applied state and this screen has no
            // list of inputs to keep in step with the Condition vocabulary.
            _economy.Conditions.Settled += HandleConditionsSettled;

            // GameManager settles the frontier once at boot, so the opening
            // evaluation reads latched unlocks rather than pre-drain state
            EvaluateSections();
        }

        private void OnDestroy()
        {
            if (_economy?.Conditions != null)
                _economy.Conditions.Settled -= HandleConditionsSettled;

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

            foreach (var entry in definition.Modules)
            {
                var address = entry?.Address;
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

                // initialize even when starting hidden so event subscriptions
                // are live before the section shows. The entry's definitionId
                // travels with it, so a prefab used twice in one chapter presents
                // two different things.
                if (instance.TryGetComponent<IChapterModule>(out var module))
                    module.Initialize(_context, entry?.DefinitionId);
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
                var visible = ConditionEvaluator.IsMet(section.Definition.VisibleWhen, _economy.Conditions);
                foreach (var module in section.Modules)
                {
                    if (module != null && module.activeSelf != visible)
                        module.SetActive(visible);
                }
            }
        }
    }
}
