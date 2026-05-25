using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RuntimeSentinel.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidUnboundedRetryLoopAnalyzer : DiagnosticAnalyzer
{
	public const string DiagnosticId = "RS1008";

	private static readonly HashSet<string> SupportedHttpMethods = new(StringComparer.Ordinal)
	{
		"GetAsync",
		"PostAsync",
		"PutAsync",
		"DeleteAsync",
		"SendAsync",
		"GetStringAsync",
		"GetByteArrayAsync",
		"GetStreamAsync",
		"PatchAsync"
	};

	private static readonly DiagnosticDescriptor Rule = new(
		id: DiagnosticId,
		title: new LocalizableResourceString(nameof(Resources.RS1008_Title), Resources.ResourceManager, typeof(Resources)),
		messageFormat: new LocalizableResourceString(nameof(Resources.RS1008_MessageFormat), Resources.ResourceManager, typeof(Resources)),
		category: "Communication",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: new LocalizableResourceString(nameof(Resources.RS1008_Description), Resources.ResourceManager, typeof(Resources)));

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSyntaxNodeAction(AnalyzeWhileLoop, SyntaxKind.WhileStatement);
		context.RegisterSyntaxNodeAction(AnalyzeForLoop, SyntaxKind.ForStatement);
		context.RegisterSyntaxNodeAction(AnalyzeDoWhileLoop, SyntaxKind.DoStatement);
	}

	private static void AnalyzeWhileLoop(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not WhileStatementSyntax loop)
		{
			return;
		}

		if (!IsInfiniteCondition(loop.Condition))
		{
			return;
		}

		AnalyzeLoopCore(context, loop.Statement, loop.GetLocation());
	}

	private static void AnalyzeForLoop(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not ForStatementSyntax loop)
		{
			return;
		}

		if (loop.Condition is not null)
		{
			return;
		}

		AnalyzeLoopCore(context, loop.Statement, loop.GetLocation());
	}

	private static void AnalyzeDoWhileLoop(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not DoStatementSyntax loop)
		{
			return;
		}

		if (!IsInfiniteCondition(loop.Condition))
		{
			return;
		}

		AnalyzeLoopCore(context, loop.Statement, loop.GetLocation());
	}

	private static void AnalyzeLoopCore(SyntaxNodeAnalysisContext context, StatementSyntax loopBody, Location diagnosticLocation)
	{
		if (ContainsBreakOrReturn(loopBody))
		{
			return;
		}

		if (!ContainsRetrySignal(loopBody))
		{
			return;
		}

		if (!ContainsHttpCall(loopBody, context.SemanticModel))
		{
			return;
		}

		context.ReportDiagnostic(Diagnostic.Create(Rule, diagnosticLocation));
	}

	private static bool IsInfiniteCondition(ExpressionSyntax condition)
	{
		return condition.IsKind(SyntaxKind.TrueLiteralExpression);
	}

	private static bool ContainsBreakOrReturn(SyntaxNode loopBody)
	{
		return loopBody.DescendantNodes().Any(n =>
			n.IsKind(SyntaxKind.BreakStatement)
			|| n.IsKind(SyntaxKind.ReturnStatement)
			|| n.IsKind(SyntaxKind.ThrowStatement));
	}

	private static bool ContainsRetrySignal(SyntaxNode loopBody)
	{
		foreach (var invocation in loopBody.DescendantNodes().OfType<InvocationExpressionSyntax>())
		{
			if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
			{
				continue;
			}

			var containingTypeName = memberAccess.Expression.ToString();
			var methodName = memberAccess.Name.Identifier.ValueText;
			if ((containingTypeName == "Task" && methodName == "Delay")
				|| (containingTypeName == "Thread" && methodName == "Sleep"))
			{
				return true;
			}
		}

		return false;
	}

	private static bool ContainsHttpCall(SyntaxNode loopBody, SemanticModel semanticModel)
	{
		foreach (var invocation in loopBody.DescendantNodes().OfType<InvocationExpressionSyntax>())
		{
			var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
			if (symbol is null)
			{
				continue;
			}

			if (symbol.ContainingType.ToDisplayString() == "System.Net.Http.HttpClient"
				&& SupportedHttpMethods.Contains(symbol.Name))
			{
				return true;
			}
		}

		return false;
	}
}
