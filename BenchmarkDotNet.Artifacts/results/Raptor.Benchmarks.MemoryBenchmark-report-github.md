```

BenchmarkDotNet v0.14.0, Arch Linux
AMD Ryzen 7 260 w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                   | Mean     | Error    | StdDev   | Allocated |
|------------------------- |---------:|---------:|---------:|----------:|
| Memory_ArrayAccess       | 56.18 μs | 0.185 μs | 0.164 μs |         - |
| Memory_AllocDeallocClean | 13.85 μs | 0.058 μs | 0.055 μs |         - |
| Memory_AllocDeallocChurn | 17.10 μs | 0.109 μs | 0.102 μs |         - |
