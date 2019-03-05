namespace GB.Emulator.Core
{
    public partial class Cpu
    {
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
