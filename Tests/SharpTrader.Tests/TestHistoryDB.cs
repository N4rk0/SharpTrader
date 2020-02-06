﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Tests
{
    public class TestHistoryDB
    {
        private const string MarketName = "Binance";
        private HistoricalRateDataBase HistoryDB;

        private const string DataDir = ".\\Data\\";

        public void Test()
        {
            HistoryDB = new HistoricalRateDataBase(DataDir);

            foreach (var histInfo in HistoryDB.ListAvailableData())
            {
                var data = HistoryDB.GetSymbolHistory(histInfo, DateTime.MinValue);
                List<ITradeBar> candles = new List<ITradeBar>();
                while (data.Ticks.MoveNext())
                    candles.Add(data.Ticks.Current); 
                Console.WriteLine($"Validate before shuffle  {histInfo.market} - {histInfo.symbol} - {histInfo.timeframe} ");
                HistoryDB.ValidateData(histInfo); 
                Console.WriteLine($"Validate after shuffle {histInfo.market} - {histInfo.symbol} - {histInfo.timeframe}  ");
                HistoryDB.Delete(histInfo.market, histInfo.symbol, histInfo.timeframe);
                Shuffle(candles);
                HistoryDB.AddCandlesticks(histInfo.market, histInfo.symbol, candles);
                HistoryDB.ValidateData(histInfo);
                HistoryDB.SaveAndClose(histInfo);
            }
        }


        void Shuffle<T>(List<T> a)
        {
            Random Random = new Random();
            // Loops through array
            for (int i = a.Count - 1; i > 0; i--)
            {
                // Randomize a number between 0 and i (so that the range decreases each time)
                int rnd = Random.Next(0, i);

                // Save the value of the current i, otherwise it'll overright when we swap the values
                T temp = a[i];

                // Swap the new and old values
                a[i] = a[rnd];
                a[rnd] = temp;
            }
        }
    }
}
