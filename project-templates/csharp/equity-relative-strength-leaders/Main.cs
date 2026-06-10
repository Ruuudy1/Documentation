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
    private readonly int _lookback = 90;
    private readonly int _portfolioSize = 10;

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
        var benchmarkHistory = History<TradeBar>(_benchmark, _lookback, Resolution.Daily).ToList();
        if (benchmarkHistory.Count < 2)
        {
            return Enumerable.Empty<Symbol>();
        }
        var benchmarkReturn = (double)(benchmarkHistory[^1].Close / benchmarkHistory[0].Close - 1m);

        // Store each constituent's return relative to SPY.
        var relativeStrengthBySymbol = new Dictionary<Symbol, double>();
        foreach (var constituent in constituents)
        {
            var history = History<TradeBar>(constituent.Symbol, _lookback, Resolution.Daily).ToList();
            if (history.Count < 2)
            {
                continue;
            }
            var totalReturn = (double)(history[^1].Close / history[0].Close - 1m);
            relativeStrengthBySymbol[constituent.Symbol] = totalReturn - benchmarkReturn;
        }
        // Select the 10 ETF constituents with the highest 90-day return relative to SPY.
        return relativeStrengthBySymbol.OrderByDescending(kvp => kvp.Value).Take(_portfolioSize).Select(kvp => kvp.Key);
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
