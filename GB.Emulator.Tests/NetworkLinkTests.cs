using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using GB.Emulator.Core;
using GB.Emulator.Core.InputOutput;
using GB.Emulator.Link;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Tests;

[TestClass]
public class NetworkLinkTests
{
    [TestMethod]
    public async Task RandomPoolAssignsOnlyReadySameRevisionPlayersAndNeverSharesOne()
    {
        await using var server = new LinkLobbyServer(port: 0);
        string hash = new('A', 64);
        await using var botA = await LinkLobbyClient.ConnectAsync("127.0.0.1", server.Port, "CPU A", "TETRIS", hash, autoMatch: false);
        await using var botB = await LinkLobbyClient.ConnectAsync("127.0.0.1", server.Port, "CPU B", "TETRIS", hash, autoMatch: false);
        await using var wrong = await LinkLobbyClient.ConnectAsync("127.0.0.1", server.Port, "Other revision", "TETRIS", new string('B', 64));
        await using var a = await LinkLobbyClient.ConnectAsync("127.0.0.1", server.Port, "Alice", "TETRIS", hash);
        Assert.AreEqual(Guid.Empty, a.State!.Session, "Booting bots must not be offered to humans.");
        server.RegisterAutomatedPlayer(botA.State!.Self); server.RegisterAutomatedPlayer(botB.State!.Self);
        await Until(() => a.State!.Session != Guid.Empty);
        await using var b = await LinkLobbyClient.ConnectAsync("127.0.0.1", server.Port, "Bob", "TETRIS", hash);
        await Until(() => b.State!.Session != Guid.Empty);
        Assert.IsTrue(b.Peer.IsConnected, "A pairing in the initial hello must initialise the cable without another broadcast.");
        Guid Partner(LinkLobbyClient c) => c.State!.Players.Single(p => p.Id == c.State.Self).Partner;
        Assert.AreNotEqual(Partner(a), Partner(b));
        Assert.IsTrue(a.State!.Players.Single(p => p.Id == Partner(a)).Automated);
        Assert.AreEqual(Guid.Empty, wrong.State!.Session, "A different ROM revision must not receive a bot.");
        await using var waiting = await LinkLobbyClient.ConnectAsync("127.0.0.1", server.Port, "Waiting", "TETRIS", hash);
        await Until(() => waiting.State!.Players.Length == 6);
        Assert.AreEqual(Guid.Empty, waiting.State!.Session, "The full pool cannot share a running Game Boy.");
        Guid retired = Partner(a);
        a.Unlink();
        await Until(() => !waiting.State!.Players.Any(p => p.Id == retired));
        Assert.AreEqual(Guid.Empty, waiting.State!.Session, "A used machine must be reset before returning to the pool.");
        await using var replacement = await LinkLobbyClient.ConnectAsync("127.0.0.1", server.Port, "Fresh CPU", "TETRIS", hash, autoMatch: false);
        server.RegisterAutomatedPlayer(replacement.State!.Self);
        await Until(() => waiting.State!.Session != Guid.Empty);
        Assert.AreEqual(replacement.State.Self, Partner(waiting));
        Assert.AreEqual(Guid.Empty, a.State!.Session, "Unplugging must not immediately reconnect the human.");
    }

    [TestMethod]
    public async Task BotsCannotBeSelectedByNameButAnOptOutClientCanRequestRandomMatching()
    {
        await using var server = new LinkLobbyServer(port: 0);
        await using var bot = await LinkLobbyClient.ConnectAsync("127.0.0.1", server.Port, "CPU", "TETRIS", new string('0', 64), autoMatch: false);
        server.RegisterAutomatedPlayer(bot.State!.Self);
        await using var human = await LinkLobbyClient.ConnectAsync("127.0.0.1", server.Port, "Human", "TETRIS", new string('0', 64), autoMatch: false);
        human.Invite(bot.State.Self); await Until(() => human.Error != null);
        Assert.AreEqual(Guid.Empty, human.State!.Session);
        human.FindOpponent(); await Until(() => human.State!.Session != Guid.Empty);
        Assert.IsNull(human.Error);
    }

    private static async Task Until(Func<bool> predicate)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        while (!predicate() && started.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(5);
        Assert.IsTrue(predicate(), "The bounded link operation did not complete.");
    }
    private static Task<LinkLobbyClient> Join(LinkLobbyServer server, string name, string title = "LINK DEMO") =>
        LinkLobbyClient.ConnectAsync("127.0.0.1", server.Port, name, title, new string('0', 64));
    private static async Task Pair(LinkLobbyClient a, LinkLobbyClient b)
    {
        await Until(() => a.State!.Players.Length >= 2 && b.State!.Players.Length >= 2);
        a.Invite(b.State!.Self);
        await Until(() => b.State!.Players.Single(p => p.Id == b.State.Self).InviteFrom == a.State!.Self);
        Assert.AreEqual(Guid.Empty, a.State!.Session, "An invitation must not attach a cable without consent.");
        b.Accept(a.State.Self);
        await Until(() => a.State!.Session != Guid.Empty && a.State.Session == b.State!.Session && a.Peer.IsConnected && b.Peer.IsConnected);
    }
    [TestMethod]
    public async Task RomHeaderCreatesSeparateRoomsAndPairingRequiresConsent()
    {
        byte[] header = new byte[Header.Length]; Encoding.ASCII.GetBytes("LINK DEMO").CopyTo(header, 0x34);
        await using var server = new LinkLobbyServer(port: 0);
        await using var a = await Join(server, "Alice", Header.FromBytes(header).Title);
        await using var b = await Join(server, "Bob", "link demo");
        await using var c = await Join(server, "Carol", "OTHER GAME");
        await Until(() => a.State!.Players.Length == 3);
        a.Invite(c.State!.Self); await Until(() => a.Error != null);
        Assert.AreEqual(Guid.Empty, c.State!.Session);
        await Pair(a, b);
        Assert.AreEqual(2, a.State!.Players.Count(p => p.Game == "LINK DEMO"));
        Assert.AreEqual(Guid.Empty, c.State!.Session);
        c.Invite(a.State.Self); await Until(() => c.Error != null);
        Assert.AreEqual(a.State.Session, b.State!.Session);
    }
    [TestMethod]
    public async Task NetworkCablePreservesByteOrderInternalClockAndBothInterrupts()
    {
        await using var server = new LinkLobbyServer(port: 0);
        await using var a = await Join(server, "A"); await using var b = await Join(server, "B"); await Pair(a, b);
        var master = new SerialPort { Peer = a.Peer }; var slave = new SerialPort { Peer = b.Peer };
        int masterInterrupts = 0, slaveInterrupts = 0;
        master.InterruptRequested += (_, _) => masterInterrupts++; slave.InterruptRequested += (_, _) => slaveInterrupts++;
        for (int i = 0; i < 16; i++)
        {
            byte x = (byte)(17 + i), y = (byte)(221 - i);
            slave.Write8(0xFF01, y); slave.Write8(0xFF02, 0x80);
            master.Write8(0xFF01, x); master.Write8(0xFF02, 0x81);
            master.Step(4095); Assert.IsTrue(master.IsTransferPending);
            await Until(() => !a.Peer.IsWaiting && !b.Peer.IsWaiting);
            master.Step(1); slave.Pump();
            Assert.AreEqual(y, master.Read8(0xFF01)); Assert.AreEqual(x, slave.Read8(0xFF01));
            Assert.IsFalse(master.IsTransferPending); Assert.IsFalse(slave.IsTransferPending);
            Assert.AreEqual(i + 1, masterInterrupts); Assert.AreEqual(i + 1, slaveInterrupts);
        }
        Assert.AreEqual(16L, a.BytesExchanged); Assert.AreEqual(16L, b.BytesExchanged);
        Assert.AreEqual(new LinkTrafficStatistics(16, 16), a.Traffic);
        Assert.AreEqual(new LinkTrafficStatistics(16, 16), b.Traffic);
    }
    [TestMethod]
    public async Task ExternalClocksWaitAndDisconnectUnparksTheMasterWithPullupData()
    {
        await using var server = new LinkLobbyServer(port: 0);
        await using var a = await Join(server, "A"); await using var b = await Join(server, "B"); await Pair(a, b);
        var master = new SerialPort { Peer = a.Peer }; var slave = new SerialPort { Peer = b.Peer };
        master.Write8(0xFF02, 0x80); slave.Write8(0xFF02, 0x80);
        master.Step(8192); slave.Step(8192); await Task.Delay(30);
        Assert.IsTrue(master.IsTransferPending); Assert.IsTrue(slave.IsTransferPending);
        Assert.IsFalse(master.IsWaitingForPeer, "An external-clock transfer must leave the CPU free to read menus and joypad input.");
        Assert.AreEqual(default(LinkTrafficStatistics), a.Traffic, "An armed/cancelled request is not an acknowledged serial byte.");
        Assert.AreEqual(default(LinkTrafficStatistics), b.Traffic);
        master.Write8(0xFF02, 0x81); master.Step(4096);
        await Until(() => !a.Peer.IsWaiting); master.Pump(); slave.Pump();
        master.Write8(0xFF02, 0x81); master.Step(4096);
        Assert.IsTrue(master.IsWaitingForPeer);
        await b.DisposeAsync(); await Until(() => a.State!.Session == Guid.Empty);
        Assert.IsFalse(master.IsWaitingForPeer); master.Step(0);
        Assert.AreEqual((byte)255, master.Read8(0xFF01)); Assert.IsFalse(master.IsTransferPending);
    }
    [TestMethod]
    public async Task TwoNativeSm83ProgramsExchangeTheirOwnRegistersThroughTcp()
    {
        await using var server = new LinkLobbyServer(port: 0);
        await using var a = await Join(server, "A"); await using var b = await Join(server, "B"); await Pair(a, b);
        Gameboy Native(byte value, byte clock)
        {
            byte[] rom = new byte[32768]; rom[0x100] = 0xC3; rom[0x101] = 0x50; rom[0x102] = 1;
            byte[] code = [0x3E, value, 0xE0, 1, 0x3E, clock, 0xE0, 2, 0xF0, 2, 0xCB, 0x7F, 0x20, 0xFA, 0xF0, 1, 0xEA, 0, 0xC0, 0x3E, 1, 0xEA, 1, 0xC0, 0x76];
            code.CopyTo(rom, 0x150); var machine = new Gameboy();
            machine.Load(new Cartridge { Data = rom, Header = Header.FromBytes(rom.AsSpan(0x100, Header.Length).ToArray()) }); return machine;
        }
        var master = Native(0x35, 0x81); var slave = Native(0xC9, 0x80); master.Serial.Peer = a.Peer; slave.Serial.Peer = b.Peer;
        await Until(() =>
        {
            master.RunForCycles(512); slave.RunForCycles(512);
            return master.Memory.Peek(0xC001) == 1 && slave.Memory.Peek(0xC001) == 1;
        });
        Assert.AreEqual((byte)0xC9, master.Memory.Peek(0xC000)); Assert.AreEqual((byte)0x35, slave.Memory.Peek(0xC000));
        Assert.AreEqual((byte)8, (byte)(master.Memory.Peek(0xFF0F) & 8)); Assert.AreEqual((byte)8, (byte)(slave.Memory.Peek(0xFF0F) & 8));
    }
    [TestMethod]
    public async Task ReArmingAnExternalClockDoesNotDiscardAReplyAlreadyInFlight()
    {
        await using var server = new LinkLobbyServer(port: 0);
        await using var a = await Join(server, "A"); await using var b = await Join(server, "B"); await Pair(a, b);
        var master = new SerialPort { Peer = a.Peer }; var slave = new SerialPort { Peer = b.Peer };
        slave.Write8(0xFF01, 0x55); slave.Write8(0xFF02, 0x80);
        master.Write8(0xFF01, 0x29); master.Write8(0xFF02, 0x81); master.Step(4096);
        await Until(() => !a.Peer.IsWaiting && !b.Peer.IsWaiting);
        // Reply arrives between the guest's instruction-boundary pump and its next SC write.
        slave.Write8(0xFF02, 0x80);
        slave.Pump(); master.Pump();
        Assert.IsFalse(slave.IsTransferPending, "Re-arming the same external transfer lost the matched reply.");
        Assert.AreEqual((byte)0x29, slave.Read8(0xFF01));
        Assert.AreEqual((byte)0x55, master.Read8(0xFF01));
    }

    [TestMethod]
    public async Task ExternalClockArmingDoesNotFreezeTheGuestBeforeItReadsItsMenu()
    {
        await using var server = new LinkLobbyServer(port: 0);
        await using var a = await Join(server, "A"); await using var b = await Join(server, "B"); await Pair(a, b);
        byte[] rom = new byte[32768];
        rom[0x100] = 0xC3; rom[0x101] = 0x50; rom[0x102] = 1;
        // Arm external SC, then execute guest work without a master supplying clocks.
        byte[] code = [0x3E, 0x80, 0xE0, 2, 0x3E, 0x7B, 0xEA, 0, 0xC0, 0x76];
        code.CopyTo(rom, 0x150);
        var machine = new Gameboy();
        machine.Load(new Cartridge { Data = rom, Header = Header.FromBytes(rom.AsSpan(0x100, Header.Length).ToArray()) });
        machine.Serial.Peer = a.Peer;
        machine.RunForCycles(8192);
        Assert.AreEqual((byte)0x7B, machine.Memory.Peek(0xC000));
        Assert.IsTrue(machine.Serial.IsTransferPending);
        Assert.IsFalse(machine.Serial.IsWaitingForPeer);
    }

    [TestMethod]
    public async Task HealthProbeDoesNotCreatePlayersAndPrivateLobbiesRequireTheToken()
    {
        await using var server = new LinkLobbyServer(port: 0, options: new(AccessToken: "test-only-token"));
        Assert.IsTrue(await LinkLobbyProbe.CheckAsync("127.0.0.1", server.Port, accessToken: "test-only-token"));
        await Assert.ThrowsExactlyAsync<IOException>(async () => await LinkLobbyClient.ConnectAsync("127.0.0.1", server.Port, "A", "LINK DEMO", new string('0', 64)));
        await using var a = await LinkLobbyClient.ConnectAsync("127.0.0.1", server.Port, "A", "LINK DEMO", new string('0', 64), accessToken: "test-only-token");
        Assert.HasCount(1, a.State!.Players);
        Assert.IsTrue(await LinkLobbyProbe.CheckAsync("127.0.0.1", server.Port, accessToken: "test-only-token"));
        Assert.HasCount(1, a.State.Players);
    }
    [TestMethod]
    public async Task TlsProbePinsTheConfiguredCertificateAndOrdinaryClientsRejectUntrustedCertificates()
    {
        using var key = System.Security.Cryptography.RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=localhost", key, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var serverCertificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(certificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx), null,
            OperatingSystem.IsWindows() ? System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.UserKeySet : System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.EphemeralKeySet);
        await using var server = new LinkLobbyServer(port: 0, options: new(serverCertificate, "tls-test-token"));
        Assert.IsTrue(await LinkLobbyProbe.CheckAsync("127.0.0.1", server.Port, true, "tls-test-token", certificate));
        await Assert.ThrowsExactlyAsync<System.Security.Authentication.AuthenticationException>(async () => await LinkLobbyClient.ConnectAsync("localhost", server.Port, "A", "LINK DEMO", new string('0', 64), useTls: true, accessToken: "tls-test-token"));
    }
    [TestMethod]
    public async Task MalformedConnectionCannotDisruptOtherPlayersAndServerShutdownDisconnectsThem()
    {
        await using var server = new LinkLobbyServer(port: 0);
        await using var a = await Join(server, "A");
        using (var bad = new TcpClient())
        {
            await bad.ConnectAsync(IPAddress.Loopback, server.Port);
            await bad.GetStream().WriteAsync(new byte[] { 0x7F, 0xFF, 0xFF, 0xFF });
            using var deadline = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
            Assert.AreEqual(0, await bad.GetStream().ReadAsync(new byte[1], deadline.Token));
        }
        Assert.IsTrue(a.IsConnected);
        await server.DisposeAsync(); await Until(() => !a.IsConnected);
    }
}
