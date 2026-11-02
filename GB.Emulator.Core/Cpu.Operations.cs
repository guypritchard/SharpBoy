namespace GB.Emulator.Core
{
    public sealed class CpuOperations
    {
        private readonly CpuFlags flags;
        internal CpuOperations(CpuFlags flags) => this.flags = flags;

            public byte RotateLeftCircular(byte value)
            {
                byte result = (byte)((value << 1) | (value >> 7));

                this.flags.Z = result == 0;
                this.flags.N = false;
                this.flags.H = false;
                this.flags.C = (value & 0x80) != 0;

                return result;
            }

            public byte RotateLeft(byte value)
            {
                bool oldCarry = this.flags.C;
                this.flags.C = (value & 0x80) != 0;
                byte result = (byte)((value << 1) | (oldCarry ? 1 : 0));

                this.flags.Z = result == 0;
                this.flags.N = false;
                this.flags.H = false;

                return result;
            }

            public byte RotateRightCircular(byte value)
            {
                byte result = (byte)((value >> 1) | (value << 7));

                this.flags.Z = result == 0;
                this.flags.N = false;
                this.flags.H = false;
                this.flags.C = (value & 0x01) != 0;

                return result;
            }

            public byte RotateRight(byte value)
            {
                bool oldCarry = this.flags.C;
                this.flags.C = (value & 0x01) != 0;
                byte result = (byte)((value >> 1) | (oldCarry ? 0x80 : 0x00));

                this.flags.Z = result == 0;
                this.flags.N = false;
                this.flags.H = false;

                return result;
            }

            public byte Decrement(byte value)
            {
                byte local = value;

                // This is a subtract operation so set the 'subtract' flag.
                this.flags.N = true;

                // Set the half carry flag if we're going to 'carry the one' from the ones to the tens unit in hex.
                this.flags.H = (local & 0x0F) == 0;

                local -= 1;

                // Set the 'zero' flag if the value is indeed now zero.
                this.flags.Z = local == 0;

                return local;
            }

            public ushort Decrement(ushort value)
            {
                return (ushort)(value - 1);
            }

            public byte Increment(byte value)
            {
                byte result = (byte)(value + 1);

                // This is not a subtract operation so set the 'subtract' flag to false.
                this.flags.N = false;

                // Half-carry occurs when the low nibble wraps.
                this.flags.H = (value & 0x0F) == 0x0F;

                // Set the 'zero' flag if the value is indeed now zero.
                this.flags.Z = result == 0;

                return result;
            }

            public ushort Increment(ushort value)
            {
                return (ushort)(value + 1);
            }

            public byte Add(byte value1, byte value2)
            {
                int result = value1 + value2;
                byte sum = (byte)result;

                // This is not a subtract operation so set the 'subtract' flag to false.
                this.flags.N = false;

                // Carry occurs when the result exceeds 8 bits.
                this.flags.C = result > 0xFF;

                // Half-carry occurs when the low nibble wraps.
                this.flags.H = ((value1 & 0x0F) + (value2 & 0x0F)) > 0x0F;

                // Set the 'zero' flag if the value is indeed now zero.
                this.flags.Z = sum == 0;

                return sum;
            }

            public ushort Add(ushort value1, ushort value2)
            {
                return (ushort)(value1 + value2);
            }

            public byte AddWithCarry(byte value1, byte value2)
            {
                int carry = this.flags.C ? 1 : 0;
                int result = value1 + value2 + carry;
                byte sum = (byte)result;

                // This is not a subtract operation so set the 'subtract' flag to false.
                this.flags.N = false;

                // Carry occurs when the result exceeds 8 bits.
                this.flags.C = result > 0xFF;

                // Half-carry occurs when the low nibble wraps.
                this.flags.H = ((value1 & 0x0F) + (value2 & 0x0F) + carry) > 0x0F;

                // Set the 'zero' flag if the value is indeed now zero.
                this.flags.Z = sum == 0;

                return sum;
            }

            public byte And(byte value1, byte value2)
            {
                byte result = (byte)(value1 & value2);

                this.flags.Z = result == 0;
                this.flags.N = false;
                this.flags.H = true;
                this.flags.C = false;

                return result;
            }

            public byte Or(byte value1, byte value2)
            {
                byte result = (byte)(value1 | value2);

                this.flags.Z = result == 0;
                this.flags.N = false;
                this.flags.H = false;
                this.flags.C = false;

                return result;
            }

            public byte Xor(byte value1, byte value2)
            {
                byte result = (byte)(value1 ^ value2);

                this.flags.Z = result == 0;
                this.flags.N = false;
                this.flags.H = false;
                this.flags.C = false;

                return result;
            }

            public byte Subtract(byte value1, byte value2)
            {
                int result = value1 - value2;
                byte difference = (byte)result;

                // This is a subtract operation so set the 'subtract' flag.
                this.flags.N = true;

                // Carry occurs when we go below zero.
                this.flags.C = result < 0;

                // Half-carry occurs when we borrow from the upper nibble.
                this.flags.H = (value1 & 0x0F) < (value2 & 0x0F);

                // Set the 'zero' flag if the value is indeed now zero.
                this.flags.Z = difference == 0;

                return difference;
            }

            public byte SubtractWithCarry(byte value1, byte value2)
            {
                int carry = this.flags.C ? 1 : 0;
                int result = value1 - value2 - carry;
                byte difference = (byte)result;

                // This is a subtract operation so set the 'subtract' flag.
                this.flags.N = true;

                // Carry occurs when we go below zero.
                this.flags.C = result < 0;

                // Half-carry occurs when we borrow from the upper nibble.
                this.flags.H = (value1 & 0x0F) < ((value2 & 0x0F) + carry);

                // Set the 'zero' flag if the value is indeed now zero.
                this.flags.Z = difference == 0;

                return difference;
            }

    }
}
