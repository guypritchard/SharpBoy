using GB.Emulator.Core;
using GB.Emulator.Core.InputOutput;
using GB.Experiments;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace GB.Emulator.Tests;

[TestClass]
public class SerialExperimentTests
{
    [TestMethod]
    public void ExternalTransferCompletesAndRequestsSerialInterrupt()
    {
        var gameboy = new Gameboy();
        gameboy.Memory.Write8(0x42, 0xFF01);
        gameboy.Memory.Write8(0x80, 0xFF02);

        Assert.IsTrue(gameboy.Serial.IsTransferPending);
        Assert.AreEqual((byte)0x42, gameboy.Serial.ClockExternal(0x99));
        Assert.AreEqual((byte)0x99, gameboy.Memory.Read8(0xFF01));
        Assert.AreEqual((byte)0x99, gameboy.Memory.Peek(0xFF01));
        Assert.AreEqual(0, gameboy.Memory.Read8(0xFF02) & 0x80);
        Assert.AreEqual(0x08, gameboy.Memory.Read8(0xFF0F) & 0x08);
    }

    [TestMethod]
    public void InternalClockExchangesWithAnotherSerialPort()
    {
        var master = new SerialPort();
        var slave = new SerialPort();
        master.Peer = new SerialLinkCable(slave);
        master.Write8(0xFF01, 0x12);
        slave.Write8(0xFF01, 0x34);
        slave.Write8(0xFF02, 0x80);
        master.Write8(0xFF02, 0x81);

        master.Step(4095);
        Assert.IsTrue(master.IsTransferPending);
        master.Step(1);

        Assert.AreEqual((byte)0x34, master.Read8(0xFF01));
        Assert.AreEqual((byte)0x12, slave.Read8(0xFF01));
        Assert.IsFalse(master.IsTransferPending);
        Assert.IsFalse(slave.IsTransferPending);
    }

    [TestMethod]
    public void CpuCyclesAdvanceInternalSerialClock()
    {
        var gameboy = new Gameboy();
        gameboy.Load(new Cartridge { Data = new byte[0x8000] });
        gameboy.Memory.Write8(0x25, 0xFF01);
        gameboy.Memory.Write8(0x81, 0xFF02);

        for (int i = 0; i < 1024; i++) gameboy.Step();

        Assert.IsFalse(gameboy.Serial.IsTransferPending);
        Assert.AreEqual((byte)0xFF, gameboy.Memory.Read8(0xFF01));
        Assert.AreEqual(0x08, gameboy.Memory.Read8(0xFF0F) & 0x08);
    }

    [TestMethod]
    public void DemoRomServesRepeatedRequestsThroughSerialPort()
    {
        var server = new GameboySerialServer(DemoRom.Create());

        Assert.AreEqual(DemoRom.Message, server.GetPage());
        Assert.AreEqual(DemoRom.Message, server.GetPage());
    }

    [TestMethod]
    public void DemoRomCalculatesPrimesThroughSerialPort()
    {
        var server = new GameboySerialServer(DemoRom.Create());

        Assert.AreEqual(2, server.GetPrime(1));
        Assert.AreEqual(3, server.GetPrime(2));
        Assert.AreEqual(11, server.GetPrime(5));
        Assert.AreEqual(29, server.GetPrime(10));
        Assert.AreEqual(97, server.GetPrime(25));
        Assert.AreEqual(251, server.GetPrime(54));
        Assert.AreEqual(257, server.GetPrime(55));
        Assert.AreEqual(1613, server.GetPrime(255));
        Assert.AreEqual(1619, server.GetPrime(256));
        Assert.AreEqual(7919, server.GetPrime(1000));
        Assert.AreEqual(65521, server.GetPrime(DemoRom.MaximumPrimeIndex));
        Assert.AreEqual(DemoRom.Message, server.GetPage());
        Assert.AreEqual(7, server.GetPrime(4));
    }

    [TestMethod]
    public void SnapshotRestoresPendingSerialTransfer()
    {
        var gameboy = new Gameboy();
        gameboy.Memory.Write8(0x12, 0xFF01);
        gameboy.Memory.Write8(0x80, 0xFF02);
        GameboyState state = gameboy.CaptureState();
        gameboy.Serial.ClockExternal(0x34);

        gameboy.RestoreState(state);

        Assert.IsTrue(gameboy.Serial.IsTransferPending);
        Assert.AreEqual((byte)0x12, gameboy.Memory.Read8(0xFF01));
    }

    [TestMethod]
    public async Task TcpAdapterServesGameboyResponse()
    {
        var adapter = new VirtualSerialTcpAdapter(new GameboySerialServer(DemoRom.Create()), 0);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task server = adapter.RunAsync(cancellation.Token);
        try
        {
            using var client = new HttpClient();
            string address = $"http://127.0.0.1:{adapter.Port}/";
            Assert.AreEqual(DemoRom.Message, await client.GetStringAsync(address));
            Assert.AreEqual(DemoRom.Message, await client.GetStringAsync(address));
            using var missing = await client.GetAsync(address + "missing");
            Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
            using var prime = await client.GetAsync(address + "api/primes/10");
            Assert.AreEqual(HttpStatusCode.OK, prime.StatusCode);
            Assert.AreEqual("application/json", prime.Content.Headers.ContentType?.MediaType);
            Assert.AreEqual("{\"n\":10,\"prime\":29}\n", await prime.Content.ReadAsStringAsync());
            Assert.AreEqual("{\"n\":55,\"prime\":257}\n", await client.GetStringAsync(address + "api/primes/55"));
            using var invalid = await client.GetAsync(address + "api/primes/6543");
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        finally
        {
            await cancellation.CancelAsync();
            await server;
        }
    }

    [TestMethod]
    public async Task WorkerPoolDistributesRequestsRoundRobin()
    {
        await using var pool = new GameboyWorkerPool(3);

        Assert.AreEqual(257, await pool.GetPrimeAsync(55, CancellationToken.None));
        Assert.AreEqual(263, await pool.GetPrimeAsync(56, CancellationToken.None));
        Assert.AreEqual(269, await pool.GetPrimeAsync(57, CancellationToken.None));
        Assert.AreEqual(DemoRom.Message, await pool.GetPageAsync(CancellationToken.None));

        CollectionAssert.AreEqual(new long[] { 2, 1, 1 }, pool.AssignmentCounts.ToArray());
    }

    [TestMethod]
    public async Task WorkerPoolServesConcurrentHttpRequests()
    {
        await using var pool = new GameboyWorkerPool(2);
        var adapter = new VirtualSerialTcpAdapter(pool, 0);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task listener = adapter.RunAsync(cancellation.Token);
        try
        {
            using var client = new HttpClient();
            string address = $"http://127.0.0.1:{adapter.Port}/api/primes/";
            Task<string>[] requests = { client.GetStringAsync(address + "55"), client.GetStringAsync(address + "56"),
                client.GetStringAsync(address + "57"), client.GetStringAsync(address + "58") };
            string[] responses = await Task.WhenAll(requests);

            CollectionAssert.AreEqual(new[] { "{\"n\":55,\"prime\":257}\n", "{\"n\":56,\"prime\":263}\n",
                "{\"n\":57,\"prime\":269}\n", "{\"n\":58,\"prime\":271}\n" }, responses);
            CollectionAssert.AreEqual(new long[] { 2, 2 }, pool.AssignmentCounts.ToArray());
        }
        finally
        {
            await cancellation.CancelAsync();
            await listener;
        }
    }
}
