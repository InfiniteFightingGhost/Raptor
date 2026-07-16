```

BenchmarkDotNet v0.14.0, Arch Linux
AMD Ryzen 7 260 w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                        | Mean        | Error     | StdDev    | Gen0   | Allocated |
|------------------------------ |------------:|----------:|----------:|-------:|----------:|
| Verifier_Scale_100            |    360.0 ns |   1.38 ns |   1.22 ns |      - |         - |
| Verifier_Scale_1000           |  2,616.9 ns |  11.99 ns |  11.22 ns |      - |         - |
| Verifier_Scale_10000          | 25,885.0 ns | 125.32 ns | 117.23 ns |      - |         - |
| Verifier_Safety_InvalidJump   |  2,654.6 ns |  21.86 ns |  20.45 ns | 0.0687 |     584 B |
| Verifier_Safety_InvalidMemory |  3,168.9 ns |  17.57 ns |  16.44 ns | 0.0839 |     720 B |
