using System;
using System.Collections.Generic;
using System.Text;

namespace GB.Emulator.Core
{
  public static class ByteOp
  {
    public static ushort ToSignedByte(ushort value)
    {
      ushort signed = (value > (ushort)0x7c)
        ? (ushort)(value - (ushort)0x100)
        : (ushort)value;

      return (ushort)signed;
    }

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

    public static bool BitGet(byte p1, int index)
    {
      var v = p1 >> index;
      return (v & 1) == 1;
    }

    public static byte BitSet(byte p1, int index, bool value)
    {
      int v = p1;
      var newValue = value ? 1 : 0;
      v ^= (-newValue ^ v) & (1 << index);
      return (byte)v;
    }
  }
}
