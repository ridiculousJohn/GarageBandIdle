using RidiculousGaming.GarageBandIdle.Content;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Loop;
using RidiculousGaming.Utilities;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Bootstrap, tick routing, and focus. All definition content is discovered
    // through the ContentDatabase (Addressables labels, see ContentLabels) so
    // new assets are picked up with no code or registration changes.
    //
    // What this class deliberately does NOT do is hold the economy's systems
    // (design doc section 12, rule 12). It builds the permanent pool and one
    // frontier EconomyContext through the factory, and routes the tick to
    // whichever context has focus. A second economy - an event sandbox (slice
    // 8), a cleared chapter's replay economy (rule 7) - is another Build call
    // and a focus switch, not another set of fields here.
    [RequireComponent(typeof(TickSystem))]
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance => SingletonManager.GetInstance<GameManager>();
        public static bool IsAllocated => SingletonManager.IsAllocated<GameManager>();

        // the slice's hardcoded touchpoints; these stay string ids (not fields on
        // CurrencyManager) so the currency set remains open
        public const string RecordsCurrencyId = "records";

        // the second global currency: banked by the capstone, allocated in Chapter 2.
        // A currency rather than a manager - a balance, an earned total, conditions,
        // a save block and formatting all come for free, and a second owner for one
        // number is a synchronization bug waiting for a second writer.
        public const string RoadiesCurrencyId = "roadies";

        // UI display touchpoints only (CurrencyHeaderModule/TapModule name what
        // they show); fan ACCRUAL takes its currency from the chapter's fans
        // config, and what a press pays is the jam producer's data. The playable
        // pass (slice 10) replaces these with a data-driven currency header.
        public const string CashCurrencyId = "cash";
        public const string FansCurrencyId = "fans";
        public const string FansUnlockFlagId = "fans";

        public ContentDatabase Database { get; private set; }

        // the global currencies (Records today, Roadies later): created once,
        // reset by no run operation, and the permanent save block in slice 9
        public CurrencyManager PermanentCurrencies { get; private set; }

        // the economy currently receiving the tick. Exactly one context is
        // focused at a time, which is why this is a single field rather than a
        // set: an unfocused economy accrues nothing live (rule 7).
        public EconomyContext Focused { get; private set; }

        // the chapter being played forward. Held separately from Focused because
        // focus will move to an event sandbox and back (slice 8) while the
        // frontier economy stays the thing the player is progressing.
        public EconomyContext Frontier { get; private set; }

        public ChapterDefinition CurrentChapter => Frontier?.Chapter;

        private TickSystem _tickSystem;

        private void Awake()
        {
            if (SingletonManager.DestroyIfRegistered(this))
            {
                Debug.LogWarning($"[{GetType().Name}] Attempted to create multiple instances of {GetType().Name}. Destroying this instance.");
                return;
            }

            Database = new ContentDatabase();

            // One boot pass covers every content reference - conditions,
            // payloads, rewards, module addresses - and it runs BEFORE any
            // economy is built or settled: construction settles, and that settle
            // acquires (an unlock whose gate holds at boot latches and grants
            // during Build), so a report issued after construction described a
            // mistake something had already acted on. The validator reads no
            // running system - the reward manager it takes is built from the
            // same database registry the factory reads - and it only reports
            // (rule 10); refusing broken content the moment it would ACT stays
            // each system's own fail-closed guard.
            ContentValidator.Validate(Database, RecordsCurrencyId, new RewardManager(Database.Rewards.All));

            PermanentCurrencies = EconomyContextFactory.BuildPermanentPool(Database);
            PermanentCurrencies.ValidateReference(RecordsCurrencyId, "GameManager (income multiplier)");

            // the lowest chapter index is the starting chapter; chapter
            // advancement (ChapterManager) is a later slice
            ChapterDefinition startingChapter = null;
            foreach (var chapter in Database.Chapters.All)
            {
                if (startingChapter == null || chapter.Index < startingChapter.Index)
                    startingChapter = chapter;
            }

            if (startingChapter == null)
            {
                Debug.LogError("GameManager: no ChapterDefinition assets found. Run 'GarageBandIdle > Import Chapter 1 JSON' in the editor menu, then press Play again.");
            }
            else
            {
                Frontier = EconomyContextFactory.Build(startingChapter, Database, PermanentCurrencies,
                    EconomyRecipe.FrontierChapter);
                SetFocus(Frontier);

                // the UI display touchpoint above, checked against the pool that
                // actually has to hold it: the header reads cash from the frontier
                // economy, so a cash id missing from that chapter's roster is a
                // blank readout, reported here rather than seen
                Frontier.Currencies.ValidateReference(CashCurrencyId, "GameManager (UI cash display)");
            }

            _tickSystem = GetComponent<TickSystem>();
            _tickSystem.Ticked += OnTicked;
        }

        private void OnDestroy()
        {
            if (_tickSystem != null)
                _tickSystem.Ticked -= OnTicked;

            // a discarded context has to stop listening to the systems that
            // outlive it - the permanent pool outlives every economy
            Frontier?.Dispose();

            SingletonManager.Unregister(this);
        }

        // The focus switch (rule 7): exactly one economy receives the tick.
        // Enforced here rather than by the contexts because this is the only
        // thing holding more than one - a context can refuse to tick while
        // unfocused (it does), but it cannot know that another one is focused.
        public void SetFocus(EconomyContext context)
        {
            if (Focused == context)
                return;

            // the outgoing context stamps its last-interaction time on the way
            // out, which is the value slice 9's idle earnings will read
            Focused?.Unfocus();
            Focused = context;
            Focused?.Focus();
        }

        private void OnTicked(double seconds)
        {
            // only the focused economy accrues (rule 7); the context enforces the
            // same rule from its side, so a stray reference cannot tick a
            // background economy
            Focused?.Tick(seconds);
        }

        // Deliberately NO player-action routes here. They existed as thin
        // Focused?.Jam()-style forwards, and that indirection is exactly what
        // let a module display one economy's numbers while mutating whichever
        // context happened to hold focus. A module acts on the economy its
        // ChapterContext shows; focus governs the tick (rule 7), nothing else.
    }
}
