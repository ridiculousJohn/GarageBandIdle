using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;
using UnityEngine.TestTools;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The producer's own contracts (design doc section 12, rule 13). The
    // load-bearing claims: a currency's rate and its yield are separate
    // numbers that move independently, the currency-level composition applies
    // ONCE over the summed contributions rather than per contributor, a gated
    // contribution is worth the same nothing to the readout and to the payout,
    // an empty or wholly dormant producer composes to zero rather than letting
    // a multiplier be the sole source of a number, and the list is REBUILT
    // rather than registered so nothing needs unhooking when a contributor
    // goes away.
    //
    // The fixtures hand a producer its list directly, without the
    // ProductionSystem that assembles one in the game, so a failure here is the
    // producer's own rather than the assembler's.
    public class CurrencyProducerTests
    {
        [OneTimeTearDown]
        public void OneTimeTearDown() => TestContent.DestroyAll();

        // the ids of cash's two numbers, derived the same way the producer derives
        // them - a fixture spelling the strings could agree with itself and disagree
        // with the game
        private static readonly ModifierSelector CashRate =
            TestContent.Sel(CurrencyProducer.NumberId("cash", ProductionFeed.Rate));

        private static readonly ModifierSelector CashYield =
            TestContent.Sel(CurrencyProducer.NumberId("cash", ProductionFeed.Yield));

        // A contributor with no purchase mechanics behind it: Scale stands in
        // for whatever the real kind folds in on its side (a generator's owned
        // count and the modifiers reaching that line), so these tests exercise
        // the producer rather than a generator.
        private class FakeContributor : IProductionContributor
        {
            private readonly List<ProductionContribution> _contributions = new();

            public FakeContributor(string id, params ProductionContribution[] contributions)
            {
                ContributorId = id;
                _contributions.AddRange(contributions);
            }

            public double Scale { get; set; } = 1;

            public string ContributorId { get; }
            public IReadOnlyList<ProductionContribution> Contributions => _contributions;

            public BigNumber ValueOf(ProductionContribution contribution)
                => (BigNumber)(contribution.Amount * Scale);
        }

        // A derived modifier's value is read live and is never re-validated
        // after it is added - AddDerived checks addressability, not tuning -
        // which is the reachable path to a composed total below zero.
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

        // ids are per (contributor, currency) here, the convention chapter 1
        // authors; the fixtures that care about the id pass one explicitly
        private static ProductionContribution Rate(string currencyId, double amount, Condition gate = null,
            string id = "line")
            => new(id, currencyId, amount, ProductionFeed.Rate, gate);

        private static ProductionContribution Yield(string currencyId, double amount, Condition gate = null,
            string id = "line")
            => new(id, currencyId, amount, ProductionFeed.Yield, gate);

        // what an assembler hands over: every contribution these contributors
        // declare, paired with whoever holds it
        private static List<ProductionEntry> EntriesOf(params FakeContributor[] contributors)
        {
            var entries = new List<ProductionEntry>();
            foreach (var contributor in contributors)
            {
                foreach (var contribution in contributor.Contributions)
                    entries.Add(new ProductionEntry(contributor, contribution));
            }
            return entries;
        }

        private static CurrencyProducer MakeProducer(string currencyId, ModifierSystem modifiers,
            ICurrencies currencies, FlagSystem flags = null)
            => new(currencyId, currencies, modifiers, TestContent.MakeContext(currencies, flags: flags));

        // The composition is a fact about the PRODUCER, not about each
        // contributor: a term reaching `cash_rate` names the summed number, and
        // no line answers to that id, so the multiplier lands once over the sum
        // of everything feeding the currency rather than on any one line.
        //
        // The total is checked WITH the per-line readouts because the total
        // alone cannot tell the two apart - multiplication distributes over the
        // sum, so a currency-level multiplier folded into each line reaches the
        // same 10. What separates them is attribution: the amp's row must say
        // what the amp makes, and every buff on the currency reaching every row
        // is how a row starts claiming credit for the whole economy.
        [Test]
        public void Rate_ComposesTheCurrencyLevelModifiersOnceOverTheSum()
        {
            var currencies = TestContent.MakeEconomy();
            var modifiers = new ModifierSystem();
            var producer = MakeProducer("cash", modifiers, currencies);

            producer.Rebuild(EntriesOf(
                new FakeContributor("amp", Rate("cash", 2)),
                new FakeContributor("drummer", Rate("cash", 3))));

            modifiers.Grant(CashRate, ModifierOperation.Multiply, ContentScope.Run, 2);

            Assert.AreEqual(10.0, producer.Rate.ToDouble(), 1e-9,
                "(2 + 3) x 2 - the multiplier landed once over the sum, not once per contributor");

            Assert.AreEqual(2.0, producer.ValueOf(producer.RateContributions[0]).ToDouble(), 1e-9,
                "the amp's line is what the amp makes, with the currency's multiplier not folded in");
            Assert.AreEqual(3.0, producer.ValueOf(producer.RateContributions[1]).ToDouble(), 1e-9,
                "and the drummer's line likewise");
        }

        // Contributions stay individually addressable so a generator row can
        // show what THAT generator makes, and the parts sum to the producer's
        // base - the currency-level composition being the whole-producer fact
        // that sits on top of it.
        [Test]
        public void ValueOf_ReportsOneContributionAndTheySumToTheBase()
        {
            var currencies = TestContent.MakeEconomy();
            var modifiers = new ModifierSystem();
            var producer = MakeProducer("cash", modifiers, currencies);

            var amp = new FakeContributor("amp", Rate("cash", 2)) { Scale = 4 };
            producer.Rebuild(EntriesOf(amp, new FakeContributor("drummer", Rate("cash", 3))));

            Assert.AreEqual(8.0, producer.ValueOf(producer.RateContributions[0]).ToDouble(), 1e-9,
                "the amp's own line: its amount scaled by what it owns");

            var sum = BigNumber.Zero;
            foreach (var entry in producer.RateContributions)
                sum += producer.ValueOf(entry);

            Assert.AreEqual(11.0, sum.ToDouble(), 1e-9);
            Assert.AreEqual(11.0, producer.Rate.ToDouble(), 1e-9,
                "with no modifiers granted, the composed rate IS the base");
        }

        // Rate and yield are different quantities, not two flavours of one:
        // "taps pay double" must not speed up the fan trickle, and an idle
        // buff must not inflate what a press pays.
        [Test]
        public void RateAndYield_AreModifiedIndependently()
        {
            var currencies = TestContent.MakeEconomy();
            var modifiers = new ModifierSystem();
            var producer = MakeProducer("cash", modifiers, currencies);

            producer.Rebuild(EntriesOf(
                new FakeContributor("band", Rate("cash", 10)),
                new FakeContributor("jam", Yield("cash", 4))));

            modifiers.Grant(CashYield, ModifierOperation.Multiply, ContentScope.Run, 3);

            Assert.AreEqual(12.0, producer.Yield.ToDouble(), 1e-9);
            Assert.AreEqual(10.0, producer.Rate.ToDouble(), 1e-9, "a yield buff left the rate alone");

            modifiers.Grant(CashRate, ModifierOperation.Multiply, ContentScope.Run, 2);

            Assert.AreEqual(20.0, producer.Rate.ToDouble(), 1e-9);
            Assert.AreEqual(12.0, producer.Yield.ToDouble(), 1e-9, "and a rate buff leaves the yield alone");
        }

        // One gate, asked once: a readout that ignored it would advertise a
        // number the payout does not deliver, which is worse than showing
        // nothing because it looks authored rather than stale.
        [Test]
        public void AGatedContribution_IsWorthNothingUntilItsGateHolds()
        {
            var currencies = TestContent.MakeEconomy();
            var modifiers = new ModifierSystem();
            var flags = new FlagSystem();
            var producer = MakeProducer("cash", modifiers, currencies, flags);

            producer.Rebuild(EntriesOf(
                new FakeContributor("band", Rate("cash", 1)),
                new FakeContributor("rehearsal", Rate("cash", 4, new FlagSetCondition("covers")))));

            var gated = producer.RateContributions[1];

            Assert.AreEqual(0.0, producer.ValueOf(gated).ToDouble(), 1e-9);
            Assert.AreEqual(1.0, producer.Rate.ToDouble(), 1e-9, "the dormant contribution counted for nothing");

            flags.Set("covers");

            Assert.AreEqual(4.0, producer.ValueOf(gated).ToDouble(), 1e-9);
            Assert.AreEqual(5.0, producer.Rate.ToDouble(), 1e-9,
                "and it counts the moment the gate holds, with no rebuild");
        }

        // A modifier scales what contributions MAKE. With nothing contributing
        // there is nothing to scale, so the composition is skipped rather than
        // applied to zero. A multiplier scales what contributions make, and a
        // producer nothing feeds - or whose every line is gated off - makes
        // nothing, so no multiplier may make the readout advertise income for a
        // currency with no live source.
        [Test]
        public void AProducerWithNothingLive_ComposesToZero()
        {
            var currencies = TestContent.MakeEconomy();
            var modifiers = new ModifierSystem();
            var flags = new FlagSystem();
            var producer = MakeProducer("cash", modifiers, currencies, flags);

            modifiers.Grant(CashRate, ModifierOperation.Multiply, ContentScope.Run, 100);

            Assert.AreEqual(0.0, producer.Rate.ToDouble(), 1e-9, "nothing was ever handed to this producer");
            Assert.IsFalse(producer.HasRate);

            producer.Rebuild(EntriesOf(
                new FakeContributor("rehearsal", Rate("cash", 4, new FlagSetCondition("covers")))));

            Assert.AreEqual(0.0, producer.Rate.ToDouble(), 1e-9, "every contribution is gated off");
            Assert.IsFalse(producer.HasRate);

            flags.Set("covers");

            Assert.AreEqual(400.0, producer.Rate.ToDouble(), 1e-9);
            Assert.IsTrue(producer.HasRate);
        }

        // Assembled, never registered. Nothing unhooks a contributor that goes
        // away - the next rebuild simply does not include it - which is why
        // enable, disable and reset need no bookkeeping here.
        [Test]
        public void Rebuild_ReplacesTheListRatherThanAddingToIt()
        {
            var currencies = TestContent.MakeEconomy();
            var modifiers = new ModifierSystem();
            var producer = MakeProducer("cash", modifiers, currencies);

            var amp = new FakeContributor("amp", Rate("cash", 2));

            producer.Rebuild(EntriesOf(amp, new FakeContributor("drummer", Rate("cash", 3))));
            Assert.AreEqual(5.0, producer.Rate.ToDouble(), 1e-9);

            producer.Rebuild(EntriesOf(amp));

            Assert.AreEqual(2.0, producer.Rate.ToDouble(), 1e-9, "the drummer left by not being handed over again");
            Assert.AreEqual(1, producer.RateContributions.Count);

            producer.Rebuild(null);

            Assert.AreEqual(0, producer.RateContributions.Count);
            Assert.AreEqual(0.0, producer.Rate.ToDouble(), 1e-9);
        }

        // Firing is external and unnamed: the producer pays its yield and asks
        // nothing about the caller. Accrue is the rate's counterpart, and the
        // only place a per-second number becomes a quantity.
        [Test]
        public void FireAndAccrue_PayTheirOwnNumberAndNothingElse()
        {
            var currencies = TestContent.MakeEconomy();
            var modifiers = new ModifierSystem();
            var producer = MakeProducer("cash", modifiers, currencies);

            producer.Rebuild(EntriesOf(
                new FakeContributor("band", Rate("cash", 10)),
                new FakeContributor("jam", Yield("cash", 4))));

            producer.Fire();
            Assert.AreEqual(4.0, currencies.Get("cash").ToDouble(), 1e-9, "one firing paid the yield");

            producer.Accrue(2.5);
            Assert.AreEqual(29.0, currencies.Get("cash").ToDouble(), 1e-9, "4 + 10/sec over 2.5s");

            producer.Accrue(0);
            Assert.AreEqual(29.0, currencies.Get("cash").ToDouble(), 1e-9, "no elapsed time pays nothing");
        }

        // Fail closed on a broken assembler as well as on broken content. A
        // contribution filed under the wrong producer would pay out of another
        // currency's composition, and one that never declared what it feeds
        // must not be guessed into a rate.
        [Test]
        public void Rebuild_RefusesAContributionForAnotherCurrencyOrWithNoDeclaredFeed()
        {
            var currencies = TestContent.MakeEconomy();
            var modifiers = new ModifierSystem();
            var producer = MakeProducer("cash", modifiers, currencies);

            LogAssert.Expect(LogType.Error,
                "CurrencyProducer 'cash': 'poster' contributes to 'fans', not this currency. Ignoring it.");
            LogAssert.Expect(LogType.Error,
                "CurrencyProducer 'cash': contribution from 'stray' feeds 'None', which names neither of a producer's two numbers. Ignoring it.");

            producer.Rebuild(EntriesOf(
                new FakeContributor("amp", Rate("cash", 2)),
                new FakeContributor("poster", Rate("fans", 7)),
                new FakeContributor("stray", new ProductionContribution("stray_cash", "cash", 9, ProductionFeed.None))));

            Assert.AreEqual(1, producer.RateContributions.Count);
            Assert.AreEqual(0, producer.YieldContributions.Count);
            Assert.AreEqual(2.0, producer.Rate.ToDouble(), 1e-9, "only the well-formed contribution counted");
        }

        // Production can never drain a balance: a negative contribution pays
        // nothing rather than subtracting from a sibling's, and a negative
        // composed total pays nothing rather than taking currency back.
        [Test]
        public void NegativeValues_PayNothingRatherThanDraining()
        {
            var currencies = TestContent.MakeEconomy();
            var modifiers = new ModifierSystem();
            var producer = MakeProducer("cash", modifiers, currencies);

            producer.Rebuild(EntriesOf(
                new FakeContributor("amp", Rate("cash", 2)),
                new FakeContributor("broken", Rate("cash", -5))));

            Assert.AreEqual(2.0, producer.Rate.ToDouble(), 1e-9,
                "the negative contribution took nothing off the amp's");

            // a derived modifier is not asked whether its value is sane the way a
            // grant is, so a negative one can still reach the composition - the
            // producer's own floor is what keeps it from taking currency back
            modifiers.AddDerived(new FixedDerived(CashRate, ModifierOperation.Multiply, -100));

            Assert.AreEqual(0.0, producer.Rate.ToDouble(), 1e-9,
                "and a composed total below zero pays nothing rather than taking currency back");

            currencies.Set("cash", 50);
            producer.Accrue(1);

            Assert.AreEqual(50.0, currencies.Get("cash").ToDouble(), 1e-9, "accruing a floored rate took nothing");
        }
    }
}
