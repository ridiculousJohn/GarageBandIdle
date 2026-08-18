using System;
using System.Collections.Generic;
using System.Linq;
using RidiculousGaming.GarageBandIdle;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // Dictionary-backed IDefinitionSource; ContentDatabase replaces it in the
    // content-load step.
    public class FakeDefs : IDefinitionSource
    {
        private readonly List<Definition> definitions = new();

        public FakeDefs Add(Definition definition)
        {
            definitions.Add(definition);
            return this;
        }

        public T Get<T>(string id) where T : Definition =>
            definitions.OfType<T>().FirstOrDefault(d => d.Id == id);

        public IEnumerable<T> All<T>() where T : Definition => definitions.OfType<T>();
    }

    // The standing test tree mirrors Chapter 1's shape: root -> ch1 -> tier1,
    // currencies and flags filed exactly as the content doc files them.
    public class TestTree
    {
        public readonly DateTime Now = new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

        public readonly FakeDefs Defs = new();
        public readonly ScopeDefinition RootDef;
        public readonly ScopeDefinition Ch1Def;
        public readonly ScopeDefinition Tier1Def;
        public readonly ScopeState Root;
        public readonly ScopeState Ch1;
        public readonly ScopeState Tier1;

        public TestTree()
        {
            Tier1Def = MakeScope("tier1");
            Tier1Def.declaredCurrencyIds.AddRange(new[] { "cash", "fans", "rehearsal" });
            Tier1Def.declaredFlags.AddRange(new[] { "fans_revealed", "rehearsal_revealed" });

            Ch1Def = MakeScope("ch1");
            Ch1Def.declaredCurrencyIds.Add("ch1_records");
            Ch1Def.declaredFlags.AddRange(new[] { "album", "gj1_done" });
            Ch1Def.children.Add(Tier1Def);

            RootDef = MakeScope("root");
            RootDef.declaredCurrencyIds.AddRange(new[] { "records", "roadies" });
            RootDef.declaredFlags.Add("ch1_complete");
            RootDef.children.Add(Ch1Def);

            Root = ScopeState.Build(RootDef);
            Ch1 = Root.FindInSubtree("ch1");
            Tier1 = Root.FindInSubtree("tier1");
        }

        public GameContext Ctx(ScopeState scope) => new GameContext(scope, Defs, Now);

        public static ScopeDefinition MakeScope(string id)
        {
            var def = ScriptableObject.CreateInstance<ScopeDefinition>();
            def.EditorInit(id);
            return def;
        }

        public static T MakeDefinition<T>(string id, params string[] tags) where T : Definition
        {
            var def = ScriptableObject.CreateInstance<T>();
            def.EditorInit(id, tags);
            return def;
        }
    }
}
