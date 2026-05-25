using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RuntimeSentinel.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RequireExplicitHttpTimeoutAnalyzer : DiagnosticAnalyzer
{
	public const string DiagnosticId = "RS1007";

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
		title: new LocalizableResourceString(nameof(Resources.RS1007_Title), Resources.ResourceManager, typeof(Resources)),
		messageFormat: new LocalizableResourceString(nameof(Resources.RS1007_MessageFormat), Resources.ResourceManager, typeof(Resources)),
		category: "Communication",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: new LocalizableResourceString(nameof(Resources.RS1007_Description), Resources.ResourceManager, typeof(Resources)));

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

		if (!IsSupportedHttpCall(invocation, context.SemanticModel, out var methodSymbol)
			|| methodSymbol is null)
		{
			return;
		}

		if (HasCancellationTokenArgument(invocation, methodSymbol))
		{
			return;
		}

		if (!TryGetReceiverSymbol(invocation, context.SemanticModel, out var receiverSymbol))
		{
			return;
		}

		if (receiverSymbol is not ILocalSymbol and not IParameterSymbol)
		{
			return;
		}

		if (HasTimeoutConfiguredBeforeCall(invocation, receiverSymbol, context.SemanticModel))
		{
			return;
		}

		context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
	}

	private static bool IsSupportedHttpCall(
		InvocationExpressionSyntax invocation,
		SemanticModel semanticModel,
		out IMethodSymbol? methodSymbol)
	{
		var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
		if (symbol is null)
		{
			methodSymbol = null;
			return false;
		}

		methodSymbol = symbol;

		return methodSymbol.ContainingType.ToDisplayString() == "System.Net.Http.HttpClient"
			&& SupportedHttpMethods.Contains(methodSymbol.Name);
	}

	private static bool HasCancellationTokenArgument(InvocationExpressionSyntax invocation, IMethodSymbol methodSymbol)
	{
		for (var i = 0; i < invocation.ArgumentList.Arguments.Count; i++)
		{
			var argument = invocation.ArgumentList.Arguments[i];
			IParameterSymbol? parameter = null;

			if (argument.NameColon is not null)
			{
				parameter = methodSymbol.Parameters.FirstOrDefault(p => p.Name == argument.NameColon.Name.Identifier.ValueText);
			}
			else if (i < methodSymbol.Parameters.Length)
			{
				parameter = methodSymbol.Parameters[i];
			}

			if (parameter?.Type.ToDisplayString() == "System.Threading.CancellationToken")
			{
				return true;
			}
		}

		return false;
	}

	private static bool TryGetReceiverSymbol(
		InvocationExpressionSyntax invocation,
		SemanticModel semanticModel,
		out ISymbol? receiverSymbol)
	{
		receiverSymbol = null;

		if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
		{
			return false;
		}

		receiverSymbol = semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol;
		return receiverSymbol is not null;
	}

	private static bool HasTimeoutConfiguredBeforeCall(
		InvocationExpressionSyntax invocation,
		ISymbol receiverSymbol,
		SemanticModel semanticModel)
	{
		var scope = (SyntaxNode?)invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>()
			?? invocation.FirstAncestorOrSelf<LocalFunctionStatementSyntax>();

		if (scope is null)
		{
			return false;
		}

		if (HasTimeoutInReceiverInitializer(scope, invocation, receiverSymbol))
		{
			return true;
		}

		foreach (var assignment in scope.DescendantNodes().OfType<AssignmentExpressionSyntax>())
		{
			if (assignment.SpanStart >= invocation.SpanStart)
			{
				continue;
			}

			if (!IsTimeoutAssignmentForReceiver(assignment, receiverSymbol, semanticModel))
			{
				continue;
			}

			return true;
		}

		return false;
	}

	private static bool HasTimeoutInReceiverInitializer(
		SyntaxNode scope,
		InvocationExpressionSyntax invocation,
		ISymbol receiverSymbol)
	{
		foreach (var local in scope.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
		{
			if (local.SpanStart >= invocation.SpanStart)
			{
				continue;
			}

			foreach (var variable in local.Declaration.Variables)
			{
				if (variable.Identifier.ValueText != receiverSymbol.Name)
				{
					continue;
				}

				if (variable.Initializer?.Value is not ObjectCreationExpressionSyntax objectCreation
					|| objectCreation.Initializer is null)
				{
					continue;
				}

				if (objectCreation.Initializer.Expressions
					.OfType<AssignmentExpressionSyntax>()
					.Any(a => a.Left is IdentifierNameSyntax id && id.Identifier.ValueText == "Timeout"))
				{
					return true;
				}
			}
		}

		return false;
	}

	private static bool IsTimeoutAssignmentForReceiver(
		AssignmentExpressionSyntax assignment,
		ISymbol receiverSymbol,
		SemanticModel semanticModel)
	{
		if (assignment.Left is not MemberAccessExpressionSyntax memberAccess)
		{
			return false;
		}

		if (memberAccess.Name.Identifier.ValueText != "Timeout")
		{
			return false;
		}

		var targetSymbol = semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol;
		if (targetSymbol is null)
		{
			return false;
		}

		return SymbolEqualityComparer.Default.Equals(targetSymbol, receiverSymbol);
	}
}
