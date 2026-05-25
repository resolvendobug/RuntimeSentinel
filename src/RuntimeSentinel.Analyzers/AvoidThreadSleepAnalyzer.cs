using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RuntimeSentinel.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidThreadSleepAnalyzer : DiagnosticAnalyzer
{
	public const string DiagnosticId = "RS1001";

	private static readonly DiagnosticDescriptor Rule = new(
		id: DiagnosticId,
		title: new LocalizableResourceString(nameof(Resources.RS1001_Title), Resources.ResourceManager, typeof(Resources)),
		messageFormat: new LocalizableResourceString(nameof(Resources.RS1001_MessageFormat), Resources.ResourceManager, typeof(Resources)),
		category: "Concurrency",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: new LocalizableResourceString(nameof(Resources.RS1001_Description), Resources.ResourceManager, typeof(Resources)));

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
	}

	private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not InvocationExpressionSyntax invocation)
		{
			return;
		}

		var symbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
		if (symbol is null)
		{
			return;
		}

		if (symbol.Name != "Sleep")
		{
			return;
		}

		if (symbol.ContainingType.ToDisplayString() != "System.Threading.Thread")
		{
			return;
		}

		if (!IsInsideAsyncMethod(invocation))
		{
			return;
		}

		var diagnostic = Diagnostic.Create(Rule, invocation.GetLocation());
		context.ReportDiagnostic(diagnostic);
	}

	private static bool IsInsideAsyncMethod(SyntaxNode invocation)
	{
		var methodDeclaration = invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
		if (methodDeclaration is not null)
		{
			return methodDeclaration.Modifiers.Any(SyntaxKind.AsyncKeyword);
		}

		var localFunction = invocation.Ancestors().OfType<LocalFunctionStatementSyntax>().FirstOrDefault();
		if (localFunction is not null)
		{
			return localFunction.Modifiers.Any(SyntaxKind.AsyncKeyword);
		}

		return false;
	}
}
