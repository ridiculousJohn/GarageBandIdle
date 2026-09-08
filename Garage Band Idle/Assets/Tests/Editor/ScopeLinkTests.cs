using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Events;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The link pass and the one enumeration it walks (design doc 12.14.8).
    // What a static reference means is decided once, when a tree is built, so
    // two trees from one content set hold their own answers and nothing
    // searches at runtime.
    public class ScopeLinkTests
    {
        // The enumeration is the ONE place a kind of action list is named. The
        // linker walks it, the validator walks it, and neither spells the sites
        // out - so a scope authored with one of each names all of them here,
        // and a seventh kind added anywhere else would be invisible to both.
        [Test]
        public void An_interior_scope_enumerates_every_action_list_it_declares_with_its_site()
        {
            var tier = TestTree.MakeTier("enumerated");

            var rung = new Rung { offerCondition = new Always(), actions = { new SetFlag { flagId = "rung" } } };
            tier.rung = rung;

            var trigger = TestTree.MakeDefinition<TriggerDefinition>("herald");
            trigger.condition = new Always();
            trigger.actions.Add(new SetFlag { flagId = "trigger" });
            tier.triggers.Add(trigger);

            var group = TestTree.MakeDefinition<BarGroupDefinition>("covers");
            var bar = TestTree.MakeDefinition<BarDefinition>("cover_1");
            bar.fillAmount = 10;
            bar.fillRate = 1;
            bar.onComplete.Add(new SetFlag { flagId = "bar" });
            group.bars.Add(bar);
            tier.barGroups.Add(group);

            var upgrade = TestTree.MakeDefinition<UpgradeDefinition>("stage_presence");
            upgrade.actions.Add(new SetFlag { flagId = "upgrade" });
            tier.upgrades.Add(upgrade);

            var evt = TestTree.MakeDefinition<EventDefinition>("garage_jam");
            evt.onEntry.Add(new SetFlag { flagId = "entry" });
            evt.rewards.Add(new SetFlag { flagId = "reward" });
            evt.onEnd.Add(new SetFlag { flagId = "end" });
            tier.events.Add(evt);

            var sites = tier.ActionLists().ToList();

            CollectionAssert.AreEqual(
                new object[]
                {
                    rung.actions, trigger.actions, bar.onComplete, upgrade.actions,
                    evt.onEntry, evt.rewards, evt.onEnd
                },
                sites.Select(site => (object)site.Actions).ToArray(),
                "every list this scope declares, once each, in one enumeration");

            // Each carries the site it belongs to, which is what the validator
            // reports a finding against and the linker resolves in.
            var owners = new[] { "enumerated", "herald", "cover_1", "stage_presence",
                                 "garage_jam", "garage_jam", "garage_jam" };
            for (var i = 0; i < owners.Length; i++)
                StringAssert.Contains(owners[i], sites[i].Site);
            Assert.AreEqual(sites.Count, sites.Select(site => site.Site).Distinct().Count(),
                "no two sites read the same");
        }

        // Root has no rung field and no events list at all (12.3): the kinds it
        // cannot have are unauthorable rather than filtered, so there is
        // nothing for the enumeration to skip.
        [Test]
        public void A_root_scope_enumerates_only_the_lists_it_can_hold()
        {
            var root = TestTree.MakeRoot("root");

            var trigger = TestTree.MakeDefinition<TriggerDefinition>("story");
            trigger.condition = new Always();
            trigger.actions.Add(new SetFlag { flagId = "seen" });
            root.triggers.Add(trigger);

            var upgrade = TestTree.MakeDefinition<UpgradeDefinition>("backstage");
            upgrade.actions.Add(new SetFlag { flagId = "bought" });
            root.upgrades.Add(upgrade);

            CollectionAssert.AreEqual(
                new object[] { trigger.actions, upgrade.actions },
                root.ActionLists().Select(site => (object)site.Actions).ToArray());
        }

        // Nothing is written into an asset, so two games built from one content
        // set hold separate links - and every action and condition in each
        // reads and mutates only its own nodes.
        [Test]
        public void Two_trees_from_one_content_set_read_and_mutate_only_their_own_nodes()
        {
            var tree = new TestTree();
            tree.Tier1Def.rung = new Rung
            {
                offerCondition = new CurrencyAtLeast { currency = tree.Fans, threshold = 50 },
                actions =
                {
                    new AddCurrency { currencies = { tree.Ch1Records }, amount = 1 },
                    new ResetScope { scope = tree.Tier1Def }
                }
            };
            tree.Rebuild();

            var aCh1 = tree.Ch1;
            var aTier1 = tree.Tier1;
            var b = ScopeState.Build(tree.Content);
            var bCh1 = (ChapterScopeState)TestNavigation.Node(b, tree.Ch1Def);
            var bTier1 = (TierScopeState)TestNavigation.Node(b, tree.Tier1Def);
            Assert.AreNotSame(aTier1, bTier1, "two trees, one content set");

            aTier1.balances["fans"] = 60;
            bTier1.balances["fans"] = 60;
            bTier1.activeEvent = new ActiveEvent { eventId = "open_mic", goalReached = true };

            // The conditions read the tree they are asked in, not the asset.
            Assert.IsTrue(new EventRewardPending().Evaluate(new GameContext(bTier1, tree.Now)));
            Assert.IsFalse(new EventRewardPending().Evaluate(new GameContext(aTier1, tree.Now)));

            // A's rung is offered and B's is refused, from the same Rung object.
            Assert.IsTrue(tree.Tier1Def.rung.IsOffered(new GameContext(aTier1, tree.Now)));
            Assert.IsFalse(tree.Tier1Def.rung.IsOffered(new GameContext(bTier1, tree.Now)),
                "B's own armed reward refuses B's reset");

            Assert.IsTrue(tree.Tier1Def.rung.TryExecute(new GameContext(aTier1, tree.Now)));

            Assert.AreEqual(BigNumber.One, aCh1.balances["ch1_records"]);
            Assert.AreEqual(BigNumber.Zero, aTier1.balances["fans"], "A's tier cleared");
            Assert.AreEqual(BigNumber.Zero, bCh1.balances["ch1_records"], "B saw none of it");
            Assert.AreEqual((BigNumber)60, bTier1.balances["fans"]);
            Assert.IsNotNull(bTier1.activeEvent, "and B's record is still where it was");
        }

        // The link store is per node and holder-keyed, so a definition tree
        // that never built one of its sites is a thrown miss rather than a
        // silent search: reaching that means construction skipped a site.
        [Test]
        public void A_link_the_pass_never_stored_throws_rather_than_searching()
        {
            var tree = new TestTree();
            var unlinked = new ResetScope { scope = tree.Tier1Def };   // authored nowhere

            var thrown = Assert.Throws<System.InvalidOperationException>(
                () => tree.Tier1.Link(unlinked));
            StringAssert.Contains("link", thrown.Message);
            Assert.Throws<System.InvalidOperationException>(() => unlinked.Execute(tree.Ctx(tree.Tier1)));
        }
    }
}
