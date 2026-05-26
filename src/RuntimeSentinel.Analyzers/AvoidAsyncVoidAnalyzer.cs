using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RuntimeSentinel.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidAsyncVoidAnalyzer : DiagnosticAnalyzer
{
	public const string DiagnosticId = "RS1011";

	private static readonly DiagnosticDescriptor Rule = new(
		id: DiagnosticId,
		title: new LocalizableResourceString(nameof(Resources.RS1011_Title), Resources.ResourceManager, typeof(Resources)),
		messageFormat: new LocalizableResourceString(nameof(Resources.RS1011_MessageFormat), Resources.ResourceManager, typeof(Resources)),
		category: "Async",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: new LocalizableResourceString(nameof(Resources.RS1011_Description), Resources.ResourceManager, typeof(Resources)));

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
	}

	private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not MethodDeclarationSyntax method)
		{
			return;
		}

		// Must have async modifier
		if (!method.Modifiers.Any(SyntaxKind.AsyncKeyword))
		{
			return;
		}

		// Return type must be void (syntactically)
		if (method.ReturnType is not PredefinedTypeSyntax predefined ||
			!predefined.Keyword.IsKind(SyntaxKind.VoidKeyword))
		{
			return;
		}

		// Allow event handlers: two parameters where the second derives from System.EventArgs
		if (IsEventHandler(method, context.SemanticModel))
		{
			return;
		}

		var methodName = method.Identifier.Text;
		var diagnostic = Diagnostic.Create(Rule, method.Identifier.GetLocation(), methodName);
		context.ReportDiagnostic(diagnostic);
	}

	private static bool IsEventHandler(MethodDeclarationSyntax method, SemanticModel semanticModel)
	{
		var parameters = method.ParameterList.Parameters;

		if (parameters.Count != 2)
		{
			return false;
		}

		var secondParam = parameters[1];
		if (secondParam.Type is null)
		{
			return false;
		}

		var typeInfo = semanticModel.GetTypeInfo(secondParam.Type);
		if (typeInfo.Type is null)
		{
			return false;
		}

		return InheritsFromEventArgs(typeInfo.Type);
	}

	private static bool InheritsFromEventArgs(ITypeSymbol type)
	{
		var current = type;
		while (current is not null)
		{
			if (current.ToDisplayString() == "System.EventArgs")
			{
				return true;
			}

			current = current.BaseType;
		}

		return false;
	}
}
