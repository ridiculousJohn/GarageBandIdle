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

        // One node per scope: identity, the re-stamped-not-cleared timestamp,
        // and the complete mutable payload (design doc 12.3).
        [Serializable]
        private class SaveNode
        {
            public string scopeId;
            public DateTime lastActiveUtc;
            public ScopeFacts facts;
            public List<SaveNode> children = new();
        }

        private static JsonSerializerSettings MakeSettings() => new()
        {
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            Converters = { new BigNumberConverter() },
        };

        // ---- serialization ----

        public static string Serialize(ScopeState root)
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
                lastActiveUtc = state.lastActiveUtc,
                facts = state.facts,
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
        public static bool TryDeserialize(string json, ScopeDefinition rootDefinition, out ScopeState root)
        {
            root = null;
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
                FilterToDeclared(node.facts, state.Definition);
                FilterTreeScopedFacts(node.facts, state);
                state.facts = node.facts;
            }
            state.lastActiveUtc = node.lastActiveUtc;
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

        // Tree-scoped facts carry ownership and reach rules, not just id
        // existence (12.3): roadie allocation is a ROOT fact keyed by root's
        // direct children (the chapters); a pending claim is a CHAPTER fact
        // whose currencies must be homed in that chapter's subtree or on its
        // ancestor chain - a sibling chapter's currency can never appear in it.
        private static void FilterTreeScopedFacts(ScopeFacts facts, ScopeState state)
        {
            var isRoot = state.Parent == null;
            var isChapter = !isRoot && state.Parent.Parent == null;

            if (facts.roadieAllocation.Count > 0)
            {
                if (!isRoot)
                {
                    Debug.LogWarning($"SaveSystem: roadie allocation on non-root scope '{state.ScopeId}' - dropped; allocation is a root fact.");
                    facts.roadieAllocation.Clear();
                }
                else
                {
                    List<string> invalid = null;
                    foreach (var key in facts.roadieAllocation.Keys)
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
                        if (!isChapterId)
                            (invalid ??= new List<string>()).Add(key);
                    }
                    if (invalid != null)
                    {
                        foreach (var key in invalid)
                        {
                            Debug.LogWarning($"SaveSystem: roadie allocation key '{key}' is not a chapter - dropped.");
                            facts.roadieAllocation.Remove(key);
                        }
                    }
                }
            }

            if (!isRoot && facts.entitlements.Count > 0)
            {
                Debug.LogWarning($"SaveSystem: entitlements on non-root scope '{state.ScopeId}' - dropped; entitlements are root facts.");
                facts.entitlements.Clear();
            }

            if (facts.pendingClaim != null)
            {
                if (!isChapter)
                {
                    Debug.LogWarning($"SaveSystem: pending claim on non-chapter scope '{state.ScopeId}' - dropped; claims are chapter facts.");
                    facts.pendingClaim = null;
                }
                else
                {
                    var valid = new HashSet<string>();
                    CollectCurrencyIds(state.Definition, valid);       // this chapter's subtree
                    for (var node = state.Parent; node != null; node = node.Parent)
                        foreach (var currencyId in node.Definition.currencyIds)
                            valid.Add(currencyId);                     // the ancestor chain
                    List<string> unknown = null;
                    foreach (var key in facts.pendingClaim.amounts.Keys)
                    {
                        if (!valid.Contains(key))
                            (unknown ??= new List<string>()).Add(key);
                    }
                    if (unknown != null)
                    {
                        foreach (var key in unknown)
                        {
                            Debug.LogWarning($"SaveSystem: pending-claim currency '{key}' is not reachable from chapter '{state.ScopeId}' - dropped.");
                            facts.pendingClaim.amounts.Remove(key);
                        }
                    }
                }
            }
        }

        private static void CollectCurrencyIds(ScopeDefinition definition, HashSet<string> into)
        {
            foreach (var currencyId in definition.currencyIds)
                into.Add(currencyId);
            foreach (var child in definition.children)
                CollectCurrencyIds(child, into);
        }

        // Unknown ids from removed content are dropped with a warning (12.10).
        // Only families whose declarations exist TODAY are filtered: currencies,
        // flags, and triggers live on ScopeDefinition; pending-claim currencies
        // and roadie allocation validate against the tree (below). Generator
        // counts, upgrade latches, bar/group ids, modifier ids, event ids, buff
        // ids, and song ids gain their filters WITH their definition families
        // (build plan steps 3-7) - the same incremental contract as the
        // validation pass.
        private static void FilterToDeclared(ScopeFacts facts, ScopeDefinition definition)
        {
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
        }

        private static void DropUndeclaredKeys(Dictionary<string, BigNumber> map, ScopeDefinition definition, string label)
        {
            List<string> undeclared = null;
            foreach (var key in map.Keys)
            {
                if (!definition.declaredCurrencyIds.Contains(key))
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
        public static void WriteAtomic(string path, ScopeState root)
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
        public static LoadOutcome LoadFromDisk(string path, ScopeDefinition rootDefinition, out ScopeState root)
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

        private static bool TryLoadFile(string path, ScopeDefinition rootDefinition, out ScopeState root)
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
