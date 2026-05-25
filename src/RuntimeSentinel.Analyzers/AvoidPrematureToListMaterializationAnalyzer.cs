using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RuntimeSentinel.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidPrematureToListMaterializationAnalyzer : DiagnosticAnalyzer
{
	public const string DiagnosticId = "RS1005";
	private const string MaxMaterializationConfigKey = "runtimesentinel_rs1005_max_materialization";
	private const int DefaultMaxMaterialization = 200;

	private static readonly DiagnosticDescriptor Rule = new(
		id: DiagnosticId,
		title: new LocalizableResourceString(nameof(Resources.RS1005_Title), Resources.ResourceManager, typeof(Resources)),
		messageFormat: new LocalizableResourceString(nameof(Resources.RS1005_MessageFormat), Resources.ResourceManager, typeof(Resources)),
		category: "Memory",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: new LocalizableResourceString(nameof(Resources.RS1005_Description), Resources.ResourceManager, typeof(Resources)));

	private static readonly ImmutableHashSet<string> PipelineMethods =
		ImmutableHashSet.Create(StringComparer.Ordinal, "Where", "Select", "SelectMany", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending");

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

		if (symbol.Name != "ToList")
		{
			return;
		}

		if (!IsLinqType(symbol.ContainingType))
		{
			return;
		}

		if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
		{
			return;
		}

		var source = memberAccess.Expression;
		if (!ContainsPipelineMethods(source, context.SemanticModel))
		{
			return;
		}

		var maxMaterialization = GetConfiguredMaxMaterialization(context);
		var takeLimit = FindTakeLimit(source, context.SemanticModel);
		if (takeLimit is not null && takeLimit.Value <= maxMaterialization)
		{
			return;
		}

		context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
	}

	private static bool ContainsPipelineMethods(ExpressionSyntax expression, SemanticModel semanticModel)
	{
		if (expression is not InvocationExpressionSyntax invocation)
		{
			return false;
		}

		var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
		if (symbol is not null && IsLinqType(symbol.ContainingType) && PipelineMethods.Contains(symbol.Name))
		{
			return true;
		}

		if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
		{
			return ContainsPipelineMethods(memberAccess.Expression, semanticModel);
		}

		return false;
	}

	private static int GetConfiguredMaxMaterialization(SyntaxNodeAnalysisContext context)
	{
		var options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(context.Node.SyntaxTree);
		if (options.TryGetValue(MaxMaterializationConfigKey, out var configuredValue)
			&& int.TryParse(configuredValue, out var maxMaterialization)
			&& maxMaterialization > 0)
		{
			return maxMaterialization;
		}

		return DefaultMaxMaterialization;
	}

	private static int? FindTakeLimit(ExpressionSyntax expression, SemanticModel semanticModel)
	{
		if (expression is not InvocationExpressionSyntax invocation)
		{
			return null;
		}

		var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
		if (symbol is not null && IsLinqType(symbol.ContainingType) && symbol.Name == "Take")
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
			return FindTakeLimit(memberAccess.Expression, semanticModel);
		}

		return null;
	}

	private static bool IsLinqType(INamedTypeSymbol? containingType)
	{
		if (containingType is null)
		{
			return false;
		}

		var fullName = containingType.ToDisplayString();
		return fullName == "System.Linq.Enumerable" || fullName == "System.Linq.Queryable";
	}
}