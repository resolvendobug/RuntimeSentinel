using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RuntimeSentinel.Analyzers.Tests;

public class AvoidBlockingTaskResultOrWaitAnalyzerTests
{
	[Fact]
	public async Task Should_Report_Diagnostic_When_TaskWait_Is_Used()
	{
		const string source = """
using System.Threading.Tasks;

class Worker
{
    public void Run()
    {
        var task = Task.Delay(1);
        task.Wait();
    }
}
""";

		var diagnostics = await GetDiagnosticsAsync(source);

		Assert.Contains(diagnostics, d => d.Id == AvoidBlockingTaskResultOrWaitAnalyzer.DiagnosticId);
	}

	[Fact]
	public async Task Should_Report_Diagnostic_When_TaskResult_Is_Used()
	{
		const string source = """
using System.Threading.Tasks;

class Worker
{
    public int Run()
    {
        var task = Task.FromResult(42);
        return task.Result;
    }
}
""";

		var diagnostics = await GetDiagnosticsAsync(source);

		Assert.Contains(diagnostics, d => d.Id == AvoidBlockingTaskResultOrWaitAnalyzer.DiagnosticId);
	}

	[Fact]
	public async Task Should_Not_Report_Diagnostic_When_Await_Is_Used()
	{
		const string source = """
using System.Threading.Tasks;

class Worker
{
    public async Task<int> RunAsync()
    {
        await Task.Delay(1);
        return 42;
    }
}
""";

		var diagnostics = await GetDiagnosticsAsync(source);

		Assert.DoesNotContain(diagnostics, d => d.Id == AvoidBlockingTaskResultOrWaitAnalyzer.DiagnosticId);
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

		var analyzer = new AvoidBlockingTaskResultOrWaitAnalyzer();
		var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(analyzer);
		var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);

		return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
	}
}