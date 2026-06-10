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
        self.universe_settings.resolution = Resolution.MINUTE
        # Refilter the ETF constituents monthly to match the rebalance cadence.
        self.universe_settings.schedule.on(self.date_rules.month_start("SPY"))
        # Add a universe of the SPY constituents ranked by 90-day relative strength.
        self._universe = self.add_universe(self.universe.etf("SPY", universe_filter_func=self._select_assets))
        # Create a Scheduled Event to rebalance the portfolio monthly.
        self.schedule.on(self.date_rules.month_start("SPY"), self.time_rules.at(9, 0), self._rebalance)

    def _select_assets(self, constituents: list[ETFConstituentUniverse]) -> list[Symbol]:
        symbols = [self._benchmark, *[constituent.symbol for constituent in constituents]]
        history = self.history(symbols, timedelta(90), Resolution.DAILY)
        if history.empty:
            return []
        closes = history.close.unstack(0).dropna(axis=1)
        if self._benchmark not in closes:
            return []
        relative_strength = closes.iloc[-1] / closes.iloc[0] - 1
        relative_strength -= relative_strength[self._benchmark]
        # Select the 10 ETF constituents with the highest 90-day return relative to SPY.
        return list(relative_strength.drop(self._benchmark).sort_values(ascending=False).index[:10])

    def _rebalance(self) -> None:
        selected_symbols = [symbol for symbol in self._universe.selected]
        if not selected_symbols:
            return
        # Equal-weight the selected relative-strength leaders.
        weight = 1 / len(selected_symbols)
        targets = [PortfolioTarget(symbol, weight) for symbol in selected_symbols]
        self.set_holdings(targets, liquidate_existing_holdings=True)
