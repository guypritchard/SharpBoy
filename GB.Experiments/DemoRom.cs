using System.Text;

namespace GB.Experiments;

/// <summary>A small Game Boy program serving text and calculating 16-bit primes over SB/SC.</summary>
public static class DemoRom
{
    public const string Message = "Hello from the Game Boy serial port!\n";
    public const int MaximumPrimeIndex = 6542; // The 6542nd prime is 65521.

    public static byte[] Create()
    {
        var rom = new byte[0x8000];
        var code = new List<byte>();
        var labels = new Dictionary<string, int>();
        var relativeFixups = new List<(int Offset, string Label)>();
        var absoluteFixups = new List<(int Offset, string Label)>();

        void Mark(string name) => labels.Add(name, 0x100 + code.Count);
        void Emit(params byte[] bytes) => code.AddRange(bytes);
        void JumpRelative(string name)
        {
            Emit(0x20, 0); // JR NZ,s8
            relativeFixups.Add((code.Count - 1, name));
        }
        void JumpAbsolute(string name, byte opcode = 0xC3)
        {
            Emit(opcode, 0, 0); // JP [condition],a16 or CALL a16
            absoluteFixups.Add((code.Count - 2, name));
        }
        void ReceiveInto(byte hramOffset, string suffix)
        {
            Emit(0x3E, 0xFF, 0xE0, 0x01); // SB = idle byte
            Emit(0x3E, 0x80, 0xE0, 0x02); // Arm external-clock transfer
            string wait = "wait_for_" + suffix;
            Mark(wait);
            Emit(0xF0, 0x02, 0xE6, 0x80); // Poll SC bit 7
            JumpRelative(wait);
            Emit(0xF0, 0x01, 0xE0, hramOffset); // Store received byte in HRAM
        }

        Mark("receive");
        Emit(0x3E, 0xFF, 0xE0, 0x01, 0x3E, 0x80, 0xE0, 0x02);
        Mark("wait_for_command");
        Emit(0xF0, 0x02, 0xE6, 0x80);
        JumpRelative("wait_for_command");
        Emit(0xF0, 0x01, 0xFE, (byte)'G'); // Read command; CP 'G'
        JumpAbsolute("page", 0xCA);
        Emit(0xFE, (byte)'P'); // CP 'P'
        JumpAbsolute("prime_request", 0xCA);
        JumpAbsolute("receive");

        Mark("page");
        Emit(0x21, 0, 0); // LD HL,message
        absoluteFixups.Add((code.Count - 2, "message"));
        Mark("send_page_byte");
        Emit(0x2A, 0x47, 0xE0, 0x01); // LD A,(HL+); LD B,A; write SB
        Emit(0x3E, 0x80, 0xE0, 0x02);
        Mark("wait_for_page_byte");
        Emit(0xF0, 0x02, 0xE6, 0x80);
        JumpRelative("wait_for_page_byte");
        Emit(0x78, 0xB7); // LD A,B; OR A
        JumpRelative("send_page_byte");
        JumpAbsolute("receive");

        // Protocol: P, 16-bit index, 16-bit search ceiling, then 16-bit prime;
        // each word is sent low byte first. The CPU builds a sieve for this request.
        Mark("prime_request");
        ReceiveInto(0x80, "index_low");
        ReceiveInto(0x81, "index_high");
        ReceiveInto(0x84, "ceiling_low");
        ReceiveInto(0x85, "ceiling_high");
        JumpAbsolute("build_sieve", 0xCD);
        Emit(0xF0, 0x80, 0xD6, 0x01, 0xE0, 0x80); // Exclude prime 2 from count
        Emit(0xF0, 0x81, 0xDE, 0x00, 0xE0, 0x81);
        Emit(0x47, 0xF0, 0x80, 0xB0); // Did the caller request prime 2?
        JumpAbsolute("prime_two", 0xCA);
        Emit(0x11, 0x03, 0x00); // LD DE,3 (first odd candidate)

        Mark("scan");
        JumpAbsolute("test_bit", 0xCD); // A != 0 for a composite
        Emit(0xB7); // OR A
        JumpAbsolute("next_candidate", 0xC2);
        Emit(0xF0, 0x80, 0xD6, 0x01, 0xE0, 0x80); // Remaining low -= 1
        Emit(0xF0, 0x81, 0xDE, 0x00, 0xE0, 0x81); // Remaining high -= borrow
        Emit(0x47, 0xF0, 0x80, 0xB0); // OR both remaining bytes
        JumpAbsolute("prime_found", 0xCA);
        Mark("next_candidate");
        Emit(0x13, 0x13); // INC DE twice: only odd candidates matter
        JumpAbsolute("scan");

        Mark("prime_two");
        Emit(0x11, 0x02, 0x00); // LD DE,2
        Mark("prime_found");
        Emit(0x7B, 0xE0, 0x01, 0x3E, 0x80, 0xE0, 0x02); // Send low byte
        Mark("wait_for_prime_low");
        Emit(0xF0, 0x02, 0xE6, 0x80);
        JumpRelative("wait_for_prime_low");
        Emit(0x7A, 0xE0, 0x01, 0x3E, 0x80, 0xE0, 0x02); // Send high byte
        Mark("wait_for_prime_high");
        Emit(0xF0, 0x02, 0xE6, 0x80);
        JumpRelative("wait_for_prime_high");
        JumpAbsolute("receive");

        // C000-DFFF is exactly 8192 bytes: one bit per unsigned 16-bit number.
        // The ROM table contains only odd p, p*p and step 2p. It has no prime
        // results; composite p values are skipped using the current sieve.
        Mark("build_sieve");
        Emit(0x21, 0, 0); // LD HL,square_table
        absoluteFixups.Add((code.Count - 2, "square_table"));
        Mark("next_square");
        Emit(0x2A, 0xB7, 0xC8); // Read p; RET Z at table terminator
        Emit(0xE0, 0x86); // Save p in HRAM
        Emit(0x2A, 0x5F, 0x2A, 0x57); // DE = p*p
        Emit(0x2A, 0x4F, 0x2A, 0x47); // BC = marking step
        Emit(0xE5, 0xC5, 0xD5); // Save table pointer, step and square
        Emit(0xF0, 0x86, 0x5F, 0x16, 0x00); // DE = p
        JumpAbsolute("test_bit", 0xCD);
        Emit(0xD1, 0xC1, 0xE1, 0xB7); // Restore and test composite bit
        JumpAbsolute("next_square", 0xC2);

        Mark("check_multiple");
        Emit(0xF0, 0x85, 0xBA); // Compare ceiling high against multiple high
        JumpAbsolute("next_square", 0xDA); // ceiling < multiple
        JumpAbsolute("mark_multiple", 0xC2); // ceiling high > multiple high
        Emit(0xF0, 0x84, 0xBB); // Compare low bytes
        JumpAbsolute("next_square", 0xDA);
        Mark("mark_multiple");
        Emit(0xE5, 0xC5, 0xD5);
        JumpAbsolute("mark_bit", 0xCD);
        Emit(0xD1, 0xC1, 0xE1);
        Emit(0x7B, 0x81, 0x5F); // E += C
        Emit(0x7A, 0x88, 0x57); // D += B + carry
        JumpAbsolute("next_square", 0xDA); // 16-bit overflow
        JumpAbsolute("check_multiple");

        Mark("mark_bit");
        JumpAbsolute("locate_bit", 0xCD);
        Emit(0x7E, 0xB1, 0x77, 0xC9); // (HL) |= C; RET
        Mark("test_bit");
        JumpAbsolute("locate_bit", 0xCD);
        Emit(0x7E, 0xA1, 0xC9); // A = (HL) & C; RET

        Mark("locate_bit"); // Input DE=number; output HL=bitset address, C=mask
        Emit(0x7B, 0xE6, 0x07, 0x4F, 0x06, 0x00); // BC = E & 7
        Emit(0x21, 0, 0); // LD HL,mask_table
        absoluteFixups.Add((code.Count - 2, "mask_table"));
        Emit(0x09, 0x7E, 0x4F); // Look up bit mask, keep in C
        Emit(0x62, 0x6B); // HL = DE
        for (int i = 0; i < 3; i++) Emit(0xCB, 0x3C, 0xCB, 0x1D); // HL >>= 1
        Emit(0x7C, 0xC6, 0xC0, 0x67, 0xC9); // H += $C0; RET

        Mark("message");
        Emit(Encoding.ASCII.GetBytes(Message));
        Emit(0);
        Mark("mask_table");
        Emit(0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40, 0x80);
        Mark("square_table");
        for (int p = 3; p <= 255; p += 2)
        {
            int square = p * p;
            int step = p * 2;
            Emit((byte)p, (byte)square, (byte)(square >> 8), (byte)step, (byte)(step >> 8));
        }
        Emit(0); // table terminator

        foreach (var (offset, label) in relativeFixups)
        {
            int displacement = labels[label] - (0x100 + offset + 1);
            if (displacement < sbyte.MinValue || displacement > sbyte.MaxValue)
                throw new InvalidOperationException($"Jump to {label} is out of range.");
            code[offset] = unchecked((byte)displacement);
        }
        foreach (var (offset, label) in absoluteFixups)
        {
            code[offset] = (byte)labels[label];
            code[offset + 1] = (byte)(labels[label] >> 8);
        }

        if (0x100 + code.Count > 0x4000)
            throw new InvalidOperationException("The demo cartridge must fit in the first ROM bank.");
        code.CopyTo(rom, 0x100);
        return rom;
    }
}
