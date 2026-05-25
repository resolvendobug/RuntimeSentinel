using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RuntimeSentinel.Analyzers.Tests;

public class AvoidUnboundedParallelForEachAnalyzerTests
{
	[Fact]
	public async Task Should_Report_Diagnostic_When_ParallelForEach_Has_No_ParallelOptions()
	{
		const string source = """
using System.Threading.Tasks;

class Worker
{
    public void Run(int[] items)
    {
        Parallel.ForEach(items, item =>
        {
            _ = item;
        });
    }
}
""";

		var diagnostics = await GetDiagnosticsAsync(source);

		Assert.Contains(diagnostics, d => d.Id == AvoidUnboundedParallelForEachAnalyzer.DiagnosticId);
	}

	[Fact]
	public async Task Should_Not_Report_Diagnostic_When_ParallelForEach_Uses_ParallelOptions()
	{
		const string source = """
using System.Threading.Tasks;

class Worker
{
    public void Run(int[] items)
    {
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = 4
        };

        Parallel.ForEach(items, options, item =>
        {
            _ = item;
        });
    }
}
""";

		var diagnostics = await GetDiagnosticsAsync(source);

		Assert.DoesNotContain(diagnostics, d => d.Id == AvoidUnboundedParallelForEachAnalyzer.DiagnosticId);
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

		var analyzer = new AvoidUnboundedParallelForEachAnalyzer();
		var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(analyzer);
		var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);

		return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
	}
}