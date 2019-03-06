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
                    return ByteOp.BitSet(Cpu.Registers.Flags, 7);
                }
            }

            public static bool N
            {
                get
                {
                    return ByteOp.BitSet(Cpu.Registers.Flags, 6);
                }
            }

            public static bool H
            {
                get
                {
                    return ByteOp.BitSet(Cpu.Registers.Flags, 5);
                }
            }

            public static bool C
            {
                get
                {
                    return ByteOp.BitSet(Cpu.Registers.Flags, 4);
                }
            }

            public static bool _3
            {
                get
                {
                    return ByteOp.BitSet(Cpu.Registers.Flags, 3);
                }
            }
            public static bool _2
            {
                get
                {
                    return ByteOp.BitSet(Cpu.Registers.Flags, 2);
                }
            }
            public static bool _1
            {
                get
                {
                    return ByteOp.BitSet(Cpu.Registers.Flags, 1);
                }
            }

            public static bool _0
            {
                get
                {
                    return ByteOp.BitSet(Cpu.Registers.Flags, 0);
                }
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
        }
    }
}
