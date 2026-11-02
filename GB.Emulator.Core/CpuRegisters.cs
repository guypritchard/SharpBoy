namespace GB.Emulator.Core
{
    public sealed class CpuFlags
    {
        private readonly CpuRegisters registers;
        internal CpuFlags(CpuRegisters registers) => this.registers = registers;
        private bool Get(int bit) => ByteOp.BitGet(this.registers.Flags, bit);
        private void Set(int bit, bool value) => this.registers.Flags = ByteOp.BitSet(this.registers.Flags, bit, value);
        public bool Z { get => Get(7); set => Set(7, value); }
        public bool N { get => Get(6); set => Set(6, value); }
        public bool H { get => Get(5); set => Set(5, value); }
        public bool C { get => Get(4); set => Set(4, value); }
        public bool _3 => Get(3);
        public bool _2 => Get(2);
        public bool _1 => Get(1);
        public bool _0 => Get(0);
        public string Dump() => $"Flags:{this.registers.Flags:X}\tZ:{Z}\tN:{N}\tH:{H}\tC:{C}\t";
    }

    public sealed class CpuRegisters
    {
        public byte A, B, C, D, E = 0xD8, H = 0xFF, L = 0xE2, Flags;
        public byte F { get => Flags; set => Flags = value; }
        public ushort HL { get => ByteOp.Concat(L, H); set => ByteOp.Split(value, out H, out L); }
        public ushort BC { get => ByteOp.Concat(C, B); set => ByteOp.Split(value, out B, out C); }
        public ushort DE { get => ByteOp.Concat(E, D); set => ByteOp.Split(value, out D, out E); }
        public ushort SP = 0xFFFE;
        public ushort PC = 0x0100;
        public string Dump() => $"PC:{PC:X4}\tSP:{SP:X4}\tHL:{HL:X4}\tA:{A:X2}\tB:{B:X2}\tC:{C:X2}\tD:{D:X2}";
        internal CpuRegistersState CaptureState() => new(A, F, B, C, D, E, H, L, Flags, SP, PC);
        internal void RestoreState(CpuRegistersState state)
        {
            A = state.A; F = state.F; B = state.B; C = state.C; D = state.D;
            E = state.E; H = state.H; L = state.L; Flags = state.Flags; SP = state.SP; PC = state.PC;
        }
        public void Reset()
        {
            A = F = B = C = D = Flags = 0;
            E = 0xD8; H = 0xFF; L = 0xE2; SP = 0xFFFE; PC = 0x0100;
        }
    }
}
