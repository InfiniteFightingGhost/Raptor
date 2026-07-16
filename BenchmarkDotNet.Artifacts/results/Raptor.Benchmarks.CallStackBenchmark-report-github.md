```

BenchmarkDotNet v0.14.0, Arch Linux
AMD Ryzen 7 260 w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method             | Mean              | Error          | StdDev         | Median            | Gen0     | Allocated |
|------------------- |------------------:|---------------:|---------------:|------------------:|---------:|----------:|
| CallStack_Depth_10 |   842,554.8734 ns |  8,144.2226 ns |  7,618.1109 ns |   838,644.3359 ns |        - |         - |
| CallStack_Depth_30 | 2,350,237.2843 ns | 17,555.6962 ns | 15,562.6674 ns | 2,348,619.4492 ns |        - |         - |
| Ffi_InternalCall   |    81,133.6978 ns |    421.9917 ns |    374.0847 ns |    81,010.9195 ns |        - |         - |
| Ffi_DirectBind     |    51,737.7509 ns |    364.3053 ns |    322.9471 ns |    51,716.6266 ns |        - |         - |
| Ffi_TypedWrapper   |    60,475.8086 ns |    734.8589 ns |    687.3875 ns |    60,084.0997 ns |        - |         - |
| Ffi_Fallback       |   720,294.5923 ns |  9,519.7209 ns |  8,904.7528 ns |   719,430.8564 ns | 171.8750 | 1440001 B |
| Ffi_DirectOverhead |    49,653.6458 ns |    472.7186 ns |    442.1813 ns |    49,543.2073 ns |        - |         - |
| NativeDelegate     |         0.0015 ns |      0.0030 ns |      0.0028 ns |         0.0000 ns |        - |         - |
