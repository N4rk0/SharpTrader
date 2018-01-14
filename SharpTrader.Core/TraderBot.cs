﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{


    public abstract class TraderBot : IChartDataListener
    {
        public bool Active { get; set; }
        public IMarketsManager MarketsManager { get; }
        public GraphDrawer Drawer { get; }
        public TraderBot(IMarketsManager marketApi)
        {
            Drawer = new GraphDrawer();
            MarketsManager = marketApi;
        }
        public abstract void Start();

        public abstract void OnNewCandle(ISymbolFeed sender, ICandlestick newCandle);
    }


}
