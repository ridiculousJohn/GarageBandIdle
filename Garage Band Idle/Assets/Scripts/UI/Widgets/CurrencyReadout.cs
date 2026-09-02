using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The interpolating display of one currency (design doc 12.11): truth at
    // the snap, the tick's realized slope between snaps. One implementation,
    // used by the header line and by the bar group's pool readout, so the
    // clamp rule lives once rather than in every widget that shows a balance.
    public sealed class CurrencyReadout
    {
        private readonly Label label;
        private readonly CurrencyDefinition currency;

        // The home is resolved once, since the tick report is keyed by it: a
        // currency has exactly one home on this chain, and two chapters may
        // both declare a same-named one (12.3).
        private readonly ScopeState home;

        private BigNumber truth = BigNumber.Zero;
        private BigNumber slope = BigNumber.Zero;
        private double stamp;

        public CurrencyReadout(Label label, ScopeState scope, CurrencyDefinition currency)
        {
            this.label = label;
            this.currency = currency;
            home = Producer.FindCurrencyHome(scope, currency);
        }

        // The refresh: truth, the report's realized slope, and the game-time
        // stamp the interpolation measures from. A null report is a NON-tick
        // transaction, which invalidates every measured slope - the display
        // sits at truth until the next tick measures the new state.
        public void Snap(GameContext ctx, TickReport report, double gameTimeSeconds)
        {
            truth = ctx.GetBalance(currency.Id);
            slope = report == null ? BigNumber.Zero : report.CurrencySlope(home, currency.Id);
            stamp = gameTimeSeconds;
            label.text = NumberFormatter.Format(truth);
        }

        // Clamped at zero: a draining pool's negative slope is honest motion, a
        // negative balance is not. The same-frame rule holds by construction -
        // a stamp equal to now gives exactly truth.
        public void Interpolate(double gameTimeSeconds)
        {
            var display = BigNumber.Max(BigNumber.Zero, truth + slope * (gameTimeSeconds - stamp));
            label.text = NumberFormatter.Format(display);
        }
    }
}
