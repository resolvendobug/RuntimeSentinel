using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RuntimeSentinel.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidStringConcatInLoopAnalyzer : DiagnosticAnalyzer
{
	public const string DiagnosticId = "RS1012";

	private static readonly DiagnosticDescriptor Rule = new(
		id: DiagnosticId,
		title: new LocalizableResourceString(nameof(Resources.RS1012_Title), Resources.ResourceManager, typeof(Resources)),
		messageFormat: new LocalizableResourceString(nameof(Resources.RS1012_MessageFormat), Resources.ResourceManager, typeof(Resources)),
		category: "Memory",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: new LocalizableResourceString(nameof(Resources.RS1012_Description), Resources.ResourceManager, typeof(Resources)));

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.AddAssignmentExpression);
	}

	private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not AssignmentExpressionSyntax assignment)
		{
			return;
		}

		// Left-hand side must be of type string
		var leftType = context.SemanticModel.GetTypeInfo(assignment.Left).Type;
		if (leftType is null || leftType.SpecialType != SpecialType.System_String)
		{
			return;
		}

		// Must be inside a loop statement
		if (!IsInsideLoop(assignment))
		{
			return;
		}

		context.ReportDiagnostic(Diagnostic.Create(Rule, assignment.GetLocation()));
	}

	private static bool IsInsideLoop(SyntaxNode node)
	{
		var current = node.Parent;
		while (current is not null)
		{
			if (current is ForStatementSyntax
				or ForEachStatementSyntax
				or WhileStatementSyntax
				or DoStatementSyntax)
			{
				return true;
			}

			// Stop at method/lambda boundaries to avoid cross-method false positives
			if (current is MethodDeclarationSyntax
				or LocalFunctionStatementSyntax
				or AnonymousFunctionExpressionSyntax)
			{
				return false;
			}

			current = current.Parent;
		}

		return false;
	}
}
