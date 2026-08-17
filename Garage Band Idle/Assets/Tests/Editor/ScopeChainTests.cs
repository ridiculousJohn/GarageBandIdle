using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The chain (design doc section 12, rule 12): what is in scope from one
    // scope - its own truth, then its ancestors', outward to the root - and the
    // three resolutions over that ONE iteration. ScopeTreeTests pins the tree's
    // shape and the outward read at depth 2; what is pinned here is what step 2
    // built, which is every read that has to pass the parent and every change
    // signal that has to come back inward:
    //
    // - a currency resolves to its FIRST owner outward, however many links away
    // - a flag is satisfied by ANY link outward, and by no sibling's
    // - modifiers accumulate across EVERY link, and no sibling's store joins in
    // - an outer latch and an outer grant each reach an inner scope through one
    //   subscription, exactly once per change, whatever the depth
    // - and disposal severs all of that, because a discarded node still
    //   cascading would feed a dead scope's subscribers a ladder nobody plays
    public class ScopeChainTests
    {
        [OneTimeTearDown]
        public void OneTimeTearDown() => TestContent.DestroyAll();

        // The standard currency set is all these fixtures need: every claim here
        // is about which SCOPE holds an id or a fact, so the currencies and their
        // groups are the default two-group set and only the rosters differ.
        private static ContentDatabase Database(params ScopeDefinition[] scopes)
            => TestContent.MakeDatabase(scopes: scopes);

        // The one fixture with content inside a scope: the generator whose line an
        // outer store's grant has to reach.
        private static ContentDatabase Database(GeneratorDefinition generator,
            params ScopeDefinition[] scopes)
            => TestContent.MakeDatabase(generators: new[] { generator }, scopes: scopes);

        // The smallest tree with a read that has to pass the PARENT: root -> mid
        // -> leaf, the root owning 'cash'. The middle scope's roster is the
        // parameter, because "owns nothing" and "owns 'fans'" are the two cases
        // below and they differ in nothing else.
        private static Scope BuildDepthThree(List<string> midCurrencyIds = null)
        {
            var leaf = TestContent.MakeScope("tier_2");
            var mid = TestContent.MakeScope("tier_1",
                currencyIds: midCurrencyIds,
                childScopeIds: new List<string> { "tier_2" });
            var root = TestContent.MakeScope("garage",
                currencyIds: new List<string> { "cash" },
                childScopeIds: new List<string> { "tier_1" });

            return ScopeFactory.Build(root, "frontier", Database(root, mid, leaf));
        }

        // ---- a currency resolves to its first owner outward --------------------

        // This is the exact gap step 1 documented and step 2 closed. Step 1 shipped
        // with "reads past the parent" listed as still open: a scope could reach
        // its parent's pool through the parent link and nothing further, so depth 2
        // was the whole of the outward read and ScopeTreeTests pins only that. A
        // grandchild reading the root's balance is the case that needs the walk
        // rather than the link - and it is the ordinary case in a real ladder,
        // where the permanent pool sits past the root.
        [Test]
        public void AGrandchildReadsARootOwnedCurrency_ThroughItsOwnSurface()
        {
            var root = BuildDepthThree();
            var mid = root.Children[0];
            var leaf = mid.Children[0];

            Assert.IsFalse(mid.Pool.Contains("cash"), "no scope between declares the id");
            Assert.IsFalse(leaf.Pool.Contains("cash"), "and the leaf's roster declares none");

            leaf.Currencies.Add("cash", 40);

            Assert.AreEqual(40.0, root.Pool.Get("cash").ToDouble(), 1e-9,
                "the write landed two links out, in the pool that owns the id");
            Assert.AreEqual(40.0, leaf.Currencies.Get("cash").ToDouble(), 1e-9,
                "and the grandchild reads that same balance back through its own surface");
            Assert.IsFalse(leaf.Pool.Contains("cash"),
                "one balance for the id, in one pool - no link on the way out got a copy");
        }

        // FIRST owner outward wins, which is what makes moving a currency outward
        // a pure data edit: the write stops at the nearest pool holding the id and
        // nothing further out is touched. The root owns 'cash' and never 'fans', so
        // a walk that ran to the root regardless would have nowhere to land.
        [Test]
        public void ACurrencyResolvesToTheFirstOwnerOutward_NotTheOutermostLink()
        {
            var root = BuildDepthThree(new List<string> { "fans" });
            var mid = root.Children[0];
            var leaf = mid.Children[0];

            leaf.Currencies.Add("fans", 7);

            Assert.AreEqual(7.0, mid.Pool.Get("fans").ToDouble(), 1e-9,
                "the nearest owner outward took the write");
            Assert.AreEqual(7.0, leaf.Currencies.Get("fans").ToDouble(), 1e-9,
                "and answers the read");
            Assert.IsFalse(root.Pool.Contains("fans"),
                "while the outermost link never held the id at all");
        }

        // ---- a flag is satisfied by any link outward ---------------------------

        // Reads go outward for flags too, by a different rule: ANY link having it
        // set satisfies the resolution, so an inner gate can watch a fact an outer
        // scope latches without the inner scope declaring anything. A sibling is
        // the contrast that shows the walk is the CHAIN and not the tree - a
        // sibling's registry sits on no chain the other could reach, and flag ids
        // may legitimately repeat across scopes precisely because of that.
        [Test]
        public void AnOuterFlagSatisfiesAnInnerScopesResolution_ASiblingsDoesNot()
        {
            var first = TestContent.MakeScope("first",
                flags: new List<FlagDeclaration> { new("soundcheck") });
            var second = TestContent.MakeScope("second");
            var garage = TestContent.MakeScope("garage",
                currencyIds: new List<string> { "cash" },
                flags: new List<FlagDeclaration> { new("gig_booked") },
                childScopeIds: new List<string> { "first", "second" });

            var root = ScopeFactory.Build(garage, "frontier", Database(garage, first, second));
            var inner = root.Children[0];
            var sibling = root.Children[1];

            root.Flags.Set("gig_booked");

            Assert.IsTrue(inner.Conditions.IsFlagSet("gig_booked"),
                "the resolution a flagSet condition asks through");
            Assert.IsTrue(inner.Chain.ResolveFlag("gig_booked"), "and the walk under it");
            Assert.IsFalse(inner.Flags.IsSet("gig_booked"),
                "the inner registry never got a copy - the answer came from the outer link");

            inner.Flags.Set("soundcheck");

            Assert.IsFalse(sibling.Conditions.IsFlagSet("soundcheck"),
                "a sibling's latch is on no chain this scope walks");
            Assert.IsFalse(sibling.Chain.ResolveFlag("soundcheck"));
        }

        // ---- notifications come inward ----------------------------------------

        // The asymmetry's other half: reads go outward, so an inner gate on an
        // outer fact has to re-evaluate when that fact moves. Each chain node
        // aggregates its outer node's flag events into its own and the inner
        // condition context subscribes to that aggregate alone, so an outer latch
        // dirties the inner scope with no scope in between forwarding anything.
        [Test]
        public void AnOuterFlagLatch_DirtiesTheInnerScope()
        {
            var tier = TestContent.MakeScope("tier_1");
            var garage = TestContent.MakeScope("garage",
                currencyIds: new List<string> { "cash" },
                flags: new List<FlagDeclaration> { new("gig_booked") },
                childScopeIds: new List<string> { "tier_1" });

            var root = ScopeFactory.Build(garage, "frontier", Database(garage, tier));
            var inner = root.Children[0];

            // the live control the disposal test uses: the flag has to be shown
            // moving from a settled child, or "it moved" says nothing about what
            // moved it
            inner.Settle();
            Assert.IsFalse(inner.Conditions.IsDirty, "the child starts settled");

            root.Flags.Set("gig_booked");

            Assert.IsTrue(inner.Conditions.IsDirty,
                "the aggregated FlagSet cascaded inward, so a gate on it re-asks at the next settle");
        }

        // ---- modifiers accumulate across every link ---------------------------

        // The third resolution: EVERY link contributes, so a store outward composes
        // into an inner scope's numbers. Nothing in the child had to be told the
        // buff exists - the generator composes through its chain, which folds each
        // link's store in turn. The sibling grant is the contrast again: its store
        // is on no chain this line walks, so it reaches nothing here.
        [Test]
        public void AParentGrant_ScalesAChildGeneratorsLine_ASiblingGrantDoesNot()
        {
            var drummer = TestContent.MakeGenerator("drummer", "cash",
                baseCost: 10, costGrowth: 1.15, baseOutput: 3);
            var band = TestContent.MakeScope("band", generatorIds: new List<string> { "drummer" });
            var merch = TestContent.MakeScope("merch");
            var garage = TestContent.MakeScope("garage",
                currencyIds: new List<string> { "cash" },
                childScopeIds: new List<string> { "band", "merch" });

            var root = ScopeFactory.Build(garage, "frontier", Database(drummer, garage, band, merch));
            var bandScope = root.Children[0];
            var merchScope = root.Children[1];
            var generator = bandScope.Generators.Get("drummer");

            // one unit owned, because an unowned generator's line is zero whatever
            // reaches it. The cash comes from the root's pool through the child's
            // own surface, which is the outward read above doing its ordinary job.
            root.Currencies.Add("cash", generator.NextCost);
            Assert.IsTrue(generator.TryBuy(bandScope.Currencies), "the buy resolves the cost outward");
            Assert.AreEqual(3.0, TestContent.LineValue(generator).ToDouble(), 1e-9,
                "the base line, with nothing composed into it yet");

            merchScope.Modifiers.Grant(TestContent.Sel("drummer_cash"),
                ModifierOperation.Multiply, ContentScope.PermanentInChapter, 2);

            Assert.AreEqual(3.0, TestContent.LineValue(generator).ToDouble(), 1e-9,
                "a sibling's store is on no chain this line walks");

            root.Modifiers.Grant(TestContent.Sel("drummer_cash"),
                ModifierOperation.Multiply, ContentScope.PermanentInChapter, 2);

            Assert.AreEqual(6.0, TestContent.LineValue(generator).ToDouble(), 1e-9,
                "the parent's store folds into the composition the child's line reads");
        }

        // ONE subscription however deep the tree: each node aggregates its own
        // stores' signals with its outer node's ALREADY-AGGREGATED ones, so a grant
        // arrives once. Depth 3 is what makes "once" a real claim - a node
        // subscribing to every ancestor directly, while the ancestors still
        // cascade, would deliver the root's grant to the leaf twice.
        [Test]
        public void AnOuterGrant_RaisesTheInnerChainsModifiersChangedOncePerGrant()
        {
            var root = BuildDepthThree();
            var mid = root.Children[0];
            var leaf = mid.Children[0];

            var signals = 0;
            leaf.Chain.ModifiersChanged += _ => signals++;

            mid.Modifiers.Grant(TestContent.Sel("cash_rate"),
                ModifierOperation.Multiply, ContentScope.PermanentInChapter, 2);

            Assert.AreEqual(1, signals, "the parent's grant, once");

            root.Modifiers.Grant(TestContent.Sel("cash_rate"),
                ModifierOperation.Multiply, ContentScope.PermanentInChapter, 2);

            Assert.AreEqual(2, signals, "and the root's, once - not once per link it crossed");
        }

        // ---- disposal severs the cascade --------------------------------------

        // At N levels that disposal discipline is load-bearing (rule 12): a
        // discarded node still cascading would feed a dead scope's subscribers
        // changes for a ladder nobody is playing. Both aggregates have to go, so
        // the flag half and the modifier half are checked together here.
        //
        // The child's observables are captured BEFORE the dispose, the same shape
        // ScopeTreeTests.DisposingTheRoot_DisposesTheSubtree uses, because disposal
        // takes the whole subtree - there is nothing to subscribe to afterwards.
        // Each is shown moving while the cascade is live, or "it did not move"
        // proves nothing.
        [Test]
        public void DisposingTheRoot_SeversTheCascade_ForFlagsAndModifiersAlike()
        {
            var tier = TestContent.MakeScope("tier_1");
            var garage = TestContent.MakeScope("garage",
                currencyIds: new List<string> { "cash" },
                flags: new List<FlagDeclaration> { new("gig_booked"), new("encore_called") },
                childScopeIds: new List<string> { "tier_1" });

            var root = ScopeFactory.Build(garage, "frontier", Database(garage, tier));
            var inner = root.Children[0];

            var signals = 0;
            inner.Chain.ModifiersChanged += _ => signals++;

            // the controls, while the cascade is live
            inner.Settle();
            root.Modifiers.Grant(TestContent.Sel("cash_rate"),
                ModifierOperation.Multiply, ContentScope.PermanentInChapter, 2);
            Assert.AreEqual(1, signals, "a live child hears the store it composes outward through");
            root.Flags.Set("gig_booked");
            Assert.IsTrue(inner.Conditions.IsDirty, "and the latch it resolves outward to");
            inner.Settle();

            root.Dispose();

            // a second declared flag, because a latch is one-way: re-setting
            // 'gig_booked' is silent whether the cascade was severed or not, so it
            // could never tell the two apart
            root.Flags.Set("encore_called");
            root.Modifiers.Grant(TestContent.Sel("cash_rate"),
                ModifierOperation.Multiply, ContentScope.PermanentInChapter, 3);

            Assert.AreEqual(1, signals,
                "disposing the ROOT unhooked the child's node - a live one would have raised again");
            Assert.IsFalse(inner.Conditions.IsDirty,
                "and its conditions heard nothing: no dirty flag for a scope nobody is playing");
        }
    }
}
