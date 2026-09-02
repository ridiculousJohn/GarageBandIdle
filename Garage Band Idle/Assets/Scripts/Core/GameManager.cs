using System;
using System.IO;
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
        // children - the load-boundary name resolution, one scan (12.3). A
        // fresh game has no record and step 9 owns the chapter select, so until
        // then boot enters the FIRST chapter by id - deterministic because
        // composition sorts the roster (12.14.5), and the sole authored chapter
        // while Chapter 1 stands alone. An explicit stopgap, removed when the
        // select exists.
        public static ChapterScopeState EntryChapter(RootScopeState root)
        {
            var recorded = root.currentChapterId;
            if (string.IsNullOrEmpty(recorded))
                return (ChapterScopeState)root.Children[0];
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

        // A FILE under persistentDataPath: handing LoadFromDisk the directory
        // would read every fresh install as Failed.
        private static string SavePath => Path.Combine(Application.persistentDataPath, "save.json");

        private void Awake()
        {
            var now = DateTime.UtcNow;
            // The manager retains the database and releases it in OnDestroy -
            // the Addressables handles need an owner, and nothing else can - so
            // this field publishes first, before anything below can throw past it.
            database = ContentDatabase.LoadRoot(ContentDatabase.RootAddress, ContentDatabase.ChapterLabel);

            // Built into locals, and the session field publishes LAST, because
            // it is the guard every lifecycle hook tests: nothing added between
            // here and that write can leave a hook reading a half-booted driver.
            // A throw before it would otherwise let a quit SAVE what the entry
            // sweep half-executed - the sweep latches a trigger before running
            // its actions, so a refused payout persists the latch without its
            // reward.
            var booted = GameBoot.Load(database.Root, SavePath, config);
            // Entering re-offers any unpaid window as the idle dialog phase;
            // nothing renders it until step 9, the session state is simply correct.
            booted.SwitchChapter(GameBoot.EntryChapter(booted.Root), now);

            clock = new GameClock(now);
            session = booted;               // the guard, published last
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
            uiRoot.Bind(session, registry, clock);
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
            uiRoot.Interpolate();
        }

        // Backgrounding stamps the live chapter and preserves an unpaid window
        // (SwitchChapter(null) is the backgrounding rule); the return re-enters
        // the recorded chapter, which is where the away window recomputes.
        private void OnApplicationPause(bool paused)
        {
            if (session == null)
                return;
            clock.Resample(DateTime.UtcNow);
            var now = clock.RealTimeUtc;
            if (paused)
            {
                session.SwitchChapter(null, now);
                Save(now);
            }
            else
            {
                session.SwitchChapter(GameBoot.EntryChapter(session.Root), now);
            }
        }

        private void OnApplicationQuit()
        {
            if (session == null)
                return;
            clock.Resample(DateTime.UtcNow);
            var now = clock.RealTimeUtc;
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
