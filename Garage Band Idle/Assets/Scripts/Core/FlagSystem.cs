using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // One flag the chapter declares: the id everything gates on, and the
    // latch's LIFETIME (design doc section 12, rule 11) - declared here, on the
    // flag, never on the setFlag effects that set it, so one flag cannot carry
    // two lifetimes. PermanentInChapter (the default) survives album releases:
    // taught systems stay taught. Run clears at every release, so everything
    // gating on the flag goes dark together and re-arms when a run-scoped
    // setter re-fires - which is what makes a whole sub-system (section, bars,
    // accrual) re-earnable each run through ONE condition authored in ONE
    // place, the setter's gate.
    [Serializable]
    public class FlagDeclaration
    {
        [SerializeField]
        [Tooltip("Stable flag id, e.g. fans / covers / album. Never rename once saves exist.")]
        private string _id;

        [SerializeField]
        [Tooltip("The latch's lifetime. PermanentInChapter (the default): survives album releases. " +
            "Run: clears at every release - pair it with a run-scoped setter, or nothing re-sets it.")]
        private ContentScope _scope = ContentScope.PermanentInChapter;

        public string Id => _id;
        public ContentScope Scope => _scope;

        public FlagDeclaration() { }

        public FlagDeclaration(string id, ContentScope scope = ContentScope.PermanentInChapter)
        {
            _id = id;
            _scope = scope;
        }
    }

    // Progress flags: string ids set by content (content-unlock upgrades,
    // setFlag rewards) and observed anywhere through FlagSetCondition - the
    // single reveal registry (design doc section 12, rule 9). A flag latches
    // on; whether an album release clears it is the flag's declared scope, so
    // un-setting is a boundary's decision, never an evaluation's.
    public class FlagSystem
    {
        private readonly HashSet<string> _flags = new();

        // the chapter's declared flag ids; null means unrestricted (no chapter
        // loaded, or a test fixture that doesn't care about declarations)
        private readonly HashSet<string> _known;

        // the declared-run-scope subset; an undeclared or unrestricted flag
        // defaults to the permanent latch, so a typo can never opt a flag
        // INTO resetting
        private readonly HashSet<string> _runScoped;

        // fires once per flag, when it is first set
        public event Action<string> FlagSet;

        // fires once per flag when a run reset clears a run-scoped latch, the
        // counterpart to FlagSet. Both fire only after ALL state for the
        // operation has settled (state, then notify).
        public event Action<string> FlagCleared;

        public FlagSystem() { }

        // fixture/validation convenience: a known set with every flag on the
        // permanent default
        public FlagSystem(IEnumerable<string> knownIds)
        {
            if (knownIds != null)
                _known = new HashSet<string>(knownIds);
        }

        public FlagSystem(IEnumerable<FlagDeclaration> declarations)
        {
            if (declarations == null)
                return;

            _known = new HashSet<string>();
            _runScoped = new HashSet<string>();
            foreach (var declaration in declarations)
            {
                if (declaration == null || string.IsNullOrEmpty(declaration.Id))
                    continue;

                _known.Add(declaration.Id);
                if (declaration.Scope == ContentScope.Run)
                    _runScoped.Add(declaration.Id);
            }
        }

        // false only when a declared-flags list exists and the id is not on it;
        // validation uses this to catch typos in content
        public bool IsKnown(string id) => _known == null || _known.Contains(id);

        // Whether this flag's latch is declared run-scoped. Asked by the snapshot
        // filter that builds an event sandbox's seed: a sandbox takes the chapter's
        // permanent facts and none of the run's, and the answer to "is this flag a
        // run fact" is already resolved here from the declarations. Keeping it here
        // rather than recording a scope per flag in the snapshot is deliberate -
        // scope is CONTENT, so re-deriving it from the declarations cannot go stale
        // the way a copy in saved state would.
        public bool IsRunScoped(string id) => _runScoped != null && _runScoped.Contains(id);

        public bool IsSet(string id) => _flags.Contains(id);

        public void Set(string id)
        {
            // an undeclared flag is a content mistake: report loudly but still
            // latch, so a typo degrades to a warning rather than lost progress
            if (!IsKnown(id))
                Debug.LogError($"FlagSystem: flag '{id}' is not declared by the chapter's flags list.");

            if (_flags.Add(id))
                FlagSet?.Invoke(id);
        }

        // Restore (save load, event-sandbox seeding): REPLACES the whole latch
        // set. Every flag in the snapshot ends up set and every flag currently set
        // that the snapshot omits ends up cleared - a merge would let a previous
        // restore's flags survive into a different snapshot, which is the same
        // class of bug as a selective modifier reset (design doc section 12, rule
        // 6): two ways to arrive at one state, able to disagree.
        //
        // A flag the chapter no longer declares is SKIPPED rather than latched,
        // which is the one place this deliberately differs from Set. Set latches an
        // undeclared flag because it is content executing and losing progress is
        // worse than a stray latch; here the id comes from stored state, and since
        // every gate validates against the declaration list, nothing can be gating
        // on it - latching it would achieve nothing and would pollute the next
        // capture.
        //
        // All state settles before any notification fires, and notify: false defers
        // the whole set to the context-wide restore (state, then notify).
        public void Restore(IReadOnlyCollection<string> setFlagIds, bool notify = true)
        {
            if (setFlagIds == null)
            {
                Debug.LogError("FlagSystem: Restore with no saved flags. Ignoring - clearing every flag was more likely a missing snapshot than an authored empty one.");
                return;
            }

            var wanted = new HashSet<string>();
            foreach (var id in setFlagIds)
            {
                if (string.IsNullOrEmpty(id))
                    continue;
                if (!IsKnown(id))
                {
                    Debug.LogError($"FlagSystem: Restore names flag '{id}', which the chapter does not declare. Skipping it - no gate can reference an undeclared flag.");
                    continue;
                }
                wanted.Add(id);
            }

            List<string> cleared = null;
            List<string> set = null;

            foreach (var id in _flags)
            {
                if (!wanted.Contains(id))
                    (cleared ??= new List<string>()).Add(id);
            }
            foreach (var id in wanted)
            {
                if (!_flags.Contains(id))
                    (set ??= new List<string>()).Add(id);
            }

            if (cleared != null)
            {
                foreach (var id in cleared)
                    _flags.Remove(id);
            }
            if (set != null)
            {
                foreach (var id in set)
                    _flags.Add(id);
            }

            if (!notify)
                return;

            if (cleared != null)
            {
                foreach (var id in cleared)
                    FlagCleared?.Invoke(id);
            }
            if (set != null)
            {
                foreach (var id in set)
                    FlagSet?.Invoke(id);
            }
        }

        // Every flag currently latched, for a capture. A copy rather than the live
        // set: a snapshot that aliased this would change under the caller as the
        // game ran on.
        public IReadOnlyCollection<string> CaptureSetFlags() => new List<string>(_flags);

        // Run reset (album release): every set run-scoped flag clears, and
        // everything gating on it goes dark at the next settle - re-set only by
        // a setter whose own fact re-fires (the projection re-asserts flags
        // whose setters' latches SURVIVED, which is why a run flag with only
        // permanent setters is a content error). All state settles before any
        // notification fires, and a no-op reset stays silent.
        public bool ResetRunScoped()
        {
            if (_runScoped == null)
                return false;

            List<string> cleared = null;
            foreach (var id in _runScoped)
            {
                if (!_flags.Remove(id))
                    continue;

                cleared ??= new List<string>();
                cleared.Add(id);
            }

            if (cleared == null)
                return false;

            foreach (var id in cleared)
                FlagCleared?.Invoke(id);
            return true;
        }
    }
}
