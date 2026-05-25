using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RuntimeSentinel.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidRetryWithoutExponentialBackoffAnalyzer : DiagnosticAnalyzer
{
	public const string DiagnosticId = "RS1009";

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
		title: new LocalizableResourceString(nameof(Resources.RS1009_Title), Resources.ResourceManager, typeof(Resources)),
		messageFormat: new LocalizableResourceString(nameof(Resources.RS1009_MessageFormat), Resources.ResourceManager, typeof(Resources)),
		category: "Communication",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: new LocalizableResourceString(nameof(Resources.RS1009_Description), Resources.ResourceManager, typeof(Resources)));

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
		if (context.Node is WhileStatementSyntax loop)
		{
			AnalyzeLoop(context, loop.Statement, loop.GetLocation());
		}
	}

	private static void AnalyzeForLoop(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is ForStatementSyntax loop)
		{
			AnalyzeLoop(context, loop.Statement, loop.GetLocation());
		}
	}

	private static void AnalyzeDoWhileLoop(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is DoStatementSyntax loop)
		{
			AnalyzeLoop(context, loop.Statement, loop.GetLocation());
		}
	}

	private static void AnalyzeLoop(SyntaxNodeAnalysisContext context, StatementSyntax loopBody, Location location)
	{
		if (!ContainsHttpCall(loopBody, context.SemanticModel))
		{
			return;
		}

		if (!TryGetRetryDelayExpression(loopBody, context.SemanticModel, out var delayExpression))
		{
			return;
		}

		if (!IsFixedDelayExpression(delayExpression, context.SemanticModel))
		{
			return;
		}

		context.ReportDiagnostic(Diagnostic.Create(Rule, location));
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

	private static bool TryGetRetryDelayExpression(
		SyntaxNode loopBody,
		SemanticModel semanticModel,
		out ExpressionSyntax delayExpression)
	{
		delayExpression = null!;

		foreach (var invocation in loopBody.DescendantNodes().OfType<InvocationExpressionSyntax>())
		{
			var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
			if (symbol is null)
			{
				continue;
			}

			var containingType = symbol.ContainingType.ToDisplayString();
			var methodName = symbol.Name;
			var isDelay = (containingType == "System.Threading.Tasks.Task" && methodName == "Delay")
				|| (containingType == "System.Threading.Thread" && methodName == "Sleep");

			if (!isDelay || invocation.ArgumentList.Arguments.Count == 0)
			{
				continue;
			}

			delayExpression = invocation.ArgumentList.Arguments[0].Expression;
			return true;
		}

		return false;
	}

	private static bool IsFixedDelayExpression(ExpressionSyntax expression, SemanticModel semanticModel)
	{
		if (semanticModel.GetConstantValue(expression) is { HasValue: true })
		{
			return true;
		}

		if (expression is InvocationExpressionSyntax invocation
			&& invocation.Expression is MemberAccessExpressionSyntax memberAccess)
		{
			var containingType = memberAccess.Expression.ToString();
			var methodName = memberAccess.Name.Identifier.ValueText;
			var isTimeSpanFactory = containingType == "TimeSpan"
				&& (methodName == "FromMilliseconds" || methodName == "FromSeconds");

			if (isTimeSpanFactory && invocation.ArgumentList.Arguments.Count > 0)
			{
				var arg = invocation.ArgumentList.Arguments[0].Expression;
				if (semanticModel.GetConstantValue(arg) is { HasValue: true })
				{
					return true;
				}
			}
		}

		return false;
	}
}
