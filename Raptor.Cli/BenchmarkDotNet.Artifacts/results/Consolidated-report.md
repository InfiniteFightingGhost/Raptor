# Raptor VM Benchmark Consolidated Report
Generated on: 2026-07-16 18:31:20

## CallStackBenchmark

| Method             | Mean              | Error          | StdDev         | Median            | Gen0     | Allocated |
|------------------- |------------------:|---------------:|---------------:|------------------:|---------:|----------:|
| CallStack_Depth_10 |   791,308.1233 ns |  3,328.6690 ns |  3,113.6391 ns |   790,685.9971 ns |        - |         - |
| CallStack_Depth_30 | 2,219,041.1102 ns | 15,934.9761 ns | 14,905.5865 ns | 2,222,230.6836 ns |        - |         - |
| Ffi_InternalCall   |    76,732.2975 ns |    190.8728 ns |    169.2037 ns |    76,702.1674 ns |        - |         - |
| Ffi_DirectBind     |    49,330.5904 ns |    216.3115 ns |    202.3379 ns |    49,371.6562 ns |        - |         - |
| Ffi_TypedWrapper   |    57,596.7739 ns |    107.6902 ns |     95.4646 ns |    57,582.6268 ns |        - |         - |
| Ffi_Fallback       |   671,533.1070 ns | 11,783.3999 ns | 11,022.1996 ns |   672,168.0098 ns | 171.8750 | 1440001 B |
| Ffi_DirectOverhead |    47,421.4016 ns |    155.4707 ns |    145.4274 ns |    47,451.1673 ns |        - |         - |
| NativeDelegate     |         0.0003 ns |      0.0010 ns |      0.0009 ns |         0.0000 ns |        - |         - |

