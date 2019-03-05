using System;
using System.Runtime.Serialization;

namespace GB.Emulator.Core
{
    public class CartridgeLoadException : Exception
    {
        public CartridgeLoadException()
        {
        }

        public CartridgeLoadException(string message) : base(message)
        {
        }

        public CartridgeLoadException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected CartridgeLoadException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
