# Raptor VM Benchmark Consolidated Report
Generated on: 2026-07-16 16:54:41

## CallStackBenchmark

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

## ControlFlowBenchmark

| Method                        | Mean     | Error   | StdDev  | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------------ |---------:|--------:|--------:|------:|--------:|----------:|------------:|
| Benchmark_PredictableBranch   | 183.6 μs | 1.60 μs | 1.50 μs |  1.00 |    0.01 |         - |          NA |
| Benchmark_UnpredictableBranch | 563.5 μs | 4.06 μs | 3.80 μs |  3.07 |    0.03 |         - |          NA |

## GameplayBenchmark

| Method                   | Mean     | Error    | StdDev   | Allocated |
|------------------------- |---------:|---------:|---------:|----------:|
| Gameplay_EcsUpdate       | 20.79 μs | 0.199 μs | 0.186 μs |         - |
| Gameplay_GridPathfinding | 13.25 μs | 0.048 μs | 0.042 μs |         - |
| Gameplay_DialogueTree    | 82.90 μs | 0.328 μs | 0.307 μs |         - |
| Gameplay_InventorySort   | 49.88 μs | 0.642 μs | 0.601 μs |         - |

## InstructionLatencyBenchmark

| Method             | Mean     | Error   | StdDev   | Median   | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------- |---------:|--------:|---------:|---------:|------:|--------:|----------:|------------:|
| Benchmark_Baseline | 155.3 μs | 1.11 μs |  1.04 μs | 155.0 μs |  1.00 |    0.01 |         - |          NA |
| Benchmark_Add      | 236.2 μs | 4.71 μs | 11.38 μs | 242.3 μs |  1.52 |    0.07 |         - |          NA |
| Benchmark_Sub      | 235.3 μs | 4.70 μs | 11.71 μs | 240.5 μs |  1.51 |    0.08 |         - |          NA |
| Benchmark_Mul      | 235.3 μs | 4.70 μs | 12.13 μs | 242.4 μs |  1.51 |    0.08 |         - |          NA |
| Benchmark_Div      | 251.7 μs | 4.98 μs |  8.72 μs | 254.4 μs |  1.62 |    0.06 |         - |          NA |
| Benchmark_Sqrt     | 195.4 μs | 2.34 μs |  1.96 μs | 194.6 μs |  1.26 |    0.01 |         - |          NA |
| Benchmark_Fisr     | 434.8 μs | 4.76 μs |  4.45 μs | 436.3 μs |  2.80 |    0.03 |         - |          NA |
| Benchmark_Rand     | 266.6 μs | 0.95 μs |  0.84 μs | 266.3 μs |  1.72 |    0.01 |         - |          NA |
| Benchmark_Loadc    | 192.4 μs | 1.23 μs |  1.15 μs | 192.2 μs |  1.24 |    0.01 |         - |          NA |
| Benchmark_Move     | 192.3 μs | 0.81 μs |  0.72 μs | 192.3 μs |  1.24 |    0.01 |         - |          NA |
| Benchmark_Jump     | 237.5 μs | 0.79 μs |  0.74 μs | 237.6 μs |  1.53 |    0.01 |         - |          NA |

## LifecycleBenchmark

| Method            | Mean         | Error       | StdDev      | Gen0   | Allocated |
|------------------ |-------------:|------------:|------------:|-------:|----------:|
| Lifecycle_Compile |  37,110.7 ns |   707.85 ns |   695.21 ns | 2.4414 |   20824 B |
| Lifecycle_Verify  |     125.4 ns |     1.32 ns |     1.17 ns |      - |         - |
| Lifecycle_Load    |     123.7 ns |     0.67 ns |     0.59 ns |      - |         - |
| Lifecycle_Execute | 187,346.0 ns | 3,699.16 ns | 5,305.23 ns |      - |         - |

## MemoryBenchmark

| Method                   | Mean     | Error    | StdDev   | Allocated |
|------------------------- |---------:|---------:|---------:|----------:|
| Memory_ArrayAccess       | 56.18 μs | 0.185 μs | 0.164 μs |         - |
| Memory_AllocDeallocClean | 13.85 μs | 0.058 μs | 0.055 μs |         - |
| Memory_AllocDeallocChurn | 17.10 μs | 0.109 μs | 0.102 μs |         - |

## MultithreadedBenchmark

| Method                | Mean     | Error   | StdDev   | Gen0   | Allocated |
|---------------------- |---------:|--------:|---------:|-------:|----------:|
| Multithreaded_Scale_1 | 185.4 μs | 3.60 μs |  4.55 μs |      - |         - |
| Multithreaded_Scale_4 | 163.1 μs | 3.26 μs |  4.78 μs | 0.2441 |    2389 B |
| Multithreaded_Scale_8 | 248.4 μs | 7.22 μs | 21.30 μs |      - |    3288 B |

## RegisterPressureBenchmark

| Method                 | Mean     | Error   | StdDev  | Allocated |
|----------------------- |---------:|--------:|--------:|----------:|
| Registers_Pressure_4   | 967.9 μs | 5.85 μs | 5.47 μs |         - |
| Registers_Pressure_64  | 667.2 μs | 3.54 μs | 3.14 μs |         - |
| Registers_Pressure_128 | 662.3 μs | 3.14 μs | 2.78 μs |         - |

## VerifierBenchmark

| Method                        | Mean        | Error     | StdDev    | Gen0   | Allocated |
|------------------------------ |------------:|----------:|----------:|-------:|----------:|
| Verifier_Scale_100            |    360.0 ns |   1.38 ns |   1.22 ns |      - |         - |
| Verifier_Scale_1000           |  2,616.9 ns |  11.99 ns |  11.22 ns |      - |         - |
| Verifier_Scale_10000          | 25,885.0 ns | 125.32 ns | 117.23 ns |      - |         - |
| Verifier_Safety_InvalidJump   |  2,654.6 ns |  21.86 ns |  20.45 ns | 0.0687 |     584 B |
| Verifier_Safety_InvalidMemory |  3,168.9 ns |  17.57 ns |  16.44 ns | 0.0839 |     720 B |

## VmBenchmarks

| Method                         | Mean           | Error       | StdDev      | Allocated |
|------------------------------- |---------------:|------------:|------------:|----------:|
| Benchmark_Fibonacci            |       177.0 ns |     0.70 ns |     0.62 ns |         - |
| Benchmark_MonteCarlo           | 1,836,551.0 ns | 9,561.78 ns | 8,476.27 ns |         - |
| Benchmark_Perceptron           |   181,875.2 ns | 1,304.03 ns | 1,155.99 ns |         - |
| Benchmark_RayTracerSingleFrame |     8,247.8 ns |    87.40 ns |    81.75 ns |         - |
| Benchmark_PhysicsMovement      |   179,893.1 ns |   808.03 ns |   755.83 ns |         - |
| Benchmark_CombatDamage         |       800.2 ns |    10.18 ns |     9.52 ns |         - |

