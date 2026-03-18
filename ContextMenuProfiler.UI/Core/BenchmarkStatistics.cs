using System;
using System.Collections.Generic;

namespace ContextMenuProfiler.UI.Core
{
    public readonly record struct BenchmarkStatistics(
        int TotalExtensions,
        int DisabledExtensions,
        long TotalLoadTime,
        long ActiveLoadTime,
        long DisabledLoadTime,
        long RealLoadTimeMs)
    {
        public int ActiveExtensions => TotalExtensions - DisabledExtensions;
    }

    public static class BenchmarkStatisticsCalculator
    {
        public static BenchmarkStatistics Calculate(IEnumerable<BenchmarkResult> results)
        {
            if (results == null)
            {
                return default;
            }

            int totalExtensions = 0;
            int disabledExtensions = 0;
            long totalLoadTime = 0;
            long activeLoadTime = 0;
            long disabledLoadTime = 0;
            long realLoadTimeMs = 0;

            foreach (var result in results)
            {
                totalExtensions++;
                totalLoadTime += result.TotalTime;

                if (result.IsEnabled)
                {
                    activeLoadTime += result.TotalTime;
                    realLoadTimeMs += result.WallClockTime > 0
                        ? result.WallClockTime
                        : Math.Max(0, result.TotalTime);
                }
                else
                {
                    disabledExtensions++;
                    disabledLoadTime += result.TotalTime;
                }
            }

            return new BenchmarkStatistics(
                totalExtensions,
                disabledExtensions,
                totalLoadTime,
                activeLoadTime,
                disabledLoadTime,
                realLoadTimeMs);
        }
    }
}
