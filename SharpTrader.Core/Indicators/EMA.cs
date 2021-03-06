﻿using System;
using System.Collections.Generic;
using System.Text;

namespace SharpTrader.Indicators
{
    /// <summary>
    /// Exponential moving average
    /// </summary>
    public class EMA<T> : Indicator<T, IndicatorDataPoint> where T : IBaseData
    {
        public int Period { get; set; }
        private double Alpha;
        private Func<T, double> ValueSelector;
        private IndicatorDataPoint LastOutput;
        public override bool IsReady => SamplesCount >= Period;
        public EMA(int emaPeriod, Func<T, double> valueSelector, TimeSerieNavigator<T> signal, DateTime warmUpTime)
            : base("EMA", signal, warmUpTime)
        {
            ValueSelector = valueSelector;
            Period = emaPeriod;
            Alpha = 1d / Period;
        }
        public EMA(string name, int period) : base(name)
        {
            Period = period;
            Alpha = 1d / Period;
        }

        protected override double CalculatePeek(double sample)
        {
            var signal = sample;
            var last = LastOutput.Value;
            return last + Alpha * (signal - last);
        }

        protected override IndicatorDataPoint Calculate(T input)
        {
            if (LastOutput == null)
            {
                LastOutput = new IndicatorDataPoint(input.Time, input.Value);
            }
            else
            { 
                var signal = ValueSelector != null ? ValueSelector(input) : input.Value;
                var last = LastOutput.Value;
                LastOutput = new IndicatorDataPoint(input.Time, last + Alpha * (signal - last));
            }
            return LastOutput;
        }

        public override void Reset()
        {
            this.LastOutput = null;
            base.Reset();
        }
    }

}
