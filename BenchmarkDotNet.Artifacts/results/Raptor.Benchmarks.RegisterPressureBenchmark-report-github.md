```

BenchmarkDotNet v0.14.0, Arch Linux
AMD Ryzen 7 260 w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                 | Mean     | Error   | StdDev  | Allocated |
|----------------------- |---------:|--------:|--------:|----------:|
| Registers_Pressure_4   | 967.9 μs | 5.85 μs | 5.47 μs |         - |
| Registers_Pressure_64  | 667.2 μs | 3.54 μs | 3.14 μs |         - |
| Registers_Pressure_128 | 662.3 μs | 3.14 μs | 2.78 μs |         - |
