using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RuntimeSentinel.Analyzers.Tests;

public class AvoidPerRequestHttpClientInstantiationAnalyzerTests
{
	[Fact]
	public async Task Should_Report_Diagnostic_When_HttpClient_Is_Instantiated_Inside_Method()
	{
		const string source = """
using System.Net.Http;

class Worker
{
    public string Fetch()
    {
        var client = new HttpClient();
        return client.GetType().Name;
    }
}
""";

		var diagnostics = await GetDiagnosticsAsync(source);

		Assert.Contains(diagnostics, d => d.Id == AvoidPerRequestHttpClientInstantiationAnalyzer.DiagnosticId);
	}

	[Fact]
	public async Task Should_Not_Report_Diagnostic_When_HttpClient_Is_Static_Field()
	{
		const string source = """
using System.Net.Http;

class Worker
{
    private static readonly HttpClient Client = new HttpClient();

    public string Fetch()
    {
        return Client.GetType().Name;
    }
}
""";

		var diagnostics = await GetDiagnosticsAsync(source);

		Assert.DoesNotContain(diagnostics, d => d.Id == AvoidPerRequestHttpClientInstantiationAnalyzer.DiagnosticId);
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

		var analyzer = new AvoidPerRequestHttpClientInstantiationAnalyzer();
		var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(analyzer);
		var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);

		return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
	}
}
