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

            //var invocationNode = nodeStack.FirstOrDefault(n => n.IsKind(SyntaxKind.InvocationExpression)) as InvocationExpressionSyntax;
            var invocationNode = node as InvocationExpressionSyntax;
            if (invocationNode?.Expression == null)
            {
                return;
            }

            var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);

            var invocationTypeInfo = model.GetTypeInfo((invocationNode.Expression as MemberAccessExpressionSyntax).Expression);

            if (invocationTypeInfo.Type.TypeKind != TypeKind.Interface)
            {
                // Our biggest problem is narrowing the contexts in which we show our offering.  This is hard because the semantic
                // model can't really tell whether the selected invocation is on a mock or not.  But at least we can avoid appearing in
                // regular method calls - we just say that we will be used only in the context of an interface call.
                return;
            }

            // Adding this improves our accuracy, because we won't show our offering unless the compilation references NSubstitute.
            if (!model.Compilation.ExternalReferences.Any(r => r.Display.Contains("NSubstitute")))
            {
                return;
            }

            var guessedMethodGroups = model.GetMemberGroup(invocationNode.Expression);
            List<CodeAction> addArgOverloadActions = new List<CodeAction>();

            if (!guessedMethodGroups.Any())
            {
                return;
            }

            var calledMethodName = ((invocationNode.Expression as MemberAccessExpressionSyntax).Name as IdentifierNameSyntax).Identifier.Text;

            var matchingMethodSymbols = guessedMethodGroups[0].ContainingType.GetMembers()
                                                                                .Where(m => m.Name == calledMethodName && m is IMethodSymbol)
                                                                                .Cast<IMethodSymbol>();

            foreach (var matchingMethodSymbol in matchingMethodSymbols)
            {
                //if (methodGroup.ContainingType
                //                   .GetMembers()
                //                   .FirstOrDefault(m => m.Name == calledMethodName) is IMethodSymbol firstMatchedOverload)
                //{
                var paramNames = matchingMethodSymbol.Parameters.Select(p => p.Type.ToString()).ToArray();

                var addArgAnysAction = CodeAction.Create("Add Arg.Any<>() Arguments", c =>
                {
                    var generatedArgList = CreateArgsAny(paramNames);
                    var oldInvNode = invocationNode;
                    var newInvNode = oldInvNode
                                        .WithArgumentList(generatedArgList)
                                        .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
                    var newRoot = root.ReplaceNode(oldInvNode, newInvNode);
                    return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
                });

                addArgOverloadActions.Add(addArgAnysAction);
                //context.RegisterRefactoring(addArgAnysAction);
                //}
            }

            if (addArgOverloadActions.Count == 1)
            {
                context.RegisterRefactoring(addArgOverloadActions[0]);
            }
            else
            {
                var actionGroup = CodeAction.Create("Add Arg.Any<>() Arguments", addArgOverloadActions.ToImmutableArray(), false);
                context.RegisterRefactoring(actionGroup);
            }
            //if (guessedMethodGroups.Any())
            //{

            //    var methodGroup = guessedMethodGroups[0];
            //    if (methodGroup.ContainingType
            //                        .GetMembers()
            //                        .FirstOrDefault(m => m.Name == calledMethodName) is IMethodSymbol firstMatchedOverload)
            //    {
            //        var paramNames = firstMatchedOverload.Parameters.Select(p => p.Type.ToString()).ToArray();

            //        var addArgAnysAction = CodeAction.Create("Add Arg.Any<>() Arguments", c =>
            //        {
            //            var generatedArgList = CreateArgsAny(paramNames);
            //            var oldInvNode = invocationNode;
            //            var newInvNode = oldInvNode
            //                                .WithArgumentList(generatedArgList)
            //                                .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
            //            var newRoot = root.ReplaceNode(oldInvNode, newInvNode);
            //            return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
            //        });

            //        context.RegisterRefactoring(addArgAnysAction);
            //    }
            //}
        }

        private ArgumentListSyntax CreateArgsAny(IEnumerable<string> argumentTypeNames)
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
