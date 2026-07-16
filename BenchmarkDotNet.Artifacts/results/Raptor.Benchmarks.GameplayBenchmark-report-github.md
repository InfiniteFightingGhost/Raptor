```

BenchmarkDotNet v0.14.0, Arch Linux
AMD Ryzen 7 260 w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                   | Mean     | Error    | StdDev   | Allocated |
|------------------------- |---------:|---------:|---------:|----------:|
| Gameplay_EcsUpdate       | 20.79 μs | 0.199 μs | 0.186 μs |         - |
| Gameplay_GridPathfinding | 13.25 μs | 0.048 μs | 0.042 μs |         - |
| Gameplay_DialogueTree    | 82.90 μs | 0.328 μs | 0.307 μs |         - |
| Gameplay_InventorySort   | 49.88 μs | 0.642 μs | 0.601 μs |         - |
