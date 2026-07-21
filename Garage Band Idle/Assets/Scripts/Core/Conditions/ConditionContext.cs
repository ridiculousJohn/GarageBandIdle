using RidiculousGaming.GarageBandIdle.Economy;

namespace RidiculousGaming.GarageBandIdle
{
    // Everything a Condition may read when evaluated or validated. One context
    // serves gates, unlocks, section visibility, and event availability; new
    // systems (the bar system, records) plug in here rather than growing new
    // evaluator entry points.
    public class ConditionContext
    {
        public CurrencyManager Currencies { get; }
        public GeneratorSystem Generators { get; }
        public FlagSystem Flags { get; }

        // currency id backing recordsCumulative; Records are never spent, so
        // cumulative Records equals the currency's lifetime-earned total
        public string RecordsCurrencyId { get; }

        // definition registries, used by Validate to resolve content ids;
        // null in unit tests, which validate against the live systems instead
        public ContentDatabase Database { get; }

        // completed-bar counts for barsCompleted; null until the bars slice,
        // which makes every barsCompleted condition evaluate as unmet
        public IBarCompletionSource Bars { get; }

        public ConditionContext(CurrencyManager currencies, GeneratorSystem generators, FlagSystem flags,
            string recordsCurrencyId = "records", ContentDatabase database = null, IBarCompletionSource bars = null)
        {
            Currencies = currencies;
            Generators = generators;
            Flags = flags;
            RecordsCurrencyId = recordsCurrencyId;
            Database = database;
            Bars = bars;
        }
    }
}
