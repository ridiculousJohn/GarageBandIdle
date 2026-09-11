using System;
using System.IO;
using RidiculousGaming.GarageBandIdle.Monetization;
using RidiculousGaming.GarageBandIdle.Save;
using RidiculousGaming.GarageBandIdle.UI;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // The headless boot: load outcome to tree-plus-session, plain C# so the
    // mapping is testable without a scene. The MonoBehaviour above it only
    // forwards lifecycle calls into these two.
    public static class GameBoot
    {
        // LoadedPrimary and LoadedBackup use the loaded tree, NoSave builds
        // fresh, and Failed is a hard stop: "couldn't read your save" is never
        // answered by starting a new game over it (12.10) - the throw is the
        // visible error, and the previous save stays on disk untouched.
        public static GameSession Load(ComposedContent content, string savePath, GameConfig config)
        {
            var outcome = SaveSystem.LoadFromDisk(savePath, content, out var root);
            if (outcome == LoadOutcome.Failed)
                throw new InvalidOperationException(
                    $"the save at '{savePath}' exists and cannot be loaded - refusing to start over it (12.10).");
            if (outcome == LoadOutcome.NoSave)
                root = ScopeState.Build(content);
            return new GameSession(root, config);
        }

        // Where play resumes: the recorded chapter resolved over root's direct
        // children - the load-boundary name resolution, one scan (12.3). A fresh
        // game has no record and answers null: boot stays NoChapter and the select
        // is the first screen, which is the one state the select exists for (12.9).
        public static ChapterScopeState EntryChapter(RootScopeState root)
        {
            var recorded = root.currentChapterId;
            if (string.IsNullOrEmpty(recorded))
                return null;
            foreach (var child in root.Children)
                if (child.ScopeId == recorded)
                    return (ChapterScopeState)child;
            // The load path clears a record naming no authored chapter, so
            // reaching this is a code bug, not a content or save state.
            throw new InvalidOperationException(
                $"recorded chapter '{recorded}' is not a child of root - the load filter should have cleared it.");
        }
    }

    // The thin driver (design doc 12.13): bootstrap, save/load, chapter
    // switching. Glue only - it holds the config reference and nothing the
    // session already owns, and the lifecycle forwarding here stays untested
    // by design. It advances the clock at the top of EVERY entry point before
    // any game code runs (12.9), which is why it is the one place that reads
    // Time.* and DateTime.UtcNow at all; the pacing state lives in the session.
    public class GameManager : MonoBehaviour
    {
        [SerializeField] private GameConfig config;
        [SerializeField] private ModuleRegistry registry;
        [SerializeField] private UIRoot uiRoot;

        private ContentDatabase database;
        private GameSession session;
        private GameClock clock;

        // The two callback surfaces (12.11), owned here because the driver owns
        // the frame they deliver on and the one save site they call.
        private AdManager ads;
        private IAPManager store;

        // A FILE under persistentDataPath: handing LoadFromDisk the directory
        // would read every fresh install as Failed.
        private static string SavePath => Path.Combine(Application.persistentDataPath, "save.json");

        private void Awake()
        {
            var now = DateTime.UtcNow;
            // The manager retains the database and releases it in OnDestroy -
            // the Addressables handles need an owner, and nothing else can - so
            // this field publishes first, before anything below can throw past it.
            try
            {
                database = ContentDatabase.LoadRoot(ContentDatabase.RootAddress, ContentDatabase.ChapterLabel);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                var widgetReport = ModuleWidgetFactory.Validate(database.Root, registry);
                if (widgetReport.HasErrors)
                    throw new ContentValidationException("widget registry validation failed.", widgetReport);
#endif
            }
            catch (ContentValidationException e)
            {
                // The driver is where a person is watching, so the driver prints
                // the findings; the load only refuses with them.
                e.Report.LogAll();
                throw;
            }
            database.Report?.LogAll();      // a development build's warnings; null in release

            // Built into locals, and the session field publishes LAST, because
            // it is the guard every lifecycle hook tests: nothing added between
            // here and that write can leave a hook reading a half-booted driver.
            // A throw before it would otherwise let a quit SAVE what the entry
            // sweep half-executed - the sweep latches a trigger before running
            // its actions, so a refused payout persists the latch without its
            // reward.
            var booted = GameBoot.Load(database.Root, SavePath, config);
            // Entering re-offers any unpaid window as the idle dialog phase; a fresh
            // game has no record, so the switch is a no-op and the select is the first
            // screen (12.9).
            booted.SwitchChapter(GameBoot.EntryChapter(booted.Root), now);

            clock = new GameClock(now);
            // The one save site, handed to the managers so a grant is on disk
            // before the store is told it landed.
            ads = new AdManager(booted, new FakeAdService(), config, () => Save(clock.RealTimeUtc));
            store = new IAPManager(booted, new FakeStoreService(), config, () => Save(clock.RealTimeUtc));
            session = booted;               // the guard, published last

            // Restoration lands on a frame AFTER boot, so the first offer is
            // already computed undoubled at the base cap and a restored
            // entitlement applies from the next one. A launch gated on a
            // network restore is a worse product than one undoubled offer.
            store.RequestRestore();
        }

        // The bind waits for Start: boot runs in Awake, every Awake precedes
        // every OnEnable, and the UIDocument builds its tree in its own
        // OnEnable - so a bind issued from the boot path would read a panel that
        // does not exist yet. Start runs after every OnEnable, and Bind renders
        // once unconditionally (12.11).
        private void Start()
        {
            if (session == null)
                return;
            uiRoot.Bind(session, registry, clock, ads, store);
        }

        // A boot failure leaves session null and its thrown error in the log;
        // the guards below keep the dead driver from burying it under a
        // per-frame exception of its own.
        private void Update()
        {
            if (session == null)
                return;
            clock.Frame(DateTime.UtcNow, Time.unscaledDeltaTime);
            session.Accumulate(clock.RealTimeUtc);
            // After the accumulation, so a callback's transaction always lands
            // between the frame's tick and the repaint - never inside one.
            ads.Update(clock.RealTimeUtc);
            store.Update(clock.RealTimeUtc);
            uiRoot.Interpolate();
        }

        // Backgrounding stamps the live chapter and preserves an unpaid window
        // (SwitchChapter(null) is the backgrounding rule); the return re-enters
        // the recorded chapter, which is where the away window recomputes. A
        // player who backgrounds on the select has no record, and the null
        // re-entry is a no-op. The drain is what makes the switch run at the
        // call (12.9): a submission behind a queued refresh's command would
        // leave the save ahead of the stamp. The resume needs none - the queue
        // is empty by the time a frame has drained it.
        private void OnApplicationPause(bool paused)
        {
            if (session == null)
                return;
            clock.Resample(DateTime.UtcNow);
            var now = clock.RealTimeUtc;
            if (paused)
            {
                session.Drain();
                session.SwitchChapter(null, now);
                Save(now);
            }
            else
            {
                session.SwitchChapter(GameBoot.EntryChapter(session.Root), now);
            }
        }

        // The drain before the switch, for the same reason backgrounding
        // drains: the stamp the save writes has to be on the tree before the
        // write (12.9).
        private void OnApplicationQuit()
        {
            if (session == null)
                return;
            clock.Resample(DateTime.UtcNow);
            var now = clock.RealTimeUtc;
            session.Drain();
            session.SwitchChapter(null, now);
            Save(now);
        }

        private void OnDestroy() => database?.Release();

        // The one save site. The stamp-on-save line covers saves taken WITHOUT
        // backgrounding - foreground only and only while Live (12.9); a
        // periodic autosave is one call here whenever it is wanted. Pause and
        // quit arrive as NoChapter, where it no-ops, because SwitchChapter(null)
        // already stamped the outgoing chapter.
        private void Save(DateTime nowUtc)
        {
            if (session.Phase == SessionPhase.Live)
                session.ForegroundChapter.StampActive(nowUtc);
            SaveSystem.WriteAtomic(SavePath, session.Root, database.Root);
        }
    }
}
