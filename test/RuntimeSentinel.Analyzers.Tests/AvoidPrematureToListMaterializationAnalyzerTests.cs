using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RuntimeSentinel.Analyzers.Tests;

public class AvoidPrematureToListMaterializationAnalyzerTests
{
	[Fact]
	public async Task Should_Report_Diagnostic_When_ToList_Materializes_Unbounded_Pipeline()
	{
		const string source = """
using System.Linq;

class Worker
{
    public void Run()
    {
        var items = Enumerable.Range(1, 10000)
            .Where(x => x % 2 == 0)
            .Select(x => x * 2)
            .ToList();

        _ = items;
    }
}
""";

		var diagnostics = await GetDiagnosticsAsync(source);

		Assert.Contains(diagnostics, d => d.Id == AvoidPrematureToListMaterializationAnalyzer.DiagnosticId);
	}

	[Fact]
	public async Task Should_Not_Report_Diagnostic_When_ToList_Has_Explicit_Take_Limit()
	{
		const string source = """
using System.Linq;

class Worker
{
    public void Run()
    {
        var items = Enumerable.Range(1, 10000)
            .Where(x => x % 2 == 0)
            .Take(50)
            .ToList();

        _ = items;
    }
}
""";

		var diagnostics = await GetDiagnosticsAsync(source);

		Assert.DoesNotContain(diagnostics, d => d.Id == AvoidPrematureToListMaterializationAnalyzer.DiagnosticId);
	}

	[Fact]
	public async Task Should_Report_Diagnostic_When_ToList_Uses_Take_Above_Default_Limit()
	{
		const string source = """
using System.Linq;

class Worker
{
    public void Run()
    {
        var items = Enumerable.Range(1, 10000)
            .Where(x => x % 2 == 0)
            .Take(500)
            .ToList();

        _ = items;
    }
}
""";

		var diagnostics = await GetDiagnosticsAsync(source);

		Assert.Contains(diagnostics, d => d.Id == AvoidPrematureToListMaterializationAnalyzer.DiagnosticId);
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

		var analyzer = new AvoidPrematureToListMaterializationAnalyzer();
		var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(analyzer);
		var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);

		return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
	}
}