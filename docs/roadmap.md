# Raptor Roadmap

Living document. Check items off as they land; keep the *Sources* — they're chosen so you can
read **before you start** and come back **when you're stuck**. Update the doc when reality
changes rather than letting it rot.

Guiding constraints (decided, non-negotiable):
- **AOT-only**, **zero GC** (generational references + region borrow checking), **no unions**.
- **Deterministic and replayable** without killing performance.
- Types: a **Lua-like dynamic core** with **optional, programmer-supplied annotations**.

---

## Short-term (this week → ~2 weeks)

### 0. Clear the landmines
- [ ] Update `docs/architecture.md` and `docs/assembler.md` to the v2 encoding (`docs/isa.md` is the source of truth).
- [ ] `RaptorBinary`: bump the format major version and **reject** mismatched blobs (a v1 `.rbc` currently decodes as garbage).
- [ ] Stop aliasing the live register file: HALT returns `_registers` directly; return a copy instead.

*Sources:* your own `docs/isa.md`. For the binary format, `BinaryWriter`/`MemoryMarshal` in the .NET docs.

### 1. Type system Phase 1 — runtime value model
- [ ] NaN-box `nil` / `bool` / `number` into the 64-bit registers.
- [ ] `TYPEOF`, `ISNUM`, `ISBOOL`.
- [ ] Checked tier: `ADD_ANY`, `LT_ANY`, `EQ_ANY` — numeric fast path unchanged, deterministic panic on non-numbers.
- [ ] Replace the lossy `(double)(ulong)ptr` with `Unsafe.As` / `BitConverter.DoubleToInt64Bits` bit reinterpretation.
- [ ] Numbers must stay as fast as today (verify with the throughput benchmark).

*Before:* Andy Wingo, "Value representation in JavaScript implementations" (wingolog.org). Lua/LuaJIT `lobject.h` (`TValue`).
*Stuck:* JavaScriptCore `JSValue`, SpiderMonkey `Value`; search "NaN boxing canonicalization" for the `0.0/0.0` tag-collision trap.

### 2. Compiler: functions, scoping, register allocation
- [ ] Procedure declarations + `CALL`/`RETURN` emitted from language constructs.
- [ ] Lexical scoping and upvalues.
- [ ] Linear-scan register allocator (replace ad-hoc allocation and stray `MOVE`s).
- [ ] Constant folding / dead-code elimination pass (extend `AstOptimizer`).

*Before:* Bob Nystrom, *Crafting Interpreters* (craftinginterpreters.com) — locals, upvalues, bytecode VM. Then Poletto & Sarkar, "Linear Scan Register Allocation" (1999).
*Stuck:* Appel, *Modern Compiler Implementation*; Cytron et al., "Efficiently Computing SSA Form" (1991); Cooper & Torczon, *Engineering a Compiler*.

### 3. Type system Phase 2 — strings
- [ ] Immutable, interned strings in the VM heap; `CONCAT`, `LEN`, pointer equality, memoised hash.
- [ ] Reference counting or region ownership (still no tracing GC).

*Before:* Bacon & Rajan, "Concurrent Cycle Collection in Reference Counted Systems" (RC alone leaks cycles); Swift ARC model.
*Stuck:* Jones, Hosking & Moss, *The Garbage Collection Handbook* (RC + cycle collection chapters).

### 4. Determinism harness + replay spine
- [ ] Test: same program twice → identical register snapshots and RNG sequence.
- [ ] Float determinism policy: ban or pin `POW`/transcendentals (`Math.Pow` is not bit-reproducible). Canonicalize NaNs.
- [ ] FFI record/replay boundary.

*Before:* Glenn Fiedler, "Deterministic Lockstep" and "Networked Physics" (gafferongames.com); Goldberg, "What Every Computer Scientist Should Know About Floating-Point Arithmetic".
*Stuck:* Monniaux, "The pitfalls of verifying floating-point computations"; FoundationDB/Antithesis deterministic-simulation writeups.

### 5. Benchmark re-baseline + regression tracking
- [ ] Re-run the full suite at the **locked clock** (v2 + perf changed numbers); refresh `docs/benchmarks.md`.
- [ ] Make `ThroughputBenchmark` (ns/VM-instruction, MIPS) the headline metric.
- [ ] Keep the CI `--fast` step a **smoke test**, not a perf gate.

*Before:* your own `docs/benchmarks.md` "Memory-Layout Sensitivity" section — obey it. BenchmarkDotNet docs.
*Stuck:* Brendan Gregg, *Systems Performance*; Agner Fog's microarchitecture PDFs (agner.org/optimize).

### 6. Robustness debt
- [ ] Single pinned arena for `_instructions` / `_registers` / `_constants` at controlled 4 KB offsets (kills layout sensitivity).
- [ ] Non-O(n) allocator (segregated / bitmap) once strings/tables exist.
- [ ] Fix the array-temp register TODO in `Emitter`.

*Before:* your `docs/benchmarks.md` layout note.
*Stuck:* Intel SDM / AMD Software Optimization Guide (4K aliasing, store forwarding); Agner Fog.

---

## Long-term (→ March 2027 and beyond)

### 7. Type system Phases 3–4 — composite types + static layer
- [ ] `table` / struct + **regions + generational references**.
- [ ] Optional annotations (`x: number`, `fn f(a: number) -> bool`) + a **type-checker pass**.
- [ ] "Types select opcodes": statically typed → unchecked ops; `any` → checked ops.

*Before:* Siek & Taha, "Gradual Typing for Functional Languages"; **Luau** type-system docs (luau.org); Vale generational references (vale.dev).
*Stuck:* Pierce, *Types and Programming Languages*; Rust Book ch. 4 + the *Rustonomicon*; Tofte & Talpin, "Region-Based Memory Management".

### 8. Closures & upvalues
- [ ] `CLOSURE`, `GETUPVAL`/`SETUPVAL`, open→closed upvalue handling on scope exit.

*Before:* *Crafting Interpreters* closures chapter; Lua `lfunc.c` / `lopcodes.c`.
*Stuck:* Lua `lvm.c` (open-upvalue resolution against the live stack).

### 9. Replay / time-travel core
- [ ] Command/event log, deterministic re-execution, snapshots, and *causal* metadata (which inputs caused which state).

*Before:* Lamport, "Time, Clocks, and the Ordering of Events in a Distributed System" (1978); Fiedler's replay posts.
*Stuck:* provenance — Cheney et al., "Provenance in Databases"; Buneman/Khanna/Tan, "Why and Where"; Uustalu & Vene, "The Essence of Dataflow Programming".

### 10. Testing: fuzzing, differential, property-based
- [ ] Verifier/VM fuzzer: random `uint[]` → verifier must accept-or-throw, never AV/hang; accepted → `RunFast` panics cleanly.
- [ ] Differential tests (Raptor vs Lua on shared semantics).
- [ ] Property tests for arithmetic and encode/decode.

*Before:* *The Fuzzing Book* (fuzzingbook.org); Claessen & Hughes, "QuickCheck" (2000); McKeeman, "Differential Testing for Software".
*Stuck:* JVM bytecode verifier spec (JVMS §4.10); SharpFuzz / FsCheck docs.

### 11. Unity demo + engine integration
- [ ] Unity C# host embedding Raptor, driving game objects via FFI, demonstrating deterministic replay of a scenario (Stage-1 capstone artifact).

*Before:* Unity plugin + IL2CPP interop docs; *Game Programming Patterns* (Nystrom); Mike Acton, "Data-Oriented Design".
*Stuck:* Unity forums on IL2CPP + `delegate*` / `UnmanagedCallersOnly`; Richard Fabian, *Data-Oriented Design*.

### 12. Tooling & language completeness
- [ ] Stepping debugger (breakpoints, watch, step) on top of `RunDebug` + `SourceMap`.
- [ ] LSP for editor support (stretch).
- [ ] Stdlib growth, module system, and a decided error-handling model.

*Before:* Debug Adapter Protocol (microsoft.github.io/debug-adapter-protocol); Language Server Protocol.
*Stuck:* how other VMs expose debug info (Lua `ldebug.c`; CPython `ceval`).

### 13. The research bet: temporal / causal programming
- [ ] First-class causal edges between effects and inputs; "why did this value happen" queries; branching timelines with rollback.

*Before:* Lamport (above); Pearl, *Causality* (concepts); Moseley & Marks, "Out of the Tar Pit"; version vectors / Interval Tree Clocks.
*Stuck:* provenance + FRP literature (item 9); Acar, *Self-Adjusting Computation*.

### 14. Capstone packaging
- [ ] Narrative pairing engineering (cycles/VM-inst, beats native Lua, unsafe C#, zero-alloc, no GC) with the research direction (deterministic causal replay).
- [ ] Re-run everything clock-locked; freeze numbers; fold `examples/` into the evaluation.

*Before:* your own `docs/` and `examples/`.
*Stuck:* SIGPLAN / VEE interpreter-performance papers for framing.

---

## Suggested sequencing
`0` → `1` → `2` → `4` → `3` → `6` → `7` → `8` → `9` → `11` → (`10`, `12` in parallel) → `13` → `14`

## Bibliography (consolidated)
- Nystrom, *Crafting Interpreters*; *Game Programming Patterns*.
- Pierce, *Types and Programming Languages*; Harper, *Practical Foundations for Programming Languages*.
- Appel, *Modern Compiler Implementation*; Cooper & Torczon, *Engineering a Compiler*; Aho et al., *Compilers*.
- Jones/Hosking/Moss, *The Garbage Collection Handbook*; Gregg, *Systems Performance*.
- Fiedler (gafferongames.com); Goldberg (floating point); Lamport (time/clocks); Pearl (*Causality*); Moseley & Marks (Out of the Tar Pit); Acar (Self-Adjusting Computation).
- Ertl & Gregg, "The Structure and Performance of Efficient Interpreters"; Poletto & Sarkar (linear scan); Deutsch & Schiffman (inline caching); Cytron et al. (SSA); Claessen & Hughes (QuickCheck); McKeeman (differential testing); Tofte & Talpin (regions); Bacon & Rajan (cycle collection).
- Online: wingolog (value representation); luau.org; vale.dev; fuzzingbook.org; agner.org/optimize; `.NET` NativeAOT + tiered-compilation docs; Debug Adapter Protocol; JVMS §4.10.