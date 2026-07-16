```

BenchmarkDotNet v0.14.0, Arch Linux
AMD Ryzen 7 260 w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method            | Mean         | Error       | StdDev      | Gen0   | Allocated |
|------------------ |-------------:|------------:|------------:|-------:|----------:|
| Lifecycle_Compile |  37,110.7 ns |   707.85 ns |   695.21 ns | 2.4414 |   20824 B |
| Lifecycle_Verify  |     125.4 ns |     1.32 ns |     1.17 ns |      - |         - |
| Lifecycle_Load    |     123.7 ns |     0.67 ns |     0.59 ns |      - |         - |
| Lifecycle_Execute | 187,346.0 ns | 3,699.16 ns | 5,305.23 ns |      - |         - |
