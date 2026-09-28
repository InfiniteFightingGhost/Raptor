---
description: Triple-check pass over a Raptor change (correctness, verifier, perf).
mode: agent
---

Run three independent passes over the current diff (`git diff`) and report each
separately. Follow repo rules in `AGENTS.md`.

**Pass 1 — correctness.** For every changed opcode/encoder path in `Raptor/`:
trace one concrete `.rapt` example through compiler → `VMChunk` → VM dispatch.
Check operand widths, register indices < 256, sign handling. Cross-check run modes
agree (see `VmRunModesTests.cs`).

**Pass 2 — verifier/security.** Assume hostile bytecode. Does `BytecodeVerifier.cs`
reject every new malformed shape this change admits (truncated streams, out-of-range
registers, bad jumps)? Pinned-heap check: `VirtualMachine` keeps raw pointers
(`_heapPtr`, `HeapPtr` in `VMState`) into `GCHandle`-pinned arrays — confirm every
new `offset`/`count` flowing into those `fixed` spans and pointer arithmetic is
bounds-checked against the heap size before dereference, and that FFI-exposed
pointers cannot escape those bounds. If the change adds an opcode without verifier
coverage, FAIL the review and name the missing test in `BytecodeVerifierTests.cs`.

**Pass 3 — perf/alloc.** Flag any managed allocation introduced into the interpret
loop (`new`, boxing, closure capture, LINQ, string formatting in dispatch).
Flag APIs newer than net8.0. If a claim about speed is made, demand a
`Raptor.Benchmarks -- --fast` number.

End with exactly one of: `PASS`, `PASS WITH NOTES`, `FAIL`, plus the single most
important issue if not PASS.
