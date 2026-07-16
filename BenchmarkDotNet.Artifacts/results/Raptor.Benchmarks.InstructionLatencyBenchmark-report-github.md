```

BenchmarkDotNet v0.14.0, Arch Linux
AMD Ryzen 7 260 w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method             | Mean     | Error   | StdDev   | Median   | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------- |---------:|--------:|---------:|---------:|------:|--------:|----------:|------------:|
| Benchmark_Baseline | 155.3 μs | 1.11 μs |  1.04 μs | 155.0 μs |  1.00 |    0.01 |         - |          NA |
| Benchmark_Add      | 236.2 μs | 4.71 μs | 11.38 μs | 242.3 μs |  1.52 |    0.07 |         - |          NA |
| Benchmark_Sub      | 235.3 μs | 4.70 μs | 11.71 μs | 240.5 μs |  1.51 |    0.08 |         - |          NA |
| Benchmark_Mul      | 235.3 μs | 4.70 μs | 12.13 μs | 242.4 μs |  1.51 |    0.08 |         - |          NA |
| Benchmark_Div      | 251.7 μs | 4.98 μs |  8.72 μs | 254.4 μs |  1.62 |    0.06 |         - |          NA |
| Benchmark_Sqrt     | 195.4 μs | 2.34 μs |  1.96 μs | 194.6 μs |  1.26 |    0.01 |         - |          NA |
| Benchmark_Fisr     | 434.8 μs | 4.76 μs |  4.45 μs | 436.3 μs |  2.80 |    0.03 |         - |          NA |
| Benchmark_Rand     | 266.6 μs | 0.95 μs |  0.84 μs | 266.3 μs |  1.72 |    0.01 |         - |          NA |
| Benchmark_Loadc    | 192.4 μs | 1.23 μs |  1.15 μs | 192.2 μs |  1.24 |    0.01 |         - |          NA |
| Benchmark_Move     | 192.3 μs | 0.81 μs |  0.72 μs | 192.3 μs |  1.24 |    0.01 |         - |          NA |
| Benchmark_Jump     | 237.5 μs | 0.79 μs |  0.74 μs | 237.6 μs |  1.53 |    0.01 |         - |          NA |
