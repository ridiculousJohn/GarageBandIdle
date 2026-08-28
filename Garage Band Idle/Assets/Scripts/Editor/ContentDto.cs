using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace RidiculousGaming.GarageBandIdle.Editor
{
    // The authored JSON shape (design doc 12.14.5). One DTO per authored block,
    // strict: unknown keys abort the import, which is the whole typo guard -
    // `amount` where a condition wants `value` is a load-time failure rather
    // than a silently absent operand.
    //
    // A DTO differs from its definition class in exactly one way: an authored
    // REFERENCE is an id here and an object field there. That is why the two
    // cannot be one type, and why every DTO stays this thin.

    // The polymorphic families are discriminated by a `type` field naming the
    // class. A type with no class behind it aborts: a kind that silently became
    // null would change meaning per site (a null gate is closed, a null bar gate
    // is open), so nothing is written.
    internal abstract class KindDto
    {
        public string type;
    }

    // ---- conditions ----

    internal abstract class ConditionDto : KindDto
    {
        public string uiText;
    }

    internal class CurrencyAtLeastDto : ConditionDto
    {
        public string currency;
        public BigNumber threshold;
    }

    internal class EarnedTotalAtLeastDto : ConditionDto
    {
        public string currency;
        public BigNumber threshold;
    }

    internal class OwnedCountAtLeastDto : ConditionDto
    {
        public string generator;
        public int count;
    }

    internal class FlagSetDto : ConditionDto
    {
        public string flagId;
    }

    internal class UpgradePurchasedDto : ConditionDto
    {
        public string upgrade;
    }

    internal class BarsCompletedDto : ConditionDto
    {
        public string group;
        public int count = 1;
    }

    internal class EventRecordExistsDto : ConditionDto
    {
        public string host;
    }

    internal class EventRewardPendingDto : ConditionDto
    {
        public string host;
    }

    internal class AlwaysDto : ConditionDto
    {
    }

    internal class IdleAccumulationDto : ConditionDto
    {
    }

    internal class AllDto : ConditionDto
    {
        public List<ConditionDto> conditions = new();
    }

    internal class AnyDto : ConditionDto
    {
        public List<ConditionDto> conditions = new();
    }

    internal class NotDto : ConditionDto
    {
        public ConditionDto condition;
    }

    // ---- actions ----

    internal abstract class ActionDto : KindDto
    {
    }

    internal class AddCurrencyDto : ActionDto
    {
        public List<string> currencies = new();
        public BigNumber amount;
        public PayoutFormulaDto formula;
    }

    internal class SetFlagDto : ActionDto
    {
        public string flagId;
    }

    internal class AddModifierDto : ActionDto
    {
        public string scope;
        public string modifier;
    }

    internal class RemoveModifierDto : ActionDto
    {
        public string scope;
        public string modifier;
    }

    internal class ResetScopeDto : ActionDto
    {
        public string scope;
    }

    internal class ExecuteRungDto : ActionDto
    {
        public string tier;
    }

    internal class RestartScopeDto : ActionDto
    {
        public string scope;
    }

    // ---- payout formulas ----

    internal abstract class PayoutFormulaDto : KindDto
    {
    }

    internal class ConstantFormulaDto : PayoutFormulaDto
    {
        public BigNumber value;
    }

    internal class RootCurveFormulaDto : PayoutFormulaDto
    {
        public string currency;
        public BigNumber divisor = 1;
        // Pow's POWER stays a double, by BigDouble's own signature - the runtime
        // cannot compute with a wider one either.
        public double exponent = 1;
    }

    // ---- multiplier formulas ----

    internal abstract class MultiplierFormulaDto : KindDto
    {
    }

    internal class LinearOnBalanceDto : MultiplierFormulaDto
    {
        public string currency;
        public BigNumber coefficient;
    }

    internal class RoadieTotalBoostDto : MultiplierFormulaDto
    {
        public BigNumber perRoadie;
    }

    internal class RoadieActiveBoostDto : MultiplierFormulaDto
    {
        public BigNumber perRoadie;
    }

    // ---- the declaration blocks ----

    // Every block carries `tags`, since every Definition does. A tag must be
    // declared by some scope on the carrier's own chain (12.2).
    internal abstract class DefinitionDto
    {
        public string id;
        public List<string> tags = new();
    }

    internal class EffectDto
    {
        public string target;
        public string currencyId;
        public string stat;
        public BigNumber multiplier = 1;
        public MultiplierFormulaDto formula;
    }

    internal class ProducesDto
    {
        public string currency;
        public string stat;
        public BigNumber value;
        public ConditionDto condition;
    }

    internal class CurrencyDto : DefinitionDto
    {
    }

    internal class ProducerDto : DefinitionDto
    {
        public List<ProducesDto> produces = new();
    }

    internal class GeneratorDto : DefinitionDto
    {
        public ConditionDto availableWhen;
        public string costCurrency;
        public BigNumber baseCost;
        public BigNumber growth = 1;
        public List<ProducesDto> produces = new();
    }

    internal class UpgradeDto : DefinitionDto
    {
        public ConditionDto gate;
        public string costCurrency;
        public BigNumber cost;
        public List<EffectDto> effects = new();
        public List<ActionDto> actions = new();
    }

    internal class ModifierDto : DefinitionDto
    {
        public Economy.StackingKind stacking = Economy.StackingKind.Replace;
        public List<EffectDto> effects = new();
        public ConditionDto appliesWhen;
    }

    internal class PerFillDto
    {
        public EffectDto effect;
        public Economy.GrowthKind growth = Economy.GrowthKind.Multiply;
    }

    internal class BarDto : DefinitionDto
    {
        public string fillCurrency;
        public BigNumber fillAmount;
        public BigNumber fillRate;
        public bool repeating;
        public ConditionDto availableWhen;
        public List<ActionDto> onComplete = new();
        public List<PerFillDto> perFill = new();
    }

    internal class BarGroupDto : DefinitionDto
    {
        public int maxActive = 1;
        public List<BarDto> bars = new();
    }

    internal class TriggerDto : DefinitionDto
    {
        public ConditionDto condition;
        public List<ActionDto> actions = new();
    }

    internal class EventDto : DefinitionDto
    {
        public ConditionDto availableWhen;
        public ConditionDto goal;
        public double timeLimitSeconds;
        public List<EffectDto> handicaps = new();
        public List<ActionDto> onEntry = new();
        public List<ActionDto> rewards = new();
        public List<ActionDto> onEnd = new();
    }

    internal class RungDto
    {
        public ConditionDto offerCondition;
        public List<ActionDto> actions = new();
    }

    // One block per scope, nesting as authored: a document IS its top scope
    // block. `rung` and `events` are interior-only; a root document authoring
    // one is an import error rather than an unknown key, since the key is real
    // on every other scope.
    internal class ScopeDto
    {
        public string type;
        public string id;
        public List<string> tags = new();
        public List<CurrencyDto> currencies = new();
        public List<string> flags = new();
        public List<string> declaredTags = new();
        public List<ProducerDto> producers = new();
        public List<GeneratorDto> generators = new();
        public List<UpgradeDto> upgrades = new();
        public List<BarGroupDto> barGroups = new();
        public List<ModifierDto> modifiers = new();
        public List<string> permanentModifiers = new();
        public List<TriggerDto> triggers = new();
        public List<EventDto> events = new();
        public RungDto rung;
        public List<ScopeDto> children = new();
    }

    // The `type` field names the class; the DTO it maps to is the same name
    // plus Dto. Registered explicitly rather than reflected, so a class that
    // is not authorable stays unauthorable.
    internal static class KindRegistry
    {
        public static readonly Dictionary<string, Type> Conditions = new()
        {
            { nameof(CurrencyAtLeast), typeof(CurrencyAtLeastDto) },
            { nameof(EarnedTotalAtLeast), typeof(EarnedTotalAtLeastDto) },
            { nameof(OwnedCountAtLeast), typeof(OwnedCountAtLeastDto) },
            { nameof(FlagSet), typeof(FlagSetDto) },
            { nameof(UpgradePurchased), typeof(UpgradePurchasedDto) },
            { nameof(BarsCompleted), typeof(BarsCompletedDto) },
            { nameof(EventRecordExists), typeof(EventRecordExistsDto) },
            { nameof(EventRewardPending), typeof(EventRewardPendingDto) },
            { nameof(Always), typeof(AlwaysDto) },
            { nameof(IdleAccumulation), typeof(IdleAccumulationDto) },
            { nameof(All), typeof(AllDto) },
            { nameof(Any), typeof(AnyDto) },
            { nameof(Not), typeof(NotDto) },
        };

        public static readonly Dictionary<string, Type> Actions = new()
        {
            { nameof(AddCurrency), typeof(AddCurrencyDto) },
            { nameof(SetFlag), typeof(SetFlagDto) },
            { nameof(AddModifier), typeof(AddModifierDto) },
            { nameof(RemoveModifier), typeof(RemoveModifierDto) },
            { nameof(ResetScope), typeof(ResetScopeDto) },
            { nameof(ExecuteRung), typeof(ExecuteRungDto) },
            { nameof(RestartScope), typeof(RestartScopeDto) },
        };

        public static readonly Dictionary<string, Type> PayoutFormulas = new()
        {
            { nameof(ConstantFormula), typeof(ConstantFormulaDto) },
            { nameof(RootCurveFormula), typeof(RootCurveFormulaDto) },
        };

        public static readonly Dictionary<string, Type> MultiplierFormulas = new()
        {
            { nameof(Economy.LinearOnBalance), typeof(LinearOnBalanceDto) },
            { nameof(Economy.RoadieTotalBoost), typeof(RoadieTotalBoostDto) },
            { nameof(Economy.RoadieActiveBoost), typeof(RoadieActiveBoostDto) },
        };

        public static readonly Dictionary<string, Type> Scopes = new()
        {
            { nameof(RootDefinition), typeof(RootDefinition) },
            { nameof(ChapterDefinition), typeof(ChapterDefinition) },
            { nameof(TierDefinition), typeof(TierDefinition) },
        };
    }

    // Reads a polymorphic block's `type` and populates the DTO it names.
    // Populate rather than Deserialize, so nested members still route back
    // through this converter while the object itself does not recurse.
    internal class KindConverter<TBase> : JsonConverter where TBase : KindDto
    {
        private readonly Dictionary<string, Type> byName;
        private readonly string family;

        public KindConverter(Dictionary<string, Type> byName, string family)
        {
            this.byName = byName;
            this.family = family;
        }

        public override bool CanConvert(Type objectType) => typeof(TBase).IsAssignableFrom(objectType);

        public override bool CanWrite => false;

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) =>
            throw new NotSupportedException();

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;
            var block = JObject.Load(reader);
            var typeName = block["type"]?.Value<string>();
            if (string.IsNullOrEmpty(typeName))
                throw new ContentImportException($"a {family} block has no 'type' - every polymorphic kind names its class (12.14.5).");
            if (!byName.TryGetValue(typeName, out var dtoType))
                throw new ContentImportException(
                    $"'{typeName}' is not a {family} kind. Authorable kinds: {string.Join(", ", byName.Keys)}.");
            var instance = Activator.CreateInstance(dtoType);
            using var sub = block.CreateReader();
            serializer.Populate(sub, instance);
            return instance;
        }
    }

    // An authored number the runtime holds as BigNumber. JSON's own grammar has
    // no range limit, but binding a token to a C# double does: past ~1.8e308 the
    // reader hands over infinity, which BigNumber refuses at construction, while
    // BigNumber itself runs to the range of a long exponent. An author should be
    // able to write any number the game can compute, so the token is read as
    // mantissa and exponent - and a value past double range is authored QUOTED,
    // the one spelling that reaches here with its digits intact.
    internal class BigNumberConverter : JsonConverter<BigNumber>
    {
        public override bool CanWrite => false;

        public override void WriteJson(JsonWriter writer, BigNumber value, JsonSerializer serializer) =>
            throw new NotSupportedException();

        public override BigNumber ReadJson(JsonReader reader, Type objectType, BigNumber existingValue,
                                           bool hasExistingValue, JsonSerializer serializer)
        {
            switch (reader.TokenType)
            {
                case JsonToken.Null:
                    return existingValue;
                // The reader already holds the exact value for anything a double
                // or a long can carry, so those need no text round trip.
                case JsonToken.Float:
                    var real = Convert.ToDouble(reader.Value, CultureInfo.InvariantCulture);
                    if (double.IsInfinity(real) || double.IsNaN(real))
                        throw new ContentImportException(
                            "a number outside double range was authored unquoted; write it as a string (\"1e400\") so its exponent survives the reader.");
                    return real;
                case JsonToken.Integer:
                    return reader.Value is BigInteger big
                        ? Parse(big.ToString(CultureInfo.InvariantCulture))
                        : (BigNumber)Convert.ToDouble(reader.Value, CultureInfo.InvariantCulture);
                case JsonToken.String:
                    return Parse((string)reader.Value);
                default:
                    throw new ContentImportException($"a number was authored as {reader.TokenType}.");
            }
        }

        // Scientific notation is split rather than parsed whole: the mantissa
        // fits a double at any magnitude, and the exponent is what a double
        // cannot carry.
        internal static BigNumber Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ContentImportException("an authored number is empty.");
            var split = text.IndexOfAny(new[] { 'e', 'E' });
            if (split < 0)
            {
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var plain)
                    || double.IsInfinity(plain) || double.IsNaN(plain))
                    throw new ContentImportException(
                        $"'{text}' is not a number a double can hold; past 1e308 write it in scientific notation (\"1e400\").");
                return plain;
            }
            if (!double.TryParse(text.Substring(0, split), NumberStyles.Float, CultureInfo.InvariantCulture, out var mantissa)
                || !long.TryParse(text.Substring(split + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var exponent))
                throw new ContentImportException($"'{text}' is not a number.");
            return BigNumber.FromMantissaExponent(mantissa, exponent);
        }
    }

    // Every enum in the schema, in one place, authored by NAME.
    //
    // Two holes, both of them Newtonsoft and .NET defaults rather than choices.
    // Any integer binds to an enum field, defined or not - AllowIntegerValues
    // closes that. And Enum.Parse takes a comma-separated list for ANY enum,
    // flags or not, ORs the values, and never checks the result is declared.
    //
    // What closes the second one depends on the enum. For an ordinary enum a
    // value is exactly one member, so a comma is refused outright and the result
    // must be declared - "Linear, Multiply" is StackingKind 3, and an undefined
    // StackingKind is the worst shape of wrong: AddModifier reads it as
    // not-Replace and counts up, then Stacked falls through to Replace and
    // discards the count. For a [Flags] enum a comma list is the authoring form
    // and IsDefined is meaningless - it answers false for every legitimate
    // combination - so neither check applies, and none is needed: integers are
    // already refused, and Enum.Parse throws on an unknown component name, so
    // the OR of declared names can only produce declared bits.
    internal class StrictEnumConverter : StringEnumConverter
    {
        public StrictEnumConverter()
        {
            AllowIntegerValues = false;
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var enumType = Nullable.GetUnderlyingType(objectType) ?? objectType;
            var flags = enumType.IsDefined(typeof(FlagsAttribute), false);
            // Read before delegating: the base consumes the token.
            var authored = reader.TokenType == JsonToken.String ? reader.Value as string : null;

            var value = base.ReadJson(reader, objectType, existingValue, serializer);
            if (value == null || flags)
                return value;

            if (authored != null && authored.Contains(","))
                throw new ContentImportException(
                    $"'{authored}' names more than one member, and {enumType.Name} is not a [Flags] enum - a value is exactly one of: {string.Join(", ", Enum.GetNames(enumType))}.");
            if (!Enum.IsDefined(enumType, value))
                throw new ContentImportException(
                    $"'{value}' is not one of: {string.Join(", ", Enum.GetNames(enumType))}.");
            return value;
        }
    }

    // Every abort path (parse, lint, resolution) raises this, so one catch at
    // the entry points turns an import failure into a failed PROCESS rather
    // than a logged warning over yesterday's assets. AssetsMutated separates the
    // aborts that left the previous import intact from the failures that could
    // not: everything after the first write.
    public class ContentImportException : Exception
    {
        public bool AssetsMutated { get; }

        public ContentImportException(string message, bool assetsMutated = false) : base(message)
        {
            AssetsMutated = assetsMutated;
        }

        public ContentImportException(string message, bool assetsMutated, Exception inner) : base(message, inner)
        {
            AssetsMutated = assetsMutated;
        }
    }
}
