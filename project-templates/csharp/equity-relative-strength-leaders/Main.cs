using System.Collections.Generic;
using System.Linq;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Data.Market;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Scheduling;
using QuantConnect.Securities;

public class RelativeStrengthLeaders : QCAlgorithm
{
    private Universe _universe;
    private readonly Symbol _benchmark = Symbol.Create("SPY", SecurityType.Equity, Market.USA);

    public override void Initialize()
    {
        SetStartDate(2022, 1, 1);
        SetEndDate(2024, 12, 31);
        SetCash(200000);
        Settings.SeedInitialPrices = true;
        UniverseSettings.Resolution = Resolution.Minute;
        // Refilter the ETF constituents monthly to match the rebalance cadence.
        UniverseSettings.Schedule.On(DateRules.MonthStart("SPY"));
        // Add a universe of the SPY constituents ranked by 90-day relative strength.
        _universe = AddUniverse(Universe.ETF("SPY", SelectAssets));
        // Create a Scheduled Event to rebalance the portfolio monthly.
        Schedule.On(DateRules.MonthStart("SPY"), TimeRules.At(9, 0), Rebalance);
    }

    private IEnumerable<Symbol> SelectAssets(IEnumerable<ETFConstituentUniverse> constituents)
    {
        var symbols = constituents.Select(constituent => constituent.Symbol).Append(_benchmark).ToList();
        var history = History<TradeBar>(symbols, 90, Resolution.Daily).ToList();
        if (history.Count == 0)
        {
            return [];
        }

        var returnsBySymbol = history
            .SelectMany(bars => bars.Values)
            .GroupBy(bar => bar.Symbol)
            .Select(group => group.OrderBy(bar => bar.EndTime).ToList())
            .Where(bars => bars.Count == 90)
            .Select(bars =>
            {
                return new
                {
                    bars[0].Symbol,
                    Return = (double)(bars[^1].Close / bars[0].Close - 1m)
                };
            })
            .ToDictionary(x => x.Key, x => x.Return);
        if (!returnsBySymbol.TryGetValue(_benchmark, out var benchmarkReturn))
        {
            return [];
        }
        // Select the 10 ETF constituents with the highest 90-day return relative to SPY.
        return returnsBySymbol
            .Where(kvp => kvp.Key != _benchmark)
            .OrderByDescending(kvp => kvp.Value - benchmarkReturn)
            .Take(10)
            .Select(kvp => kvp.Key);
    }

    private void Rebalance()
    {
        var selectedSymbols = _universe.Selected.ToList();
        if (selectedSymbols.Count == 0)
        {
            return;
        }
        // Equal-weight the selected relative-strength leaders.
        var weight = 1m / selectedSymbols.Count;
        var targets = selectedSymbols.Select(symbol => new PortfolioTarget(symbol, weight)).ToList();
        SetHoldings(targets, liquidateExistingHoldings: true);
    }
}
