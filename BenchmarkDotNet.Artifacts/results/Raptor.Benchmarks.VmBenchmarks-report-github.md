```

BenchmarkDotNet v0.14.0, Arch Linux
AMD Ryzen 7 260 w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                         | Mean       | Error   | StdDev  | Gen0   | Allocated |
|------------------------------- |-----------:|--------:|--------:|-------:|----------:|
| Benchmark_Fibonacci            |   493.5 μs | 2.51 μs | 1.96 μs |      - |    2.2 KB |
| Benchmark_MonteCarlo           | 1,912.5 μs | 4.76 μs | 3.71 μs |      - |   2.21 KB |
| Benchmark_Perceptron           |   186.9 μs | 0.55 μs | 0.46 μs | 0.2441 |    2.2 KB |
| Benchmark_RayTracerSingleFrame |   272.1 μs | 5.24 μs | 5.61 μs |      - |    2.2 KB |
