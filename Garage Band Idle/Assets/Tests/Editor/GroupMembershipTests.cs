using System;
using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // A group lists members of any kind declared on its own scope, and a member
    // is OFF unless every group listing it holds it (design doc 12.7). The five
    // seams read that one answer; this fixture is the four that are not a bar's
    // draw - a generator's terms, a producer's firing, a currency's activeWhen,
    // and an upgrade's effects.
    internal class MembershipFixture
    {
        public readonly DateTime Now = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);

        public readonly RootDefinition RootDef;
        public readonly ChapterDefinition Ch1Def;
        public readonly TierDefinition Tier1Def;
        public readonly CurrencyDefinition Cash;
        public readonly List<ChapterDefinition> Chapters = new();

        public RootScopeState Root;
        public ChapterScopeState Ch1;
        public ScopeState Tier1;

        public MembershipFixture()
        {
            Tier1Def = TestTree.MakeTier("tier1");
            Cash = TestTree.DeclareCurrency(Tier1Def, "cash");

            Ch1Def = TestTree.MakeChapter("ch1");
            Ch1Def.children.Add(Tier1Def);

            RootDef = TestTree.MakeRoot("root");
            Chapters.Add(Ch1Def);
        }

        public void Build()
        {
            Root = ScopeState.Build(ComposedContent.Compose(RootDef, Chapters));
            Ch1 = (ChapterScopeState)TestNavigation.Node(Root, Ch1Def);
            Tier1 = TestNavigation.Node(Root, Tier1Def);
        }

        public GroupDefinition Group(ScopeDefinition scope, string id, params Definition[] members)
        {
            var group = TestTree.MakeDefinition<GroupDefinition>(id);
            group.maxActive = 4;
            group.members.AddRange(members);
            scope.groups.Add(group);
            return group;
        }

        // The stage the player's choice writes, bypassing the command: a seam
        // test is not a SetActiveMembers test.
        public void Select(ScopeState scope, GroupDefinition group, params Definition[] members)
        {
            var set = new HashSet<string>();
            foreach (var member in members)
                set.Add(member.Id);
            scope.activeMembers[group.Id] = set;
        }

        public GeneratorDefinition Generator(string id, CurrencyDefinition pays, double rate)
        {
            var generator = TestTree.MakeDefinition<GeneratorDefinition>(id);
            generator.availableWhen = new Always();
            generator.costCurrency = Cash;
            generator.baseCost = 10;
            generator.growth = 1.15;
            generator.produces.Add(TestTree.Entry(pays, Stat.Rate, rate));
            Tier1Def.generators.Add(generator);
            return generator;
        }

        public GameContext Ctx(ScopeState scope) => new GameContext(scope, Now);
    }

    public class GroupMembershipTests
    {
        private static void AssertClose(double expected, BigNumber actual, string what = null) =>
            Assert.AreEqual(expected, actual.ToDouble(), 1e-9, what ?? string.Empty);

        // A generator's terms are zero while it is off, exactly as an unowned
        // one's are - the units are hired and the group decides whether they
        // play tonight.
        [Test]
        public void A_generator_is_off_until_its_group_holds_it()
        {
            var f = new MembershipFixture();
            var amp = f.Generator("practice_amp", f.Cash, 5);
            amp.produces.Add(TestTree.Entry(f.Cash, Stat.Yield, 100));
            var group = f.Group(f.Tier1Def, "stage", amp);
            f.Build();
            f.Tier1.generatorCounts["practice_amp"] = 2;

            AssertClose(0, Producer.GetRate(f.Ctx(f.Tier1), f.Cash), "off, so it pays no rate");
            TickSystem.Tick(f.Root, f.Ch1, ScriptableObject.CreateInstance<GameConfig>(), 10, f.Now.AddSeconds(10));
            AssertClose(0, f.Tier1.balances["cash"], "so the tick deposits nothing");
            Producer.FireGeneratorYield(f.Ctx(f.Tier1), amp);
            AssertClose(0, f.Tier1.balances["cash"], "and its fired yield pays nothing");

            f.Select(f.Tier1, group, amp);

            AssertClose(10, Producer.GetRate(f.Ctx(f.Tier1), f.Cash), "two units at 5");
            Producer.FireGeneratorYield(f.Ctx(f.Tier1), amp);
            AssertClose(200, f.Tier1.balances["cash"], "two units at 100");
        }

        // A producer fires nothing while it is off: Resolve answers zero for
        // every target, so the preview the row draws and the firing agree.
        [Test]
        public void A_producer_is_off_until_its_group_holds_it()
        {
            var f = new MembershipFixture();
            var jam = TestTree.MakeDefinition<ProducerDefinition>("jam");
            jam.produces.Add(TestTree.Entry(f.Cash, Stat.Yield, 3));
            f.Tier1Def.producers.Add(jam);
            var group = f.Group(f.Tier1Def, "stage", jam);
            f.Build();

            foreach (var (target, amount) in Producer.ResolveYield(f.Ctx(f.Tier1), jam))
                AssertClose(0, amount, target.Id);
            Producer.FireProducer(f.Ctx(f.Tier1), jam);
            AssertClose(0, f.Tier1.balances["cash"], "an off producer fires nothing");

            f.Select(f.Tier1, group, jam);
            Producer.FireProducer(f.Ctx(f.Tier1), jam);

            AssertClose(3, f.Tier1.balances["cash"], "the same firing pays once it is on");
        }

        // A currency reads inactive while it is off, which means what it has
        // always meant: zero terms into it, and a deposit throws.
        [Test]
        public void A_currency_is_inactive_until_its_group_holds_it()
        {
            var f = new MembershipFixture();
            var amp = f.Generator("practice_amp", f.Cash, 5);
            var group = f.Group(f.Tier1Def, "revealed", f.Cash);
            f.Build();
            f.Tier1.generatorCounts["practice_amp"] = 1;

            Assert.IsFalse(f.Cash.IsActive(f.Ctx(f.Tier1)), "off");
            AssertClose(0, Producer.GetRate(f.Ctx(f.Tier1), f.Cash), "so every source term into it is zero");

            f.Select(f.Tier1, group, f.Cash);

            Assert.IsTrue(f.Cash.IsActive(f.Ctx(f.Tier1)), "on");
            AssertClose(5, Producer.GetRate(f.Ctx(f.Tier1), f.Cash), "and the same source pays");
        }

        // An upgrade's effects gather only while it is on: bought and off, its
        // factor is absent, so the gather reads the 1x of an effect nothing
        // carries.
        [Test]
        public void An_upgrade_contributes_no_effect_until_its_group_holds_it()
        {
            var f = new MembershipFixture();
            f.Generator("practice_amp", f.Cash, 5);
            var strings = TestTree.MakeDefinition<UpgradeDefinition>("amp_strings");
            strings.gate = new Always();
            strings.costCurrency = f.Cash;
            strings.cost = 0;
            strings.effects.Add(new Effect { target = "cash", stat = Stat.Rate, multiplier = 2 });
            f.Tier1Def.upgrades.Add(strings);
            var group = f.Group(f.Tier1Def, "rack", strings);
            f.Build();
            f.Tier1.generatorCounts["practice_amp"] = 1;
            f.Tier1.purchasedUpgrades.Add("amp_strings");

            AssertClose(5, Producer.GetRate(f.Ctx(f.Tier1), f.Cash), "bought, but its carrier is off");

            f.Select(f.Tier1, group, strings);

            AssertClose(10, Producer.GetRate(f.Ctx(f.Tier1), f.Cash), "the effect joins once it is on");
        }

        // A definition may be listed by more than one group (12.7), and every
        // one of them has to hold it: two groups are two questions, and one
        // answer of no is the answer.
        [Test]
        public void A_member_of_two_groups_is_on_only_while_both_hold_it()
        {
            var f = new MembershipFixture();
            var amp = f.Generator("practice_amp", f.Cash, 5);
            var hired = f.Group(f.Tier1Def, "hired", amp);
            var tonight = f.Group(f.Tier1Def, "tonight", amp);
            f.Build();
            f.Tier1.generatorCounts["practice_amp"] = 1;

            f.Select(f.Tier1, hired, amp);
            AssertClose(0, Producer.GetRate(f.Ctx(f.Tier1), f.Cash), "one group holds it and the other does not");

            f.Select(f.Tier1, tonight, amp);
            AssertClose(5, Producer.GetRate(f.Ctx(f.Tier1), f.Cash), "both hold it");

            f.Select(f.Tier1, hired);
            AssertClose(0, Producer.GetRate(f.Ctx(f.Tier1), f.Cash), "and letting go of either turns it off");
        }
    }
}
