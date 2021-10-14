using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Asyncify
{
    internal class InvocationChecker
    {
        private readonly SemanticModel semanticModel;

        private Lazy<ITypeSymbol> taskSymbol;
        private Lazy<ITypeSymbol> taskOfTSymbol;

        public InvocationChecker(SemanticModel semanticModel)
        {
            this.semanticModel = semanticModel;
        }

        internal bool ShouldUseTap(InvocationExpressionSyntax invocation)
        {
            var method = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (invocation.IsWrappedInAwaitExpression() || invocation.IsWrappedInLock() || method == null || IsFollowedByCallReturningVoid(invocation))
            {
                return false;
            }

            taskSymbol = new Lazy<ITypeSymbol>(() => semanticModel.Compilation.GetTypeByMetadataName(typeof(Task).FullName));
            taskOfTSymbol = new Lazy<ITypeSymbol>(() => semanticModel.Compilation.GetTypeByMetadataName(typeof(Task).FullName + "`1"));

            if (method.HasOutOrRefParameters())
            {
                return false;
            }

            var symbolToCheck = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (symbolToCheck == null)
                return false;//Broken code case

            MemberAccessExpressionSyntax memberAccessExpressionSyntax = invocation.Expression as MemberAccessExpressionSyntax;
            ExpressionSyntax receiverExpr = memberAccessExpressionSyntax?.Expression;

            ITypeSymbol receiverType = (receiverExpr != null) ? semanticModel.GetTypeInfo(receiverExpr, default).Type : null;
            string replacement = DetectSynchronousUsages(invocation.GetLocation(), symbolToCheck.OriginalDefinition, receiverType, semanticModel);
            if (replacement != null)
            {
                return true;
            }

            bool retVal = IsAwaitableMethod(symbolToCheck) && (InvocationCallsIsWrappedInResultCall(invocation) || InvocationCallsIsWrappedInGetAwaiterCall(invocation));

            return retVal;
        }

        private static string DetectSynchronousUsages(Location location, IMethodSymbol methodCallSymbol, ITypeSymbol receiverTypeCandidate, SemanticModel semanticModel)
        {
            if (methodCallSymbol.ContainingType == null)
            {
                return null;
            }
            ITypeSymbol receiverType = receiverTypeCandidate ?? methodCallSymbol.ContainingType;
            if (!methodCallSymbol.ContainingAssembly.ToDisplayString(null).StartsWith("System.", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string methodName = methodCallSymbol.Name;
            string typeName = methodCallSymbol.ContainingType.Name;

            return (from a in semanticModel.LookupSymbols(location.SourceSpan.Start, receiverType, methodName + "Async", true).OfType<IMethodSymbol>()
                    where !a.IsVirtual && !a.IsAbstract
                    select a into m
                    select m.Name).FirstOrDefault();
        }
        private bool IsFollowedByCallReturningVoid(InvocationExpressionSyntax invocation)
        {
            var parentMemberAccess = invocation.Parent as MemberAccessExpressionSyntax;
            var parentIdentifier = parentMemberAccess?.Name as IdentifierNameSyntax;
            if (parentIdentifier == null)
            {
                return false;
            }

            var symbol = semanticModel.GetSymbolInfo(parentIdentifier).Symbol as IMethodSymbol;
            return symbol?.ReturnType.SpecialType == SpecialType.System_Void;
        }

        private bool InvocationCallsIsWrappedInResultCall(InvocationExpressionSyntax invocation)
        {
            SyntaxNode node = invocation;
            while (node.Parent != null)
            {
                node = node.Parent;

                var memberAccess = node as MemberAccessExpressionSyntax;
                var identifierName = memberAccess?.Name as IdentifierNameSyntax;
                if (identifierName != null && identifierName.Identifier.ValueText == nameof(Task<int>.Result))
                {
                    return true;
                }
            }
            return false;
        }

        private bool InvocationCallsIsWrappedInGetAwaiterCall(InvocationExpressionSyntax invocation)
        {
            SyntaxNode node = invocation;
            while (node.Parent != null)
            {
                node = node.Parent;

                var memberAccess = node as MemberAccessExpressionSyntax;
                var identifierName = memberAccess?.Name as IdentifierNameSyntax;
                if (identifierName != null && identifierName.Identifier.ValueText == nameof(Task<int>.GetAwaiter))
                {
                    return true;
                }
            }
            return false;
        }


        private bool IsAwaitableMethod(IMethodSymbol invokedSymbol)
        {
            return invokedSymbol.IsAsync || IsTask(invokedSymbol.ReturnType as INamedTypeSymbol);
        }

        private bool IsTask(INamedTypeSymbol returnType)
        {
            if (returnType == null)
            {
                return false;
            }

            return returnType.IsGenericType ?
                returnType.ConstructedFrom.Equals(taskOfTSymbol.Value) :
                returnType.Equals(taskSymbol.Value);
        }
    }
}
