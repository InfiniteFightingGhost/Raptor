```

BenchmarkDotNet v0.14.0, Arch Linux
AMD Ryzen 7 260 w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                | Mean     | Error   | StdDev   | Gen0   | Allocated |
|---------------------- |---------:|--------:|---------:|-------:|----------:|
| Multithreaded_Scale_1 | 185.4 μs | 3.60 μs |  4.55 μs |      - |         - |
| Multithreaded_Scale_4 | 163.1 μs | 3.26 μs |  4.78 μs | 0.2441 |    2389 B |
| Multithreaded_Scale_8 | 248.4 μs | 7.22 μs | 21.30 μs |      - |    3288 B |
