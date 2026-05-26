using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RuntimeSentinel.Analyzers.Tests;

public class AvoidStringConcatInLoopAnalyzerTests
{
    [Fact]
    public async Task Should_Report_Diagnostic_When_String_PlusEquals_Inside_For_Loop()
    {
        const string source = """
class Builder
{
    public string Build(string[] items)
    {
        var result = string.Empty;
        for (var i = 0; i < items.Length; i++)
        {
            result += items[i];
        }
        return result;
    }
}
""";

        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.Contains(diagnostics, d => d.Id == AvoidStringConcatInLoopAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Should_Report_Diagnostic_When_String_PlusEquals_Inside_ForEach_Loop()
    {
        const string source = """
using System.Collections.Generic;

class Builder
{
    public string Build(IEnumerable<string> items)
    {
        var result = string.Empty;
        foreach (var item in items)
        {
            result += item;
        }
        return result;
    }
}
""";

        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.Contains(diagnostics, d => d.Id == AvoidStringConcatInLoopAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Should_Report_Diagnostic_When_String_PlusEquals_Inside_While_Loop()
    {
        const string source = """
class Builder
{
    public string Build(string[] items)
    {
        var result = string.Empty;
        var i = 0;
        while (i < items.Length)
        {
            result += items[i];
            i++;
        }
        return result;
    }
}
""";

        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.Contains(diagnostics, d => d.Id == AvoidStringConcatInLoopAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Should_Report_Diagnostic_When_String_PlusEquals_Inside_DoWhile_Loop()
    {
        const string source = """
class Builder
{
    public string Build(string[] items)
    {
        var result = string.Empty;
        var i = 0;
        do
        {
            result += items[i];
            i++;
        }
        while (i < items.Length);
        return result;
    }
}
""";

        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.Contains(diagnostics, d => d.Id == AvoidStringConcatInLoopAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Should_Not_Report_Diagnostic_When_String_PlusEquals_Outside_Loop()
    {
        const string source = """
class Builder
{
    public string Build(string a, string b)
    {
        var result = string.Empty;
        result += a;
        result += b;
        return result;
    }
}
""";

        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == AvoidStringConcatInLoopAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Should_Not_Report_Diagnostic_When_Int_PlusEquals_Inside_Loop()
    {
        const string source = """
class Counter
{
    public int Sum(int[] values)
    {
        var total = 0;
        foreach (var v in values)
        {
            total += v;
        }
        return total;
    }
}
""";

        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == AvoidStringConcatInLoopAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Should_Not_Report_Diagnostic_When_StringBuilder_Is_Used_In_Loop()
    {
        const string source = """
using System.Text;
using System.Collections.Generic;

class Builder
{
    public string Build(IEnumerable<string> items)
    {
        var sb = new StringBuilder();
        foreach (var item in items)
        {
            sb.Append(item);
        }
        return sb.ToString();
    }
}
""";

        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == AvoidStringConcatInLoopAnalyzer.DiagnosticId);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        var references = trustedPlatformAssemblies!
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            assemblyName: "AnalyzerTests",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new AvoidStringConcatInLoopAnalyzer();
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(analyzer);
        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
