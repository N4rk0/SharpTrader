﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Indicators
{
    public class Max<T> : Indicator<T, T> where T : IBaseData
    {
        T LastOutput;
        public RollingWindow<T> Inputs { get; }
        public int Period { get; }
        public override bool IsReady => Samples > Period;
        public Max(string name, int period)
            : base(name)
        {
            Inputs = new RollingWindow<T>(period + 1);
            Period = period;
        }

        protected override T Calculate(T input)
        {
            T output;
            var sampleOut = Inputs[Period - 1];
            Inputs.Add(input);
            if (Inputs.Count < 2)
                output = input;
            else if (sampleOut.Value < LastOutput.Value)
                //if the sample that's going out of range is NOT the current max then we only need to check if the new sample is higher than current max
                output = input.Value > LastOutput.Value ? input : LastOutput;
            else if (input.Value > sampleOut.Value)
                //the MAX is going out of range, but signalIn is higher than old max then signalIn IS the new MAX
                output = input;
            else
            {
                //sample that was the old max is going out of range so we need to search again
                output = Inputs[0];
                for (int i = 1; i < Inputs.Count; i++)
                {
                    var rec = Inputs[i];
                    if (rec.Value > output.Value)
                        output = rec;
                }
            }
            LastOutput = output;
            return output;
        }

        protected override T CalculatePeek(double sample)
        {
            throw new NotImplementedException();
        }

        public override void Reset()
        {
            this.Inputs.Reset();
            LastOutput = default;
            base.Reset();
        }
    }
}
