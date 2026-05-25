using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RuntimeSentinel.Analyzers.Tests;

public class AvoidRetryWithoutJitterAnalyzerTests
{
	[Fact]
	public async Task Should_Report_Diagnostic_When_Exponential_Backoff_Has_No_Jitter()
	{
		const string source = """
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

class Worker
{
    public async Task FetchAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                _ = await client.GetAsync("https://example.com", cancellationToken);
                break;
            }
            catch
            {
                await Task.Delay((int)Math.Pow(2, attempt) * 100);
            }
        }
    }
}
""";

		var diagnostics = await GetDiagnosticsAsync(source);
		Assert.Contains(diagnostics, d => d.Id == AvoidRetryWithoutJitterAnalyzer.DiagnosticId);
	}

	[Fact]
	public async Task Should_Not_Report_Diagnostic_When_Exponential_Backoff_Uses_Random_Jitter()
	{
		const string source = """
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

class Worker
{
    public async Task FetchAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        var random = new Random();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                _ = await client.GetAsync("https://example.com", cancellationToken);
                break;
            }
            catch
            {
                var jitter = random.Next(0, 50);
                await Task.Delay((int)Math.Pow(2, attempt) * 100 + jitter);
            }
        }
    }
}
""";

		var diagnostics = await GetDiagnosticsAsync(source);
		Assert.DoesNotContain(diagnostics, d => d.Id == AvoidRetryWithoutJitterAnalyzer.DiagnosticId);
	}

	[Fact]
	public async Task Should_Not_Report_Diagnostic_When_No_Http_Call_Is_Present()
	{
		const string source = """
using System;
using System.Threading.Tasks;

class Worker
{
    public async Task RunAsync()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await Task.Delay((int)Math.Pow(2, attempt) * 100);
        }
    }
}
""";

		var diagnostics = await GetDiagnosticsAsync(source);
		Assert.DoesNotContain(diagnostics, d => d.Id == AvoidRetryWithoutJitterAnalyzer.DiagnosticId);
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

		var analyzer = new AvoidRetryWithoutJitterAnalyzer();
		var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(analyzer);
		var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);

		return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
	}
}
