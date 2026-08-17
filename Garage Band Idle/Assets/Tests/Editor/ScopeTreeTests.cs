using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The scope TREE (design doc section 12, rule 12): a definition instantiated
    // more than once, and scopes placed under one another. What is pinned here is
    // only what step 1 built - the definition/instance split, the ordered
    // children, the outward read at depth 2, and the disposal and uniqueness
    // rules that make more than one instance safe:
    //
    // - identity is the caller's; a definition is shared, an instance is not
    // - two instances of one definition share no balance and no latch
    // - children keep the authored order, and their instance ids derive from the
    //   parent's, so a save block matched by id survives a re-ordered ladder
    // - a child reads a parent-owned currency through its own surface, and the
    //   write lands in the pool that owns the id
    // - disposing the root takes the subtree's subscriptions with it
    // - an id ANY scope in the assembly already holds is refused rather than
    //   duplicated - ancestor, sibling or cousin - and the tree's shape is
    //   fixed once the factory has attached it
    // - a definition cannot be its own ancestor, and each authored edge builds
    //   exactly one child
    public class ScopeTreeTests
    {
        [OneTimeTearDown]
        public void OneTimeTearDown() => TestContent.DestroyAll();

        // The standard currency set is all these fixtures need: every claim here
        // is about which SCOPE holds an id, so the currencies and their groups
        // are the default two-group set and only the rosters differ.
        private static ContentDatabase Database(params ScopeDefinition[] scopes)
            => TestContent.MakeDatabase(scopes: scopes);

        // The smallest tree with an outward read in it: a root owning 'cash' and
        // one child under it. The child's own roster is the parameter, because
        // "declares none" and "declares an id the parent already holds" are the
        // two cases below and they differ in nothing else.
        private static Scope BuildDepthTwo(List<string> childCurrencyIds = null)
        {
            var child = TestContent.MakeScope("tier_1", currencyIds: childCurrencyIds);
            var parent = TestContent.MakeScope("garage",
                currencyIds: new List<string> { "cash" },
                childScopeIds: new List<string> { "tier_1" });

            // the instance id is deliberately not the definition id: 'frontier'
            // is WHICH instantiation this is, and the child ids derive from it
            return ScopeFactory.Build(parent, "frontier", Database(parent, child));
        }

        // ---- the definition/instance split -----------------------------------

        // A replay economy (rule 7) is a second INSTANCE of one definition, so
        // identity has to be the caller's to assign while the definition stays
        // shared - slice 9 rematches save blocks by instance id, and a minted one
        // would orphan every block.
        [Test]
        public void TwoInstancesOfOneDefinition_CarryTheCallersInstanceIds_AndShareTheDefinition()
        {
            var definition = TestContent.MakeScope("garage", currencyIds: new List<string> { "cash" });
            var database = Database(definition);

            var frontier = ScopeFactory.Build(definition, "frontier", database);
            var replay = ScopeFactory.Build(definition, "replay", database);

            Assert.AreEqual("frontier", frontier.InstanceId,
                "the id the caller asked for, not one the factory minted");
            Assert.AreEqual("replay", replay.InstanceId);
            Assert.AreSame(definition, frontier.Definition);
            Assert.AreSame(definition, replay.Definition,
                "one definition, two instantiations - nothing was cloned");
        }

        // The point of the split: state lives on the instance. If a balance or a
        // latch could reach the definition, a replay would play the frontier's
        // progress and a second instance would be a second view of one economy.
        [Test]
        public void TwoInstancesOfOneDefinition_HoldIndependentTruth()
        {
            var definition = TestContent.MakeScope("garage",
                currencyIds: new List<string> { "cash" },
                flags: new List<FlagDeclaration> { new("demo_cut") });
            var database = Database(definition);
            var frontier = ScopeFactory.Build(definition, "frontier", database);
            var replay = ScopeFactory.Build(definition, "replay", database);

            frontier.Currencies.Add("cash", 25);
            frontier.Flags.Set("demo_cut");

            Assert.AreEqual(25.0, frontier.Currencies.Get("cash").ToDouble(), 1e-9);
            Assert.AreEqual(0.0, replay.Currencies.Get("cash").ToDouble(), 1e-9,
                "a balance lives in the instance's own pool");
            Assert.IsTrue(frontier.Flags.IsSet("demo_cut"));
            Assert.IsFalse(replay.Flags.IsSet("demo_cut"),
                "and so does a latch: one declaration, two independent flag systems");
        }

        // ---- the ladder ------------------------------------------------------

        // Order is authored, not sorted (design doc section 1): it is display
        // order and, later, same-depth reset order. The ids here are reverse
        // alphabetical on purpose, so an incidental sort anywhere in the recursion
        // would show up as the opposite answer. The instance ids derive from the
        // parent's rather than from a position, which is what lets the ladder be
        // re-ordered without orphaning a save block.
        [Test]
        public void Children_KeepTheAuthoredOrder_AndDeriveTheirInstanceIdsFromTheParents()
        {
            var second = TestContent.MakeScope("a_child");
            var first = TestContent.MakeScope("b_child");
            var parent = TestContent.MakeScope("garage",
                currencyIds: new List<string> { "cash" },
                childScopeIds: new List<string> { "b_child", "a_child" });

            var root = ScopeFactory.Build(parent, "frontier", Database(parent, first, second));

            Assert.AreEqual(2, root.Children.Count);
            Assert.AreEqual("b_child", root.Children[0].Definition.Id,
                "list order, not alphabetical order");
            Assert.AreEqual("a_child", root.Children[1].Definition.Id);
            Assert.AreEqual("frontier/b_child", root.Children[0].InstanceId,
                "parent instance id, then the child's DEFINITION id");
            Assert.AreEqual("frontier/a_child", root.Children[1].InstanceId);
            Assert.AreSame(root, root.Children[0].Parent, "and placement points back up");
        }

        // ---- reads go outward ------------------------------------------------

        // A currency exists in exactly one scope's pool, and every scope under it
        // can still reach the id through its own surface - which is what makes
        // moving a currency outward a pure data edit. The write proves the surface
        // resolves ownership rather than shadowing: it lands in the parent's pool,
        // the only place the balance exists.
        [Test]
        public void AChildReadsAParentOwnedCurrency_ThroughItsOwnSurface()
        {
            var root = BuildDepthTwo();
            var tier = root.Children[0];

            Assert.IsFalse(tier.Pool.Contains("cash"), "the child's roster declares none");

            tier.Currencies.Add("cash", 40);

            Assert.AreEqual(40.0, root.Pool.Get("cash").ToDouble(), 1e-9,
                "the write landed in the pool that owns the id");
            Assert.AreEqual(40.0, tier.Currencies.Get("cash").ToDouble(), 1e-9,
                "and the child reads that same balance back");
            Assert.IsFalse(tier.Pool.Contains("cash"),
                "one balance for the id, in one pool - the child never got a copy");
        }

        // ---- disposal takes the subtree --------------------------------------

        // Disposal discipline is load-bearing at N levels (rule 12): a discarded
        // parent whose children kept listening would feed a dead ladder's
        // subscribers changes for a chapter nobody is playing. The observable is
        // the child's dirty flag, because a child's condition context learns about
        // the parent's balances through the subscription disposal removes - hence
        // the control below, which shows the flag moving while it is still live.
        [Test]
        public void DisposingTheRoot_DisposesTheSubtree()
        {
            var root = BuildDepthTwo();
            var tier = root.Children[0];

            tier.Settle();
            Assert.IsFalse(tier.Conditions.IsDirty, "the child starts settled");

            // the control: while the subscription is live, the parent's pool
            // moving IS a condition input for the child
            root.Pool.Add("cash", 1);
            Assert.IsTrue(tier.Conditions.IsDirty, "a live child hears the pool it reads outward from");
            tier.Settle();
            Assert.IsFalse(tier.Conditions.IsDirty);

            root.Dispose();
            root.Pool.Add("cash", 1);

            Assert.IsFalse(tier.Conditions.IsDirty,
                "disposing the ROOT unsubscribed the child too - a live subscription would have dirtied it");
        }

        // ---- ids are unique tree-wide ----------------------------------------

        // An id in two scopes has two balances, and every read would silently pick
        // whichever the resolver reached first: a spend could charge one while the
        // UI read the other. The collision is reported and the OUTER holder keeps
        // the balance, so the child still reads the one balance that exists.
        [Test]
        public void AChildRosterNamingACurrencyTheParentHolds_IsRefused()
        {
            LogAssert.Expect(LogType.Error, new Regex(
                "roster names currency 'cash', which scope instance 'frontier' already holds"));

            var root = BuildDepthTwo(new List<string> { "cash" });
            var tier = root.Children[0];

            Assert.IsFalse(tier.Pool.Contains("cash"),
                "the shadowing entry is refused, not resolved");

            root.Currencies.Add("cash", 12);

            Assert.AreEqual(12.0, tier.Currencies.Get("cash").ToDouble(), 1e-9,
                "one balance: the child's surface resolves the id to the parent's pool");
            Assert.AreEqual(12.0, root.Pool.Get("cash").ToDouble(), 1e-9);
        }

        // Uniqueness is TREE-wide, not chain-wide: a sibling's pool sits on no
        // ancestor chain either child could walk, so this is exactly the
        // collision an outward check cannot see. The first sibling keeps the
        // balance; the second's entry is refused and reported against the
        // sibling that holds it.
        [Test]
        public void ASiblingRosterNamingACurrencyAnotherSiblingHolds_IsRefused()
        {
            LogAssert.Expect(LogType.Error, new Regex(
                "roster names currency 'fans', which scope instance 'frontier/first' already holds"));

            var first = TestContent.MakeScope("first", currencyIds: new List<string> { "fans" });
            var second = TestContent.MakeScope("second", currencyIds: new List<string> { "fans" });
            var parent = TestContent.MakeScope("garage",
                childScopeIds: new List<string> { "first", "second" });

            var root = ScopeFactory.Build(parent, "frontier", Database(parent, first, second));

            Assert.IsTrue(root.Children[0].Pool.Contains("fans"), "the first claim stands");
            Assert.IsFalse(root.Children[1].Pool.Contains("fans"),
                "the second is refused - one balance in the tree, not one per branch");
        }

        // The same collision one generation apart: the claim map is
        // assembly-wide, so a cousin's holding is as visible as a sibling's.
        [Test]
        public void ACousinRosterNamingACurrencyAnotherBranchHolds_IsRefused()
        {
            LogAssert.Expect(LogType.Error, new Regex(
                "roster names currency 'fans', which scope instance 'frontier/left/left_tier' already holds"));

            var leftTier = TestContent.MakeScope("left_tier", currencyIds: new List<string> { "fans" });
            var rightTier = TestContent.MakeScope("right_tier", currencyIds: new List<string> { "fans" });
            var left = TestContent.MakeScope("left", childScopeIds: new List<string> { "left_tier" });
            var right = TestContent.MakeScope("right", childScopeIds: new List<string> { "right_tier" });
            var parent = TestContent.MakeScope("garage",
                childScopeIds: new List<string> { "left", "right" });

            var root = ScopeFactory.Build(parent, "frontier",
                Database(parent, left, right, leftTier, rightTier));

            Assert.IsTrue(root.Children[0].Children[0].Pool.Contains("fans"));
            Assert.IsFalse(root.Children[1].Children[0].Pool.Contains("fans"),
                "a cousin's claim counts: uniqueness is the tree's, not a branch's");
        }

        // A definition naming itself describes a tree that contains itself; the
        // recursion must discover that as a reported edge, never as a stack
        // overflow. The scope itself still builds - the tree is the authored
        // content minus the impossible edge.
        [Test]
        public void AScopeNamingItselfAsAChild_IsRefusedWithoutRecursing()
        {
            LogAssert.Expect(LogType.Error, new Regex(
                "scope 'ouroboros' is an ancestor of itself"));

            var definition = TestContent.MakeScope("ouroboros",
                currencyIds: new List<string> { "cash" },
                childScopeIds: new List<string> { "ouroboros" });

            var root = ScopeFactory.Build(definition, "frontier", Database(definition));

            Assert.IsNotNull(root, "the scope builds; only the impossible edge is dropped");
            Assert.AreEqual(0, root.Children.Count);
        }

        // The two-link cycle, because a self-reference is the one cycle a local
        // check could catch - this proves the guard is the PATH, not the node.
        [Test]
        public void AMutualChildReference_IsRefusedWithoutRecursing()
        {
            LogAssert.Expect(LogType.Error, new Regex(
                "scope 'top' is an ancestor of itself.*top -> bottom -> top"));

            var top = TestContent.MakeScope("top",
                currencyIds: new List<string> { "cash" },
                childScopeIds: new List<string> { "bottom" });
            var bottom = TestContent.MakeScope("bottom",
                childScopeIds: new List<string> { "top" });

            var root = ScopeFactory.Build(top, "frontier", Database(top, bottom));

            Assert.AreEqual(1, root.Children.Count, "the ladder below the cycle still builds");
            Assert.AreEqual("frontier/bottom", root.Children[0].InstanceId);
            Assert.AreEqual(0, root.Children[0].Children.Count,
                "the edge back up is dropped, not followed");
        }

        // One instantiation per authored edge: a repeated child id would build
        // two children sharing one instance id, and slice 9 matches save blocks
        // by exactly that id - two claimants would make every block ambiguous.
        [Test]
        public void ARepeatedChildScopeId_BuildsOneChild()
        {
            LogAssert.Expect(LogType.Error, new Regex(
                "lists child scope id 'tier_1' twice"));

            var tier = TestContent.MakeScope("tier_1");
            var parent = TestContent.MakeScope("garage",
                currencyIds: new List<string> { "cash" },
                childScopeIds: new List<string> { "tier_1", "tier_1" });

            var root = ScopeFactory.Build(parent, "frontier", Database(parent, tier));

            Assert.AreEqual(1, root.Children.Count, "one edge, one child");
            Assert.AreEqual("frontier/tier_1", root.Children[0].InstanceId);
        }

        // The tree's shape is fixed once the factory has attached it: no action,
        // reset or operation adds or removes a scope, so a second attach is the
        // factory being run against a scope that already has a subtree. Refused
        // loudly, and the standing children are what a caller keeps.
        [Test]
        public void ASecondAttachChildren_IsRefused_AndLeavesTheChildrenUnchanged()
        {
            var root = BuildDepthTwo();
            var tier = root.Children[0];

            // a scope from another tree entirely, so "unchanged" is a claim about
            // a real attempt to add one rather than about an empty list
            var intruder = ScopeFactory.Build(TestContent.MakeScope("other"), "other",
                TestContent.MakeDatabase());

            LogAssert.Expect(LogType.Error, new Regex("whose children are already attached"));

            root.AttachChildren(new List<Scope> { intruder });

            Assert.AreEqual(1, root.Children.Count, "the second attach added nothing");
            Assert.AreSame(tier, root.Children[0], "and replaced nothing");
        }
    }
}
