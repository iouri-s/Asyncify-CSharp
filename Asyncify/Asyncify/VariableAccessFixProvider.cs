using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Asyncify
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(VariableAccessFixProvider)), Shared]
    public class VariableAccessFixProvider : BaseAsyncifyFixer<MemberAccessExpressionSyntax>
    {
        protected override string Title => "Asyncify Variable Access";

        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(VariableAccessAnalyzer.DiagnosticId);

        protected override SyntaxNode ApplyFix(ref MethodDeclarationSyntax method, MemberAccessExpressionSyntax variableAccess, SyntaxNode syntaxRoot)
        {
            var trackedRoot = syntaxRoot.TrackNodes(method, variableAccess);
            var trackedVariableAccess = trackedRoot.GetCurrentNode(variableAccess);
            var newAccess = SyntaxFactory.AwaitExpression(variableAccess.Expression.WithLeadingTrivia(SyntaxFactory.Space));
            syntaxRoot = trackedRoot.ReplaceNode(trackedVariableAccess, newAccess);
            method = syntaxRoot.GetCurrentNode(method);
            return syntaxRoot;
        }
    }
}