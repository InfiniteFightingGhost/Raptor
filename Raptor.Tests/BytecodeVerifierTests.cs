using Raptor;
using Xunit;

namespace Raptor.Tests;

public class BytecodeVerifierTests
{
    [Fact]
    public void BytecodeVerifierThrowsVerificationExceptionForEmptyInstructions()
    {
        Action test;
        test = () =>
        {
            VMChunk badChunk = new VMChunk();
            BytecodeVerifier.Verify(badChunk, 1024);
            throw new Exception(
                "Test 1 failed: Expected VerificationException for empty instructions."
            );
        };
        Assert.Throws(typeof(VerificationException), test);
    }

    [Fact]
    public void BytecodeVerifierThrowsVerificationExceptionForMissingTerminatingInstruction()
    {
        Action test;
        test = () =>
        {
            VMChunk badChunk = new VMChunk();
            badChunk.Instructions = new uint[] { Instruction.CreateABC(OpCode.ADD, 0, 0, 0) };
            BytecodeVerifier.Verify(badChunk, 1024);
            throw new Exception(
                "Test 2 failed: Expected VerificationException for missing termination."
            );
        };
        Assert.Throws(typeof(VerificationException), test);
    }

    [Fact]
    public void BytecodeVerifierThrowsVerificationExceptionForInvalidJumpOutOfBounds()
    {
        Action test;
        test = () =>
        {
            VMChunk badChunk = new VMChunk();
            badChunk.Instructions = new uint[]
            {
                Instruction.CreateSBx26(OpCode.JUMP, 10),
                Instruction.CreateABC(OpCode.HALT, 0, 0, 0),
            };
            BytecodeVerifier.Verify(badChunk, 1024);
            throw new Exception(
                "Test 3 failed: Expected VerificationException for jump out of bounds."
            );
        };
        Assert.Throws(typeof(VerificationException), test);
    }

    [Fact]
    public void BytecodeVerifierThrowsVerificationExceptionForJumpInMiddleOfAFORInstruction()
    {
        Action test;
        test = () =>
        {
            VMChunk badChunk = new VMChunk();
            badChunk.Instructions = new uint[]
            {
                Instruction.CreateSBx26(OpCode.JUMP, 2),
                Instruction.CreateABC(OpCode.FOR, 0, 0, 0),
                Instruction.CreateAsBx(OpCode.FOR, 0, 0),
                Instruction.CreateABC(OpCode.HALT, 0, 0, 0),
            };
            BytecodeVerifier.Verify(badChunk, 1024);
            throw new Exception(
                "Test 4 failed: Expected VerificationException for jump into middle of FOR."
            );
        };
        Assert.Throws(typeof(VerificationException), test);
    }

    [Fact]
    public void BytecodeVerifierThrowsVerificationExceptionForIndexOutOfBounds()
    {
        Action test;
        test = () =>
        {
            VMChunk badChunk = new VMChunk();
            typeof(VMChunk)
                .GetProperty("Constants")
                .GetSetMethod(true)
                .Invoke(badChunk, new object[] { new double[0] });
            badChunk.Instructions = new uint[]
            {
                Instruction.CreateABC(OpCode.ADD, 0, 256, 0),
                Instruction.CreateABC(OpCode.HALT, 0, 0, 0),
            };
            BytecodeVerifier.Verify(badChunk, 1024);
            throw new Exception(
                "Test 5 failed: Expected VerificationException for out of bounds constant."
            );
        };
        Assert.Throws(typeof(VerificationException), test);
    }

    [Fact]
    public void BytecodeVerifierThrowsVerificationExceptionForIncompleteFORInstruction()
    {
        Action test;
        test = () =>
        {
            VMChunk badChunk = new VMChunk();
            badChunk.Instructions = new uint[] { Instruction.CreateABC(OpCode.FOR, 0, 0, 0) };
            BytecodeVerifier.Verify(badChunk, 1024);
            throw new Exception(
                "Test 6 failed: Expected VerificationException for incomplete FOR."
            );
        };
        Assert.Throws(typeof(VerificationException), test);
    }

    [Fact]
    public void BytecodeVerifierValidatesProgram()
    {
        Action test;
        test = () =>
        {
            VMChunk goodChunk = new VMChunk();
            goodChunk.Instructions = new uint[] { Instruction.CreateABC(OpCode.HALT, 0, 0, 0) };
            BytecodeVerifier.Verify(goodChunk, 1024);
            throw new OutOfMemoryException("Succeeded, but I dont know how to check for that :(");
        };
        Assert.Throws(typeof(OutOfMemoryException), test);
    }
}
