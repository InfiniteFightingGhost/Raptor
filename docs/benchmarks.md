# Raptor Performance & Benchmark History

This document tracks official performance baselines across releases for the **Raptor VM & Compiler**. 

To prevent repository bloat, raw generated logs (`BenchmarkDotNet.Artifacts/`) are excluded from Git version control. Official baseline metrics for key release milestones are curated here for regression testing and performance verification.

---

## Benchmark Environment Standard

* **Target Framework:** .NET 10.0
* **Reference Hardware:** AMD Ryzen 7 (Zen 4 Architecture) / Linux x64
* **Compiler Configuration:** Release Mode (`-c Release`), `<Optimize>true</Optimize>`, `<PublishAot>true</PublishAot>`

---

## Version Baseline History

### `v1.0.0-alpha` (2026-07-20)

#### 1. Instruction Opcode Latency

| Instruction | Latency (ns) | Notes / Details |
| :--- | :--- | :--- |
| **LOADC** | 0.89 ns | Constant pool register load |
| **SUB** | 0.92 ns | Double-precision subtraction |
| **MOVE** | 1.10 ns | Register-to-register copy |
| **MUL** | 1.27 ns | Double-precision multiplication |
| **DIV** | 1.45 ns | Double-precision division |
| **SQRT** | 1.50 ns | Hardware-accelerated square root |
| **ADD** | 1.52 ns | Double-precision addition |
| **JUMP** | 1.53 ns | PC offset branch |
| **RAND** | 2.43 ns | Bit-shifted Xorshift32 PRNG |
| **FISR** | 5.68 ns | Fast Inverse Square Root |

#### 2. High-Frequency Gameplay Workloads

| Workload | Execution Time (μs) | Workload Specification |
| :--- | :--- | :--- |
| **ECS Entity Update** | **20.79 μs** | Updates `px`, `py` using velocities across 1,000 entities (20.79 ns / entity). |
| **BFS Grid Pathfinding** | **13.25 μs** | Wavefront path search on 16x16 grid. |
| **Dialogue Tree Evaluation** | **82.90 μs** | Evaluates nested quest state & gold conditions 10,000 times (8.29 ns / eval). |
| **Inventory Selection Sort** | **49.88 μs** | Selection sort ordering 100 loot items by rarity. |

#### 3. Host FFI Call Overhead

| FFI Mechanism | Overhead (ns) | Notes |
| :--- | :--- | :--- |
| **Direct Host FFI Call** | **4.70 ns** | Direct method invocation via sliding register window pointer |
| **Typed FFI Wrapper** | **< 5.00 ns** | Reflected attribute method call overhead |

---

## How to Run Local Regression Tests

When developing performance optimizations or refactoring hot execution paths in `VirtualMachine.cs`:

### 1. Run the Fast Benchmark Suite (~1-2 min)
Required for PRs modifying hot VM paths:
```bash
dotnet run --configuration Release --project Raptor.Benchmarks -- fast
```

### 2. Compare Against Previous Commit / Tag
To perform a head-to-head performance comparison on your hardware:
```bash
# Save results on your feature branch
dotnet run -c Release --project Raptor.Benchmarks -- fast > feature_bench.txt

# Switch to main branch to run baseline on identical hardware
git checkout main
dotnet run -c Release --project Raptor.Benchmarks -- fast > main_bench.txt
```
