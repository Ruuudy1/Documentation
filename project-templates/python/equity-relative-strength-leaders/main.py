# region imports
from AlgorithmImports import *
# endregion


class RelativeStrengthLeaders(QCAlgorithm):

    def initialize(self) -> None:
        self.set_start_date(2022, 1, 1)
        self.set_end_date(2024, 12, 31)
        self.set_cash(200_000)
        self.settings.seed_initial_prices = True
        self._benchmark = Symbol.create("SPY", SecurityType.EQUITY, Market.USA)
        self._lookback = 90
        self._portfolio_size = 10
        self.universe_settings.resolution = Resolution.MINUTE
        # Refilter the ETF constituents monthly to match the rebalance cadence.
        self.universe_settings.schedule.on(self.date_rules.month_start("SPY"))
        # Add a universe of the SPY constituents ranked by 90-day relative strength.
        self._universe = self.add_universe(self.universe.etf("SPY", universe_filter_func=self._select_assets))
        # Create a Scheduled Event to rebalance the portfolio monthly.
        self.schedule.on(self.date_rules.month_start("SPY"), self.time_rules.at(9, 0), self._rebalance)

    def _select_assets(self, constituents: list[ETFConstituentUniverse]) -> list[Symbol]:
        benchmark_history = self.history(self._benchmark, self._lookback, Resolution.DAILY)
        if benchmark_history.empty or len(benchmark_history) < 2:
            return []
        benchmark_prices = benchmark_history["close"]
        benchmark_return = benchmark_prices.iloc[-1] / benchmark_prices.iloc[0] - 1
        # Store each constituent's return relative to SPY.
        relative_strength_by_symbol: dict[Symbol, float] = {}
        for constituent in constituents:
            history = self.history(constituent.symbol, self._lookback, Resolution.DAILY)
            if history.empty or len(history) < 2:
                continue
            prices = history["close"]
            total_return = prices.iloc[-1] / prices.iloc[0] - 1
            relative_strength_by_symbol[constituent.symbol] = float(total_return - benchmark_return)
        # Select the 10 ETF constituents with the highest 90-day return relative to SPY.
        ranked_symbols = sorted(
            relative_strength_by_symbol,
            key=lambda symbol: relative_strength_by_symbol[symbol],
            reverse=True
        )
        return ranked_symbols[:self._portfolio_size]

    def _rebalance(self) -> None:
        selected_symbols = [symbol for symbol in self._universe.selected]
        if not selected_symbols:
            return
        # Equal-weight the selected relative-strength leaders.
        weight = 1 / len(selected_symbols)
        targets = [PortfolioTarget(symbol, weight) for symbol in selected_symbols]
        self.set_holdings(targets, liquidate_existing_holdings=True)
