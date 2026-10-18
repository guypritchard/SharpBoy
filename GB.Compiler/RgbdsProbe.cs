using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace GB.Compiler;

public sealed record ToolResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Injectable process boundary; tools are never invoked through a shell.</summary>
public interface IToolRunner
{
    Task<ToolResult> RunAsync(string executable, string argument, CancellationToken cancellationToken = default);
    Task<ToolResult> RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory,
        CancellationToken cancellationToken = default) =>
        arguments.Count == 1 ? RunAsync(executable, arguments[0], cancellationToken) :
        throw new NotSupportedException("This tool runner must implement argument-list execution for builds.");
}

public sealed class SystemToolRunner : IToolRunner
{
    public Task<ToolResult> RunAsync(string executable, string argument, CancellationToken cancellationToken = default) =>
        RunAsync(executable, [argument], Environment.CurrentDirectory, cancellationToken);

    public async Task<ToolResult> RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.StartInfo.WorkingDirectory = workingDirectory;
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        try
        {
            process.Start();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
        {
            throw new FileNotFoundException($"{executable} was not found (check --rgbds-bin or PATH).", executable, ex);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return new ToolResult(process.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{executable} {string.Join(' ', arguments)} timed out after 10 seconds.");
        }
        catch
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }
    }
}

public sealed class RgbdsProbe(IToolRunner runner, string? binDirectory = null)
{
    public const string RequiredVersion = "0.9.0";
    private static readonly string[] Tools = ["rgbasm", "rgblink", "rgbfix"];
    private static readonly Version PinnedVersion = Version.Parse(RequiredVersion);
    private static readonly Regex VersionPattern = new(
        @"(?<![\w.])(?<tool>rgbasm|rgblink|rgbfix)\s+v?(?<version>\d+\.\d+\.\d+)(?<prerelease>-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?(?![\w.+-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>GBT001 missing, GBT002 incompatible, GBT003 execution failure.</summary>
    public async Task<IReadOnlyList<string>> CheckAsync(CancellationToken cancellationToken = default)
    {
        var issues = new List<string>();
        var locationHint = binDirectory is null ? "PATH (or specify --rgbds-bin)" :
            $"--rgbds-bin directory '{binDirectory}'";
        foreach (var tool in Tools)
        {
            try
            {
                var executable = Resolve(tool, binDirectory);
                var result = binDirectory is null
                    ? await runner.RunAsync(executable, "--version", cancellationToken)
                    : await runner.RunAsync(executable, ["--version"], binDirectory, cancellationToken);
                var output = string.Join(" ", result.StandardOutput.Trim(), result.StandardError.Trim()).Trim();
                if (result.ExitCode != 0)
                    issues.Add($"GBT003: {tool} --version exited with code {result.ExitCode}: {output}. Check the RGBDS installation and {locationHint}.");
                else if (!ReportsPinnedVersion(tool, output))
                    issues.Add($"GBT002: {tool} reports '{output}' instead of RGBDS {RequiredVersion}. Install RGBDS {RequiredVersion} and check {locationHint}.");
            }
            catch (FileNotFoundException)
            {
                issues.Add($"GBT001: {tool} is missing. Install RGBDS {RequiredVersion} and check {locationHint}.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or TimeoutException or OperationCanceledException)
            {
                issues.Add($"GBT003: Cannot run {tool} --version: {ex.Message}. Check the RGBDS {RequiredVersion} installation and {locationHint}.");
            }
        }
        return issues;
    }

    internal static string Resolve(string tool, string? binDirectory) => binDirectory is null ? tool :
        Path.Combine(Path.GetFullPath(binDirectory), tool + (OperatingSystem.IsWindows() ? ".exe" : ""));

    private static bool ReportsPinnedVersion(string tool, string output)
    {
        foreach (Match match in VersionPattern.Matches(output))
        {
            if (string.Equals(match.Groups["tool"].Value, tool, StringComparison.OrdinalIgnoreCase))
                return !match.Groups["prerelease"].Success
                    && Version.TryParse(match.Groups["version"].Value, out var reportedVersion)
                    && reportedVersion == PinnedVersion;
        }

        return false;
    }
}
