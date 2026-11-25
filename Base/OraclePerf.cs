using System;
using System.Diagnostics;

namespace ProjectZ.Base
{
    /// <summary>
    /// Opt-in wall-clock tracing for startup and map-load investigations.
    /// Enable with ORACLE_PERF_TRACE=1. It writes to stdout so the existing
    /// screenshot/launch harnesses can capture the timestamps.
    /// </summary>
    public static class OraclePerf
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly bool Enabled =
            Environment.GetEnvironmentVariable("ORACLE_PERF_TRACE") == "1";
        private static double _lastMark;

        public static void Mark(string label)
        {
            if (!Enabled)
                return;
            var elapsed = Clock.Elapsed.TotalMilliseconds;
            var delta = elapsed - _lastMark;
            _lastMark = elapsed;
            Console.WriteLine($"oracle-perf: {elapsed,9:0.0} ms (+{delta,7:0.0}) {label}");
        }

        public static void Measure(string label, Action action)
        {
            if (!Enabled)
            {
                action();
                return;
            }

            var start = Clock.Elapsed.TotalMilliseconds;
            action();
            var end = Clock.Elapsed.TotalMilliseconds;
            Console.WriteLine($"oracle-perf: {end,9:0.0} ms [{end - start,7:0.0}] {label}");
        }
    }
}
