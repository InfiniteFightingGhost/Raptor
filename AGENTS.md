# Raptor — agent instructions

Raptor is a register-based VM + RaptorScript language for .NET (game-engine hot loops).
Solution `Raptor.sln`: `Raptor/` (VM lib, also packed as `Raptor.VM` NuGet + Unity `asmdef`),
`Raptor.Cli/` (`run|docs|build|new`), `Raptor.Tests/` (xUnit), `Raptor.Benchmarks/`,
`Raptor.AotSmoke/`. Language files use `.rapt` (`test_array.rapt` at root, `examples/`).

## Verified architecture (do not contradict)

- Registers are **64-bit doubles only**; 256 virtual registers, `GCHandle`-pinned, raw-pointer
  access. Hot loop must stay at **zero managed allocations**.
- Pipeline: RaptorScript (`Raptor/Compiler/`) → bytecode (`VMChunk.cs`, `Instruction.cs`,
  `OpCode.cs`) → `BytecodeVerifier.cs` → `VirtualMachine.cs`. `Assembler.cs` /
  `Disassembler.cs` / `RaptorBinary.cs` must round-trip exactly.
- Multiple VM run modes exist (see `VmRunModesTests.cs`) — behavior must agree across modes.
- FFI via `FFIHostTable.cs`; `ScriptEngine.cs` is the embed entry point.

## Gates (mirror `.github/workflows/ci.yml`)

```powershell
dotnet build Raptor.sln -c Release /p:TreatWarningsAsErrors=true
dotnet test Raptor.Tests -f net10.0 -c Release            # fast loop; CI also runs net8.0 + net9.0
dotnet format Raptor.sln --verify-no-changes               # CI fails on drift
dotnet run -c Release --project Raptor.Benchmarks --no-build -- --fast
```

`TreatWarningsAsErrors=true` is enforced in `Raptor.Tests` and CI — never suggest
suppressing a warning to make a build pass. `dotnet pack` must keep working (public API
surface). AOT smoke is linux-only; do not run `dotnet publish -r linux-x64` on Windows.

## Working rules

- Multi-target `net8.0;net9.0;net10.0`: no APIs newer than net8.0 without `#if` guards.
- `AllowUnsafeBlocks` is on: every new `unsafe`/pointer edit needs a bounds-safety argument.
- `BytecodeVerifier.cs` is the security boundary (untrusted `.rapt`): any new opcode or
  encoding change MUST extend the verifier + `BytecodeVerifierTests` + disassembler round-trip.
- Perf claims need numbers: use `Raptor.Benchmarks -- --fast`, MIPS on this box is ~360-660.
- Test style: xUnit, file-per-area in `Raptor.Tests/` (e.g. `FusedBranchTests.cs`).
- Run `Raptor: test (net10, fast)` task after each fix; run `Raptor: format check` before
  declaring done.
