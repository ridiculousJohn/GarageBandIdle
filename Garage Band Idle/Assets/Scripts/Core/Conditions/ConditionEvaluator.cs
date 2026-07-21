namespace RidiculousGaming.GarageBandIdle
{
    // The single entry point for every gate/unlock/visibility/availability check
    // (design doc section 12, rule 8). Dispatch is the Condition subclass's
    // Evaluate override — no per-currency or per-rule branches live anywhere else.
    public static class ConditionEvaluator
    {
        // a null condition means the content declared no gate: always met
        public static bool IsMet(Condition condition, ConditionContext context)
            => condition == null || condition.Evaluate(context);

        public static void Validate(Condition condition, ConditionContext context, string source)
            => condition?.Validate(context, source);
    }
}
