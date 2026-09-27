# Instruction Set Architecture (ISA) Reference

This document specifies the virtual machine's instruction set architecture (ISA).

## Operand Types Key

- `rA`: Destination register (8-bit, `0`..`255`), relative to the active frame pointer `RegPtr`.
- `rB`: Source register operand (8-bit, `0`..`255`). **Always a register** — a constant cannot be encoded in `B`.
- `rC` / `kC`: The `C` operand (9-bit, `0`..`511`) in RC form: `0`..`255` is register `rC`; `256`..`511` is constant-pool index `rC - 256` (`Constants[rC - 256]`).
- `opB` / `opC`: The `B` and `C` operands as written in the table below (`opC` may be a register or a constant).
- `Bx`: Unsigned 17-bit immediate value or constant pool index (`iABx`).
- `sBx17`: Signed 17-bit branch offset, biased by `65,535` (`iAsBx`).
- `sBx25`: Signed 25-bit branch offset, biased by `16,777,215` (`iAx`).

## Instruction Set Table

| Opcode | Hex (Dec) | Format | Syntax Example | Operation Description |
| :--- | :--- | :--- | :--- | :--- |
| `LOADC` | `0x00` (0) | `ABx` | `LOADC rA literal` | Loads floating-point constant `literal` into register `rA` via constant pool index `Bx`. <br> $\text{rA} = \text{Constants}[\text{Bx}]$ |
| `MOVE` | `0x01` (1) | `ABx` | `MOVE rA rB` | Copies value from register `rB` (passed as lower 9 bits of `Bx`) into register `rA`. <br> $\text{rA} = \text{rB}$ |
| `SWP` | `0x02` (2) | `ABx` | `SWP rA rB` | Swaps the values of registers `rA` and `rB`. <br> $(\text{rA}, \text{rB}) = (\text{rB}, \text{rA})$ |
| `ADD` | `0x03` (3) | `ABC` | `ADD rA opB opC` | Adds `opB` and `opC`, storing the result in `rA`. <br> $\text{rA} = \text{opB} + \text{opC}$ |
| `SUB` | `0x04` (4) | `ABC` | `SUB rA opB opC` | Subtracts `opC` from `opB`, storing the result in `rA`. <br> $\text{rA} = \text{opB} - \text{opC}$ |
| `MUL` | `0x05` (5) | `ABC` | `MUL rA opB opC` | Multiplies `opB` and `opC`, storing the result in `rA`. <br> $\text{rA} = \text{opB} \times \text{opC}$ |
| `DIV` | `0x06` (6) | `ABC` | `DIV rA opB opC` | Divides `opB` by `opC`, storing the result in `rA`. <br> $\text{rA} = \text{opB} / \text{opC}$ |
| `POW` | `0x07` (7) | `ABC` | `POW rA opB opC` | Computes `opB` to the power of `opC` using double precision. <br> $\text{rA} = \text{opB}^{\text{opC}}$ |
| `UNM` | `0x08` (8) | `ABx` | `UNM rA opB` | Stores the unary negation of `opB` in `rA`. <br> $\text{rA} = -\text{opB}$ |
| `JUMP` | `0x09` (9) | `sBx26`| `JUMP label` | Performs relative jump. <br> $\text{Ip} += \text{sBx26} - 1$ |
| `EQ` | `0x0A` (10) | `ABC` | `EQ A opB opC` | Conditional skip. Compares `opB == opC`. If equality matches expected condition `(A != 0)`, the next instruction is skipped: <br> $\text{if } ((\text{opB} == \text{opC}) == (\text{A} \ne 0)) \implies \text{Ip}++$ |
| `LT` | `0x0B` (11) | `ABC` | `LT A opB opC` | Conditional skip. Compares `opB < opC`. If condition matches `(A != 0)`, the next instruction is skipped: <br> $\text{if } ((\text{opB} < \text{opC}) == (\text{A} \ne 0)) \implies \text{Ip}++$ |
| `LE` | `0x0C` (12) | `ABC` | `LE A opB opC` | Conditional skip. Compares `opB <= opC`. If condition matches `(A != 0)`, the next instruction is skipped: <br> $\text{if } ((\text{opB} \le \text{opC}) == (\text{A} \ne 0)) \implies \text{Ip}++$ |
| `HALT` | `0x0D` (13) | `ABC` | `HALT` | Halts VM execution. Flushes and prints the contents of the thread's internal string buffer. |
| `PRINT` | `0x0E` (14) | `ABC` | `PRINT opB` | Prints the double value of `opB` followed by a newline to the standard output. |
| `PRINTA`| `0x0F` (15) | `ABC` | `PRINTA opB` | Prints the value of `opB` cast to a character (ASCII) to the standard output. |
| `RAND` | `0x10` (16) | `ABC` | `RAND rA` | Generates a pseudo-random double in `[0, 1]` using XORShift32 and writes it to `rA`. |
| `SQRT` | `0x11` (17) | `ABx` | `SQRT rA opB` | Computes the square root of `opB` and writes it to `rA`. <br> $\text{rA} = \sqrt{\text{opB}}$ |
| `FISR` | `0x12` (18) | `ABx` | `FISR rA opB` | Computes the Fast Inverse Square Root of `opB` and writes it to `rA`. <br> $\text{rA} = 1 / \sqrt{\text{opB}}$ |
| `CALL` | `0x13` (19) | `ABx` | `CALL method() rStart` | Slides register base pointer and transfers control. <br> $\text{StackFramePush}(\text{Ip} - \text{InstPtr}, \text{RegPtr})$ <br> $\text{RegPtr} += \text{rStart}$ <br> $\text{Ip} = \text{InstPtr} + \text{MethodTable}[\text{methodIndex}]$ |
| `RETURN`| `0x14` (20) | `ABx` | `RETURN rStart rEnd` | Restores calling frame and returns values. Copies return range `[rStart, rEnd]` to callee window start, pops StackFrame, and restores parent `RegPtr` and `Ip`. |
| `FOR` | `0x15` (21) | *2 Words*| `FOR rIndex max step < label` | **Word 1 (`ABC`):** Stores `rIndex`, `max`, `step`. <br> **Word 2 (`AsBx`):** Stores comparison operator (`comp`) and relative `jumpOffset`. <br> Increments `rIndex` by `step`. If condition `(rIndex comp max)` is true, jumps: $\text{Ip} += \text{jumpOffset} - 2$. |
| `MOD` | `0x16` (22) | `ABC` | `MOD rA opB opC` | Computes the floating-point modulo `opB % opC`, storing the result in `rA`. <br> $\text{rA} = \text{opB} \pmod{\text{opC}}$ |
| `NEWARR`| `0x17` (23) | `AB` | `NEWARR rA size` | Allocates a double array of `size` elements (automatically allocating `size * 8` bytes + 4 header bytes) on the heap, and writes the resulting payload address to register `rA`. |
| `FREEARR`| `0x18` (24) | `ABC` | `FREEARR rA` | Deallocates the array pointed to by `rA` on the heap, and immediately coalesces contiguous free blocks. Sets `rA` to `0`. |
| `SETARR` | `0x19` (25) | `ABC` | `SETARR rPtr index val` | Writes double `val` into heap array `rPtr` at element index `index` (offset in 8-byte steps). <br> $\text{Heap}[\text{rPtr} + \text{index} \times 8] = \text{val}$ |
| `SETARRA`| `0x1A` (26) | `ABC` | `SETARRA rPtr index val`| Writes byte `val` (ASCII character) into heap array `rPtr` at byte offset `index`. <br> $\text{Heap}[\text{rPtr} + \text{index}] = \text{val}$ |
| `GETARR` | `0x1B` (27) | `ABC` | `GETARR rA rPtr index` | Reads double from heap array `rPtr` at element index `index` (offset in 8-byte steps) and writes to register `rA`. <br> $\text{rA} = \text{Heap}[\text{rPtr} + \text{index} \times 8]$ |
| `GETARRA`| `0x1C` (28) | `ABC` | `GETARRA rA rPtr index`| Reads byte from heap array `rPtr` at byte offset `index` and writes to register `rA`. <br> $\text{rA} = \text{Heap}[\text{rPtr} + \text{index}]$ |
| `BINAND`| `0x1D` (29) | `ABC` | `BINAND rA opB opC` | Performs bitwise AND of `opB` and `opC` (cast to `long`), writing the result as a double to `rA`. |
| `BINOR` | `0x1E` (30) | `ABC` | `BINOR rA opB opC`  | Performs bitwise OR of `opB` and `opC` (cast to `long`), writing the result as a double to `rA`. |
| `BINXOR`| `0x1F` (31) | `ABC` | `BINXOR rA opB opC` | Performs bitwise XOR of `opB` and `opC` (cast to `long`), writing the result as a double to `rA`. |
| `BINLSH`| `0x20` (32) | `ABC` | `BINLSH rA opB opC` | Performs bitwise left shift of `opB` by `opC` bits (cast to integers), writing the result as a double to `rA`. |
| `BINRSH`| `0x21` (33) | `ABC` | `BINRSH rA opB opC` | Performs bitwise right shift of `opB` by `opC` bits (cast to integers), writing the result as a double to `rA`. |
| `LENARR`| `0x22` (34) | `ABC` | `LENARR rA rPtr` | Reads element count of heap double array `rPtr` and writes to register `rA`. <br> $\text{rA} = (\text{Heap}[\text{rPtr} - 4] - 4) / 8$ |

## Instruction Bit Encoding

> **Status:** v2 layout (7-bit opcode). The previous v1 layout used a 6-bit opcode with 9-bit
> register/constant operands in **both** `B` and `C`; v2 narrows `B` to 8 bits to free the opcode bit.

### Operand word (iABC)

```
 bit 31        30..23      22..15      14..7       6..0
    +------------+----------+----------+-----------+-------+
    |     C (9)               |  B  (8)  |  A  (8)  | op(7) |
    +------------+----------+----------+-----------+-------+
```

| Field | Bits | Meaning |
| :--- | :--- | :--- |
| `op` | 0..6 (7) | Opcode, `0`..`127`. |
| `A` | 7..14 (8) | Destination register, `0`..`255`. |
| `B` | 15..22 (8) | Source register, `0`..`255`. **Always a register.** |
| `C` | 23..31 (9) | Source operand in RC form: `0`..`255` is register `rC`; `256`..`511` is constant `Constants[C - 256]`. |

There is **at most one constant operand per instruction, and it lives in `C`**. An expression that
needs a constant on the left, or two constants at once, is materialised by the compiler with `LOADC`.
This keeps the right-hand-constant fast path (`x + 1`, `i < n`) a single instruction.

### Formats

| Format | Layout | Accessors |
| :--- | :--- | :--- |
| `iABC` | `op7 \| A8 \| B8 \| C9` | `A`, `B`, `C` |
| `iABx` | `op7 \| A8 \| Bx17` | `Bx` unsigned, `0`..`131071` |
| `iAsBx` | `op7 \| A8 \| sBx17` | `sBx17` signed, bias `65,535` |
| `iAx` | `op7 \| Ax25` | `sBx25` signed, bias `16,777,215` |

### Superinstruction second words

`FOR` and the fused comparisons (`JLT`/`JLE`/`JEQ`) are two words. Word 1 is the operand word
(`iABC`); word 2 carries the branch target and is read with `*Ip++` before the branch is applied:

| Instruction | Word 2 | Meaning |
| :--- | :--- | :--- |
| `FOR` | `op7 \| comp8 \| sBx17` | Comparison code `0`..`5` in `A`, biased loop-back offset. |
| `JLT`/`JLE`/`JEQ` | `op7 \| Ax25` | `sBx25` target offset relative to the word after word 2. |

### Limits

* **Registers:** 256 (`A` and `B` are 8-bit; `C`'s register range is `0`..`255`).
* **Constants:** 256, addressed by `C` in RC form (`C - 256`). Matches the `SetConstant` cap.
  `LOADC` uses `Bx17` for its pool index but the assembler enforces the 256 cap.
* **Method table / `Bx`:** 17-bit (`0`..`131071`); the table itself is far smaller.
* **Branch range:** `sBx25` ±16M words for `JUMP` and fused branches; `sBx17` ±65k for `FOR`.

### Migration note (v1 → v2)

v1 packed register/constant selection into 9-bit `B`/`C` (`index < 256 ? register : Constants[index - 256]`),
so **both** `B` and `C` could be constants. v2 keeps that RC encoding in `C` but narrows `B` to 8 bits,
which frees the extra opcode bit. Consequences:

* `B` is no longer constant-addressable — a left-hand constant must be `LOADC`'d into a register first.
* At most one constant operand per instruction, and only in `C`.
* `Bx` 18 → 17 bits; `sBx16` → `sBx17`; `sBx26` → `sBx25`.
* `FOR` bounds (`max`, `step`) and array sizes/indices are frequently constants today
  (`FOR i epochs 1 < loop`, `NEWARR r 100`, `SETARR r 0 v`); under v2 the compiler/asm materialises
  them into registers.

