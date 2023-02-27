using GB.Emulator.Core.InputOutput;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GB.Emulator.Core
{
    public class SpriteTileManager : IMemoryRange
    {
        public SpriteTileManager()
        {

        }

        public ushort Start => 0x8000;

        public ushort End => 0x8FFF;

        public byte Read8(ushort location)
        {
            throw new NotImplementedException();
        }

        public void Write8(ushort location, byte value)
        {
            Trace.WriteLine("Writing to sprite tile manager");
        }
    }
}
