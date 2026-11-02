using System.Collections.Immutable;
using GB.Sdk;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GB.Compiler;

/// <summary>Only C# syntax and semantic binding; NOT a target-subset validator.</summary>
public static class SourceValidator
{
    public static IReadOnlyList<Diagnostic> Validate(IEnumerable<(string Path, string Text)> sources)
    {
        return CreateCompilation(sources).GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .OrderBy(diagnostic => diagnostic.Location.GetLineSpan().Path, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .ToArray();
    }

    internal static CSharpCompilation CreateCompilation(IEnumerable<(string Path, string Text)> sources)
    {
        var trees = sources.Select(source => CSharpSyntaxTree.ParseText(source.Text, path: source.Path)).ToArray();

        // Host reference assemblies are supplied for symbol binding only. A later validator must
        // reject calls to host libraries before the program could ever become a target ROM.
        var trustedAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Host .NET reference assemblies were not found.");
        var references = trustedAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(typeof(GameEntryPointAttribute).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToImmutableArray();

        var compilation = CSharpCompilation.Create(
            "GameSource", trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, checkOverflow: false));
        return compilation;
    }

    public static string Format(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        var location = diagnostic.Location.IsInSource
            ? $"{span.Path}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}): "
            : "";
        return $"{location}error {diagnostic.Id}: {diagnostic.GetMessage()}";
    }
}
