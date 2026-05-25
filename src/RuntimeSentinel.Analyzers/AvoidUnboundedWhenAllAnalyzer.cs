using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RuntimeSentinel.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidUnboundedWhenAllAnalyzer : DiagnosticAnalyzer
{
	public const string DiagnosticId = "RS1002";
	private const string MaxFanoutConfigKey = "runtimesentinel_rs1002_max_fanout";
	private const int DefaultMaxFanout = 100;

	private static readonly DiagnosticDescriptor Rule = new(
		id: DiagnosticId,
		title: new LocalizableResourceString(nameof(Resources.RS1002_Title), Resources.ResourceManager, typeof(Resources)),
		messageFormat: new LocalizableResourceString(nameof(Resources.RS1002_MessageFormat), Resources.ResourceManager, typeof(Resources)),
		category: "Concurrency",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: new LocalizableResourceString(nameof(Resources.RS1002_Description), Resources.ResourceManager, typeof(Resources)));

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

		if (!IsTaskWhenAllInvocation(invocation, context.SemanticModel))
		{
			return;
		}

		if (invocation.ArgumentList.Arguments.Count == 0)
		{
			return;
		}

		var firstArg = invocation.ArgumentList.Arguments[0].Expression;
		if (!IsSelectProjection(firstArg, context.SemanticModel))
		{
			return;
		}

		var maxFanout = GetConfiguredMaxFanout(context);
		var batchLimit = FindBatchLimit(firstArg, context.SemanticModel);
		if (batchLimit is not null && batchLimit.Value <= maxFanout)
		{
			return;
		}

		var diagnostic = Diagnostic.Create(Rule, invocation.GetLocation());
		context.ReportDiagnostic(diagnostic);
	}

	private static bool IsTaskWhenAllInvocation(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
	{
		var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
		if (symbol is null)
		{
			return false;
		}

		return symbol.Name == "WhenAll"
			&& symbol.ContainingType.ToDisplayString() == "System.Threading.Tasks.Task";
	}

	private static bool IsSelectProjection(ExpressionSyntax expression, SemanticModel semanticModel)
	{
		if (expression is not InvocationExpressionSyntax selectInvocation)
		{
			return false;
		}

		var symbol = semanticModel.GetSymbolInfo(selectInvocation).Symbol as IMethodSymbol;
		if (symbol is null)
		{
			return false;
		}

		return symbol.Name == "Select"
			&& symbol.ContainingType.ToDisplayString() == "System.Linq.Enumerable";
	}

	private static int GetConfiguredMaxFanout(SyntaxNodeAnalysisContext context)
	{
		var options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(context.Node.SyntaxTree);
		if (options.TryGetValue(MaxFanoutConfigKey, out var configuredValue)
			&& int.TryParse(configuredValue, out var maxFanout)
			&& maxFanout > 0)
		{
			return maxFanout;
		}

		return DefaultMaxFanout;
	}

	private static int? FindBatchLimit(ExpressionSyntax expression, SemanticModel semanticModel)
	{
		if (expression is not InvocationExpressionSyntax selectInvocation
			|| selectInvocation.Expression is not MemberAccessExpressionSyntax memberAccess)
		{
			return null;
		}

		return FindBatchLimitInChain(memberAccess.Expression, semanticModel);
	}

	private static int? FindBatchLimitInChain(ExpressionSyntax expression, SemanticModel semanticModel)
	{
		if (expression is not InvocationExpressionSyntax invocation)
		{
			return null;
		}

		var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
		if (symbol is not null && IsBoundingMethod(symbol))
		{
			if (invocation.ArgumentList.Arguments.Count == 0)
			{
				return null;
			}

			var takeArgument = invocation.ArgumentList.Arguments[0].Expression;
			var constantValue = semanticModel.GetConstantValue(takeArgument);
			if (constantValue.HasValue && constantValue.Value is int intValue)
			{
				return intValue;
			}

			// Bound exists but cannot be evaluated statically; assume bounded.
			return 0;
		}

		if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
		{
			return FindBatchLimitInChain(memberAccess.Expression, semanticModel);
		}

		return null;
	}

	private static bool IsBoundingMethod(IMethodSymbol symbol)
	{
		var containingType = symbol.ContainingType.ToDisplayString();
		if (symbol.Name == "Take"
			&& (containingType == "System.Linq.Enumerable" || containingType == "System.Linq.Queryable"))
		{
			return true;
		}

		return symbol.Name == "Chunk" && containingType == "System.Linq.Enumerable";
	}
}