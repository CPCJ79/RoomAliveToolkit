/*
 * Copyrigth Microsoft Research 2016
 * */

using System;
using System.Diagnostics;

namespace RoomAliveToolkit
{
    public class FrameRate
    {
        public FrameRate(float intervalSeconds)
        {
            stopwatch = Stopwatch.StartNew();
            this.intervalTicks = (long)(intervalSeconds * Stopwatch.Frequency);
            startTicks = stopwatch.ElapsedTicks;
        }

        public bool Tick()
        {
            ticks++;

            long timeNow = stopwatch.ElapsedTicks;

            if ((timeNow - startTicks) > intervalTicks)
            {
                frameRate = (double)ticks / (double)(timeNow - startTicks) * Stopwatch.Frequency;
                frameRate = ((double)((int)(frameRate * 100.0))) / 100.0;
                ticks = 0;
                startTicks = timeNow;
                updated = true;
                return true;
            }
            return false;
        }

        public void PrintMessage(string type)
        {
            if (updated)
            {
                Console.WriteLine("{0} framerate = {1}", type, frameRate);
                updated = false;
            }
        }

        private readonly Stopwatch stopwatch;
        private long startTicks;
        private int ticks = 0;
        private readonly long intervalTicks;
        private bool updated = false;
        private double frameRate;

        public double Framerate
        {
            get { return frameRate; }
        }
    }
}
