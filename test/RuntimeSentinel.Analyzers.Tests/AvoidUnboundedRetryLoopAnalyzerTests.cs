using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RuntimeSentinel.Analyzers.Tests;

public class AvoidUnboundedRetryLoopAnalyzerTests
{
	[Fact]
	public async Task Should_Report_Diagnostic_For_Infinite_Retry_Loop_With_Http_Call()
	{
		const string source = """
using System;
using System.Net.Http;
using System.Threading.Tasks;

class Worker
{
    public async Task FetchAsync()
    {
        using var client = new HttpClient();

        while (true)
        {
            try
            {
                await client.GetAsync("https://example.com");
            }
            catch
            {
                await Task.Delay(100);
            }
        }
    }
}
""";

		var diagnostics = await GetDiagnosticsAsync(source);
		Assert.Contains(diagnostics, d => d.Id == AvoidUnboundedRetryLoopAnalyzer.DiagnosticId);
	}

	[Fact]
	public async Task Should_Not_Report_Diagnostic_For_Bounded_Retry_Loop()
	{
		const string source = """
using System;
using System.Net.Http;
using System.Threading.Tasks;

class Worker
{
    public async Task FetchAsync()
    {
        using var client = new HttpClient();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await client.GetAsync("https://example.com");
                break;
            }
            catch
            {
                await Task.Delay(100);
            }
        }
    }
}
""";

		var diagnostics = await GetDiagnosticsAsync(source);
		Assert.DoesNotContain(diagnostics, d => d.Id == AvoidUnboundedRetryLoopAnalyzer.DiagnosticId);
	}

	[Fact]
	public async Task Should_Not_Report_Diagnostic_When_No_Http_Call_Is_Present()
	{
		const string source = """
using System.Threading.Tasks;

class Worker
{
    public async Task RunAsync()
    {
        while (true)
        {
            await Task.Delay(100);
            break;
        }
    }
}
""";

		var diagnostics = await GetDiagnosticsAsync(source);
		Assert.DoesNotContain(diagnostics, d => d.Id == AvoidUnboundedRetryLoopAnalyzer.DiagnosticId);
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

		var analyzer = new AvoidUnboundedRetryLoopAnalyzer();
		var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(analyzer);
		var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);

		return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
	}
}
