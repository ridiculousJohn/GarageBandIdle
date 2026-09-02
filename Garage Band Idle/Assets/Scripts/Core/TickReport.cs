using System.Collections.Generic;

namespace RidiculousGaming.GarageBandIdle
{
    // The transient record of what ONE tick actually moved (design doc 12.11):
    // plain C#, never serialized, one instance per tick, recorded AT the
    // mutation sites and never as a balance delta. The gross gathers lie
    // exactly where display matters most - ch1's covers demand rehearsal at
    // 2/s against 0.5/s of production, so the pool-limited draw holds the
    // balance near zero while the bar fills at 0.5/s: a GetRate slope would
    // show the balance climbing and a ResolveDemand slope would fill the bar
    // 4x too fast. Site recording also keeps one-shot mutations out of the
    // slope by construction - a bar completion's AddCurrency and the sweep's
    // trigger payouts move truth without moving a slope, and a state delta
    // would extrapolate both as if they repeated every second.
    public sealed class TickReport
    {
        // The tick's REAL dt - the slopes are per real second, and game_speed
        // rides in for free because the realized amounts already contain it.
        public double Seconds { get; }

        // Keyed by the SCOPE OBJECT plus the id. ScopeState does not override
        // equality, so the tuple compares the scope by reference identity,
        // which is what the key needs: a currency has one home and a bar one
        // declaring scope, and two chapters may both declare "cash".
        private readonly Dictionary<(ScopeState, string), BigNumber> currencyNet = new();
        private readonly Dictionary<(ScopeState, string), BigNumber> barFill = new();

        public TickReport(double seconds) => Seconds = seconds;

        // The three recording sites. The amounts are what actually landed, not
        // what was wanted: a stalled bar records the short draw and the short
        // fill, which is the pair a display needs.
        public void RecordDeposit(ScopeState home, string currencyId, BigNumber amount) =>
            Add(currencyNet, home, currencyId, amount);

        public void RecordDraw(ScopeState home, string currencyId, BigNumber amount) =>
            Add(currencyNet, home, currencyId, -amount);

        public void RecordFill(ScopeState scope, string barId, BigNumber amount) =>
            Add(barFill, scope, barId, amount);

        // Unrecorded is zero, never a miss - a tick that moved nothing at a
        // key is indistinguishable from one that moved zero there.
        public BigNumber CurrencyNet(ScopeState home, string currencyId) =>
            currencyNet.TryGetValue((home, currencyId), out var value) ? value : BigNumber.Zero;

        public BigNumber BarFill(ScopeState scope, string barId) =>
            barFill.TryGetValue((scope, barId), out var value) ? value : BigNumber.Zero;

        // Per real second. An empty report has no slope, so a nonpositive
        // Seconds answers zero rather than dividing by it.
        public BigNumber CurrencySlope(ScopeState home, string currencyId) =>
            Slope(CurrencyNet(home, currencyId));

        public BigNumber BarSlope(ScopeState scope, string barId) =>
            Slope(BarFill(scope, barId));

        private BigNumber Slope(BigNumber moved) =>
            Seconds > 0 ? moved / (BigNumber)Seconds : BigNumber.Zero;

        private static void Add(Dictionary<(ScopeState, string), BigNumber> into, ScopeState scope,
                                string id, BigNumber amount)
        {
            into.TryGetValue((scope, id), out var running);
            into[(scope, id)] = running + amount;
        }
    }
}
