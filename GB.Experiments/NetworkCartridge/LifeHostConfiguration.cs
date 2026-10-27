using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GB.Experiments.NetworkCartridge;

/// <summary>Explicit placement. A null SSH destination keeps a group on the coordinator.</summary>
public sealed record LifeHost(string? Destination, string? Executable, int Nodes, int Processes = 1, int FirstPort = 20000);

public sealed record LifeHostConfiguration(LifeHost[] Hosts, string? SshConfigFile = null)
{
    public static LifeHostConfiguration Load(string path, int workers)
    {
        var result = JsonSerializer.Deserialize<LifeHostConfiguration>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
            ?? throw new ArgumentException("Empty Life hosts configuration.");
        if (result.SshConfigFile != null) result = result with { SshConfigFile = Path.GetFullPath(result.SshConfigFile, Path.GetDirectoryName(Path.GetFullPath(path))!) };
        result.Validate(workers); return result;
    }

    public void Validate(int workers)
    {
        if (Hosts == null || Hosts.Length is < 1 or > 32 || Hosts.Any(h => h == null))
            throw new ArgumentException("Specify 1-32 Life hosts.");
        if (Hosts.Any(h => h.Nodes is < 1 or > 1000 || h.Processes < 1 || h.Processes > h.Nodes) ||
            Hosts.Sum(h => (long)h.Nodes) != workers || Hosts.Sum(h => (long)h.Processes) > 32)
            throw new ArgumentException("Host nodes must total the grid size; use 1-32 process groups in total, at least one per host.");
        foreach (var host in Hosts)
        {
            if (host.Destination == null) continue;
            // SSH appends the remote command through a shell. Accept executable paths, never shell expressions.
            if (!Regex.IsMatch(host.Destination, @"\A[A-Za-z0-9_][A-Za-z0-9_.@-]*\z") ||
                string.IsNullOrEmpty(host.Executable) || !Regex.IsMatch(host.Executable, @"\A[A-Za-z0-9_/][A-Za-z0-9_./:\\-]*\z"))
                throw new ArgumentException("Use a user@host or SSH-config alias and an executable path without spaces or shell syntax.");
            if (host.FirstPort < 1024 || (long)host.FirstPort + host.Nodes - 1 > 65535)
                throw new ArgumentException("Remote loopback port ranges must fit within 1024-65535.");
            if ((host.Nodes + host.Processes - 1) / host.Processes > 128)
                throw new ArgumentException("Use enough remote process groups for at most 128 nodes per SSH session.");
        }
        foreach (var hosts in Hosts.Where(h => h.Destination != null).GroupBy(h => h.Destination))
        {
            var ranges = hosts.OrderBy(h => h.FirstPort).ToArray();
            for (int i = 1; i < ranges.Length; i++)
                if (ranges[i].FirstPort < ranges[i - 1].FirstPort + ranges[i - 1].Nodes)
                    throw new ArgumentException("Remote loopback port ranges overlap on the same host.");
        }
    }

    internal static ProcessStartInfo CreateSshStart(LifeHost host, LifeWorkerAssignment[] assignments, int offset, string? configFile)
    {
        var start = new ProcessStartInfo("ssh")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (configFile != null) { start.ArgumentList.Add("-F"); start.ArgumentList.Add(configFile); }
        foreach (string arg in new[] { "-T", "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=yes", "-o", "ExitOnForwardFailure=yes",
            "-o", "ConnectTimeout=10", "-o", "ServerAliveInterval=5", "-o", "ServerAliveCountMax=3" }) start.ArgumentList.Add(arg);
        for (int i = 0; i < assignments.Length; i++)
        {
            start.ArgumentList.Add("-R");
            start.ArgumentList.Add($"127.0.0.1:{host.FirstPort + offset + i}:127.0.0.1:{assignments[i].Port}");
        }
        start.ArgumentList.Add(host.Destination!);
        start.ArgumentList.Add(host.Executable!);
        start.ArgumentList.Add("--life-worker-group");
        return start;
    }
}
