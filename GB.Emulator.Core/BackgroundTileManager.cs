using GB.Emulator.Core.InputOutput;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GB.Emulator.Core
{
    public class BackgroundTileManager : IMemoryRange
    {
        public BackgroundTileManager()
        {

        }

        public ushort Start => 0x9800;

        public ushort End => 0x97FF;

        public byte Read8(ushort location)
        {
            throw new NotImplementedException();
        }

        public void Write8(ushort location, byte value)
        {
            Trace.WriteLine("Writing to background tile manager");
        }
    }
}
