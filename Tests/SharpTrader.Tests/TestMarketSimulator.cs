﻿using SharpTrader.Bots;
using SharpTrader.Indicators;
using SharpTrader.Plotting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Tests
{
    public class TestMarketSimulator
    {
        private const string MarketName = "Binance";
        private HistoricalRateDataBase HistoryDB;

        private const string DataDir = ".\\Data\\";

        public void Test()
        {
            HistoryDB = new HistoricalRateDataBase(DataDir);
            MultiMarketSimulator simulator = new MultiMarketSimulator(DataDir, HistoryDB);
            var binanceMarket = simulator.GetMarketApi("Binance");

            //var ETHBTC = binanceMarket.GetSymbolFeed("ETHBTC");
            //var XMRBTC = binanceMarket.GetSymbolFeed("XMRBTC");
            TestBot2 tester = new TestBot2(simulator);
            tester.Start();
            simulator.Run(
                new DateTime(2017, 08, 28),
                new DateTime(2018, 1, 1),
                DateTime.MinValue
                );

            //save DATASET
            tester.DataSetCreator.Data.SaveToDisk("d:\\dataset.json");

            foreach (var feed in binanceMarket.ActiveFeeds)
            {
                var balance = binanceMarket.GetBalance(feed.Asset);
                if (balance > 0 && feed.QuoteAsset == "BTC")
                    binanceMarket.MarketOrder(feed.Symbol, TradeType.Sell, balance);
            }
            var lostInFee = binanceMarket.Trades.Select(tr => tr.Fee).Sum();
            Console.WriteLine($"Trades:{binanceMarket.Trades.Count()} - lost in fee:{lostInFee}");
            foreach (var (Symbol, balance) in binanceMarket.Balances)
            {
                Console.WriteLine($"{Symbol }: {balance}");
            }


            var vm = TraderBotResultsPlotViewModel.RunWindow(tester);
            vm.UpdateChart();
            Console.ReadLine();
        }

        public void TestMeanAndVarianceIndicator()
        {
            TimeSerie<ICandlestick> ts = new TimeSerie<ICandlestick>();
            MeanAndVariance mv = new MeanAndVariance(5, new TimeSerieNavigator<ICandlestick>(ts));
            var mvnav = mv.GetNavigator();
            for (int i = 0; i < 10000; i++)
            {
                ts.AddRecord(new Candlestick() { Close = i % 5, OpenTime = new DateTime(i), CloseTime = new DateTime(i + 1) });
                mv.Calculate();
                if (mv.IsReady)
                {
                    Debug.Assert(mvnav.LastTick.Mean == 2 && mvnav.LastTick.Variance == 2);
                    if (mvnav.LastTick.Mean != 2)
                        Console.WriteLine("error");
                }
            }
        }



    }


}
