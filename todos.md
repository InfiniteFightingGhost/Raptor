- [ ] Gas Limit & Instruction Budget (Thread Lock Guard)
- [ ] More verbose compiler errors (think Rust errors, not C#).
- [ ] Add more functions to the standard library.
- [X] Check if the FFI host call overhead can be reduced even further to 4~ nanoseconds.
*Result*: overhead now is sub 5 nanoseconds(4.7) on average.
- [ ] Implement RaptorCostAttribute so that the dev can decide the cost of a function(by default cost is 1 gas).
- [ ] Implement RaptorPure virtual machine handling.
- [ ] Implement RaptorConstant handling in the compiler and assembler.
- [ ] Implement more Raptor.Cli functionality. Lock in cli commands and their functionality.
- [X] Look for more performance optimizations all throughout.
*Result*: Removed BasePointer and two additions in order to get FFI host calling sub 5 nanoseconds.
- [ ] Make the Raptor-Lang extension actually work + colorfull code.
