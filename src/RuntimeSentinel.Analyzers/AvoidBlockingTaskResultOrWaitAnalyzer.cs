using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RuntimeSentinel.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidBlockingTaskResultOrWaitAnalyzer : DiagnosticAnalyzer
{
	public const string DiagnosticId = "RS1004";

	private static readonly DiagnosticDescriptor Rule = new(
		id: DiagnosticId,
		title: new LocalizableResourceString(nameof(Resources.RS1004_Title), Resources.ResourceManager, typeof(Resources)),
		messageFormat: new LocalizableResourceString(nameof(Resources.RS1004_MessageFormat), Resources.ResourceManager, typeof(Resources)),
		category: "Async",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: new LocalizableResourceString(nameof(Resources.RS1004_Description), Resources.ResourceManager, typeof(Resources)));

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
		context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
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

		if (symbol.Name != "Wait")
		{
			return;
		}

		if (!IsTaskType(symbol.ContainingType))
		{
			return;
		}

		context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
	}

	private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not MemberAccessExpressionSyntax memberAccess)
		{
			return;
		}

		if (memberAccess.Name.Identifier.ValueText != "Result")
		{
			return;
		}

		var symbol = context.SemanticModel.GetSymbolInfo(memberAccess).Symbol as IPropertySymbol;
		if (symbol is null)
		{
			return;
		}

		if (!IsTaskType(symbol.ContainingType))
		{
			return;
		}

		context.ReportDiagnostic(Diagnostic.Create(Rule, memberAccess.GetLocation()));
	}

	private static bool IsTaskType(INamedTypeSymbol? typeSymbol)
	{
		if (typeSymbol is null)
		{
			return false;
		}

		return typeSymbol.ToDisplayString() == "System.Threading.Tasks.Task"
			|| typeSymbol.OriginalDefinition.ToDisplayString() == "System.Threading.Tasks.Task<TResult>";
	}
}