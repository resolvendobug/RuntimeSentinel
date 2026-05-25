using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RuntimeSentinel.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidRetryWithoutJitterAnalyzer : DiagnosticAnalyzer
{
	public const string DiagnosticId = "RS1010";

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
		title: new LocalizableResourceString(nameof(Resources.RS1010_Title), Resources.ResourceManager, typeof(Resources)),
		messageFormat: new LocalizableResourceString(nameof(Resources.RS1010_MessageFormat), Resources.ResourceManager, typeof(Resources)),
		category: "Communication",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: new LocalizableResourceString(nameof(Resources.RS1010_Description), Resources.ResourceManager, typeof(Resources)));

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

		if (!ContainsExponentialSignal(delayExpression))
		{
			return;
		}

		if (ContainsJitterSignal(delayExpression, context.SemanticModel))
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

	private static bool ContainsExponentialSignal(ExpressionSyntax delayExpression)
	{
		var text = delayExpression.ToString();
		return text.Contains("Math.Pow", StringComparison.Ordinal)
			|| text.Contains("<<", StringComparison.Ordinal);
	}

	private static bool ContainsJitterSignal(ExpressionSyntax delayExpression, SemanticModel semanticModel)
	{
		var delayText = delayExpression.ToString();
		if (delayText.Contains("Random", StringComparison.Ordinal)
			|| delayText.Contains("jitter", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		foreach (var invocation in delayExpression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
		{
			var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
			if (symbol is null)
			{
				continue;
			}

			if (symbol.ContainingType.ToDisplayString() == "System.Random"
				&& symbol.Name.StartsWith("Next", StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}
}
