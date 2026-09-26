using System;
using System.IO;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;

namespace Raptor.Benchmarks;

/// <summary>
/// Shared BenchmarkDotNet configuration. Pins warmup/iteration/launch counts so
/// runs are comparable across days and machines, and enables the memory diagnoser
/// for every suite (recording whether the "zero-GC" path stays zero-alloc).
/// Also pins the artifacts path: BDN can execute benchmarks from a generated
/// project, which makes the default artifacts directory unstable and causes
/// <see cref="Program"/>'s consolidation step to merge stale reports.
/// </summary>
public static class RaptorBenchmarkConfig
{
    public static IConfig Create()
    {
        string artifactsPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "BenchmarkDotNet.Artifacts")
        );

        return ManualConfig
            .CreateMinimumViable()
            .WithArtifactsPath(artifactsPath)
            .AddExporter(
                MarkdownExporter.GitHub,
                HtmlExporter.Default,
                BenchmarkDotNet.Exporters.Csv.CsvExporter.Default
            )
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddJob(
                Job
                    .Default.WithWarmupCount(3)
                    .WithIterationCount(10)
                    .WithLaunchCount(1)
                    .WithId("RaptorJob")
            );
    }
}
