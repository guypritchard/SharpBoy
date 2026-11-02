using System.Text;

namespace GB.Experiments.NetworkCartridge;

/// <summary>Real LR35902 instructions: connect, send, collect partial reads, compare and close.</summary>
public static class NetworkCartridgeRom
{
    public const string Message = "Hello from the network cartridge!\n";
    public const ushort ResultAddress = 0xC000, ReplyAddress = 0xC100;

    public static byte[] Create()
    {
        byte[] message = Encoding.ASCII.GetBytes(Message);
        var rom = new byte[0x8000];
        var code = new List<byte>();
        var labels = new Dictionary<string, int>();
        var fixups = new List<(int Offset, string Label)>();
        void Emit(params byte[] bytes) => code.AddRange(bytes);
        void Mark(string label) => labels.Add(label, 0x150 + code.Count);
        void Address(byte opcode, ushort address) => Emit(opcode, (byte)address, (byte)(address >> 8));
        void Jump(string label, byte opcode = 0xC3)
        { Emit(opcode, 0, 0); fixups.Add((code.Count - 2, label)); }
        void Write(ushort address, byte value) { Emit(0x3E, value); Address(0xEA, address); }
        void Command(NetworkCommand command)
        { Write(NetworkCartridgeDevice.Command, (byte)command); Jump("wait", 0xCD); }

        Emit(0xF3, 0x31, 0xFE, 0xFF); // DI; LD SP,$FFFE
        Write(ResultAddress, 0);
        Address(0xFA, 0xA000); Emit(0xFE, (byte)'N'); Jump("fail", 0xC2);
        Address(0xFA, 0xA001); Emit(0xFE, 1); Jump("fail", 0xC2);
        Command(NetworkCommand.Connect);
        for (int i = 0; i < message.Length; i++) Write((ushort)(NetworkCartridgeDevice.TxBuffer + i), message[i]);
        Write(NetworkCartridgeDevice.TxLength, (byte)message.Length);
        Write(NetworkCartridgeDevice.TxLength + 1, 0);
        Command(NetworkCommand.Send);
        Address(0x11, ReplyAddress); // DE = reply write pointer
        Emit(0x0E, 0); // C = total received
        Mark("receive");
        Command(NetworkCommand.Receive);
        Address(0xFA, NetworkCartridgeDevice.RxLength + 1); Emit(0xB7); Jump("fail", 0xC2);
        Address(0xFA, NetworkCartridgeDevice.RxLength); Emit(0xB7); Jump("fail", 0xCA); // EOF
        Emit(0x47); // B = chunk size
        Address(0x21, NetworkCartridgeDevice.RxBuffer);
        Mark("copy");
        Emit(0x79, 0xFE, (byte)message.Length); Jump("fail", 0xD2); // prevent excess reply/overflow
        Emit(0x2A, 0x12, 0x13, 0x0C, 0x05); // A=(HL+); (DE)=A; DE++; C++; B--
        Jump("copy", 0xC2);
        Command(NetworkCommand.Acknowledge);
        Emit(0x79, 0xFE, (byte)message.Length); Jump("receive", 0xDA);
        Address(0x11, ReplyAddress);
        Jump("message", 0x21); // LD HL,message (absolute fixup)
        Emit(0x0E, (byte)message.Length);
        Mark("compare");
        Emit(0x2A, 0x47, 0x1A, 0xB8); // B=(HL+); A=(DE); CP B
        Jump("fail", 0xC2);
        Emit(0x13, 0x0D); Jump("compare", 0xC2);
        Command(NetworkCommand.Close);
        Write(ResultAddress, 1);
        Jump("halt");
        Mark("fail"); Write(ResultAddress, 0xFF);
        Mark("halt"); Emit(0x76); Jump("halt");
        Mark("wait");
        Address(0xFA, NetworkCartridgeDevice.Status); Emit(0xE6, NetworkCartridgeDevice.Busy);
        Jump("wait", 0xC2);
        Address(0xFA, NetworkCartridgeDevice.Status); Emit(0xE6, NetworkCartridgeDevice.Failed);
        Jump("fail", 0xC2); Emit(0xC9);
        Mark("message"); Emit(message);

        foreach (var (offset, label) in fixups)
        { code[offset] = (byte)labels[label]; code[offset + 1] = (byte)(labels[label] >> 8); }
        if (0x150 + code.Count > 0x4000) throw new InvalidOperationException("Demo exceeds ROM0.");
        rom[0x100] = 0xC3; rom[0x101] = 0x50; rom[0x102] = 1;
        Convert.FromHexString("CEED6666CC0D000B" + "03730083000C000D" + "0008111F8889000E" +
            "DCCC6EE6DDDDD999" + "BBBB67636E0EECCC" + "DDDC999FBBB9333E").CopyTo(rom, 0x104);
        Encoding.ASCII.GetBytes("NETCART DEMO").CopyTo(rom, 0x134);
        rom[0x14A] = 1; // ROM-only header; peripheral is attached explicitly by the experiment host.
        code.CopyTo(rom, 0x150);
        byte checksum = 0;
        for (int i = 0x134; i <= 0x14C; i++) checksum = unchecked((byte)(checksum - rom[i] - 1));
        rom[0x14D] = checksum;
        int sum = rom.Sum(b => (int)b);
        rom[0x14E] = (byte)(sum >> 8); rom[0x14F] = (byte)sum;
        return rom;
    }
}
