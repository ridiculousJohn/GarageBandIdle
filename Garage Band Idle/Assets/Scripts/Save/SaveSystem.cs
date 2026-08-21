using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Save
{
    public enum LoadOutcome
    {
        LoadedPrimary,
        LoadedBackup,   // primary missing or corrupt; the previous save answered
        NoSave,         // fresh install - neither file exists
        Failed          // both files exist and neither deserializes
    }

    // Serializes the ScopeState tree, nested as the scopes are - and nothing
    // else (design doc 12.10). Envelope: {schemaVersion, checksum, payload}
    // where payload is the tree as a STRING and the checksum is SHA-256 over
    // the schemaVersion plus that string verbatim - the version is BOUND, so a
    // corrupted version byte reads as corruption, never as a different schema -
    // and verification never depends on re-serialization canonicalization. No
    // grants, no derived values, no replay of actions.
    public static class SaveSystem
    {
        public const int CurrentSchemaVersion = 1;

        // Explicit per-version migrations: Migrations[n] upgrades a version-n
        // payload one step. Loading a version with no registered path - or one
        // NEWER than current - is refused outright, never best-effort parsed.
        private static readonly Dictionary<int, Func<JObject, JObject>> Migrations = new();

        // One node per scope: identity, the re-stamped-not-cleared timestamp
        // (chapters only), and the complete mutable payload (design doc 12.3).
        // The payload stays a raw token here and is read against the type the
        // scope's position in the definition tree dictates - a save never names
        // its own payload type.
        [Serializable]
        private class SaveNode
        {
            public string scopeId;
            public DateTime lastActiveUtc;
            public JObject facts;
            public List<SaveNode> children = new();
        }

        private static JsonSerializerSettings MakeSettings() => new()
        {
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            Converters = { new BigNumberConverter() },
        };

        // ---- serialization ----

        public static string Serialize(RootScopeState root)
        {
            var payload = JsonConvert.SerializeObject(ToNode(root), MakeSettings());
            var envelope = new JObject
            {
                ["schemaVersion"] = CurrentSchemaVersion,
                ["checksum"] = EnvelopeChecksum(CurrentSchemaVersion, payload),
                ["payload"] = payload,
            };
            return envelope.ToString(Formatting.None);
        }

        // The checksum binds the version AND the payload: a corrupted version
        // byte must read as corruption (fall back to backup), never as "newer
        // build" or a wrong migration route.
        private static string EnvelopeChecksum(int version, string payload) =>
            ChecksumOf(version + "\n" + payload);

        private static SaveNode ToNode(ScopeState state)
        {
            var node = new SaveNode
            {
                scopeId = state.ScopeId,
                lastActiveUtc = state is ChapterScopeState chapter ? chapter.lastActiveUtc : default,
                facts = JObject.FromObject(state.facts, JsonSerializer.Create(MakeSettings())),
            };
            foreach (var child in state.Children)
                node.children.Add(ToNode(child));
            return node;
        }

        // ---- deserialization ----

        // Builds a fresh tree from the definitions and applies the saved facts
        // onto it. Unknown ids from removed content are dropped with a warning;
        // content added since the save starts fresh. False on any structural
        // failure - malformed json, checksum mismatch, unmigratable version.
        public static bool TryDeserialize(string json, ScopeDefinition rootDefinition, out RootScopeState root)
        {
            root = null;
            if (rootDefinition == null)
                throw new ArgumentNullException(nameof(rootDefinition), "SaveSystem: loading requires the content tree.");
            try
            {
                var envelope = JObject.Parse(json);
                var version = envelope.Value<int?>("schemaVersion") ?? -1;
                var payloadString = envelope.Value<string>("payload");
                var checksum = envelope.Value<string>("checksum");
                if (payloadString == null || checksum != EnvelopeChecksum(version, payloadString))
                {
                    Debug.LogWarning("SaveSystem: checksum mismatch - save rejected.");
                    return false;
                }

                if (version > CurrentSchemaVersion)
                {
                    Debug.LogWarning($"SaveSystem: save is schema v{version}, newer than this build's v{CurrentSchemaVersion} - refused.");
                    return false;
                }

                var payload = JObject.Parse(payloadString);
                while (version < CurrentSchemaVersion)
                {
                    if (!Migrations.TryGetValue(version, out var migrate))
                    {
                        Debug.LogWarning($"SaveSystem: no migration registered from schema v{version} - refused, never best-effort parsed.");
                        return false;
                    }
                    payload = migrate(payload);
                    version++;
                }

                var rootNode = payload.ToObject<SaveNode>(JsonSerializer.Create(MakeSettings()));
                if (rootNode == null || rootNode.scopeId != rootDefinition.Id)
                {
                    Debug.LogWarning("SaveSystem: payload root does not match the content tree's root - refused.");
                    return false;
                }

                root = ScopeState.Build(rootDefinition);
                Apply(rootNode, root);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"SaveSystem: failed to read save - {e.Message}");
                root = null;
                return false;
            }
        }

        private static void Apply(SaveNode node, ScopeState state)
        {
            if (node.facts != null)
            {
                var payload = ReadFacts(node.facts, state);
                FilterToDeclared(payload, state);
                FilterTreeScopedFacts(payload, state);
                state.ApplyLoadedFacts(payload);
            }
            if (state is ChapterScopeState chapter)
                chapter.lastActiveUtc = node.lastActiveUtc;
            EnsureDeclared(state);

            foreach (var childNode in node.children)
            {
                ScopeState childState = null;
                foreach (var child in state.Children)
                {
                    if (child.ScopeId == childNode.scopeId)
                    {
                        childState = child;
                        break;
                    }
                }
                if (childState == null)
                {
                    Debug.LogWarning($"SaveSystem: saved scope '{childNode.scopeId}' no longer exists - dropped.");
                    continue;
                }
                Apply(childNode, childState);
            }
            // A definition child with no saved node keeps its freshly built
            // state - content added since the save simply starts new.
        }

        // Reads a saved payload against the type the scope's POSITION dictates,
        // never a type the save names for itself: root facts land on the root,
        // chapter facts on a chapter, and a tier gets the base payload. Members
        // the target type does not have are dropped by the read itself.
        private static ScopeFacts ReadFacts(JObject token, ScopeState state)
        {
            var serializer = JsonSerializer.Create(MakeSettings());
            if (state is RootScopeState)
                return token.ToObject<RootFacts>(serializer);
            if (state is ChapterScopeState)
                return token.ToObject<ChapterFacts>(serializer);
            return token.ToObject<ScopeFacts>(serializer);
        }

        // Tree-scoped facts carry reach rules, not just id existence (12.3): the
        // roadie allocation is keyed by root's direct children (the chapters),
        // and a pending claim's currencies must be homed in that chapter's
        // subtree or on its ancestor chain - a sibling chapter's currency can
        // never appear in it. Placement itself needs no check: the payload types
        // make a root fact on a tier unrepresentable.
        private static void FilterTreeScopedFacts(ScopeFacts facts, ScopeState state)
        {
            if (facts is RootFacts rootFacts && rootFacts.roadieAllocation.Count > 0)
            {
                List<string> invalid = null;
                foreach (var key in rootFacts.roadieAllocation.Keys)
                {
                    var isChapterId = false;
                    foreach (var child in state.Children)
                    {
                        if (child.ScopeId == key)
                        {
                            isChapterId = true;
                            break;
                        }
                    }
                    // A nonpositive stationing is not an allocation.
                    if (!isChapterId)
                        Debug.LogWarning($"SaveSystem: roadie allocation key '{key}' is not a chapter - dropped.");
                    else if (rootFacts.roadieAllocation[key] <= 0)
                        Debug.LogWarning($"SaveSystem: roadie allocation for '{key}' is {rootFacts.roadieAllocation[key]} - dropped.");
                    else
                        continue;
                    (invalid ??= new List<string>()).Add(key);
                }
                if (invalid != null)
                {
                    foreach (var key in invalid)
                        rootFacts.roadieAllocation.Remove(key);
                }
            }

            if (facts is ChapterFacts chapterFacts && chapterFacts.pendingClaim != null)
            {
                // Each line names its own home, so validating one is a single
                // question rather than a union of name sets: the scope is this
                // chapter, something in its subtree, or something on its chain,
                // and it declares that currency.
                var entries = chapterFacts.pendingClaim.amounts;
                for (var i = entries.Count - 1; i >= 0; i--)
                {
                    var entry = entries[i];
                    if (entry == null)
                    {
                        Debug.LogWarning($"SaveSystem: a null pending-claim entry at chapter '{state.ScopeId}' - dropped.");
                        entries.RemoveAt(i);
                        continue;
                    }
                    var home = state.FindInSubtree(entry.scopeId) ?? state.FindOnChain(entry.scopeId);
                    if (home != null && home.Definition.DeclaresCurrency(entry.currencyId))
                        continue;
                    Debug.LogWarning($"SaveSystem: pending-claim currency '{entry.currencyId}' at scope '{entry.scopeId}' is not reachable from chapter '{state.ScopeId}' - dropped.");
                    entries.RemoveAt(i);
                }
            }
        }

        // Unknown ids from removed content are dropped with a warning (12.10).
        // Filtered here: the families a scope DECLARES (currencies, flags,
        // triggers, generators, upgrades) plus the modifier stacks, whose ids
        // name declared content too - a modifier is declared on a scope, and a
        // stack's id resolves by walking outward from the scope holding it.
        // Pending-claim currencies and
        // roadie allocation validate against the tree instead (below). Bar and
        // group ids, event ids, buff ids, and song ids gain their filters WITH
        // their definition families - the same incremental contract as the
        // validation pass.
        private static void FilterToDeclared(ScopeFacts facts, ScopeState state)
        {
            var definition = state.Definition;
            DropUndeclaredKeys(facts.balances, definition, "balance");
            DropUndeclaredKeys(facts.earnedTotals, definition, "earned total");
            facts.flags.RemoveWhere(flagId =>
            {
                if (definition.DeclaresFlag(flagId))
                    return false;
                Debug.LogWarning($"SaveSystem: flag '{flagId}' is not declared by scope '{definition.Id}' - dropped.");
                return true;
            });
            facts.firedTriggers.RemoveWhere(triggerId =>
            {
                foreach (var trigger in definition.triggers)
                {
                    if (trigger != null && trigger.Id == triggerId)
                        return false;
                }
                Debug.LogWarning($"SaveSystem: trigger latch '{triggerId}' is not declared by scope '{definition.Id}' - dropped.");
                return true;
            });
            facts.purchasedUpgrades.RemoveWhere(upgradeId =>
            {
                foreach (var upgrade in definition.upgrades)
                {
                    if (upgrade != null && upgrade.Id == upgradeId)
                        return false;
                }
                Debug.LogWarning($"SaveSystem: upgrade latch '{upgradeId}' is not declared by scope '{definition.Id}' - dropped.");
                return true;
            });

            // An owned count is positive or absent: zero already reads as absent,
            // and a negative one would buy the next unit at a discount.
            List<string> staleCounts = null;
            foreach (var pair in facts.generatorCounts)
            {
                var declared = false;
                foreach (var generator in definition.generators)
                {
                    if (generator != null && generator.Id == pair.Key)
                    {
                        declared = true;
                        break;
                    }
                }
                if (!declared)
                    Debug.LogWarning($"SaveSystem: generator count '{pair.Key}' is not declared by scope '{definition.Id}' - dropped.");
                else if (pair.Value <= 0)
                    Debug.LogWarning($"SaveSystem: generator count '{pair.Key}' is {pair.Value} - dropped.");
                else
                    continue;
                (staleCounts ??= new List<string>()).Add(pair.Key);
            }
            if (staleCounts != null)
            {
                foreach (var key in staleCounts)
                    facts.generatorCounts.Remove(key);
            }

            // A stack is a count of a modifier declared at this scope or an
            // ancestor - the same walk the read does. A nonpositive count is not
            // a stack: RemoveModifier deletes the key at zero, so one on disk is
            // tampering or a stale write.
            List<string> staleStacks = null;
            foreach (var pair in facts.modifierStacks)
            {
                if (pair.Value <= 0)
                    Debug.LogWarning($"SaveSystem: modifier stack '{pair.Key}' has count {pair.Value} - dropped.");
                else if (!DeclaresModifierOnChain(state, pair.Key))
                    Debug.LogWarning($"SaveSystem: modifier '{pair.Key}' is not declared on the chain from '{state.ScopeId}' - dropped.");
                else
                    continue;
                (staleStacks ??= new List<string>()).Add(pair.Key);
            }
            if (staleStacks != null)
            {
                foreach (var key in staleStacks)
                    facts.modifierStacks.Remove(key);
            }
        }

        private static bool DeclaresModifierOnChain(ScopeState state, string modifierId)
        {
            for (var node = state; node != null; node = node.Parent)
                foreach (var modifier in node.Definition.modifiers)
                    if (modifier != null && modifier.Id == modifierId)
                        return true;
            return false;
        }

        private static void DropUndeclaredKeys(Dictionary<string, BigNumber> map, ScopeDefinition definition, string label)
        {
            List<string> undeclared = null;
            foreach (var key in map.Keys)
            {
                if (!definition.DeclaresCurrency(key))
                    (undeclared ??= new List<string>()).Add(key);
            }
            if (undeclared == null)
                return;
            foreach (var key in undeclared)
            {
                Debug.LogWarning($"SaveSystem: {label} '{key}' is not declared by scope '{definition.Id}' - dropped.");
                map.Remove(key);
            }
        }

        // A currency declared since the save was written gets its zero entries.
        private static void EnsureDeclared(ScopeState state)
        {
            foreach (var currencyId in state.Definition.currencyIds)
            {
                if (!state.balances.ContainsKey(currencyId))
                    state.balances[currencyId] = BigNumber.Zero;
                if (!state.earnedTotals.ContainsKey(currencyId))
                    state.earnedTotals[currencyId] = BigNumber.Zero;
            }
        }

        // ---- files: atomic write with backup, load with fallback ----

        public static string BackupPath(string path) => path + ".bak";

        // Write temp, verify what actually hit the disk, then swap - keeping
        // the previous save as the backup the loader falls back to (12.10).
        // The backup only ever receives content that VERIFIES: a corrupt
        // primary (the recovery case) is deleted, never rotated over a good
        // backup - File.Replace would otherwise install it as the new .bak.
        public static void WriteAtomic(string path, RootScopeState root)
        {
            var json = Serialize(root);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);

            var readBack = File.ReadAllText(tmp);
            if (!VerifyEnvelope(readBack))
            {
                File.Delete(tmp);
                throw new IOException("SaveSystem: written save failed verification - previous save left untouched.");
            }

            if (FileLoadable(path, root.Definition))
            {
                File.Replace(tmp, path, BackupPath(path));
            }
            else if (File.Exists(path))
            {
                // Atomic replace with NO backup rotation: an unloadable primary
                // must never become the .bak, and delete-then-move would open a
                // crash window with no primary on disk at all.
                File.Replace(tmp, path, null);
            }
            else
            {
                File.Move(tmp, path);
            }
        }

        // NoSave is ONLY the genuinely-fresh case: both files affirmatively not
        // found. Any other read failure - permissions, invalid path, disk - is
        // Failed, because "couldn't read your save" must never be answered by
        // starting a new game. (File.Exists returns false for ALL of those, so
        // it decides nothing here.)
        public static LoadOutcome LoadFromDisk(string path, ScopeDefinition rootDefinition, out RootScopeState root)
        {
            root = null;

            var primaryRead = TryRead(path, out var primaryJson);
            if (primaryRead == ReadResult.Ok && TryDeserialize(primaryJson, rootDefinition, out root))
                return LoadOutcome.LoadedPrimary;

            var backupRead = TryRead(BackupPath(path), out var backupJson);
            if (backupRead == ReadResult.Ok && TryDeserialize(backupJson, rootDefinition, out root))
            {
                Debug.LogWarning("SaveSystem: primary save unusable - loaded the backup.");
                return LoadOutcome.LoadedBackup;
            }

            return primaryRead == ReadResult.NotFound && backupRead == ReadResult.NotFound
                ? LoadOutcome.NoSave
                : LoadOutcome.Failed;
        }

        private enum ReadResult { Ok, NotFound, Error }

        // Tri-state read: "the file is not there" and "the file cannot be read"
        // are different answers, and only the first may ever mean fresh install.
        private static ReadResult TryRead(string path, out string json)
        {
            json = null;
            try
            {
                json = File.ReadAllText(path);
                return ReadResult.Ok;
            }
            catch (FileNotFoundException)
            {
                return ReadResult.NotFound;
            }
            catch (DirectoryNotFoundException)
            {
                return ReadResult.NotFound;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"SaveSystem: could not read '{path}' - {e.Message}");
                return ReadResult.Error;
            }
        }

        private static bool TryLoadFile(string path, ScopeDefinition rootDefinition, out RootScopeState root)
        {
            root = null;
            return TryRead(path, out var json) == ReadResult.Ok && TryDeserialize(json, rootDefinition, out root);
        }

        // Backup eligibility equals LOADABILITY, not just envelope integrity: a
        // checksum-valid but unusable primary (newer schema, missing migration,
        // wrong root, malformed payload) must never rotate over the known-good
        // backup. Verifying a bad file emits its diagnostics - honest tracing.
        private static bool FileLoadable(string path, ScopeDefinition rootDefinition) =>
            TryLoadFile(path, rootDefinition, out _);

        // Structural check only - parse and checksum, no tree application. Used
        // to verify a write before it replaces the previous save.
        private static bool VerifyEnvelope(string json)
        {
            try
            {
                var envelope = JObject.Parse(json);
                var version = envelope.Value<int?>("schemaVersion") ?? -1;
                var payload = envelope.Value<string>("payload");
                return payload != null && envelope.Value<string>("checksum") == EnvelopeChecksum(version, payload);
            }
            catch
            {
                return false;
            }
        }

        private static string ChecksumOf(string payload)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
            var builder = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
                builder.Append(b.ToString("x2"));
            return builder.ToString();
        }

        // BigNumber persists as its exact parts - a string round-trip through
        // double would destroy every value past 1e308.
        private class BigNumberConverter : JsonConverter<BigNumber>
        {
            public override void WriteJson(JsonWriter writer, BigNumber value, JsonSerializer serializer)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("m");
                writer.WriteValue(value.Mantissa);
                writer.WritePropertyName("e");
                writer.WriteValue(value.Exponent);
                writer.WriteEndObject();
            }

            public override BigNumber ReadJson(JsonReader reader, Type objectType, BigNumber existingValue,
                bool hasExistingValue, JsonSerializer serializer)
            {
                var obj = JObject.Load(reader);
                return BigNumber.FromMantissaExponent(obj.Value<double>("m"), obj.Value<long>("e"));
            }
        }
    }
}
