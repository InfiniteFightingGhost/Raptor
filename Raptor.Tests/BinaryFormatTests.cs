using System;
using System.IO;
using System.Linq;
using Raptor;
using Xunit;

namespace Raptor.Tests;

public class BinaryFormatTests
{
    [Fact]
    public void BinaryRoundTripAndMagicBytesTest()
    {
        VMChunk rtChunk = new VMChunk();
        Assembler rtAss = new(rtChunk);
        string rtSource =
            @"
LOADC r1 42.0
LOADC r2 58.0
ADD r3 r1 r2
PRINT r3
HALT
";
        rtAss.Parse(rtSource.Split("\n").ToList());
        BytecodeVerifier.Verify(rtChunk, 1024);

        string tempPath = Path.Combine(Path.GetTempPath(), "raptor_test_roundtrip.rbc");
        try
        {
            RaptorBinary.Save(rtChunk, tempPath);

            byte[] fileBytes = File.ReadAllBytes(tempPath);
            Assert.True(fileBytes.Length >= 20, "Binary file too small for header.");
            uint fileMagic = BitConverter.ToUInt32(fileBytes, 0);
            Assert.Equal(RaptorBinary.MagicSignature, fileMagic);

            VMChunk loadedChunk = RaptorBinary.Load(tempPath);
            Assert.Equal(rtChunk.Instructions.Length, loadedChunk.Instructions.Length);
            for (int i = 0; i < rtChunk.Instructions.Length; i++)
            {
                Assert.Equal(rtChunk.Instructions[i], loadedChunk.Instructions[i]);
            }

            VirtualMachine rtVm = new VirtualMachine();
            rtVm.LoadProgram(loadedChunk);
            ExecutionResult rtResult = rtVm.RunFast();
            Assert.Equal(VMStatus.Halted, rtResult.Status);
            Assert.Equal(100.0, rtResult.RegistersSnapshot[3]);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public void InvalidMagicRejectionTest()
    {
        string badPath = Path.Combine(Path.GetTempPath(), "raptor_test_badmagic.rbc");
        try
        {
            File.WriteAllBytes(
                badPath,
                new byte[]
                {
                    0xDE,
                    0xAD,
                    0xBE,
                    0xEF,
                    0x01,
                    0x00,
                    0x00,
                    0x00,
                    0x00,
                    0x00,
                    0x00,
                    0x00,
                    0x00,
                    0x00,
                    0x00,
                    0x00,
                    0x00,
                    0x00,
                    0x00,
                    0x00,
                }
            );

            Assert.Throws<InvalidDataException>(() => RaptorBinary.Load(badPath));
        }
        finally
        {
            if (File.Exists(badPath))
                File.Delete(badPath);
        }
    }
}
