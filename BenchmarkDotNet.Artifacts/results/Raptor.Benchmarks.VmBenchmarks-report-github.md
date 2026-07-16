```

BenchmarkDotNet v0.14.0, Arch Linux
AMD Ryzen 7 260 w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                         | Mean           | Error       | StdDev      | Allocated |
|------------------------------- |---------------:|------------:|------------:|----------:|
| Benchmark_Fibonacci            |       177.0 ns |     0.70 ns |     0.62 ns |         - |
| Benchmark_MonteCarlo           | 1,836,551.0 ns | 9,561.78 ns | 8,476.27 ns |         - |
| Benchmark_Perceptron           |   181,875.2 ns | 1,304.03 ns | 1,155.99 ns |         - |
| Benchmark_RayTracerSingleFrame |     8,247.8 ns |    87.40 ns |    81.75 ns |         - |
| Benchmark_PhysicsMovement      |   179,893.1 ns |   808.03 ns |   755.83 ns |         - |
| Benchmark_CombatDamage         |       800.2 ns |    10.18 ns |     9.52 ns |         - |
