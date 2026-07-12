```

BenchmarkDotNet v0.14.0, Arch Linux
AMD Ryzen 7 260 w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                         | Mean        | Error     | StdDev    | Gen0     | Allocated  |
|------------------------------- |------------:|----------:|----------:|---------:|-----------:|
| Benchmark_Fibonacci            |   565.22 μs | 11.017 μs |  9.766 μs |        - |     2.2 KB |
| Benchmark_MonteCarlo           | 1,898.82 μs | 17.326 μs | 16.207 μs |        - |    2.21 KB |
| Benchmark_Perceptron           |   197.21 μs |  2.228 μs |  2.084 μs |   0.2441 |     2.2 KB |
| Benchmark_RayTracerSingleFrame |   281.96 μs |  5.173 μs |  4.839 μs |        - |     2.2 KB |
| Benchmark_FfiDirectBind        |    75.88 μs |  0.177 μs |  0.138 μs |   0.2441 |     2.2 KB |
| Benchmark_FfiTypedWrapper      |    94.71 μs |  0.655 μs |  0.547 μs |   0.2441 |     2.2 KB |
| Benchmark_InternalCall         |    92.57 μs |  1.808 μs |  1.935 μs |   0.2441 |     2.2 KB |
| Benchmark_FfiFallback          |   781.91 μs | 15.578 μs | 19.131 μs | 171.8750 | 1408.17 KB |
