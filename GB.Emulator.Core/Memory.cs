using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace GB.Emulator.Core
{
    public class Memory
    {
      private readonly ushort[] memory;
      public Memory() {
        this.memory = new ushort[ushort.MaxValue];
      }
       public void Write(ushort value, ushort location) {
         try 
         {
          this.memory[location] = value;
         } 
         catch (IndexOutOfRangeException iore) 
         {
           Trace.WriteLine($"Error writing {location:X2}:{value}");
           throw iore;
         }
       }

       public ushort Read(ushort location) {
         try 
         {
          return this.memory[location];
         } 
         catch (IndexOutOfRangeException iore) 
         {
           Trace.WriteLine($"Error reading {location:X2}");
           throw iore;
         }
       }
    }
}
