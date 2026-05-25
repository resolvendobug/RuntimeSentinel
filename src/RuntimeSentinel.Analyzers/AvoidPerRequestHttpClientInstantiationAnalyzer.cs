using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RuntimeSentinel.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidPerRequestHttpClientInstantiationAnalyzer : DiagnosticAnalyzer
{
	public const string DiagnosticId = "RS1006";

	private static readonly DiagnosticDescriptor Rule = new(
		id: DiagnosticId,
		title: new LocalizableResourceString(nameof(Resources.RS1006_Title), Resources.ResourceManager, typeof(Resources)),
		messageFormat: new LocalizableResourceString(nameof(Resources.RS1006_MessageFormat), Resources.ResourceManager, typeof(Resources)),
		category: "Communication",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: new LocalizableResourceString(nameof(Resources.RS1006_Description), Resources.ResourceManager, typeof(Resources)));

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
	}

	private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not ObjectCreationExpressionSyntax objectCreation)
		{
			return;
		}

		if (!IsInsideMethodLikeScope(objectCreation))
		{
			return;
		}

		var typeInfo = context.SemanticModel.GetTypeInfo(objectCreation);
		if (typeInfo.Type?.ToDisplayString() != "System.Net.Http.HttpClient")
		{
			return;
		}

		var diagnostic = Diagnostic.Create(Rule, objectCreation.GetLocation());
		context.ReportDiagnostic(diagnostic);
	}

	private static bool IsInsideMethodLikeScope(SyntaxNode node)
	{
		return node.FirstAncestorOrSelf<MethodDeclarationSyntax>() is not null
			|| node.FirstAncestorOrSelf<LocalFunctionStatementSyntax>() is not null
			|| node.FirstAncestorOrSelf<SimpleLambdaExpressionSyntax>() is not null
			|| node.FirstAncestorOrSelf<ParenthesizedLambdaExpressionSyntax>() is not null
			|| node.FirstAncestorOrSelf<AnonymousMethodExpressionSyntax>() is not null;
	}
}