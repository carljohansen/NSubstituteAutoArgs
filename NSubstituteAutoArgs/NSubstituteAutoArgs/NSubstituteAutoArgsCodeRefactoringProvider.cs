using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;

namespace NSubsituteAutoArgs
{
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(NSubstituteAutoArgsCodeRefactoringProvider)), Shared]
    internal class NSubstituteAutoArgsCodeRefactoringProvider : CodeRefactoringProvider
    {
        public sealed override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            // Find the node at the selection.
            var node = root.FindNode(context.Span);

            // We keep our criteria narrow to avoid as much as possible showing our offering in inapplicable situations.
            // We won't show unless you are inside the *empty* argument list of an invocation.

            if (!node.IsKind(SyntaxKind.ArgumentList))
            {
                return;
            }
            var argListNode = node as ArgumentListSyntax;
            if (argListNode.Arguments.Any())
            {
                return;
            }

            node = node.Parent;
            if (!node.IsKind(SyntaxKind.InvocationExpression))
            {
                return;
            }

            //var nodeStack = new List<SyntaxNode>();
            //currNode = node;
            //while (currNode != null)
            //{
            //    nodeStack.Add(currNode);
            //    currNode = currNode.Parent;
            //}

            var invocationNode = node as InvocationExpressionSyntax;
            if (!(invocationNode.Expression is MemberAccessExpressionSyntax memberAccessNode)) return;
            var invocationTarget = memberAccessNode.Expression;
            if (invocationTarget == null)
            {
                return;
            }

            var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);

            var invocationTypeInfo = model.GetTypeInfo(invocationTarget);

            if (invocationTypeInfo.Type.TypeKind != TypeKind.Interface)
            {
                // Our biggest problem is narrowing the contexts in which we show our offering.  This is hard because the semantic
                // model can't really tell whether the selected invocation is on a mock or not.  But at least we can avoid appearing in
                // regular method calls - we just say that we will be used only in the context of an interface call.
                return;
            }

            // Adding this improves our accuracy, because we won't show our offering unless the compilation references NSubstitute.
            // However, it comes with a performance penalty.  Most of the time we will be looking through a long list and not finding a match.
            if (!model.Compilation.ExternalReferences.Any(r => r.Display.Contains("NSubstitute")))
            {
                return;
            }

            //var guessedMethodGroups = model.GetMemberGroup(invocationNode.Expression);

            //if (!guessedMethodGroups.Any())
            //{
            //    return;
            //}

            //var matchingMethodSymbols = GetMatchingMethodSymbols(guessedMethodGroups, invocationNode);
            //if (!matchingMethodSymbols.Any())
            //{
            //    return;
            //}

            IMethodSymbol[] candiateMethods;

            var invocationSymbolInfo = model.GetSymbolInfo(invocationNode);
            if (invocationSymbolInfo.CandidateReason == CandidateReason.OverloadResolutionFailure)
            {
                // We _should_ be getting this overload resolution failure, because the user has called us before
                // adding any invocation parameters.  Roslyn's candidates tell us exactly what we need to build our offerings.
                var overloadCandidates = invocationSymbolInfo.CandidateSymbols;
                candiateMethods = overloadCandidates.Cast<IMethodSymbol>().ToArray();
            }
            else
            {
                return;
            }

            var hasOverloads = candiateMethods.Length > 1;
            var addArgOverloadActions = new List<CodeAction>();

            foreach (var candidateMethod in candiateMethods)
            {
                var addArgAnysAction = CreateCodeAction(context.Document, root, invocationNode, hasOverloads, candidateMethod);
                addArgOverloadActions.Add(addArgAnysAction);
            }

            if (hasOverloads)
            {
                var actionGroup = CodeAction.Create("Add Arg.Any<>() Arguments", addArgOverloadActions.ToImmutableArray(), false);
                context.RegisterRefactoring(actionGroup);
            }
            else
            {
                context.RegisterRefactoring(addArgOverloadActions[0]);
            }
        }

        //private static IMethodSymbol[] GetMatchingMethodSymbols(ImmutableArray<ISymbol> guessedMethodGroups,
        //                                                                             InvocationExpressionSyntax invocationNode)
        //{
        //    var calledMethodName = ((invocationNode.Expression as MemberAccessExpressionSyntax)?.Name as IdentifierNameSyntax)?.Identifier.Text;
        //    if (calledMethodName == null)
        //    {
        //        calledMethodName = ((invocationNode.Expression as MemberAccessExpressionSyntax)?.Name as GenericNameSyntax)?.Identifier.Text;
        //        if (calledMethodName == null)
        //        {
        //            return Array.Empty<IMethodSymbol>();
        //        }
        //    }

        //    return guessedMethodGroups[0].ContainingType.GetMembers()
        //                                                .Where(m => m.Name == calledMethodName && m is IMethodSymbol)
        //                                                .Cast<IMethodSymbol>()
        //                                                .ToArray();
        //}

        private static CodeAction CreateCodeAction(Document sourceDocument,
                                            SyntaxNode root,
                                            InvocationExpressionSyntax invocationNode,
                                            bool hasOverloads,
                                            IMethodSymbol matchingMethodSymbol)
        {
            var paramNames = matchingMethodSymbol.Parameters.Select(p => p.Type.ToString()).ToArray();
            var actionName = hasOverloads
                                ? GetMethodDisplay(matchingMethodSymbol)
                                : "Add Arg.Any<>() Arguments";

            var addArgAnysAction = CodeAction.Create(actionName, c =>
            {
                var generatedArgList = CreateArgsAny(paramNames);
                var oldInvNode = invocationNode;
                var newInvNode = oldInvNode
                                    .WithArgumentList(generatedArgList)
                                    .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
                var newRoot = root.ReplaceNode(oldInvNode, newInvNode);
                return Task.FromResult(sourceDocument.WithSyntaxRoot(newRoot));
            });

            return addArgAnysAction;
        }

        private static string GetMethodDisplay(IMethodSymbol methodSymbol)
        {
            const int maxLen = 50;
            var fullName = methodSymbol.ToDisplayString();
            var bracketPos = fullName.IndexOf('(');
            if (bracketPos < 0)
            {
                return fullName.Truncate(maxLen);
            }
            var dotPos = fullName.Substring(0, bracketPos).LastIndexOf('.');
            if (dotPos < 0)
            {
                return fullName.Truncate(maxLen);
            }
            return fullName.Substring(dotPos).Truncate(maxLen);
        }

        private static ArgumentListSyntax CreateArgsAny(IEnumerable<string> argumentTypeNames)
        {
            var smartGenTypes = argumentTypeNames.Select(async a => await TypeSyntaxFactory.CreateTypeSyntax(a))
                                                    .Select(t => t.Result)
                                                    .Where(i => i != null)
                                                    .ToList();

            var argumentSyntaxes = smartGenTypes.Select(CreateArgAny);
            var arglist = SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(argumentSyntaxes));

            return arglist;
        }

        private static ArgumentSyntax CreateArgAny(TypeSyntax argGenericType)
        {
            var genArg = SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList(new[] { argGenericType }));
            var calledMethodNameExp = SyntaxFactory.GenericName(SyntaxFactory.Identifier("Any"), genArg);
            var methodCallExp = SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName("Arg"), calledMethodNameExp);
            var invocation = SyntaxFactory.InvocationExpression(methodCallExp);
            return SyntaxFactory.Argument(invocation);
        }
    }
}
