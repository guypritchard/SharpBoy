using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace GB.Emulator.Core
{
    public class Memory
    {
       public void Write(ushort value, ushort location) {
         Trace.WriteLine($"Writing 0x{value:X2} to 0x{location:X2}");
       }
    }
}
