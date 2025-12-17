namespace GB.Emulator.Core
{
    public partial class Cpu
    {

        public static class Operations
        {
            public static byte Decrement(byte value)
            {
                byte local = value;

                // This is a subtract operation so set the 'subtract' flag.
                Cpu.Flags.N = true;

                // Set the half carry flag if we're going to 'carry the one' from the ones to the tens unit in hex.
                Cpu.Flags.H = (local & 0x0F) == 0;

                local -= 1;

                // Set the 'zero' flag if the value is indeed now zero.
                Cpu.Flags.Z = local == 0;

                return local;
            }
            public static byte Increment(byte value)
            {
                byte result = (byte)(value + 1);

                // This is not a subtract operation so set the 'subtract' flag to false.
                Cpu.Flags.N = false;

                // Half-carry occurs when the low nibble wraps.
                Cpu.Flags.H = (value & 0x0F) == 0x0F;

                // Set the 'zero' flag if the value is indeed now zero.
                Cpu.Flags.Z = result == 0;

                return result;
            }
            public static byte Add(byte value1, byte value2)
            {
                int result = value1 + value2;
                byte sum = (byte)result;

                // This is not a subtract operation so set the 'subtract' flag to false.
                Cpu.Flags.N = false;

                // Carry occurs when the result exceeds 8 bits.
                Cpu.Flags.C = result > 0xFF;

                // Half-carry occurs when the low nibble wraps.
                Cpu.Flags.H = ((value1 & 0x0F) + (value2 & 0x0F)) > 0x0F;

                // Set the 'zero' flag if the value is indeed now zero.
                Cpu.Flags.Z = sum == 0;

                return sum;
            }

            public static byte Subtract(byte value1, byte value2)
            {
                int result = value1 - value2;
                byte difference = (byte)result;

                // This is a subtract operation so set the 'subtract' flag.
                Cpu.Flags.N = true;

                // Carry occurs when we go below zero.
                Cpu.Flags.C = result < 0;

                // Half-carry occurs when we borrow from the upper nibble.
                Cpu.Flags.H = (value1 & 0x0F) < (value2 & 0x0F);

                // Set the 'zero' flag if the value is indeed now zero.
                Cpu.Flags.Z = difference == 0;

                return difference;
            }
        }
    }
}
