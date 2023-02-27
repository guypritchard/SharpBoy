namespace GB.Emulator.Core
{
    public partial class Cpu
    {
        public static class Flags
        {
            public static bool Z
            {
                get
                {
                    return ByteOp.BitGet(Cpu.Registers.Flags, 7);
                }
                set 
                {
                  Cpu.Registers.Flags = ByteOp.BitSet(Cpu.Registers.Flags, 7, value);
                }
            }

            public static bool N
            {
                get
                {
                    return ByteOp.BitGet(Cpu.Registers.Flags, 6);
                }
                set 
                {
                  Cpu.Registers.Flags = ByteOp.BitSet(Cpu.Registers.Flags, 6, value);
                }
            }

            public static bool H
            {
                get
                {
                    return ByteOp.BitGet(Cpu.Registers.Flags, 5);
                }
                set 
                {
                  Cpu.Registers.Flags = ByteOp.BitSet(Cpu.Registers.Flags, 5, value);
                }
            }

            public static bool C
            {
                get
                {
                    return ByteOp.BitGet(Cpu.Registers.Flags, 4);
                }
                set
                {
                    Cpu.Registers.Flags = ByteOp.BitSet(Cpu.Registers.Flags, 4, value);
                }
            }

            public static bool _3
            {
                get
                {
                    return ByteOp.BitGet(Cpu.Registers.Flags, 3);
                }
            }
            public static bool _2
            {
                get
                {
                    return ByteOp.BitGet(Cpu.Registers.Flags, 2);
                }
            }
            public static bool _1
            {
                get
                {
                    return ByteOp.BitGet(Cpu.Registers.Flags, 1);
                }
            }

            public static bool _0
            {
                get
                {
                    return ByteOp.BitGet(Cpu.Registers.Flags, 0);
                }
            }

            public static string Dump() {
              return $"Flags:{Cpu.Registers.Flags:X}\tZ:{Z}\tN:{N}\tH:{H}\tC:{C}\t";
            }
        }

        public static class Registers
        {
            public static byte A;
            public static byte F;
            public static byte B;
            public static byte C;
            public static byte D;
            public static byte E = 0xD8;
            public static byte H = 0xFF;
            public static byte L = 0xE2;

            public static ushort HL
            {
                get => ByteOp.Concat(L, H);
                set => ByteOp.Split(value, out H, out L);
            }

            public static byte Flags;

            /// <summary>
            /// Stack pointer.
            /// </summary>
            public static ushort SP = 0xCFF5;

            /// <summary>
            /// Program Counter.
            /// </summary>
            public static ushort PC = 0x100;

            public static string Dump() {
              return $"PC:{PC:X4}\tSP:{SP:X4}\tHL:{HL:X4}\tA:{A:X2}\tB:{B:X2}\tC:{C:X2}\tD:{D:X2}";
            }
        }
    }
}
