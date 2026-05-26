using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RuntimeSentinel.Analyzers.Tests;

public class AvoidAsyncVoidAnalyzerTests
{
    [Fact]
    public async Task Should_Report_Diagnostic_When_Async_Void_Used_Outside_Event_Handler()
    {
        const string source = """
using System.Threading.Tasks;

class Worker
{
    public async void DoWorkAsync()
    {
        await Task.Delay(1);
    }
}
""";

        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.Contains(diagnostics, d => d.Id == AvoidAsyncVoidAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Should_Not_Report_Diagnostic_When_Async_Task_Is_Used()
    {
        const string source = """
using System.Threading.Tasks;

class Worker
{
    public async Task DoWorkAsync()
    {
        await Task.Delay(1);
    }
}
""";

        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == AvoidAsyncVoidAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Should_Not_Report_Diagnostic_When_Method_Is_Event_Handler()
    {
        const string source = """
using System;
using System.Threading.Tasks;

class MyForm
{
    public async void Button_Click(object sender, EventArgs e)
    {
        await Task.Delay(1);
    }
}
""";

        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == AvoidAsyncVoidAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Should_Not_Report_Diagnostic_When_Method_Is_Event_Handler_With_Custom_EventArgs()
    {
        const string source = """
using System;
using System.Threading.Tasks;

class DataReceivedEventArgs : EventArgs
{
    public string? Data { get; set; }
}

class MyService
{
    public async void OnDataReceived(object sender, DataReceivedEventArgs e)
    {
        await Task.Delay(1);
    }
}
""";

        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == AvoidAsyncVoidAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Should_Not_Report_Diagnostic_When_Void_Method_Is_Not_Async()
    {
        const string source = """
class Worker
{
    public void DoWork()
    {
    }
}
""";

        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == AvoidAsyncVoidAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Should_Report_Diagnostic_For_Private_Async_Void()
    {
        const string source = """
using System.Threading.Tasks;

class Worker
{
    private async void FireAndForget()
    {
        await Task.Delay(1);
    }
}
""";

        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.Contains(diagnostics, d => d.Id == AvoidAsyncVoidAnalyzer.DiagnosticId);
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

        var analyzer = new AvoidAsyncVoidAnalyzer();
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(analyzer);
        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
