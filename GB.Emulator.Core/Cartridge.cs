using System.Collections.Generic;
using System.Text;

namespace GB.Emulator.Core
{
    public class Cartridge
    {
        public Header Header { get; set; }

        public byte[] Data { get; set; }
    }
}
