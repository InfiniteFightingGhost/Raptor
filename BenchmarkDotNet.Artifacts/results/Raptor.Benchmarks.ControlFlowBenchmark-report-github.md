```

BenchmarkDotNet v0.14.0, Arch Linux
AMD Ryzen 7 260 w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                        | Mean     | Error   | StdDev  | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------------ |---------:|--------:|--------:|------:|--------:|----------:|------------:|
| Benchmark_PredictableBranch   | 183.6 μs | 1.60 μs | 1.50 μs |  1.00 |    0.01 |         - |          NA |
| Benchmark_UnpredictableBranch | 563.5 μs | 4.06 μs | 3.80 μs |  3.07 |    0.03 |         - |          NA |
