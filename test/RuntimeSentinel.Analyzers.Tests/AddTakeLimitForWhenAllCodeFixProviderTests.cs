using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using RuntimeSentinel.CodeFixes;

namespace RuntimeSentinel.Analyzers.Tests;

public class AddTakeLimitForWhenAllCodeFixProviderTests
{
    [Fact]
    public async Task Should_Add_Take_Before_Select_For_RS1002_Diagnostic()
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

        var document = CreateDocument(source);
        var diagnostic = await GetSingleDiagnosticAsync(document, new AvoidUnboundedWhenAllAnalyzer(), AvoidUnboundedWhenAllAnalyzer.DiagnosticId);

        var provider = new AddTakeLimitForWhenAllCodeFixProvider();
        var actions = await GetCodeActionsAsync(document, diagnostic, provider);

        Assert.NotEmpty(actions);
        Assert.Contains(actions, a => a.Title.Contains("Take(100)"));
        Assert.Contains(actions, a => a.Title.Contains("SemaphoreSlim(10)"));

        var takeAction = actions.First(a => a.Title.Contains("Take(100)"));
        var updatedSource = await ApplyFixAsync(document, takeAction);

        Assert.Contains(".Take(100)", updatedSource);
    }

    [Fact]
    public async Task Should_Add_SemaphoreSlim_Pattern_For_RS1002_Diagnostic()
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

        var document = CreateDocument(source);
        var diagnostic = await GetSingleDiagnosticAsync(document, new AvoidUnboundedWhenAllAnalyzer(), AvoidUnboundedWhenAllAnalyzer.DiagnosticId);

        var provider = new AddTakeLimitForWhenAllCodeFixProvider();
        var actions = await GetCodeActionsAsync(document, diagnostic, provider);

        var semaphoreAction = actions.First(a => a.Title.Contains("SemaphoreSlim(10)"));
        var updatedSource = await ApplyFixAsync(document, semaphoreAction);

        Assert.Contains("new System.Threading.SemaphoreSlim(10)", updatedSource);
        Assert.Contains("await semaphore.WaitAsync();", updatedSource);
        Assert.Contains("semaphore.Release();", updatedSource);
    }

    [Fact]
    public async Task Should_Not_Offer_CodeFix_When_Diagnostic_Node_Is_Not_WhenAll()
    {
        const string source = """
using System;

class Worker
{
    public void Run()
    {
        Console.WriteLine("hello");
    }
}
""";

        var document = CreateDocument(source);
        var root = await document.GetSyntaxRootAsync();
        Assert.NotNull(root);

        var invocation = root!.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>().First();
        var fakeDiagnostic = Diagnostic.Create(
            new DiagnosticDescriptor(
                id: AvoidUnboundedWhenAllAnalyzer.DiagnosticId,
                title: "Fake",
                messageFormat: "Fake",
                category: "Test",
                defaultSeverity: DiagnosticSeverity.Warning,
                isEnabledByDefault: true),
            invocation.GetLocation());

        var provider = new AddTakeLimitForWhenAllCodeFixProvider();
        var actions = await GetCodeActionsAsync(document, fakeDiagnostic, provider);

        Assert.Empty(actions);
    }

    private static async Task<Diagnostic> GetSingleDiagnosticAsync(Document document, DiagnosticAnalyzer analyzer, string diagnosticId)
    {
        var compilation = await document.Project.GetCompilationAsync();
        Assert.NotNull(compilation);

        var compilationWithAnalyzers = compilation!.WithAnalyzers(ImmutableArray.Create(analyzer));
        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

        return diagnostics.Single(d => d.Id == diagnosticId);
    }

    private static async Task<List<CodeAction>> GetCodeActionsAsync(Document document, Diagnostic diagnostic, CodeFixProvider provider)
    {
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None);

        await provider.RegisterCodeFixesAsync(context);
        return actions;
    }

    private static async Task<string> ApplyFixAsync(Document document, CodeAction action)
    {
        var operations = await action.GetOperationsAsync(CancellationToken.None);
        var applyChanges = operations.OfType<ApplyChangesOperation>().Single();

        var updatedDocument = applyChanges.ChangedSolution.GetDocument(document.Id);
        Assert.NotNull(updatedDocument);

        var updatedText = await updatedDocument!.GetTextAsync();
        return updatedText.ToString();
    }

    private static Document CreateDocument(string source)
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();

        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        var references = tpa!
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

        var solution = workspace.CurrentSolution
            .AddProject(projectId, "CodeFixTests", "CodeFixTests", LanguageNames.CSharp)
            .WithProjectCompilationOptions(projectId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithProjectParseOptions(projectId, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest));

        foreach (var reference in references)
        {
            solution = solution.AddMetadataReference(projectId, reference);
        }

        var documentId = DocumentId.CreateNewId(projectId);
        solution = solution.AddDocument(documentId, "Test.cs", source);

        return solution.GetDocument(documentId)!;
    }
}
