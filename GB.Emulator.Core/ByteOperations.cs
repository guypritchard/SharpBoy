using System;
using System.Collections.Generic;
using System.Text;

namespace GB.Emulator.Core
{
    public static class ByteOp
    {
        public static ushort Concat(byte p1, byte p2)
        {
            ushort val = p2;
            val = (ushort)(val << 8);
            val = (ushort)(val + p1);
            return (ushort)val;
        }

        public static void Split(ushort p1, out byte o1, out byte o2)
        {
            o1 = (byte)(p1 >> 8);
            o2 = (byte)(p1);
        }

        public static bool BitGet(byte p1, int bitNumber)
        {
            return ((p1 << bitNumber) & 1) == 1;
        }

        public static byte BitSet(byte p1, int bitNumber, bool value)
        {
          if (value) {
            return (byte)(p1 | bitNumber);
          }

          return (byte)(p1 & ~bitNumber); 
        }
    }
}
