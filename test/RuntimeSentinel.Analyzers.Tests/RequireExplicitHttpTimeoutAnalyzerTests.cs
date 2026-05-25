using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RuntimeSentinel.Analyzers.Tests;

public class RequireExplicitHttpTimeoutAnalyzerTests
{
	[Fact]
	public async Task Should_Report_Diagnostic_When_HttpCall_Has_No_Timeout_And_No_CancellationToken()
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
        await client.GetAsync("https://example.com");
    }
}
""";

		var diagnostics = await GetDiagnosticsAsync(source);
		Assert.Contains(diagnostics, d => d.Id == RequireExplicitHttpTimeoutAnalyzer.DiagnosticId);
	}

	[Fact]
	public async Task Should_Not_Report_Diagnostic_When_ClientTimeout_Is_Configured_Before_Call()
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
        client.Timeout = TimeSpan.FromSeconds(2);
        await client.GetAsync("https://example.com");
    }
}
""";

		var diagnostics = await GetDiagnosticsAsync(source);
		Assert.DoesNotContain(diagnostics, d => d.Id == RequireExplicitHttpTimeoutAnalyzer.DiagnosticId);
	}

	[Fact]
	public async Task Should_Not_Report_Diagnostic_When_CancellationToken_Is_Passed()
	{
		const string source = """
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

class Worker
{
    public async Task FetchAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        await client.GetAsync("https://example.com", cancellationToken);
    }
}
""";

		var diagnostics = await GetDiagnosticsAsync(source);
		Assert.DoesNotContain(diagnostics, d => d.Id == RequireExplicitHttpTimeoutAnalyzer.DiagnosticId);
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

		var analyzer = new RequireExplicitHttpTimeoutAnalyzer();
		var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(analyzer);
		var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);

		return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
	}
}
