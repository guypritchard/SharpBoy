#nullable enable
using System;
using System.Runtime.InteropServices;
using GB.Emulator.Core.InputOutput;

namespace GB.Emulator.Audio;

/// <summary>Small bounded waveOut queue for the APU's signed 16-bit stereo PCM.</summary>
public sealed class WindowsAudioOutput : IDisposable
{
    private const uint Mapper = 0xFFFFFFFF;
    private const uint HeaderDone = 1;
    private readonly Apu apu;
    private readonly Slot[] slots = new Slot[8];
    private IntPtr device;
    private bool disposed;
    private int nextSlot;

    private WindowsAudioOutput(Apu apu)
    {
        this.apu = apu;
        var format = new WaveFormat
        {
            FormatTag = 1,
            Channels = 2,
            SamplesPerSecond = Apu.SampleRate,
            AverageBytesPerSecond = Apu.SampleRate * 4,
            BlockAlign = 4,
            BitsPerSample = 16
        };
        int result = WaveOutOpen(out this.device, Mapper, ref format, IntPtr.Zero, IntPtr.Zero, 0);
        if (result != 0) throw new InvalidOperationException($"Could not open the audio device (waveOut error {result}).");
        int headerSize = Marshal.SizeOf<WaveHeader>();
        for (int i = 0; i < this.slots.Length; i++)
            this.slots[i] = new Slot(Marshal.AllocHGlobal(2048), Marshal.AllocHGlobal(headerSize));
        this.apu.SamplesReady += this.OnSamplesReady;
    }

    /// <summary>Returns null when Windows audio is unavailable; emulation can continue silently.</summary>
    public static WindowsAudioOutput? TryCreate(Apu apu)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try { return new WindowsAudioOutput(apu); }
        catch (DllNotFoundException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private void OnSamplesReady(ReadOnlyMemory<short> samples)
    {
        if (this.disposed) return;
        int headerSize = Marshal.SizeOf<WaveHeader>();
        for (int attempt = 0; attempt < this.slots.Length; attempt++)
        {
            int index = (this.nextSlot + attempt) % this.slots.Length;
            Slot slot = this.slots[index];
            if (slot.Submitted)
            {
                WaveHeader previous = Marshal.PtrToStructure<WaveHeader>(slot.Header);
                if ((previous.Flags & HeaderDone) == 0) continue;
                WaveOutUnprepareHeader(this.device, slot.Header, (uint)headerSize);
                slot.Submitted = false;
            }
            short[] copy = samples.ToArray();
            Marshal.Copy(copy, 0, slot.Data, copy.Length);
            var header = new WaveHeader { Data = slot.Data, BufferLength = (uint)(copy.Length * sizeof(short)) };
            Marshal.StructureToPtr(header, slot.Header, false);
            if (WaveOutPrepareHeader(this.device, slot.Header, (uint)headerSize) != 0) return;
            if (WaveOutWrite(this.device, slot.Header, (uint)headerSize) != 0)
            {
                WaveOutUnprepareHeader(this.device, slot.Header, (uint)headerSize);
                return;
            }
            slot.Submitted = true;
            this.nextSlot = (index + 1) % this.slots.Length;
            return;
        }
        // The CPU is running ahead of the speaker; discard this block instead of growing latency.
    }

    public void Dispose()
    {
        if (this.disposed) return;
        this.disposed = true;
        this.apu.SamplesReady -= this.OnSamplesReady;
        if (this.device != IntPtr.Zero)
        {
            WaveOutReset(this.device);
            uint headerSize = (uint)Marshal.SizeOf<WaveHeader>();
            foreach (Slot slot in this.slots)
            {
                if (slot == null) continue;
                if (slot.Submitted) WaveOutUnprepareHeader(this.device, slot.Header, headerSize);
                Marshal.FreeHGlobal(slot.Data);
                Marshal.FreeHGlobal(slot.Header);
            }
            WaveOutClose(this.device);
            this.device = IntPtr.Zero;
        }
    }

    private sealed class Slot(IntPtr data, IntPtr header)
    {
        public IntPtr Data { get; } = data;
        public IntPtr Header { get; } = header;
        public bool Submitted { get; set; }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSecond;
        public uint AverageBytesPerSecond;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr Data;
        public uint BufferLength;
        public uint BytesRecorded;
        public IntPtr User;
        public uint Flags;
        public uint Loops;
        public IntPtr Next;
        public IntPtr Reserved;
    }

    [DllImport("winmm.dll", EntryPoint = "waveOutOpen")]
    private static extern int WaveOutOpen(out IntPtr device, uint deviceId, ref WaveFormat format,
        IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll", EntryPoint = "waveOutPrepareHeader")]
    private static extern int WaveOutPrepareHeader(IntPtr device, IntPtr header, uint headerSize);
    [DllImport("winmm.dll", EntryPoint = "waveOutWrite")]
    private static extern int WaveOutWrite(IntPtr device, IntPtr header, uint headerSize);
    [DllImport("winmm.dll", EntryPoint = "waveOutUnprepareHeader")]
    private static extern int WaveOutUnprepareHeader(IntPtr device, IntPtr header, uint headerSize);
    [DllImport("winmm.dll", EntryPoint = "waveOutReset")]
    private static extern int WaveOutReset(IntPtr device);
    [DllImport("winmm.dll", EntryPoint = "waveOutClose")]
    private static extern int WaveOutClose(IntPtr device);
}
