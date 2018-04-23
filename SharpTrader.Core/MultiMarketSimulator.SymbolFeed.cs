﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SymbolsTable = System.Collections.Generic.Dictionary<string, (string Asset, string Quote)>;

namespace SharpTrader
{
    public partial class MultiMarketSimulator
    {
        public IEnumerable<ITrade> Trades => Markets.SelectMany(m => m.Trades);

        class Market : IMarketApi
        {
            object LockObject = new object();
            //private Dictionary<string, decimal> _Balances = new Dictionary<string, decimal>();
            private Dictionary<string, AssetBalance> _Balances = new Dictionary<string, AssetBalance>();
            private List<Trade> _Trades = new List<Trade>();
            private Dictionary<string, SymbolFeed> SymbolsFeed = new Dictionary<string, SymbolFeed>();
            private List<Order> PendingOrders = new List<Order>();
            private List<Order> ClosedOrders = new List<Order>();
            private List<ITrade> TradesToSignal = new List<ITrade>();
            private SymbolsTable SymbolsTable;

            public string MarketName { get; private set; }
            public double MakerFee { get; private set; } = 0.0015;
            public double TakerFee { get; private set; } = 0.0025;
            public DateTime Time { get; internal set; }
            public event Action<IMarketApi, ITrade> OnNewTrade;
            public IEnumerable<ISymbolFeed> Feeds => SymbolsFeed.Values;
            public IEnumerable<ISymbolFeed> ActiveFeeds => SymbolsFeed.Values;
            public IEnumerable<ITrade> Trades => this._Trades;

            public Market(string name, double makerFee, double takerFee, string dataDir)
            {
                MarketName = name;
                MakerFee = makerFee;
                TakerFee = takerFee;
                var text = System.IO.File.ReadAllText(dataDir + "BinanceSymbolsTable.json");
                SymbolsTable = Newtonsoft.Json.JsonConvert.DeserializeObject<SymbolsTable>(text);
            }

            public async Task<ISymbolFeed> GetSymbolFeedAsync(string symbol, DateTime warmup)
            {
                throw new NotImplementedException();
            }

            public async Task<ISymbolFeed> GetSymbolFeedAsync(string symbol)
            {
                var feedFound = SymbolsFeed.TryGetValue(symbol, out SymbolFeed feed);
                if (!feedFound)
                {
                    (var asset, var counterAsset) = SymbolsTable[symbol];
                    feed = new SymbolFeed(this.MarketName, symbol, asset, counterAsset);
                    lock (LockObject)
                        SymbolsFeed.Add(symbol, feed);
                }
                if (!_Balances.ContainsKey(feed.Asset))
                    _Balances.Add(feed.Asset, new AssetBalance());
                if (!_Balances.ContainsKey(feed.QuoteAsset))
                    _Balances.Add(feed.QuoteAsset, new AssetBalance());
                return feed;
            }

            public IMarketOperation<IOrder> LimitOrder(string symbol, TradeType type, decimal amount, decimal rate, string clientOrderId = null)
            {
                var order = new Order(this.MarketName, symbol, type, OrderType.Limit, amount, (double)rate);

                var res = RegisterOrder(order);
                lock (LockObject)
                    this.PendingOrders.Add(order);
                return new MarketOperation<IOrder>(MarketOperationStatus.Completed, order) { };

            }

            private (bool result, string error) RegisterOrder(Order order)
            {
                var ass = SymbolsTable[order.Symbol];
                AssetBalance bal;
                decimal amount;
                if (order.TradeType == TradeType.Sell)
                {
                    bal = _Balances[ass.Asset];
                    amount = order.Amount;
                }
                else
                {
                    bal = _Balances[ass.Quote];
                    amount = order.Amount * (decimal)order.Price;
                }
                if (bal.Free < amount)
                {
                    return (false, "Insufficient balance");
                }

                bal.Free -= amount;
                bal.Locked += amount;
                return (true, null);

            }

            public IMarketOperation<IOrder> MarketOrder(string symbol, TradeType type, decimal amount, string clientOrderId = null)
            {
                lock (LockObject)
                {
                    var feed = SymbolsFeed[symbol];
                    var price = type == TradeType.Buy ? feed.Ask : feed.Bid;
                    var order = new Order(this.MarketName, symbol, type, OrderType.Market, amount, price);



                    var (result, error) = RegisterOrder(order);
                    if (!result)
                        return new MarketOperation<IOrder>(MarketOperationStatus.Failed, null) { ErrorInfo = error };

                    var trade = new Trade(
                        this.MarketName, symbol, this.Time,
                        type, price, amount,
                        amount * (decimal)(this.TakerFee * price), order);

                    RegisterTrade(feed, trade);
                    this.ClosedOrders.Add(order);
                    return new MarketOperation<IOrder>(MarketOperationStatus.Completed, order) { };
                }
            }

            public decimal GetFreeBalance(string asset)
            {
                _Balances.TryGetValue(asset, out var res);
                return res.Free;
            }

            public (string Symbol, AssetBalance bal)[] Balances => _Balances.Select(kv => (kv.Key, kv.Value)).ToArray();

            public IEnumerable<IOrder> OpenOrders
            {
                get
                {
                    lock (LockObject)
                        return PendingOrders.ToArray();
                }
            }

            internal void RaisePendingEvents()
            {
                List<ITrade> trades;
                lock (LockObject)
                {
                    trades = new List<ITrade>(TradesToSignal);
                    TradesToSignal.Clear();
                }
                foreach (var trade in trades)
                {
                    this.OnNewTrade?.Invoke(this, trade);
                }

                foreach (var feed in SymbolsFeed.Values)
                    feed.RaisePendingEvents(feed);
            }

            internal void ResolveOrders()
            {
                //resolve orders/trades 
                lock (LockObject)
                {
                    for (int i = 0; i < PendingOrders.Count; i++)
                    {
                        var order = PendingOrders[i];
                        var feed = SymbolsFeed[order.Symbol];
                        if (order.Type == OrderType.Limit)
                        {
                            var willBuy = (order.TradeType == TradeType.Buy && feed.Ticks.LastTick.Low + feed.Spread <= order.Price);
                            var willSell = (order.TradeType == TradeType.Sell && feed.Ticks.LastTick.High >= order.Price);

                            if (willBuy || willSell)
                            {
                                var trade = new Trade(
                                    market: this.MarketName,
                                    symbol: feed.Symbol,
                                    time: feed.Ticks.LastTick.OpenTime.AddSeconds(feed.Ticks.LastTick.Timeframe.Seconds / 2),
                                    price: order.Price,
                                    amount: order.Amount,
                                    type: order.TradeType,
                                    fee: order.Amount * (decimal)(this.MakerFee * order.Price),
                                    order: order
                                );
                                RegisterTrade(feed, trade);
                                ClosedOrders.Add(PendingOrders[i]);
                                PendingOrders.RemoveAt(i--);
                            }
                        }
                    }
                }
            }

            private void RegisterTrade(SymbolFeed feed, Trade trade)
            {
                lock (LockObject)
                {
                    var qBal = _Balances[feed.QuoteAsset];
                    var aBal = _Balances[feed.Asset];
                    if (trade.Type == TradeType.Buy)
                    {
                        aBal.Free += Convert.ToDecimal(trade.Amount);
                        qBal.Locked -= Convert.ToDecimal(trade.Amount * (decimal)trade.Price);
                        Debug.Assert(qBal.Locked >= 0, "incoerent trade");
                    }
                    if (trade.Type == TradeType.Sell)
                    {
                        qBal.Free += Convert.ToDecimal(trade.Amount * (decimal)trade.Price);
                        aBal.Locked -= Convert.ToDecimal(trade.Amount);
                        Debug.Assert(_Balances[feed.Asset].Locked >= -0.0000000001m, "incoerent trade");
                    }
                    qBal.Free -= Convert.ToDecimal(trade.Fee);

                    trade.Order.Status = OrderStatus.Filled;
                    trade.Order.Filled = trade.Amount;
                    this._Trades.Add(trade);
                    TradesToSignal.Add(trade);
                }
            }


            internal void AddNewCandle(SymbolFeed feed, Candlestick tick)
            {
                Time = tick.CloseTime;
                feed.AddNewCandle(tick);
            }

            public class AssetBalance
            {
                public decimal Free;
                public decimal Locked;
                public decimal Total => Free + Locked;
            }

            public decimal GetEquity(string asset)
            {
                decimal val = 0;
                foreach (var kv in _Balances)
                {
                    if (kv.Key == asset)
                        val += kv.Value.Free;
                    else if (kv.Value.Total != 0)
                    {
                        var symbol = (kv.Key + asset);
                        if (SymbolsFeed.ContainsKey(symbol))
                        {
                            var feed = SymbolsFeed[symbol];
                            val += ((decimal)feed.Ask * kv.Value.Total);
                        }
                        var sym2 = asset + kv.Key;
                        if (SymbolsFeed.ContainsKey(sym2))
                        {
                            var feed = SymbolsFeed[sym2];
                            val += (kv.Value.Total / (decimal)feed.Bid);
                        }
                    }
                }
                return val;
            }

            public (decimal min, decimal step) GetMinTradable(string tradeSymbol)
            {
                return (0.00000001m, 0.00000001m);
            }

            public IMarketOperation OrderCancel(string id)
            {
                lock (LockObject)
                {
                    for (int i = 0; i < PendingOrders.Count; i++)
                    {
                        var order = PendingOrders[i];
                        if (order.Id == id)
                        {
                            PendingOrders.RemoveAt(i--);
                            order.Status = OrderStatus.Cancelled;

                            var ass = SymbolsTable[order.Symbol];
                            AssetBalance bal;
                            decimal amount;
                            if (order.TradeType == TradeType.Sell)
                            {
                                bal = _Balances[ass.Asset];
                                amount = order.Amount;
                            }
                            else
                            {
                                bal = _Balances[ass.Quote];
                                amount = order.Amount * (decimal)order.Price;
                            }


                            bal.Free += amount;
                            bal.Locked -= amount;
                            Debug.Assert(bal.Locked >= -0.0000001m, "Incoerent locked amount");
                            ClosedOrders.Add(order);
                        }
                    }
                }
                return new MarketOperation<object>(MarketOperationStatus.Completed, null);
            }


            public decimal GetSymbolPrecision(string symbol)
            {
                return 0.0000000001m;
            }

            public decimal GetMinNotional(string asset)
            {
                return 0;
            }

            internal void AddBalance(string asset, decimal amount)
            {
                if (!_Balances.ContainsKey(asset))
                    _Balances.Add(asset, new AssetBalance());
                _Balances[asset].Free += amount;
            }

            public IMarketOperation<IEnumerable<ITrade>> GetLastTrades(string symbol, int count, string fromId)
            {
                IEnumerable<ITrade> trades;
                if (fromId != null)
                    trades = Trades.Where(t => t.Symbol == symbol && (long.Parse(t.Id) > long.Parse(fromId)));
                else
                    trades = Trades.Where(t => t.Symbol == symbol);
                return new MarketOperation<IEnumerable<ITrade>>(MarketOperationStatus.Completed, trades);
            }

            public IMarketOperation<IOrder> QueryOrder(string symbol, string id)
            {
                var ord = ClosedOrders.Concat(OpenOrders).Where(o => o.Symbol == symbol && o.Id == id).FirstOrDefault();
                if (ord != null)
                    return new MarketOperation<IOrder>(MarketOperationStatus.Completed, ord);
                else
                    return new MarketOperation<IOrder>(MarketOperationStatus.Failed, ord);
            }
        }

        public decimal GetEquity(string baseAsset)
        {
            return Markets.Sum(m => m.GetEquity(baseAsset));
        }

        class SymbolFeed : SymbolFeedBoilerplate, ISymbolFeed
        {

            private TimeSpan BaseTimeframe = TimeSpan.FromSeconds(60);
            public List<(TimeSpan Timeframe, List<WeakReference<IChartDataListener>> Subs)> NewCandleSubscribers =
                new List<(TimeSpan Timeframe, List<WeakReference<IChartDataListener>> Subs)>();


            private List<DerivedChart> DerivedTicks = new List<DerivedChart>(20);
            private object Locker = new object();

            public string Symbol { get; private set; }
            public string Asset { get; private set; }
            public string QuoteAsset { get; private set; }
            public double Ask { get; private set; }
            public double Bid { get; private set; }
            public string Market { get; private set; }
            public double Spread { get; set; }
            public double Volume24H { get; private set; }


            public SymbolFeed(string market, string symbol, string asset, string quoteAsset)
            {
                this.Symbol = symbol;
                this.Market = market;
                this.QuoteAsset = quoteAsset;
                this.Asset = asset;
            }

            internal void AddNewCandle(Candlestick newCandle)
            {
                BaseTimeframe = newCandle.CloseTime - newCandle.OpenTime;

                var previousTime = newCandle.OpenTime;
                Volume24H += newCandle.Volume;
                //let's calculate the volume
                if (Ticks.Count > 0)
                {
                    Ticks.PositionPush();
                    Ticks.SeekLast();
                    previousTime = Ticks.Tick.CloseTime;
                    var delta = newCandle.CloseTime - previousTime;
                    var timeAt24 = newCandle.CloseTime - TimeSpan.FromHours(24);
                    var removeStart = timeAt24 - delta;
                    if (removeStart < Ticks.FirstTickTime)
                        Ticks.SeekFirst();
                    else
                        Ticks.SeekNearestBefore(timeAt24 - delta);

                    //todo 
                    //while (Ticks.Tick.OpenTime < timeAt24)
                    //{
                    //    Volume24H -= Ticks.Tick.Volume;
                    //    Ticks.Next();
                    //}
                    Ticks.PositionPop();
                }

                Ticks.AddRecord(newCandle);

                Bid = newCandle.Close;
                Ask = Bid + Spread;

                UpdateDerivedCharts(newCandle);
                SignalTick();
            }

            public Task SetHistoryStartAsync()
            {
                throw new NotImplementedException();
            }
        }

        class Order : IOrder
        {
            private static int idCounter = 0;
            public string Symbol { get; private set; }
            public string Market { get; private set; }
            public double Price { get; private set; }
            public decimal Amount { get; private set; }
            public string Id { get; private set; }

            public OrderStatus Status { get; internal set; } = OrderStatus.Pending;

            public TradeType TradeType { get; private set; }
            public OrderType Type { get; private set; }
            public decimal Filled { get; set; }
            public Order(string market, string symbol, TradeType tradeSide, OrderType orderType, decimal amount, double rate = 0)
            {
                Symbol = symbol;
                Market = market;
                TradeType = tradeSide;
                Type = orderType;
                Amount = amount;
                Price = rate;
                Id = (idCounter++).ToString();
            }

        }

        class Trade : ITrade
        {
            private static long IdCounter = 0;
            public Trade(string market, string symbol, DateTime time, TradeType type, double price, decimal amount, decimal fee, Order order)
            {
                Market = market;
                Symbol = symbol;
                Time = time;
                Type = type;
                Price = price;
                Amount = amount;
                Fee = fee;
                Order = order;
                Id = (IdCounter++).ToString();
            }
            public string Id { get; private set; }
            public decimal Amount { get; private set; }

            public DateTime Time { get; private set; }

            public decimal Fee { get; private set; }

            public string Market { get; private set; }

            public double Price { get; private set; }

            public string Symbol { get; private set; }

            public TradeType Type { get; private set; }

            public Order Order { get; private set; }

            IOrder ITrade.Order => Order;
        }

        class MarketOperation<T> : IMarketOperation<T>
        {
            public MarketOperationStatus Status { get; internal set; }
            public T Result { get; }
            public string ErrorInfo { get; internal set; }

            public MarketOperation(MarketOperationStatus status, T res)
            {
                Status = status;
                Result = res;
            }

        }

        class MarketConfiguration
        {
            public string MarketName { get; set; }
            public double MakerFee { get; set; }
            public double TakerFee { get; set; }
        }

        class SymbolConfiguration
        {
            public string SymbolName { get; set; }
            public string MarketName { get; set; }
            public string Spread { get; set; }
        }

    }


}
