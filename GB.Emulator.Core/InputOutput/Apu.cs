#nullable enable
using System;

namespace GB.Emulator.Core.InputOutput
{
    /// <summary>DMG sound registers, four channels, frame sequencer, and stereo mixer.</summary>
    public sealed partial class Apu : IMemoryRange
    {
        public const int SampleRate = 48000;
        private const int CpuClock = 4194304;
        private const int FramePeriod = CpuClock / 512;
        private readonly byte[] registers = new byte[0x30];
        private PulseChannel pulse1 = new();
        private PulseChannel pulse2 = new();
        private WaveChannel wave = new();
        private NoiseChannel noise = new();
        private readonly short[] sampleBuffer = new short[1024];
        private int sampleIndex;
        private int samplePhase;
        private int frameCycles;
        private int frameStep;
        private float leftCapacitor;
        private float rightCapacitor;

        public ushort Start => 0xFF10;
        public ushort End => 0xFF3F;
        public bool Powered => (this.registers[0x16] & 0x80) != 0;

        /// <summary>Interleaved signed 16-bit stereo PCM. The memory is valid only during the callback.</summary>
        public event Action<ReadOnlyMemory<short>>? SamplesReady;

        public byte Read8(ushort location)
        {
            if (location < Start || location > End) throw new ArgumentOutOfRangeException(nameof(location));
            if (location == 0xFF26)
                return (byte)(0x70 | this.registers[0x16] | (this.pulse1.Enabled ? 1 : 0) |
                    (this.pulse2.Enabled ? 2 : 0) | (this.wave.Enabled ? 4 : 0) |
                    (this.noise.Enabled ? 8 : 0));
            return this.registers[location - Start];
        }

        public void Write8(ushort location, byte value)
        {
            if (location < Start || location > End) throw new ArgumentOutOfRangeException(nameof(location));
            int index = location - Start;
            if (location == 0xFF26)
            {
                if ((value & 0x80) == 0)
                {
                    Array.Clear(this.registers, 0, 0x17);
                    this.pulse1 = new();
                    this.pulse2 = new();
                    this.wave = new();
                    this.noise = new();
                    this.frameCycles = this.frameStep = 0;
                }
                else this.registers[0x16] = 0x80;
                return;
            }
            if (location is >= 0xFF30 and <= 0xFF3F)
            {
                this.registers[index] = value;
                return;
            }
            if (!this.Powered || location is >= 0xFF27 and <= 0xFF2F) return;
            this.registers[index] = value;
            switch (location)
            {
                case 0xFF11: this.pulse1.Length = 64 - (value & 0x3F); break;
                case 0xFF12: if ((value & 0xF8) == 0) this.pulse1.Enabled = false; break;
                case 0xFF14:
                    this.pulse1.LengthEnabled = (value & 0x40) != 0;
                    if ((value & 0x80) != 0) this.TriggerPulse(this.pulse1, 0);
                    break;
                case 0xFF16: this.pulse2.Length = 64 - (value & 0x3F); break;
                case 0xFF17: if ((value & 0xF8) == 0) this.pulse2.Enabled = false; break;
                case 0xFF19:
                    this.pulse2.LengthEnabled = (value & 0x40) != 0;
                    if ((value & 0x80) != 0) this.TriggerPulse(this.pulse2, 5);
                    break;
                case 0xFF1A: if ((value & 0x80) == 0) this.wave.Enabled = false; break;
                case 0xFF1B: this.wave.Length = 256 - value; break;
                case 0xFF1E:
                    this.wave.LengthEnabled = (value & 0x40) != 0;
                    if ((value & 0x80) != 0) this.TriggerWave();
                    break;
                case 0xFF20: this.noise.Length = 64 - (value & 0x3F); break;
                case 0xFF21: if ((value & 0xF8) == 0) this.noise.Enabled = false; break;
                case 0xFF22: this.noise.OwnerWidthMode = value; break;
                case 0xFF23:
                    this.noise.LengthEnabled = (value & 0x40) != 0;
                    if ((value & 0x80) != 0) this.TriggerNoise();
                    break;
            }
        }

        public void Step(int cycles)
        {
            if (cycles < 0) throw new ArgumentOutOfRangeException(nameof(cycles));
            if (!this.Powered) return;
            while (cycles > 0)
            {
                int untilSample = (CpuClock - this.samplePhase + SampleRate - 1) / SampleRate;
                int chunk = Math.Min(cycles, Math.Min(FramePeriod - this.frameCycles, untilSample));
                if (this.Powered)
                {
                    this.pulse1.Advance(chunk, this.PulsePeriod(3));
                    this.pulse2.Advance(chunk, this.PulsePeriod(8));
                    this.wave.Advance(chunk, this.WavePeriod());
                    this.noise.Advance(chunk, this.NoisePeriod());
                }
                cycles -= chunk;
                this.frameCycles += chunk;
                this.samplePhase += chunk * SampleRate;
                if (this.frameCycles == FramePeriod)
                {
                    this.frameCycles = 0;
                    if (this.Powered) this.ClockFrame();
                }
                if (this.samplePhase >= CpuClock)
                {
                    this.samplePhase -= CpuClock;
                    this.EmitSample();
                }
            }
        }

        public SoundState CaptureState()
        {
            byte[] copy = (byte[])this.registers.Clone();
            copy[0x16] = this.Read8(0xFF26);
            return new SoundState(copy);
        }

        internal ApuState Snapshot() => new((byte[])this.registers.Clone(),
            this.pulse1.Clone(), this.pulse2.Clone(), this.wave.Clone(), this.noise.Clone(),
            this.samplePhase, this.frameCycles, this.frameStep, this.leftCapacitor, this.rightCapacitor);

        internal void Restore(ApuState state)
        {
            state.Registers.CopyTo(this.registers, 0);
            this.pulse1 = state.Pulse1.Clone();
            this.pulse2 = state.Pulse2.Clone();
            this.wave = state.Wave.Clone();
            this.noise = state.Noise.Clone();
            this.samplePhase = state.SamplePhase;
            this.frameCycles = state.FrameCycles;
            this.frameStep = state.FrameStep;
            this.leftCapacitor = state.LeftCapacitor;
            this.rightCapacitor = state.RightCapacitor;
            this.sampleIndex = 0;
        }

        internal void Reset()
        {
            Array.Clear(this.registers);
            this.pulse1 = new();
            this.pulse2 = new();
            this.wave = new();
            this.noise = new();
            this.sampleIndex = this.samplePhase = this.frameCycles = this.frameStep = 0;
            this.leftCapacitor = this.rightCapacitor = 0;
        }

        internal sealed record ApuState(byte[] Registers, PulseChannel Pulse1, PulseChannel Pulse2,
            WaveChannel Wave, NoiseChannel Noise, int SamplePhase, int FrameCycles, int FrameStep,
            float LeftCapacitor, float RightCapacitor);
    }
}
