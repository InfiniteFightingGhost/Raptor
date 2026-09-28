---
description: While fixing one bug, hunt for sibling issues in adjacent Raptor code.
mode: agent
---

Input: `$ARGUMENTS` (the bug being fixed + files touched).

1. Identify the subsystem (`Compiler/`, VM dispatch, verifier, `RaptorBinary`,
   FFI, stdlib) and list its neighboring files.
2. Search those neighbors for the **same bug class**: off-by-ones on register
   indices, unchecked casts, missing verifier arms for similar opcodes,
   divergences between run modes, disassembler/assembler asymmetries
   (`DisassemblerTests` round-trip is the oracle).
3. For each finding: file + line, why it is the same class, a minimal `.rapt`
   or xUnit snippet that would expose it, severity (crash / wrong-result /
   perf / style).
4. Do NOT fix — report only, ordered by severity. Do not touch `build/`
   output directories. Respect `AGENTS.md` gates when proposing repro commands.
