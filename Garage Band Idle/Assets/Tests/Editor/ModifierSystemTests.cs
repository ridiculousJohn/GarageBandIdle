using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;
using UnityEngine.TestTools;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The modifier registry's own contracts. The load-bearing claims: one
    // composition rule for every system, a store that is REBUILT rather than
    // filtered (which is why grants are kept individually instead of
    // accumulated - a rebuild has to compose the same factors in the same
    // order), a derived modifier untouched by that rebuild because its lifetime
    // is its source's, and a target that addresses nothing refused rather than
    // silently stored.
    public class ModifierSystemTests
    {
        [OneTimeTearDown]
        public void OneTimeTearDown() => TestContent.DestroyAll();

        // a selector and the number it reaches are separate things (rule 11): one
        // describes a SET, the other is a member some set may contain
        private static readonly ModifierSelector CashYield = TestContent.Sel("cash_yield");
        private static readonly ModifierSelector FansRate = TestContent.Sel("fans_rate");
        private static readonly ModifierSubject CashYieldNumber = TestContent.YieldOf("cash");
        private static readonly ModifierSubject FansRateNumber = TestContent.RateOf("fans");

        // a derived modifier with a fixed value: enough to assert that a store
        // rebuild leaves derived modifiers standing, without dragging in the
        // currency pool RecordsIncomeModifier reads
        private class FixedDerived : DerivedModifier
        {
            private readonly ModifierSelector _selector;
            private readonly ModifierOperation _operation;
            private readonly BigNumber _value;

            public FixedDerived(ModifierSelector selector, ModifierOperation operation, double value)
            {
                _selector = selector;
                _operation = operation;
                _value = value;
            }

            public override ModifierSelector Selector => _selector;
            public override ModifierOperation Operation => _operation;
            public override BigNumber Value => _value;
        }

        // A serialized enum is an int, so an asset can carry a value no member
        // defines. Both writers come through IsWellFormed, which is what keeps such
        // a value out of the store: the value guard tests for Multiply by name, so
        // an undefined operation would slip past it and then compose as a multiply
        // anyway - a zero there wipes the whole product for the rest of the run.
        [Test]
        public void Grant_RefusesAnEnumValueNoMemberDefines()
        {
            var modifiers = new ModifierSystem();

            LogAssert.Expect(LogType.Error,
                "ModifierSystem: Grant on 'cash_yield' with operation 99, which no ModifierOperation defines. Ignoring.");
            modifiers.Grant(CashYield, (ModifierOperation)99, ContentScope.Run, 0);

            Assert.AreEqual(1.0, modifiers.For(CashYieldNumber).Multiply.ToDouble(), 1e-9,
                "the zero never reached the product it would have wiped");
        }

        [Test]
        public void UntargetedTarget_ComposesToIdentity()
        {
            var modifiers = new ModifierSystem();

            var composition = modifiers.For(CashYieldNumber);

            Assert.AreEqual(1.0, composition.Multiply.ToDouble(), 1e-9);
            Assert.AreEqual(7.0, composition.ApplyTo(7).ToDouble(), 1e-9, "identity leaves the base alone");
        }

        // A composition is a PRODUCT, and that is the whole of it (design doc rule
        // 11). With no Add beside the Multiply there is no application order for two
        // systems to disagree about: a flat bonus is a contribution to the number,
        // so the base already IS the sum of the flat parts.
        [Test]
        public void Composition_IsTheProductOfEveryMultiplierReachingTheNumber()
        {
            var modifiers = new ModifierSystem();

            modifiers.Grant(CashYield, ModifierOperation.Multiply, ContentScope.Run, 2);
            modifiers.Grant(CashYield, ModifierOperation.Multiply, ContentScope.PermanentInChapter, 3);

            var composition = modifiers.For(CashYieldNumber);
            Assert.AreEqual(6.0, composition.Multiply.ToDouble(), 1e-9, "2 x 3");
            Assert.AreEqual(30.0, composition.ApplyTo(5).ToDouble(), 1e-9, "5 x 6");
        }

        // Rule 11: a term is a NAME, and ANY term matching is enough - so a list of
        // names reaches all of them, exactly as it reads. There is no facet form:
        // `cash_rate` is the id of cash's rate, and ["cash","rate"] is not another
        // way to say it.
        [Test]
        public void AnyTermMatching_ReachesTheNumber()
        {
            var modifiers = new ModifierSystem();
            var bothRates = TestContent.Sel("cash_rate", "fans_rate");

            modifiers.Grant(bothRates, ModifierOperation.Multiply, ContentScope.Run, 2);

            Assert.AreEqual(2.0, modifiers.For(TestContent.RateOf("cash")).Multiply.ToDouble(), 1e-9,
                "naming two numbers reaches both");
            Assert.AreEqual(2.0, modifiers.For(FansRateNumber).Multiply.ToDouble(), 1e-9);
            Assert.AreEqual(1.0, modifiers.For(CashYieldNumber).Multiply.ToDouble(), 1e-9,
                "and nothing else - a yield is a different number with a different id");
        }

        // Narrowing is done by naming the narrower thing, never by intersecting
        // terms: a SET gets a tag on the members that belong to it, so "the rhythm
        // section's cash" is one term on exactly the lines meant.
        [Test]
        public void ATag_NamesASetWithoutEitherSideListingTheOther()
        {
            var modifiers = new ModifierSystem();
            var drummerCash = new ModifierSubject("drummer_cash", new[] { "rhythm_section" }, "drummer");
            var bassistCash = new ModifierSubject("bassist_cash", new[] { "rhythm_section" }, "bassist");
            var guitaristCash = new ModifierSubject("guitarist_cash", null, "guitarist");

            modifiers.Grant(TestContent.Sel("rhythm_section"), ModifierOperation.Multiply, ContentScope.Run, 2);

            Assert.AreEqual(2.0, modifiers.For(drummerCash).Multiply.ToDouble(), 1e-9);
            Assert.AreEqual(2.0, modifiers.For(bassistCash).Multiply.ToDouble(), 1e-9,
                "neither the buff nor the generator had to list the other");
            Assert.AreEqual(1.0, modifiers.For(guitaristCash).Multiply.ToDouble(), 1e-9,
                "membership is declared by the member");
        }

        // The owner half of a subject: a buff naming the generator reaches every
        // line it holds, while a buff naming one line reaches only that one. This is
        // the case the closed target enum could not express at all - "double the
        // drummer's cash" had no way to say WHICH output.
        [Test]
        public void NamingTheOwner_ReachesEveryLineItHolds_NamingALineReachesOne()
        {
            var modifiers = new ModifierSystem();
            var drummerCash = new ModifierSubject("drummer_cash", null, "drummer");
            var drummerFans = new ModifierSubject("drummer_fans", null, "drummer");

            modifiers.Grant(TestContent.Sel("drummer_cash"), ModifierOperation.Multiply, ContentScope.Run, 2);
            Assert.AreEqual(2.0, modifiers.For(drummerCash).Multiply.ToDouble(), 1e-9);
            Assert.AreEqual(1.0, modifiers.For(drummerFans).Multiply.ToDouble(), 1e-9,
                "'Bigger Kit' doubles the cash line and leaves the fans line alone");

            modifiers.Grant(TestContent.Sel("drummer"), ModifierOperation.Multiply, ContentScope.Run, 3);
            Assert.AreEqual(6.0, modifiers.For(drummerCash).Multiply.ToDouble(), 1e-9);
            Assert.AreEqual(3.0, modifiers.For(drummerFans).Multiply.ToDouble(), 1e-9,
                "naming the generator reaches both, through the owner the line offers");
        }

        // The empty selector is the one that reaches everything, and it is a
        // deliberate authoring act rather than a default: nothing narrows it, so
        // it composes into every number in scope, including ones added later.
        [Test]
        public void AnEmptySelector_ReachesEveryNumber()
        {
            var modifiers = new ModifierSystem();

            modifiers.Grant(ModifierSelector.Everything, ModifierOperation.Multiply, ContentScope.Run, 2);

            Assert.AreEqual(2.0, modifiers.For(CashYieldNumber).Multiply.ToDouble(), 1e-9);
            Assert.AreEqual(2.0, modifiers.For(FansRateNumber).Multiply.ToDouble(), 1e-9);
            Assert.AreEqual(2.0, modifiers.For(TestContent.Num("anything_at_all")).Multiply.ToDouble(), 1e-9,
                "including a number no content declares yet");
        }

        // The store is rebuilt, never filtered (design doc section 12, rule 6):
        // ResetGranted empties it whatever the scopes were, and the permanent
        // effects come back because the FACTS behind them come back. Asserting
        // that a permanent grant is cleared here is not a gap - it is the
        // property. A method that dropped run-scoped grants and left permanent
        // ones would be a second mechanism for arriving at a modifier set,
        // beside the projection, able to disagree with it silently.
        //
        // Grants are still kept individually rather than accumulated: what that
        // buys after this change is that a rebuild composes the same factors in
        // the same order, which a collapsed product could not reproduce.
        [Test]
        public void ResetGranted_ClearsEveryGrantWhateverItsScope()
        {
            var modifiers = new ModifierSystem();

            modifiers.Grant(CashYield, ModifierOperation.Multiply, ContentScope.Run, 1.5);
            modifiers.Grant(CashYield, ModifierOperation.Multiply, ContentScope.Run, 3);
            modifiers.Grant(CashYield, ModifierOperation.Multiply, ContentScope.PermanentInChapter, 2);
            modifiers.Grant(FansRate, ModifierOperation.Multiply, ContentScope.PermanentInChapter, 4);

            Assert.IsTrue(modifiers.ResetGranted());

            Assert.AreEqual(1.0, modifiers.For(CashYieldNumber).Multiply.ToDouble(), 1e-9,
                "no grant survives a rebuild, permanent included");
            Assert.AreEqual(1.0, modifiers.For(FansRateNumber).Multiply.ToDouble(), 1e-9,
                "the store composes to identity until the projection re-runs");
        }

        [Test]
        public void ResetGranted_IsSilentAndFalseWhenThereIsNothingToClear()
        {
            var modifiers = new ModifierSystem();
            modifiers.AddDerived(new FixedDerived(CashYield, ModifierOperation.Multiply, 2));
            var changes = 0;
            modifiers.Changed += _ => changes++;

            Assert.IsFalse(modifiers.ResetGranted(), "nothing granted to clear");
            Assert.AreEqual(0, changes, "and nothing notified");
            Assert.AreEqual(2.0, modifiers.For(CashYieldNumber).Multiply.ToDouble(), 1e-9,
                "a derived modifier is untouched: its lifetime is its source's, so there is nothing to rebuild");
        }

        // state, then notify: every target settles before the first
        // notification, so no subscriber observes one target cleared while
        // another still holds its grants
        [Test]
        public void ResetGranted_SettlesEveryTargetBeforeNotifying()
        {
            var modifiers = new ModifierSystem();
            modifiers.Grant(CashYield, ModifierOperation.Multiply, ContentScope.Run, 2);
            modifiers.Grant(FansRate, ModifierOperation.Multiply, ContentScope.PermanentInChapter, 3);

            var notifications = 0;
            var observedHalfReset = false;
            modifiers.Changed += _ =>
            {
                notifications++;
                if (modifiers.For(CashYieldNumber).Multiply != BigNumber.One
                    || modifiers.For(FansRateNumber).Multiply != BigNumber.One)
                    observedHalfReset = true;
            };

            modifiers.ResetGranted();

            Assert.AreEqual(2, notifications, "one notification per target that changed");
            Assert.IsFalse(observedHalfReset, "every subscriber sees all targets settled");
        }

        // What a display filters on. The notification carries the SELECTOR that
        // changed, and a subscriber asks it about its own subject - the same
        // question the composition asks, so a row can never refresh on a grant the
        // composition ignored or miss one it counted. The grant has settled by the
        // time it fires, so a row repainting inside the handler reads the new rate
        // rather than the one it was about to replace.
        [Test]
        public void Grant_NotifiesWithTheSelectorItCarries_AfterTheGrantSettles()
        {
            var modifiers = new ModifierSystem();
            var amp = TestContent.Num("practice_amp");
            var drummer = TestContent.Num("drummer");

            var notifications = 0;
            var reachedTheAmp = false;
            var reachedTheDrummer = false;
            var multiplyWhenNotified = 0.0;
            modifiers.Changed += selector =>
            {
                notifications++;
                reachedTheAmp = selector.Matches(amp);
                reachedTheDrummer = selector.Matches(drummer);
                multiplyWhenNotified = modifiers.For(amp).Multiply.ToDouble();
            };

            modifiers.Grant(TestContent.Sel("practice_amp"), ModifierOperation.Multiply, ContentScope.Run, 2);

            Assert.AreEqual(1, notifications, "one notification for the one grant");
            Assert.IsTrue(reachedTheAmp, "the amp's row sees a change that reaches it");
            Assert.IsFalse(reachedTheDrummer, "another generator's row can tell this is not its own");
            Assert.AreEqual(2.0, multiplyWhenNotified, 1e-9, "the grant was already stored when the signal fired");
        }

        // An uninitialized OPERATION is still a caller mistake. There is no
        // addressing mistake left to refuse: every selector shape is legal, since
        // empty reaches everything by rule 11, and a term naming nothing reachable
        // is a content error boot validation reports against the asset that
        // authored it - where the id can be named, which this class cannot do
        // without knowing what content exists.
        [Test]
        public void Grant_RefusesAnUninitializedOperation()
        {
            var modifiers = new ModifierSystem();

            LogAssert.Expect(LogType.Error,
                "ModifierSystem: Grant on 'cash_yield' with operation None (uninitialized). Ignoring.");
            modifiers.Grant(CashYield, ModifierOperation.None, ContentScope.Run, 2);

            Assert.AreEqual(1.0, modifiers.For(CashYieldNumber).Multiply.ToDouble(), 1e-9, "nothing was stored");
        }

        // a modifier with no scope has no lifetime, so no reset could ever treat
        // it correctly
        [Test]
        public void Grant_RefusesAnUnscopedModifier()
        {
            var modifiers = new ModifierSystem();

            LogAssert.Expect(LogType.Error,
                "ModifierSystem: Grant on 'cash_yield' with scope None. Ignoring - an unscoped modifier has no lifetime.");
            modifiers.Grant(CashYield, ModifierOperation.Multiply, ContentScope.None, 2);

            Assert.AreEqual(1.0, modifiers.For(CashYieldNumber).Multiply.ToDouble(), 1e-9);
        }

        // The negative-add and zero-add refusals are gone with Add itself (rule 11):
        // a flat bonus is a ProductionContribution now, and a negative one is
        // refused where it is authored - the importer skips it, boot validation
        // reports it, and CurrencyProducer floors a line at zero. See
        // CurrencyProducerTests.
        //
        // a non-positive multiplier is the one value guard left, and it is the
        // dangerous one: a zero wipes the whole product for the rest of the run
        // naming an id is what keeps one generator's buff off another's output
        [Test]
        public void ANamedId_ReachesOnlyThatNumber()
        {
            var modifiers = new ModifierSystem();

            modifiers.Grant(TestContent.Sel("practice_amp"), ModifierOperation.Multiply, ContentScope.Run, 2);

            Assert.AreEqual(2.0, modifiers.For(TestContent.Num("practice_amp")).Multiply.ToDouble(), 1e-9);
            Assert.AreEqual(1.0, modifiers.For(TestContent.Num("drummer")).Multiply.ToDouble(), 1e-9,
                "another id is another number");
        }

        // A subject offers its OWNER's id too, so a coarse buff reaches every line
        // its holder contributes without listing them - and would silently miss any
        // added later if it had to. "Double the drummer" and "double the drummer's
        // cash" are therefore different selectors, each answerable on its own.
        [Test]
        public void ASelectorNamingTheOwner_ReachesEveryNumberItHolds()
        {
            var modifiers = new ModifierSystem();
            var cash = new ModifierSubject("drummer_cash", null, "drummer", new[] { "bandmate" });
            var fans = new ModifierSubject("drummer_fans", null, "drummer", new[] { "bandmate" });

            modifiers.Grant(TestContent.Sel("drummer"), ModifierOperation.Multiply, ContentScope.Run, 2);

            Assert.AreEqual(2.0, modifiers.For(cash).Multiply.ToDouble(), 1e-9);
            Assert.AreEqual(2.0, modifiers.For(fans).Multiply.ToDouble(), 1e-9, "both of the owner's lines");

            modifiers.Grant(TestContent.Sel("drummer_cash"), ModifierOperation.Multiply, ContentScope.Run, 3);

            Assert.AreEqual(6.0, modifiers.For(cash).Multiply.ToDouble(), 1e-9, "and one line can be named alone");
            Assert.AreEqual(2.0, modifiers.For(fans).Multiply.ToDouble(), 1e-9,
                "which is the whole point: a cash buff cannot reach the fan line");
        }

        // a tag names a set across owners, declared by the member rather than
        // re-listed at every buff that means it
        [Test]
        public void ASelectorNamingATag_ReachesEveryMemberCarryingIt()
        {
            var modifiers = new ModifierSystem();
            var drummerCash = new ModifierSubject("drummer_cash", new[] { "rhythm_section" });
            var bassistCash = new ModifierSubject("bassist_cash", new[] { "rhythm_section" });
            var guitaristCash = new ModifierSubject("guitarist_cash");

            modifiers.Grant(TestContent.Sel("rhythm_section"), ModifierOperation.Multiply, ContentScope.Run, 2);

            Assert.AreEqual(2.0, modifiers.For(drummerCash).Multiply.ToDouble(), 1e-9);
            Assert.AreEqual(2.0, modifiers.For(bassistCash).Multiply.ToDouble(), 1e-9);
            Assert.AreEqual(1.0, modifiers.For(guitaristCash).Multiply.ToDouble(), 1e-9,
                "and nothing that does not carry the tag");
        }
    }
}
