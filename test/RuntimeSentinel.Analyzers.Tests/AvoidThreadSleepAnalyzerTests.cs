using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RuntimeSentinel.Analyzers.Tests;

public class AvoidThreadSleepAnalyzerTests
{
    [Fact]
    public async Task Should_Report_Diagnostic_When_ThreadSleep_Is_Used()
    {
        const string source = """
using System.Threading;
using System.Threading.Tasks;

class Worker
{
    public async Task RunAsync()
    {
        await Task.Delay(1);
        Thread.Sleep(100);
    }
}
""";

        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.Contains(diagnostics, d => d.Id == AvoidThreadSleepAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Should_Not_Report_Diagnostic_When_ThreadSleep_Is_Used_In_Sync_Method()
    {
        const string source = """
using System.Threading;

class Worker
{
    public void Run()
    {
        Thread.Sleep(100);
    }
}
""";

        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == AvoidThreadSleepAnalyzer.DiagnosticId);
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

        var analyzer = new AvoidThreadSleepAnalyzer();
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(analyzer);
        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
