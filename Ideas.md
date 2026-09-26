# Raptor — Ideas, Research & Reading Backlog

> Captured from a design conversation. Not a plan to implement all at once —
> a menu to pick from in small iterations. Each idea: what, why, risk, deps.

## 0. The thesis (the one thing everything should serve)

A deterministic, memory-safe-without-GC, AOT-compiled scripting language for
games, where live code edits keep state, multiplayer rolls back and replays
exactly, and bugs reproduce perfectly.

Headline bet: **whole-program reversibility as a language guarantee.**

## 1. Current state (ground truth)

- Register VM, 35 opcodes, pinned `double[256]`, function-pointer FFI
  (< 5 ns host calls), zero-GC hot path, Xorshift32 PRNG.
- Free-list heap (O(n)), manual `NEWARR`/`FREEARR`, raw-address handles
  stored as doubles.
- Prototype compiler: `var/if/while/for/expr` only. No functions in the
  language (`Return` lexed but unparsed). All showcases are hand-written `.rasm`.
- Basic chunk-level hot reload (no state migration).
- Known defects: `NEWARR`/`FOR(==/!=)` disassembly gaps; ISA/encoder drift;
  `LoadProgram` mutates chunks; `stackalloc StackFrame[32]` cap;
  verifier does not check frame-window bounds (memory-corruption path);
  optimizer IEEE-unsound (`x*0`, `x+0`); reflection FFI blocks AOT;
  benchmark numbers partly from a Stopwatch harness, not BDN.

## 2. Idea backlog (by cluster)

### A. Runtime foundations (enablers)
- **Dynamic call stack** — frames off the native stack, pinned/growable;
  remove the 32-frame cap.
- **Offset-based frames** — store `PreviousRegPtr` as an int register offset
  (like `ReturnPC` already is) so all execution state is relocatable.
- **State reification** — capture/serialize/restore VM state (registers, frames,
  IP, heap cursor, rng, gas). One primitive → many features below.
- **Verifier safety** — enforce register-window bounds; type/invariant checks.

### B. Memory model
- **Generational references** — `{slot, generation}` handles; detect
  use-after-free/double-free deterministically, no GC. (Vale)
- **Regions/arenas** — bump-allocate, bulk-free, pauseless; per-frame temporaries.
- **Deterministic destruction (higher RAII)** — `defer`/`with` → scope-exit free.
- **Per-kind strategies** — value arrays (regions), objects (generational),
  resources (linear/RAII), escape-hatch `Any`. (Vale's "seamless")
- **Adaptive strategies** — promote/demote an allocation between region and
  generational based on observed lifetime. (Frontier; no known implementation.)
- **Reversible heap** — whole-program rollback: regions for temporaries +
  periodic checkpoints for persistent state + optional undo journal.
  Pauseless compaction required. (THE BET — see §6)
- **Cross-host-heap handles** — unify script heap and Unity/.NET GC lifetimes;
  script objects as host roots. (Frontier.)
- **Auditable memory** — provenance/ownership annotations; "why is this alive."
  (Valen's auditability)

### C. Determinism & effects
- **Determinism guarantee** — language is not a source of nondeterminism:
  kill hashing, iteration order, ASLR/pointer-int, uninitialized reads,
  float drift, wall-clock, threads (unless declared).
- **Capability/effect FFI** — declared effects (`[Pure]`,`[Io]`,`[Host]`);
  sandbox mods; enable CSE/motion of pure calls.
- **Compile-time determinism certification** — reject/nondeterministic scripts
  statically. (Frontier.)

### D. Live evolution
- **Hot reload + state migration** — migrate live instances to new layouts,
  preserving invariants. (Extends current watcher.)
- **Mid-frame code swap** — replace a function while frames are active in it.
  (Frontier; Erlang can't.)

### E. Compiler & types
- **IR + CFG**, then **liveness + linear-scan/graph-coloring register allocation**
  (replace bump allocator).
- **Functions/return/break/continue**, modules/imports, strings, stdlib, errors.
- **Types without overhead** — infer/monomorphize; unboxed ops; tag only where
  unproven. Gradual typing. Add `i64` (games need ints), `f64`, `bool`, handles.
- **NaN-boxing as universal representation** — with tag elision where types are
  proven (answers "unboxing cost").

### F. Continuations & effects
- **Coroutines/generators** via reified state (cheap, zero-GC, deterministic).
- **Algebraic effects + handlers** over delimited continuations; behavior trees
  as effect handlers. (PL-research flex; crowded field.)

### G. AOT & standalone
- **AOT backend** over the shared IR — transpile to C (recommended) or emit
  IL for IL2CPP; no JIT (W^X/console target).
- **Standalone toolchain** — author in RaptorScript, produce a native executable
  with no user C# host; no .NET dependency at runtime for the true goal.
- **AOT-safe FFI** — source-generated/explicit registration (reflection is the
  blocker); C-ABI/host ABI for compiled scripts.

### H. Data-oriented
- **Automatic SoA** — lower arrays-of-structs to struct-of-arrays; vectorize
  hot loops. (Jai `soa`, but in a scripting language.)
- **Fearless concurrency** — isolated regions → parallelism without locks.

### I. Tooling
- **Time-travel debugger** — reverse execution from the reversible heap.
- **Reproducible BDN baselines** for internal suites; retire Stopwatch numbers.
- **Diagnostics** — line+column, source spans, native debug info.
- **State snapshots** — save games / rollback serialization.

## 3. Dependency spine (order to compound)

1. Honesty/consistency + measurement baseline
2. Frames off native stack + offset frames + state reification
3. Regions → generational refs
4. IR + compiler (functions + linear scan)
5. Frontier branches: reversible heap · live migration · mid-frame swap · AOT
6. Types/effects/determinism certification
7. Tooling (time-travel debugger, snapshots)

## 4. Prior art & reading (get current before claiming novelty)

### Evan Ovadia / Vale / Valen (verified links)
- Perfect Replayability: https://verdagon.dev/blog/perfect-replayability-prototyped
- Internal replay notes: https://github.com/Verdagon/Vale/blob/master/docs/PerfectReplayability.md
- Generational References: /blog/generational-references
- Hybrid-Generational Memory: /blog/hybrid-generational-memory
- Single Ownership: /blog/single-ownership-without-borrow-checking-rc-gc
- Regions: /blog/zero-cost-borrowing-regions-overview, /blog/first-regions-prototype,
  /blog/making-regions-part-1-human-factor, /blog/making-regions-part-2-generics
- Higher RAII / linear types: /blog/higher-raii-uses-linear-types
- Group Borrowing: /blog/group-borrowing
- Fearless FFI: /blog/fearless-ffi
- Hash Codes & Non-Determinism: /blog/generics-hash-codes-horrors
- Eleven Memory Safety Approaches: /grimoire/grimoire
- Zero-Overhead myth: /blog/myth-zero-overhead-memory-safety
- Value of UB/undefined behavior (for reversibility): related posts
- Ante (borrow+RC): /blog/ante-blending-borrowing-rc
- Valen: /blog/golden-spike-reviving-vale-valen · vale.dev · valen-lang.org
- Chronobase (Incendian Falls; pauseless compaction time-travel DB) — his
  2019 Roguelike Celebration talk; the closest prior art to your bet.
- Community: Discord https://discord.gg/SNB8yGH · reddit.com/r/valen

### VM & compiler
- Lua 5.4 source: `lopcodes.c`, `ldo.c`, `lvm.c`, `lcode.c`
- Ierusalimschy et al., "The Implementation of Lua 5.0"
- Smith & Nair, "Virtual Machines"
- Cooper & Torczon, "Engineering a Compiler" (IR, liveness, SSA, optimization)
- Appel, "Modern Compiler Implementation"
- Muchnick, "Advanced Compiler Design and Implementation"
- Poletto & Sarkar, "Linear Scan Register Allocation"
- Chaitin, "Register Allocation & Spilling via Graph Coloring"
- Cytron et al., "Efficiently Computing SSA Form"

### Safety & verification
- Leroy, "Java Bytecode Verification: Algorithms and Formalizations"
- Necula, "Proof-Carrying Code"

### Memory management
- Jones, Hosking, Moss, "The Garbage Collection Handbook"
- Cyclone (regions), MLKit (region inference), Rust (ownership/borrowing)
- MVCC / undo logs / write-ahead logging (databases)
- Shavit & Touitou, software transactional memory
- CRIU (checkpoint/restore); Smalltalk/Lisp images

### Replay / reversibility / time travel
- GGPO (rollback netcode); Gaffer On Games, "Deterministic Lockstep"
- Mozilla rr; UndoDB; WinDbg TTD (reverse debugging)
- Reversible computing languages: Janus (Lutz & Derby), R
- Dynamic Software Updating survey (Hicks & Nettles); Erlang hot code loading
- FoundationDB deterministic simulation testing; Antithesis

### Effects / continuations
- Plotkin & Pretnar, "Handling Algebraic Effects"
- Koka; OCaml 5 effect handlers; Unison abilities
- Danvy & Filinski, delimited continuations
- Pony (reference capabilities); WASI (capability security)

### Codegen / AOT / JIT
- Aycock, "A Brief History of Just-In-Time"
- Xu & Kjolstad, "Copy-and-Patch Compilation"
- IL2CPP & NativeAOT constraints; `RuntimeFeature.IsDynamicCodeSupported`
- Transpile-to-C precedents: Haxe, Nim, Vala
- Jai `soa`; Mike Acton, data-oriented design

## 5. The bet, in one paragraph

Whole-program reversibility as a language guarantee: a runtime that is never a
source of nondeterminism and can restore any prior state pauselessly, by fusing
generational references with a reversible allocator (regions + checkpoints) and
a compile-time determinism guarantee, in an embeddable register VM. Generalizes
application-level time-travel databases (Chronobase) and forward-only replay
(Vale) into a language primitive. Novel contribution: the fusion + guarantee +
pauselessness; not the concept of reversibility (see Janus, rr, MVCC, GGPO).

## 6. Open questions

- Whole-program vs scoped reversibility (global cost vs provability)?
- Snapshot vs undo-log vs hybrid; granularity?
- Semantics of reverting across FFI/threads/external state?
- How to invalidate/revalidate handles on rollback under generational refs?
- How to make AOT + reflection-free FFI + effects coexist?
- Does type inference reach far enough to elide tags in hot paths?
- Is "JIT off entirely" compatible with the editor/iteration story?

## 7. Glossary
- Replay = deterministic re-execution from recorded inputs (forward).
- Reversibility = restore prior state directly (backward).
- Duals over a deterministic, fully-reified runtime.
- Resilience = refactor/add code and reuse the same recording (Vale).
- The coin's three faces: determinism, input recording, state reification.
