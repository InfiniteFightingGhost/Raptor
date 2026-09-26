using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;

namespace Raptor.Benchmarks;

/// <summary>
/// Shared BenchmarkDotNet configuration. Pins warmup/iteration/launch counts so
/// runs are comparable across days and machines, and enables the memory diagnoser
/// for every suite (recording whether the "zero-GC" path stays zero-alloc).
/// </summary>
public static class RaptorBenchmarkConfig
{
    public static IConfig Create() =>
        ManualConfig
            .CreateMinimumViable()
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddJob(
                Job
                    .Default.WithWarmupCount(3)
                    .WithIterationCount(10)
                    .WithLaunchCount(1)
                    .WithId("RaptorJob")
            );
}
