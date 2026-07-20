using System;
using System.IO;
using Raptor.StdLib;
using Xunit;

namespace Raptor.Tests
{
    public class StdLibTests
    {
        private readonly FFIHostTable _hostTable;

        public StdLibTests()
        {
            _hostTable = new FFIHostTable();
            _hostTable.RegisterModule(typeof(RaptorMath));
            _hostTable.RegisterModule(typeof(RaptorPeripherals));
        }

        [Fact]
        public void Direct_Sin_CalculatesCorrectly()
        {
            unsafe
            {
                double* reg = stackalloc double[256];
                reg[0] = Math.PI / 2.0;
                var state = new VMState { RegPtr = reg };
                RaptorMath.Sin(ref state);
                Assert.Equal(1.0, reg[0], precision: 5);
            }
        }

        [Fact]
        public void Direct_Cos_CalculatesCorrectly()
        {
            unsafe
            {
                double* reg = stackalloc double[256];
                reg[0] = 0.0;
                var state = new VMState { RegPtr = reg };
                RaptorMath.Cos(ref state);
                Assert.Equal(1.0, reg[0], precision: 5);
            }
        }

        [Fact]
        public void Direct_Tan_CalculatesCorrectly()
        {
            unsafe
            {
                double* reg = stackalloc double[256];
                reg[0] = 0.0;
                var state = new VMState { RegPtr = reg };
                RaptorMath.Tan(ref state);
                Assert.Equal(0.0, reg[0], precision: 5);
            }
        }

        [Fact]
        public void Direct_Pow_CalculatesCorrectly()
        {
            unsafe
            {
                double* reg = stackalloc double[256];
                reg[0] = 2.0;
                reg[1] = 3.0;
                var state = new VMState { RegPtr = reg };
                RaptorMath.Pow(ref state);
                Assert.Equal(8.0, reg[0]);
            }
        }

        [Fact]
        public void Direct_Floor_CalculatesCorrectly()
        {
            unsafe
            {
                double* reg = stackalloc double[256];
                reg[0] = 4.9;
                var state = new VMState { RegPtr = reg };
                RaptorMath.Floor(ref state);
                Assert.Equal(4.0, reg[0]);
            }
        }

        [Fact]
        public void Direct_Ceiling_CalculatesCorrectly()
        {
            unsafe
            {
                double* reg = stackalloc double[256];
                reg[0] = 4.1;
                var state = new VMState { RegPtr = reg };
                RaptorMath.Ceiling(ref state);
                Assert.Equal(5.0, reg[0]);
            }
        }

        [Fact]
        public void Direct_Min_CalculatesCorrectly()
        {
            unsafe
            {
                double* reg = stackalloc double[256];
                reg[0] = 10.0;
                reg[1] = 5.0;
                var state = new VMState { RegPtr = reg };
                RaptorMath.Min(ref state);
                Assert.Equal(5.0, reg[0]);
            }
        }

        [Fact]
        public void Direct_Max_CalculatesCorrectly()
        {
            unsafe
            {
                double* reg = stackalloc double[256];
                reg[0] = 10.0;
                reg[1] = 5.0;
                var state = new VMState { RegPtr = reg };
                RaptorMath.Max(ref state);
                Assert.Equal(10.0, reg[0]);
            }
        }

        [Fact]
        public void Direct_Sqrt_CalculatesCorrectly()
        {
            unsafe
            {
                double* reg = stackalloc double[256];
                reg[0] = 16.0;
                var state = new VMState { RegPtr = reg };
                RaptorMath.Sqrt(ref state);
                Assert.Equal(4.0, reg[0]);
            }
        }

        [Fact]
        public void Direct_Abs_CalculatesCorrectly()
        {
            unsafe
            {
                double* reg = stackalloc double[256];
                reg[0] = -42.5;
                var state = new VMState { RegPtr = reg };
                RaptorMath.Abs(ref state);
                Assert.Equal(42.5, reg[0]);
            }
        }

        [Fact]
        public void Direct_Atan2_CalculatesCorrectly()
        {
            unsafe
            {
                double* reg = stackalloc double[256];
                reg[0] = 1.0;
                reg[1] = 1.0;
                var state = new VMState { RegPtr = reg };
                RaptorMath.Atan2(ref state);
                Assert.Equal(Math.PI / 4.0, reg[0], precision: 5);
            }
        }

        [Fact]
        public void Direct_Clamp_CalculatesCorrectly()
        {
            unsafe
            {
                double* reg = stackalloc double[256];
                // Value below min: val=-5, min=0, max=10
                reg[0] = -5.0;
                reg[1] = 0.0;
                reg[2] = 10.0;
                var state1 = new VMState { RegPtr = reg };
                RaptorMath.Clamp(ref state1);
                Assert.Equal(0.0, reg[0]);

                // Value above max: val=15, min=0, max=10
                reg[0] = 15.0;
                reg[1] = 0.0;
                reg[2] = 10.0;
                var state2 = new VMState { RegPtr = reg };
                RaptorMath.Clamp(ref state2);
                Assert.Equal(10.0, reg[0]);

                // Value within range: val=7, min=0, max=10
                reg[0] = 7.0;
                reg[1] = 0.0;
                reg[2] = 10.0;
                var state3 = new VMState { RegPtr = reg };
                RaptorMath.Clamp(ref state3);
                Assert.Equal(7.0, reg[0]);
            }
        }

        [Fact]
        public void Direct_Pi_ReturnsPiConstant()
        {
            unsafe
            {
                double* reg = stackalloc double[256];
                var state = new VMState { RegPtr = reg };
                RaptorMath.Pi(ref state);
                Assert.Equal(Math.PI, reg[0]);
            }
        }

        [Fact]
        public void Direct_PeripheralsPrint_WritesToConsole()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                unsafe
                {
                    double* reg = stackalloc double[256];
                    reg[0] = 123.45;
                    var state = new VMState { RegPtr = reg };
                    RaptorPeripherals.Print(ref state);
                }
                Assert.Contains("123.45", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Theory]
        [InlineData(0.0, 0.0)]
        [InlineData(1.57079632679, 1.0)]
        public void Integration_SinReturnsCorrectResult(double angle, double expected)
        {
            using var engine = new ScriptEngine();
            engine.RegisterHostTable(_hostTable);
            string asm =
                $@"
                LOADC r1 {angle}
                CALL math.sin() r1
                HALT";
            var result = engine.Run(asm);
            Assert.Equal(VMStatus.Halted, result.Status);
            Assert.Equal(expected, result.RegistersSnapshot[1], precision: 4);
        }

        [Fact]
        public void Integration_RaptorScript_AllMathFunctionsWork()
        {
            using var engine = new ScriptEngine();
            engine.RegisterHostTable(_hostTable);

            var chunk = engine.CompileRaptorScript(
                @"
                var a = math.sin(0.0);
                var b = math.cos(0.0);
                var c = math.tan(0.0);
                var d = math.pow(2.0, 3.0);
                var e = math.floor(4.9);
                var f = math.ceiling(4.1);
                var g = math.min(10.0, 5.0);
                var h = math.max(10.0, 5.0);
                var i = math.sqrt(25.0);
                var j = math.abs(0.0 - 12.3);
                var k = math.atan2(1.0, 1.0);
                var l = math.clamp(5.0, 0.0, 10.0);
                var p = math.pi();
            "
            );

            var result = engine.Execute(chunk);
            Assert.Equal(VMStatus.Halted, result.Status);
        }

        [Fact]
        public void Integration_RaptorScript_PeripheralsPrintWorks()
        {
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                using var engine = new ScriptEngine();
                engine.RegisterHostTable(_hostTable);

                var chunk = engine.CompileRaptorScript(
                    @"
                    var msg = 999.5;
                    peri.print(msg);
                "
                );

                var result = engine.Execute(chunk);
                Assert.Equal(VMStatus.Halted, result.Status);
                Assert.Contains("999.5", sw.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }
}
