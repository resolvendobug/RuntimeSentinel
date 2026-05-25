using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RuntimeSentinel.Analyzers.Tests;

public class AvoidUnboundedWhenAllAnalyzerTests
{
	[Fact]
	public async Task Should_Report_Diagnostic_When_WhenAll_Uses_Unbounded_Select()
	{
		const string source = """
using System.Linq;
using System.Threading.Tasks;

class Worker
{
    public async Task RunAsync(int[] items)
    {
        await Task.WhenAll(items.Select(async item =>
        {
            await Task.Delay(1);
            _ = item;
        }));
    }
}
""";

		var diagnostics = await GetDiagnosticsAsync(source);

		Assert.Contains(diagnostics, d => d.Id == AvoidUnboundedWhenAllAnalyzer.DiagnosticId);
	}

	[Fact]
	public async Task Should_Not_Report_Diagnostic_When_WhenAll_Uses_Take_Before_Select()
	{
		const string source = """
using System.Linq;
using System.Threading.Tasks;

class Worker
{
    public async Task RunAsync(int[] items)
    {
        await Task.WhenAll(items
            .Take(10)
            .Select(async item =>
            {
                await Task.Delay(1);
                _ = item;
            }));
    }
}
""";

		var diagnostics = await GetDiagnosticsAsync(source);

		Assert.DoesNotContain(diagnostics, d => d.Id == AvoidUnboundedWhenAllAnalyzer.DiagnosticId);
	}

	[Fact]
	public async Task Should_Report_Diagnostic_When_WhenAll_Uses_Take_Above_Default_Limit()
	{
		const string source = """
using System.Linq;
using System.Threading.Tasks;

class Worker
{
	public async Task RunAsync(int[] items)
	{
		await Task.WhenAll(items
			.Take(1000)
			.Select(async item =>
			{
				await Task.Delay(1);
				_ = item;
			}));
	}
}
""";

		var diagnostics = await GetDiagnosticsAsync(source);

		Assert.Contains(diagnostics, d => d.Id == AvoidUnboundedWhenAllAnalyzer.DiagnosticId);
	}

	[Fact]
	public async Task Should_Not_Report_Diagnostic_When_WhenAll_Uses_Controlled_Batch_With_Chunk()
	{
		const string source = """
using System.Linq;
using System.Threading.Tasks;

class Worker
{
	public async Task RunAsync(int[] items)
	{
		await Task.WhenAll(items
			.Chunk(50)
			.Select(async batch =>
			{
				await Task.Delay(1);
				_ = batch.Length;
			}));
	}
}
""";

		var diagnostics = await GetDiagnosticsAsync(source);

		Assert.DoesNotContain(diagnostics, d => d.Id == AvoidUnboundedWhenAllAnalyzer.DiagnosticId);
	}

	[Fact]
	public async Task Should_Report_Diagnostic_When_WhenAll_Uses_Chunk_Above_Default_Limit()
	{
		const string source = """
using System.Linq;
using System.Threading.Tasks;

class Worker
{
	public async Task RunAsync(int[] items)
	{
		await Task.WhenAll(items
			.Chunk(500)
			.Select(async batch =>
			{
				await Task.Delay(1);
				_ = batch.Length;
			}));
	}
}
""";

		var diagnostics = await GetDiagnosticsAsync(source);

		Assert.Contains(diagnostics, d => d.Id == AvoidUnboundedWhenAllAnalyzer.DiagnosticId);
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

		var analyzer = new AvoidUnboundedWhenAllAnalyzer();
		var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(analyzer);
		var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);

		return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
	}
}