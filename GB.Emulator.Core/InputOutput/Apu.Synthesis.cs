using System;

namespace GB.Emulator.Core.InputOutput
{
    public sealed partial class Apu
    {
        private int PulsePeriod(int lowIndex) =>
            (2048 - (this.registers[lowIndex] | ((this.registers[lowIndex + 1] & 7) << 8))) * 4;

        private int WavePeriod() =>
            (2048 - (this.registers[0x0D] | ((this.registers[0x0E] & 7) << 8))) * 2;

        private int NoisePeriod()
        {
            int divisor = (this.registers[0x12] & 7) * 16;
            return (divisor == 0 ? 8 : divisor) << (this.registers[0x12] >> 4);
        }

        private void TriggerPulse(PulseChannel channel, int offset)
        {
            byte envelope = this.registers[offset + 2];
            channel.Enabled = (envelope & 0xF8) != 0;
            if (channel.Length == 0) channel.Length = 64;
            channel.Timer = this.PulsePeriod(offset + 3);
            channel.Position = 0;
            channel.TriggerEnvelope(envelope);
            if (offset != 0) return;
            channel.ShadowFrequency = this.registers[3] | ((this.registers[4] & 7) << 8);
            int sweepPeriod = (this.registers[0] >> 4) & 7;
            channel.SweepTimer = sweepPeriod == 0 ? 8 : sweepPeriod;
            channel.SweepEnabled = sweepPeriod != 0 || (this.registers[0] & 7) != 0;
            if ((this.registers[0] & 7) != 0 && this.SweepFrequency(channel) > 2047)
                channel.Enabled = false;
        }

        private void TriggerWave()
        {
            this.wave.Enabled = (this.registers[0x0A] & 0x80) != 0;
            if (this.wave.Length == 0) this.wave.Length = 256;
            this.wave.Timer = this.WavePeriod();
            this.wave.Position = 0;
        }

        private void TriggerNoise()
        {
            byte envelope = this.registers[0x11];
            this.noise.Enabled = (envelope & 0xF8) != 0;
            if (this.noise.Length == 0) this.noise.Length = 64;
            this.noise.Timer = this.NoisePeriod();
            this.noise.Lfsr = 0x7FFF;
            this.noise.TriggerEnvelope(envelope);
        }

        private void ClockFrame()
        {
            if ((this.frameStep & 1) == 0)
            {
                this.pulse1.ClockLength();
                this.pulse2.ClockLength();
                this.wave.ClockLength();
                this.noise.ClockLength();
            }
            if (this.frameStep is 2 or 6) this.ClockSweep();
            if (this.frameStep == 7)
            {
                this.pulse1.ClockEnvelope(this.registers[2]);
                this.pulse2.ClockEnvelope(this.registers[7]);
                this.noise.ClockEnvelope(this.registers[0x11]);
            }
            this.frameStep = (this.frameStep + 1) & 7;
        }

        private int SweepFrequency(PulseChannel channel)
        {
            int change = channel.ShadowFrequency >> (this.registers[0] & 7);
            return (this.registers[0] & 8) != 0
                ? channel.ShadowFrequency - change : channel.ShadowFrequency + change;
        }

        private void ClockSweep()
        {
            if (--this.pulse1.SweepTimer > 0) return;
            int period = (this.registers[0] >> 4) & 7;
            this.pulse1.SweepTimer = period == 0 ? 8 : period;
            if (!this.pulse1.SweepEnabled || period == 0) return;
            int next = this.SweepFrequency(this.pulse1);
            if (next > 2047) { this.pulse1.Enabled = false; return; }
            if ((this.registers[0] & 7) == 0) return;
            this.pulse1.ShadowFrequency = next;
            this.registers[3] = (byte)next;
            this.registers[4] = (byte)((this.registers[4] & ~7) | (next >> 8));
            if (this.SweepFrequency(this.pulse1) > 2047) this.pulse1.Enabled = false;
        }

        private void EmitSample()
        {
            if (this.SamplesReady == null) return;
            int p1 = this.pulse1.Output(this.registers[1]);
            int p2 = this.pulse2.Output(this.registers[6]);
            int w = this.wave.Output(this.registers, this.registers[0x0C]);
            int n = this.noise.Output();
            int routing = this.registers[0x15];
            float left = (((routing & 0x10) != 0 ? p1 : 0) + ((routing & 0x20) != 0 ? p2 : 0) +
                ((routing & 0x40) != 0 ? w : 0) + ((routing & 0x80) != 0 ? n : 0)) / 60f;
            float right = (((routing & 1) != 0 ? p1 : 0) + ((routing & 2) != 0 ? p2 : 0) +
                ((routing & 4) != 0 ? w : 0) + ((routing & 8) != 0 ? n : 0)) / 60f;
            left *= (((this.registers[0x14] >> 4) & 7) + 1) / 8f;
            right *= ((this.registers[0x14] & 7) + 1) / 8f;
            float leftAc = left - this.leftCapacitor;
            float rightAc = right - this.rightCapacitor;
            this.leftCapacitor += leftAc * 0.004f;
            this.rightCapacitor += rightAc * 0.004f;
            this.sampleBuffer[this.sampleIndex++] = (short)Math.Clamp((int)(leftAc * 24000), -32768, 32767);
            this.sampleBuffer[this.sampleIndex++] = (short)Math.Clamp((int)(rightAc * 24000), -32768, 32767);
            if (this.sampleIndex != this.sampleBuffer.Length) return;
            this.SamplesReady(this.sampleBuffer);
            this.sampleIndex = 0;
        }

        internal abstract class SoundChannel
        {
            public bool Enabled;
            public int Length;
            public bool LengthEnabled;
            public int Timer;

            public void ClockLength()
            {
                if (this.LengthEnabled && this.Length > 0 && --this.Length == 0)
                    this.Enabled = false;
            }
        }

        internal abstract class EnvelopeChannel : SoundChannel
        {
            public int Volume;
            public int EnvelopeTimer;

            public void TriggerEnvelope(byte envelope)
            {
                this.Volume = envelope >> 4;
                int period = envelope & 7;
                this.EnvelopeTimer = period == 0 ? 8 : period;
            }

            public void ClockEnvelope(byte envelope)
            {
                int period = envelope & 7;
                if (period == 0 || --this.EnvelopeTimer > 0) return;
                this.EnvelopeTimer = period;
                int next = this.Volume + (((envelope & 8) != 0) ? 1 : -1);
                if (next is >= 0 and <= 15) this.Volume = next;
            }
        }

        internal sealed class PulseChannel : EnvelopeChannel
        {
            private static readonly byte[] DutyMasks = { 0x01, 0x81, 0x87, 0x7E };
            public int Position;
            public int ShadowFrequency;
            public int SweepTimer;
            public bool SweepEnabled;

            public void Advance(int cycles, int period)
            {
                if (!this.Enabled) return;
                this.Timer -= cycles;
                while (this.Timer <= 0)
                {
                    this.Timer += period;
                    this.Position = (this.Position + 1) & 7;
                }
            }

            public int Output(byte duty) => this.Enabled &&
                ((DutyMasks[duty >> 6] >> this.Position) & 1) != 0 ? this.Volume : 0;

            public PulseChannel Clone() => (PulseChannel)this.MemberwiseClone();
        }

        internal sealed class WaveChannel : SoundChannel
        {
            public int Position;

            public void Advance(int cycles, int period)
            {
                if (!this.Enabled) return;
                this.Timer -= cycles;
                while (this.Timer <= 0)
                {
                    this.Timer += period;
                    this.Position = (this.Position + 1) & 31;
                }
            }

            public int Output(byte[] registers, byte level)
            {
                int code = (level >> 5) & 3;
                if (!this.Enabled || code == 0) return 0;
                byte value = registers[0x20 + (this.Position >> 1)];
                int sample = (this.Position & 1) == 0 ? value >> 4 : value & 15;
                return sample >> (code - 1);
            }

            public WaveChannel Clone() => (WaveChannel)this.MemberwiseClone();
        }

        internal sealed class NoiseChannel : EnvelopeChannel
        {
            public int Lfsr = 0x7FFF;

            public void Advance(int cycles, int period)
            {
                if (!this.Enabled) return;
                this.Timer -= cycles;
                while (this.Timer <= 0)
                {
                    this.Timer += period;
                    int feedback = (this.Lfsr ^ (this.Lfsr >> 1)) & 1;
                    this.Lfsr = (this.Lfsr >> 1) | (feedback << 14);
                    if ((this.OwnerWidthMode & 8) != 0)
                        this.Lfsr = (this.Lfsr & ~(1 << 6)) | (feedback << 6);
                }
            }

            public int OwnerWidthMode;
            public int Output() => this.Enabled && (this.Lfsr & 1) == 0 ? this.Volume : 0;
            public NoiseChannel Clone() => (NoiseChannel)this.MemberwiseClone();
        }
    }
}
