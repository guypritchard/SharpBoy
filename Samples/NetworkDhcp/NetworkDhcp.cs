using GB.Sdk;

// Guest program: all packet construction, validation, retries and screen drawing
// execute on the SM83. The cartridge supplies UDP datagrams, not a DHCP service.
static class NetworkDhcp
{
    const ushort Result = 0xC000, State = 0xC001, Retry = 0xC002, Noise = 0xC003, NakBudget = 0xC005;
    const ushort Transaction = 0xC008, Address = 0xC010, Server = 0xC014, Mask = 0xC018;
    const ushort Router = 0xC01C, Dns = 0xC020, Lease = 0xC024;
    const ushort Seen = 0xC030, Message = 0xC031, OptionServer = 0xC034, OptionMask = 0xC038;
    const ushort OptionRouter = 0xC03C, OptionDns = 0xC040, OptionLease = 0xC044;
    const ushort Tx = 0xA100, Rx = 0xA500;

    static byte Read(ushort address) { return GameMemory.ReadByte(address); }
    static void Write(ushort address, byte value) { GameMemory.WriteByte(address, value); }
    static void Clear(ushort address, ushort count)
    {
        while (count != 0) { Write(address, 0); address++; count--; }
    }
    static void Copy(ushort source, ushort destination, byte count)
    {
        while (count != 0) { Write(destination, Read(source)); source++; destination++; count--; }
    }
    static bool Equal(ushort first, ushort second, byte count)
    {
        while (count != 0)
        {
            if (Read(first) != Read(second)) return false;
            first++; second++; count--;
        }
        return true;
    }
    static bool Unicast(ushort address)
    {
        byte first = Read(address);
        return first != 0 && first != 127 && first < 224;
    }
    static byte Command(byte command)
    {
        Write(0xA003, command);
        while ((Read(0xA002) & 2) != 0) { }
        return Read(0xA004);
    }

    // The LCD is disabled only in VBlank; every font/map write uses the SDK.
    static void BeginScreen()
    {
        GameDisplay.WaitForNextVBlank();
        Write(0xFF40, 0);
        GameText.Write(0x9881, "                  ");
    }
    static void EndScreen() { Write(0xFF40, 0x91); }
    static void Status(byte status)
    {
        BeginScreen();
        if (status == 0) GameText.Write(0x9881, "STARTING");
        if (status == 1) GameText.Write(0x9881, "DISCOVER");
        if (status == 2) GameText.Write(0x9881, "OFFER RECEIVED");
        if (status == 3) GameText.Write(0x9881, "REQUESTING");
        if (status == 4) GameText.Write(0x9881, "ACK RECEIVED");
        if (status == 5) GameText.Write(0x9881, "IP ACQUIRED");
        if (status == 6) GameText.Write(0x9881, "RETRYING");
        if (status == 7) GameText.Write(0x9881, "NAK - RESTARTING");
        if (status == 8) GameText.Write(0x9881, "FAILED - NO LEASE");
        if (status == 5)
        {
            GameText.Write(0x98E1, "                  ");
            GameTextNumbers.WriteIpv4(0x98E1, Address);
            GameTextNumbers.WriteIpv4(0x9941, Router);
            GameTextNumbers.WriteIpv4(0x99A1, Dns);
            GameTextNumbers.WriteIpv4(0x9A01, Server);
        }
        EndScreen();
    }
    static void InitializeScreen()
    {
        GameDisplay.InitializeBackground();
        BeginScreen();
        GameText.Write(0x9821, "NETWORK CARTRIDGE");
        GameText.Write(0x9861, "DHCP STATUS");
        GameText.Write(0x98C1, "IP ADDRESS");
        GameText.Write(0x98E1, "WAITING...");
        GameText.Write(0x9921, "GATEWAY");
        GameText.Write(0x9981, "DNS SERVER");
        GameText.Write(0x99E1, "DHCP SERVER");
        EndScreen();
        Status(0);
    }

    static void BuildPacket(bool request)
    {
        Clear(Tx, 300);
        Write(Tx, 1); Write(Tx + 1, 1); Write(Tx + 2, 6);
        Copy(Transaction, Tx + 4, 4);
        Write(Tx + 10, 128); // BOOTP broadcast flag
        Write(Tx + 28, 2); Write(Tx + 29, 83); Write(Tx + 30, 66); Write(Tx + 33, 1);
        Write(Tx + 236, 99); Write(Tx + 237, 130); Write(Tx + 238, 83); Write(Tx + 239, 99);
        Write(Tx + 240, 53); Write(Tx + 241, 1);
        Write(Tx + 242, 1);
        if (request) Write(Tx + 242, 3);
        Write(Tx + 243, 61); Write(Tx + 244, 7); Write(Tx + 245, 1);
        Copy(Tx + 28, Tx + 246, 6);
        Write(Tx + 252, 55); Write(Tx + 253, 4);
        Write(Tx + 254, 1); Write(Tx + 255, 3); Write(Tx + 256, 6); Write(Tx + 257, 51);
        Write(Tx + 258, 57); Write(Tx + 259, 2); Write(Tx + 260, 4);
        Write(Tx + 262, 255);
        if (request)
        {
            Write(Tx + 262, 50); Write(Tx + 263, 4); Copy(Address, Tx + 264, 4);
            Write(Tx + 268, 54); Write(Tx + 269, 4); Copy(Server, Tx + 270, 4);
            Write(Tx + 274, 255);
        }
        Write(0xA005, 44); Write(0xA006, 1); // 300-byte datagram
    }
    static ushort ReceiveLength()
    {
        ushort length = Read(0xA008);
        // No shifts are needed in the initial C# subset.
        ushort high = 0;
        while (high < 8) { length += length; high++; }
        return (ushort)(length + Read(0xA007));
    }
    static bool HeaderValid()
    {
        if (ReceiveLength() < 240 || ReceiveLength() > 1024) return false;
        if (Read(0xA018) != 67 || Read(0xA019) != 0) return false;
        if (Read(Rx) != 2 || Read(Rx + 1) != 1 || Read(Rx + 2) != 6) return false;
        if (!Equal(Rx + 4, Transaction, 4)) return false;
        if (Read(Rx + 28) != 2 || Read(Rx + 29) != 83 || Read(Rx + 30) != 66) return false;
        if (Read(Rx + 31) != 0 || Read(Rx + 32) != 0 || Read(Rx + 33) != 1) return false;
        if (Read(Rx + 236) != 99 || Read(Rx + 237) != 130 || Read(Rx + 238) != 83 || Read(Rx + 239) != 99) return false;
        return Read(Rx + 24) == 0 && Read(Rx + 25) == 0 && Read(Rx + 26) == 0 && Read(Rx + 27) == 0;
    }
    static bool OptionsValid()
    {
        Clear(Seen, 24);
        ushort remaining = (ushort)(ReceiveLength() - 240);
        ushort cursor = Rx + 240;
        while (remaining != 0)
        {
            byte tag = Read(cursor); cursor++; remaining--;
            if (tag == 255) return (Read(Seen) & 3) == 3;
            if (tag != 0)
            {
                if (remaining == 0) return false;
                byte length = Read(cursor); cursor++; remaining--;
                if (length > remaining || tag == 52) return false;
                byte flag = 0;
                byte size = 4;
                ushort destination = 0;
                if (tag == 53) { flag = 1; size = 1; destination = Message; }
                if (tag == 54) { flag = 2; destination = OptionServer; }
                if (tag == 51) { flag = 4; destination = OptionLease; }
                if (tag == 1) { flag = 8; destination = OptionMask; }
                if (tag == 3) { flag = 16; destination = OptionRouter; }
                if (tag == 6) { flag = 32; destination = OptionDns; }
                if (flag != 0)
                {
                    if (tag == 3 || tag == 6)
                    {
                        if (length < 4 || (length & 3) != 0) return false;
                    }
                    else if (length != size) return false;
                    if ((Read(Seen) & flag) != 0) return false;
                    Write(Seen, (byte)(Read(Seen) | flag));
                    Copy(cursor, destination, size);
                }
                cursor += length; remaining -= length;
            }
        }
        return false; // END is mandatory
    }

    // 0=ignore, 1=offer, 2=ACK, 3=NAK. Only selected-server replies can bind us.
    static byte ValidateReply()
    {
        if (!HeaderValid() || !OptionsValid()) return 0;
        if (!Unicast(OptionServer) || !Equal(OptionServer, 0xA01A, 4)) return 0;
        if (Read(State) == 1)
        {
            if (Read(Message) != 2 || !Unicast(Rx + 16)) return 0;
            Copy(Rx + 16, Address, 4); Copy(OptionServer, Server, 4);
            return 1;
        }
        if (!Equal(OptionServer, Server, 4)) return 0;
        if (Read(Message) == 6) return 3;
        if (Read(Message) != 5 || !Equal(Rx + 16, Address, 4) || (Read(Seen) & 7) != 7) return 0;
        if ((Read(OptionLease) | Read(OptionLease + 1) | Read(OptionLease + 2) | Read(OptionLease + 3)) == 0) return 0;
        Copy(OptionMask, Mask, 4); Copy(OptionRouter, Router, 4);
        Copy(OptionDns, Dns, 4); Copy(OptionLease, Lease, 4);
        return 2;
    }
    static bool Acquire()
    {
        Write(State, 1); Write(Retry, 3); Write(NakBudget, 2);
        while (true)
        {
            Write(Noise, 8);
            BuildPacket(Read(State) == 2);
            if (Read(State) == 1) Status(1); else Status(3);
            if (Command(5) != 0 || Command(2) != 0) return false;
            byte reply = 0;
            while (Read(Noise) != 0)
            {
                if (Command(5) != 0) return false;
                byte error = Command(3);
                if (error == 8) break;
                if (error != 0) return false;
                reply = ValidateReply();
                if (reply != 0) break;
                Write(Noise, (byte)(Read(Noise) - 1));
            }
            if (reply == 1)
            {
                Status(2); Write(State, 2); Write(Retry, 3);
            }
            else if (reply == 2)
            {
                Status(4);
                if (Command(4) != 0) return false;
                Write(State, 4); Status(5);
                return true;
            }
            else if (reply == 3)
            {
                Status(7); Write(NakBudget, (byte)(Read(NakBudget) - 1));
                if (Read(NakBudget) == 0) return false;
                Write(Transaction + 3, (byte)(Read(Transaction + 3) + 1));
                Write(State, 1); Write(Retry, 3);
            }
            else
            {
                Status(6); Write(Retry, (byte)(Read(Retry) - 1));
                if (Read(Retry) == 0) return false;
            }
        }
    }

    public static void Run()
    {
        Clear(Result, 96);
        InitializeScreen();
        bool ready = Read(0xA000) == 78 && Read(0xA001) == 2 && (Read(0xA009) & 1) != 0;
        Write(0xA010, 68); Write(0xA011, 0); Write(0xA012, 67); Write(0xA013, 0);
        Write(0xA014, 255); Write(0xA015, 255); Write(0xA016, 255); Write(0xA017, 255);
        Write(Transaction, 83); Write(Transaction + 1, 77); Write(Transaction + 2, 56); Write(Transaction + 3, 51);
        if (ready && Command(1) == 0 && Acquire()) Write(Result, 1);
        else { Write(State, 5); Status(8); Write(Result, 255); }
        // Startup parks in gb_idle after the completed screen and result publication.
    }
}
