using GB.Compiler;
using GB.Sdk;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

namespace GB.Compiler.Tests;

[TestClass]
public sealed class CompilerFoundationTests
{
    [TestMethod]
    public void SyntaxErrorIncludesSourceLocation()
    {
        var errors = SourceValidator.Validate([("broken.cs", "static class Game { static void Start() { int x = ; } }")]);

        Assert.IsNotEmpty(errors);
        Assert.IsTrue(errors.Any(error => SourceValidator.Format(error).StartsWith("broken.cs(1,51): error CS", StringComparison.Ordinal)),
            string.Join(Environment.NewLine, errors.Select(SourceValidator.Format)));
    }

    [TestMethod]
    public void SemanticErrorIncludesSourceLocation()
    {
        var errors = SourceValidator.Validate([("semantic.cs", "static class Game { static void Start() { Missing(); } }")]);

        Assert.IsTrue(errors.Any(error => SourceValidator.Format(error).StartsWith("semantic.cs(1,43): error CS0103:", StringComparison.Ordinal)),
            string.Join(Environment.NewLine, errors.Select(SourceValidator.Format)));
    }

    [TestMethod]
    public async Task MissingToolsAreReportedIndividually()
    {
        var issues = await new RgbdsProbe(new FakeRunner((tool, _) => throw new FileNotFoundException(tool))).CheckAsync();

        Assert.HasCount(3, issues);
        Assert.IsTrue(issues.All(issue => issue.Contains("GBT001") && issue.Contains("0.9.0") && issue.Contains("PATH")));
        Assert.IsTrue(issues.Any(issue => issue.Contains("rgbfix")));
    }

    [TestMethod]
    public async Task IncompatibleAndFailingToolsAreReported()
    {
        var issues = await new RgbdsProbe(new FakeRunner((tool, argument) =>
        {
            Assert.AreEqual("--version", argument);
            return tool switch
            {
                "rgbasm" => new ToolResult(0, "rgbasm v0.9.0", ""),
                "rgblink" => new ToolResult(0, "rgblink v0.9.1", ""),
                _ => new ToolResult(2, "", "execution failed")
            };
        })).CheckAsync();

        Assert.HasCount(2, issues);
        StringAssert.Contains(issues[0], "GBT002: rgblink");
        StringAssert.Contains(issues[1], "GBT003: rgbfix");
        StringAssert.Contains(issues[1], "execution failed");
    }

    [TestMethod]
    public void SdkEntryPointAttributeBindsWithoutExecutingGameCode()
    {
        var errors = SourceValidator.Validate([("game.cs", "using GB.Sdk; static class Game { [GameEntryPoint] public static void Start() {} }")]);

        Assert.IsEmpty(errors, string.Join(Environment.NewLine, errors.Select(SourceValidator.Format)));
    }

    [TestMethod]
    public void SdkIntrinsicCannotSilentlyExecuteOnHost()
    {
        Assert.ThrowsExactly<PlatformNotSupportedException>(() => GameMemory.WriteByte(0xC000, 1));
    }

    [TestMethod]
    public async Task PrereleaseVersionDoesNotCountAsPinnedRelease()
    {
        var issues = await new RgbdsProbe(new FakeRunner((tool, _) => new ToolResult(0, $"{tool} v0.9.0-preview", ""))).CheckAsync();

        Assert.HasCount(3, issues);
        Assert.IsTrue(issues.All(issue => issue.StartsWith("GBT002:", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ReportedVersionTakesPrecedenceOverMentionedPinnedVersion()
    {
        var issues = await new RgbdsProbe(new FakeRunner((tool, _) =>
            new ToolResult(0, $"{tool} v0.9.1 (requires 0.9.0)", ""))).CheckAsync();

        Assert.HasCount(3, issues);
        Assert.IsTrue(issues.All(issue => issue.StartsWith("GBT002:", StringComparison.Ordinal)
            && issue.Contains("v0.9.1 (requires 0.9.0)", StringComparison.Ordinal)));
    }

    private sealed class FakeRunner(Func<string, string, ToolResult> run) : IToolRunner
    {
        public Task<ToolResult> RunAsync(string executable, string argument, CancellationToken cancellationToken = default)
            => Task.FromResult(run(executable, argument));
    }
}
