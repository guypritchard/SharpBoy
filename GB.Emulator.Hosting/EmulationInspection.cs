using GB.Emulator.Core;

namespace GB.Emulator.Hosting;

public sealed record CpuSnapshot(byte A, byte F, byte B, byte C, byte D, byte E, byte H, byte L, ushort SP, ushort PC)
{
    public ushort BC => (ushort)((B << 8) | C);
    public ushort DE => (ushort)((D << 8) | E);
    public ushort HL => (ushort)((H << 8) | L);
    public bool Zero => (F & 0x80) != 0;
    public bool Subtract => (F & 0x40) != 0;
    public bool HalfCarry => (F & 0x20) != 0;
    public bool Carry => (F & 0x10) != 0;
}

public sealed record CartridgeSnapshot(string Title, string? Source, int RomBytes, int? DeclaredRomBytes,
    int? DeclaredRamBytes, CartridgeSupport Support, int LowerRomBank, int UpperRomBank,
    int? RamBank, bool RamEnabled, string BankingMode);

public sealed record HardwareSnapshot(long Cycles, byte Scanline, bool StartingUp, int StartupFrames, bool Halted, bool Stopped);

public sealed class MemorySnapshot
{
    private readonly byte[] bytes;
    internal MemorySnapshot(ushort start, byte[] bytes) { this.Start = start; this.bytes = bytes; }
    public ushort Start { get; }
    public ReadOnlySpan<byte> Bytes => this.bytes;
    public byte this[int index] => this.bytes[index];
}

/// <summary>Read-only copies; call on the host's owning thread. Does not record CPU bus accesses.</summary>
public sealed class EmulationInspection
{
    private readonly EmulationHost host;
    internal EmulationInspection(EmulationHost host) => this.host = host;

    /// <summary>Capture a detached native LCD on demand, including when automatic frame capture is disabled.</summary>
    public VideoFrame CaptureFrame()
    {
        this.host.EnsureUsable();
        return new(this.host.Machine.CaptureVideoState(), this.host.Statistics.Frames,
            this.host.Machine.EmulatedCycles, this.host.Machine.IsStartupSequenceActive);
    }

    public CpuSnapshot CaptureCpu()
    {
        this.host.EnsureUsable();
        CpuRegisters r = this.host.Machine.Cpu.Registers;
        return new(r.A, r.F, r.B, r.C, r.D, r.E, r.H, r.L, r.SP, r.PC);
    }

    public HardwareSnapshot CaptureHardware()
    {
        this.host.EnsureUsable();
        Gameboy m = this.host.Machine;
        return new(m.EmulatedCycles, m.Scanline, m.IsStartupSequenceActive, m.StartupFramesCompleted, m.Cpu.IsHalted, m.Cpu.IsStopped);
    }

    public CartridgeSnapshot? CaptureCartridge()
    {
        this.host.EnsureUsable();
        if (this.host.Machine.Cartridge is not Cartridge cart) return null;
        MemoryMap bus = this.host.Machine.Memory;
        return new(cart.Header.Title, this.host.Source, cart.Data.Length, cart.Header.DeclaredRomSizeBytes,
            cart.Header.DeclaredRamSizeBytes, this.host.Support!, bus.SelectedLowerRomBank, bus.SelectedRomBank,
            bus.SelectedCartridgeRamBank, bus.CartridgeRamEnabled, bus.CartridgeBankingMode);
    }

    public MemorySnapshot ReadMemory(ushort start, int length)
    {
        this.host.EnsureUsable();
        if (length < 0 || length > 0x10000 - start) throw new ArgumentOutOfRangeException(nameof(length));
        var bytes = new byte[length];
        for (int i = 0; i < length; i++) bytes[i] = this.host.Machine.Memory.Peek((ushort)(start + i));
        return new(start, bytes);
    }
}
