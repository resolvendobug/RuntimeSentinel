using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using RuntimeSentinel.Analyzers;

namespace RuntimeSentinel.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AddTakeLimitForWhenAllCodeFixProvider)), Shared]
public sealed class AddTakeLimitForWhenAllCodeFixProvider : CodeFixProvider
{
    private const string TakeTitle = "Aplicar limite com Take(100) antes do Select";
    private const string SemaphoreTitle = "Aplicar limitacao com SemaphoreSlim(10)";
    private const int DefaultTakeLimit = 100;
    private const int DefaultSemaphoreLimit = 10;

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(AvoidUnboundedWhenAllAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];
        if (diagnostic.Id != AvoidUnboundedWhenAllAnalyzer.DiagnosticId)
        {
            return;
        }

        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        var whenAllInvocation = root.FindNode(diagnostic.Location.SourceSpan)
            .FirstAncestorOrSelf<InvocationExpressionSyntax>();

        if (whenAllInvocation is null)
        {
            return;
        }

        if (!IsWhenAllSelectInvocation(whenAllInvocation))
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: TakeTitle,
                createChangedDocument: cancellationToken => ApplyTakeFixAsync(context.Document, whenAllInvocation, cancellationToken),
                equivalenceKey: TakeTitle),
            diagnostic);

        if (CanApplySemaphoreFix(whenAllInvocation))
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: SemaphoreTitle,
                    createChangedDocument: cancellationToken => ApplySemaphoreFixAsync(context.Document, whenAllInvocation, cancellationToken),
                    equivalenceKey: SemaphoreTitle),
                diagnostic);
        }
    }

    private static async Task<Document> ApplyTakeFixAsync(
        Document document,
        InvocationExpressionSyntax whenAllInvocation,
        CancellationToken cancellationToken)
    {
        if (whenAllInvocation.ArgumentList.Arguments.Count == 0)
        {
            return document;
        }

        if (whenAllInvocation.ArgumentList.Arguments[0].Expression is not InvocationExpressionSyntax selectInvocation
            || selectInvocation.Expression is not MemberAccessExpressionSyntax selectMemberAccess)
        {
            return document;
        }

        var sourceExpression = selectMemberAccess.Expression;

        var takeInvocation = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    ParenthesizeIfNeeded(sourceExpression.WithoutTrivia()),
                    SyntaxFactory.IdentifierName("Take")),
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(
                            SyntaxFactory.LiteralExpression(
                                SyntaxKind.NumericLiteralExpression,
                                SyntaxFactory.Literal(DefaultTakeLimit))))))
            .WithTriviaFrom(sourceExpression)
            .WithAdditionalAnnotations(Formatter.Annotation);

        var updatedSelectInvocation = selectInvocation.WithExpression(
            selectMemberAccess.WithExpression(takeInvocation));

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var updatedRoot = root.ReplaceNode(selectInvocation, updatedSelectInvocation);

        if (updatedRoot is CompilationUnitSyntax compilationUnit && !HasUsing(compilationUnit, "System.Linq"))
        {
            updatedRoot = compilationUnit.AddUsings(
                SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System.Linq")));
        }

        return document.WithSyntaxRoot(updatedRoot);
    }

    private static async Task<Document> ApplySemaphoreFixAsync(
        Document document,
        InvocationExpressionSyntax whenAllInvocation,
        CancellationToken cancellationToken)
    {
        if (whenAllInvocation.ArgumentList.Arguments.Count == 0)
        {
            return document;
        }

        if (whenAllInvocation.ArgumentList.Arguments[0].Expression is not InvocationExpressionSyntax selectInvocation
            || !TryGetSelectLambda(selectInvocation, out var lambdaExpression))
        {
            return document;
        }

        var semaphoreName = SyntaxFactory.IdentifierName("semaphore");
        var waitStatement = SyntaxFactory.ParseStatement("await semaphore.WaitAsync();");
        var releaseStatement = SyntaxFactory.ParseStatement("semaphore.Release();");

        BlockSyntax originalBody;
        if (lambdaExpression.Body is BlockSyntax bodyBlock)
        {
            originalBody = bodyBlock;
        }
        else if (lambdaExpression.Body is ExpressionSyntax bodyExpression)
        {
            originalBody = SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(bodyExpression));
        }
        else
        {
            return document;
        }

        var tryFinallyStatement = SyntaxFactory.TryStatement(
            originalBody,
            default,
            SyntaxFactory.FinallyClause(SyntaxFactory.Block(releaseStatement)));

        var newLambdaBody = SyntaxFactory.Block(waitStatement, tryFinallyStatement)
            .WithAdditionalAnnotations(Formatter.Annotation);

        LambdaExpressionSyntax updatedLambdaExpression = lambdaExpression switch
        {
            SimpleLambdaExpressionSyntax simpleLambda => simpleLambda.WithBody(newLambdaBody),
            ParenthesizedLambdaExpressionSyntax parenthesizedLambda => parenthesizedLambda.WithBody(newLambdaBody),
            _ => lambdaExpression
        };

        var updatedSelectInvocation = ReplaceLambdaInSelect(selectInvocation, updatedLambdaExpression);
        if (updatedSelectInvocation is null)
        {
            return document;
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var updatedRoot = root.ReplaceNode(selectInvocation, updatedSelectInvocation);

        var originalStatement = whenAllInvocation.FirstAncestorOrSelf<StatementSyntax>();
        if (originalStatement is null)
        {
            return document.WithSyntaxRoot(updatedRoot);
        }

        var semaphoreDeclaration = SyntaxFactory.LocalDeclarationStatement(
                SyntaxFactory.VariableDeclaration(
                    SyntaxFactory.ParseTypeName("System.Threading.SemaphoreSlim"),
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator("semaphore")
                            .WithInitializer(
                                SyntaxFactory.EqualsValueClause(
                                    SyntaxFactory.ObjectCreationExpression(
                                            SyntaxFactory.ParseTypeName("System.Threading.SemaphoreSlim"))
                                        .WithArgumentList(
                                            SyntaxFactory.ArgumentList(
                                                SyntaxFactory.SingletonSeparatedList(
                                                    SyntaxFactory.Argument(
                                                        SyntaxFactory.LiteralExpression(
                                                            SyntaxKind.NumericLiteralExpression,
                                                            SyntaxFactory.Literal(DefaultSemaphoreLimit)))))))))))
            .WithTrailingTrivia(originalStatement.GetLeadingTrivia())
            .WithAdditionalAnnotations(Formatter.Annotation);

        var currentRootStatement = updatedRoot.FindNode(originalStatement.Span).FirstAncestorOrSelf<StatementSyntax>();
        if (currentRootStatement is null)
        {
            return document.WithSyntaxRoot(updatedRoot);
        }

        var finalRoot = updatedRoot.InsertNodesBefore(currentRootStatement, new[] { semaphoreDeclaration });
        return document.WithSyntaxRoot(finalRoot);
    }

    private static bool HasUsing(CompilationUnitSyntax compilationUnit, string namespaceName)
    {
        return compilationUnit.Usings.Any(u => u.Name?.ToString() == namespaceName);
    }

    private static bool CanApplySemaphoreFix(InvocationExpressionSyntax invocation)
    {
        if (invocation.ArgumentList.Arguments.Count == 0)
        {
            return false;
        }

        if (invocation.ArgumentList.Arguments[0].Expression is not InvocationExpressionSyntax selectInvocation)
        {
            return false;
        }

        return TryGetSelectLambda(selectInvocation, out _);
    }

    private static bool TryGetSelectLambda(InvocationExpressionSyntax selectInvocation, out LambdaExpressionSyntax lambdaExpression)
    {
        lambdaExpression = null!;

        if (selectInvocation.ArgumentList.Arguments.Count == 0)
        {
            return false;
        }

        var firstArgument = selectInvocation.ArgumentList.Arguments[0].Expression;
        if (firstArgument is not LambdaExpressionSyntax lambda)
        {
            return false;
        }

        lambdaExpression = lambda;
        return true;
    }

    private static InvocationExpressionSyntax? ReplaceLambdaInSelect(
        InvocationExpressionSyntax selectInvocation,
        LambdaExpressionSyntax updatedLambdaExpression)
    {
        if (selectInvocation.ArgumentList.Arguments.Count == 0)
        {
            return null;
        }

        var updatedFirstArgument = selectInvocation.ArgumentList.Arguments[0].WithExpression(updatedLambdaExpression);
        var updatedArguments = selectInvocation.ArgumentList.Arguments.Replace(selectInvocation.ArgumentList.Arguments[0], updatedFirstArgument);

        return selectInvocation.WithArgumentList(selectInvocation.ArgumentList.WithArguments(updatedArguments));
    }

    private static bool IsWhenAllSelectInvocation(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax whenAllAccess
            || whenAllAccess.Name.Identifier.ValueText != "WhenAll")
        {
            return false;
        }

        if (invocation.ArgumentList.Arguments.Count == 0)
        {
            return false;
        }

        if (invocation.ArgumentList.Arguments[0].Expression is not InvocationExpressionSyntax selectInvocation
            || selectInvocation.Expression is not MemberAccessExpressionSyntax selectAccess
            || selectAccess.Name.Identifier.ValueText != "Select")
        {
            return false;
        }

        return true;
    }

    private static ExpressionSyntax ParenthesizeIfNeeded(ExpressionSyntax expression)
    {
        return expression is IdentifierNameSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax
            ? expression
            : SyntaxFactory.ParenthesizedExpression(expression);
    }
}
