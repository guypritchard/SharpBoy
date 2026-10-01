using System;
using System.Collections.Generic;
using System.Linq;
using GB.Emulator.Core;
using GB.Emulator.Core.InputOutput;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Tests;

[TestClass]
public class ApuTests
{
    [TestMethod]
    public void PowerControlsRegistersAndChannelStatus()
    {
        var apu = new Apu();
        apu.Write8(0xFF12, 0xF0);
        Assert.AreEqual(0, apu.Read8(0xFF12));

        apu.Write8(0xFF30, 0xAB);
        apu.Write8(0xFF26, 0x80);
        apu.Write8(0xFF12, 0xF0);
        apu.Write8(0xFF14, 0x80);
        Assert.AreEqual(1, apu.Read8(0xFF26) & 1);

        apu.Write8(0xFF26, 0);
        Assert.AreEqual(0, apu.Read8(0xFF12));
        Assert.AreEqual(0, apu.Read8(0xFF26) & 0x8F);
        Assert.AreEqual(0xAB, apu.Read8(0xFF30));
    }

    [TestMethod]
    public void PulseChannelGeneratesPcmOnlyOnSelectedOutput()
    {
        var apu = new Apu();
        List<short[]> blocks = new();
        apu.SamplesReady += samples => blocks.Add(samples.ToArray());
        apu.Write8(0xFF26, 0x80);
        apu.Write8(0xFF24, 0x77);
        apu.Write8(0xFF25, 0x10); // Channel 1 to left only.
        apu.Write8(0xFF11, 0x80); // 50% duty.
        apu.Write8(0xFF12, 0xF0);
        apu.Write8(0xFF13, 0x00);
        apu.Write8(0xFF14, 0x87);
        apu.Step(100_000);

        Assert.IsNotEmpty(blocks);
        Assert.IsTrue(blocks[0].Where((_, index) => index % 2 == 0).Any(sample => sample != 0));
        Assert.IsTrue(blocks[0].Where((_, index) => index % 2 == 1).All(sample => sample == 0));
        Assert.IsGreaterThan(2, blocks[0].Distinct().Count());
    }

    [TestMethod]
    public void WaveAndNoiseChannelsGenerateSamples()
    {
        foreach (bool useWave in new[] { true, false })
        {
            var apu = new Apu();
            bool nonzero = false;
            apu.SamplesReady += samples => nonzero |= samples.Span.IndexOfAnyExcept((short)0) >= 0;
            apu.Write8(0xFF26, 0x80);
            apu.Write8(0xFF24, 0x77);
            apu.Write8(0xFF25, useWave ? (byte)0x44 : (byte)0x88);
            if (useWave)
            {
                for (ushort address = 0xFF30; address <= 0xFF3F; address++)
                    apu.Write8(address, 0xF0);
                apu.Write8(0xFF1A, 0x80);
                apu.Write8(0xFF1C, 0x20);
                apu.Write8(0xFF1E, 0x87);
            }
            else
            {
                apu.Write8(0xFF21, 0xF0);
                apu.Write8(0xFF22, 0x00);
                apu.Write8(0xFF23, 0x80);
            }
            apu.Step(100_000);
            Assert.IsTrue(nonzero, useWave ? "Wave channel produced silence." : "Noise channel produced silence.");
        }
    }

    [TestMethod]
    public void LengthCounterStopsChannelAtFrameTick()
    {
        var apu = new Apu();
        apu.Write8(0xFF26, 0x80);
        apu.Write8(0xFF11, 0x3F); // Length = one tick.
        apu.Write8(0xFF12, 0xF0);
        apu.Write8(0xFF14, 0xC0);
        Assert.AreEqual(1, apu.Read8(0xFF26) & 1);
        apu.Step(8192);
        Assert.AreEqual(0, apu.Read8(0xFF26) & 1);
    }

    [TestMethod]
    public void GameboySnapshotRestoresApuActivity()
    {
        var gameboy = new Gameboy();
        gameboy.Load(new Cartridge { Data = new byte[0x200] });
        gameboy.Memory.Write8(0x80, 0xFF26);
        gameboy.Memory.Write8(0xF0, 0xFF12);
        gameboy.Memory.Write8(0x80, 0xFF14);
        GameboyState snapshot = gameboy.CaptureState();
        gameboy.Sound.Step(8192 * 4);
        gameboy.Memory.Write8(0, 0xFF26);
        gameboy.RestoreState(snapshot);
        Assert.AreEqual(1, gameboy.Sound.Read8(0xFF26) & 1);
        Assert.AreEqual(0xF0, gameboy.Sound.Read8(0xFF12));
    }
}
