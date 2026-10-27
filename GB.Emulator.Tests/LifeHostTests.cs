using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GB.Experiments.NetworkCartridge;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Tests;

[TestClass]
public class LifeHostTests
{
    [TestMethod]
    public void PlacementRejectsWrongCountsPortCollisionsAndShellSyntax()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new LifeHostConfiguration([new(null, null, 4)], PasswordEnvironmentVariable: "not an environment name").Validate(4));
        new LifeHostConfiguration([new(null, null, 2), new("gb-two", "/opt/sharpboy/GB.Experiments", 2)]).Validate(4);
        foreach (var hosts in new LifeHost[][]
        {
            [], [new(null, null, 3)], [new(null, null, 4, 0)],
            [new("-oProxyCommand=bad", "/opt/worker", 4)],
            [new("gb-two", "/opt/worker;touch-bad", 4)],
            [new("gb-two", "$(bad)", 4)], [new("gb-two", "C:\\Program Files\\worker.exe", 4)],
            [new("gb-two", "/opt/worker", 4, FirstPort: 65534)],
            [new("gb-two", "/opt/worker", 2), new("gb-two", "/opt/worker", 2)],
        }) Assert.ThrowsExactly<ArgumentException>(() => new LifeHostConfiguration(hosts).Validate(4));
        Assert.ThrowsExactly<ArgumentException>(() => new LifeHostConfiguration([new("gb-two", "/opt/worker", 1000)]).Validate(1000));
        new LifeHostConfiguration([new("gb-two", "/opt/worker", 1000, 8)]).Validate(1000);
    }

    [TestMethod]
    public async Task ExplicitPlacementPreservesNativeResultsInspectionAndProcessIdentity()
    {
        string file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new LifeHostConfiguration([new(null, null, 2), new(null, null, 2)])));
            var settings = LifeSettings.Parse(["--columns", "2", "--rows", "2", "--generations", "3", "--tick-delay-ms", "0", "--hosts", file]);
            var snapshots = new ConcurrentDictionary<ushort, NativeNodeSnapshot>();
            var placed = await LifeCluster.RunAsync(settings, onSnapshot: s => snapshots[s.NodeId] = s);
            var local = await LifeCluster.RunAsync(settings with { HostsFile = null, WorkerProcesses = 0 });
            CollectionAssert.AreEqual(local.Final.Cells.ToArray(), placed.Final.Cells.ToArray());
            Assert.HasCount(2, placed.Metrics!.Locations!);
            Assert.HasCount(2, placed.Metrics.WorkerProcessIds);
            foreach (var location in placed.Metrics.Locations!)
            {
                Assert.AreEqual(Environment.MachineName, location.Host); Assert.IsFalse(location.Remote);
                Assert.IsTrue(snapshots.Values.Any(s => s.ProcessId == location.ProcessId && s.HostName == location.Host));
                Assert.ThrowsExactly<ArgumentException>(() => Process.GetProcessById(location.ProcessId));
            }
            Assert.IsTrue(snapshots.Values.All(s => s.State == "Completed"));
        }
        finally { File.Delete(file); }
    }

    [TestMethod]
    public void HostConfigurationResolvesSshConfigRelativeToJsonAndRejectsUnknownFields()
    {
        string file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, """{"hosts":[{"nodes":4}],"sshConfigFile":"ssh.config"}""");
            var config = LifeHostConfiguration.Load(file, 4);
            Assert.AreEqual(Path.Combine(Path.GetDirectoryName(file)!, "ssh.config"), config.SshConfigFile);
            File.WriteAllText(file, """{"hosts":[{"nodes":4,"proceses":2}]}""");
            Assert.ThrowsExactly<JsonException>(() => LifeHostConfiguration.Load(file, 4));
        }
        finally { File.Delete(file); }
    }

    [TestMethod]
    public async Task MissingPasswordEnvironmentFailsWithoutLaunchingOrWaitingForAWorker()
    {
        string file = Path.GetTempFileName();
        try
        {
            string variable = "LIFE_TEST_UNSET_" + Guid.NewGuid().ToString("N");
            File.WriteAllText(file, JsonSerializer.Serialize(new LifeHostConfiguration(
                [new("example.invalid", "/opt/worker", 1)], PasswordEnvironmentVariable: variable)));
            var error = await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
                await LifeCluster.RunAsync(new(1, 1, 1, 0, HostsFile: file)).WaitAsync(TimeSpan.FromSeconds(10)));
            StringAssert.Contains(error.Message, "password environment variable is not set");
        }
        finally { File.Delete(file); }
    }

    [TestMethod]
    public async Task MismatchedFirmwareFailsBeforeStartingAnyNativeMachines()
    {
        string assembly = typeof(LifeWorkerGroup).Assembly.Location;
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(assembly); start.ArgumentList.Add("--life-worker-group");
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteLineAsync("""{"Nodes":[{"NodeId":1,"Port":12345}],"Frames":false,"Inspection":false,"DisplayNodes":[],"FirmwareSha256":"different"}""");
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            Assert.AreEqual(1, process.ExitCode);
            StringAssert.Contains(await error, "firmware differs"); Assert.AreEqual("", await output);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }
}
