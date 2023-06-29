using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Composition;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeRefactoring1
{
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(CodeRefactoring1CodeRefactoringProvider)), Shared]
    internal class CodeRefactoring1CodeRefactoringProvider : CodeRefactoringProvider
    {
        public sealed override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {

            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            // Find the node at the selection.
            var node = root.FindNode(context.Span);

            bool isPossibleMatch = false;
            var currNode = node;
            while (currNode != null && !isPossibleMatch)
            {
                isPossibleMatch = currNode.IsKind(SyntaxKind.InvocationExpression);
                currNode = currNode.Parent;
            }

            if (!isPossibleMatch)
            {
                return;
            }

            var nodeStack = new List<SyntaxNode>();
            currNode = node;
            while (currNode != null)
            {
                nodeStack.Add(currNode);
                currNode = currNode.Parent;
            }

            var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
            // var diags = model.GetDiagnostics();

            var invocationNode = nodeStack.FirstOrDefault(n => n.IsKind(SyntaxKind.InvocationExpression)) as InvocationExpressionSyntax;
            if (invocationNode == null)
            {
                return;
            }
            var calledMethodName = invocationNode.TryGetInferredMemberName();
            if (calledMethodName == null)
            {
                //  return;
            }


            var isNSub = false;
            foreach (var checkNode in nodeStack)
            {
                if (checkNode.IsKind(SyntaxKind.InvocationExpression))
                {
                    var invocationTypeInfo = model.GetTypeInfo(checkNode);
                    var ns = invocationTypeInfo.ConvertedType.ContainingNamespace;

                    while (ns != null)
                    {
                        if (ns.Name == "NSubstitute")
                        {
                            break;
                        }
                        ns = ns.ContainingNamespace;
                    }

                    isNSub = ns != null;
                    if (isNSub)
                    {
                        var xx = model.GetOperation(checkNode);
                        break;
                    }
                }
            }

            if (!isNSub)
            {
                //  return;
            }

            if (invocationNode.Expression == null)
                return;

            var guessedMethodGroups = model.GetMemberGroup(invocationNode.Expression);
            calledMethodName = ((invocationNode.Expression as MemberAccessExpressionSyntax).Name as IdentifierNameSyntax).Identifier.Text;
            if (guessedMethodGroups.Any())
            {
                var methodGroup = guessedMethodGroups[0];
                var x = methodGroup.ContainingType.GetMembers().FirstOrDefault(m => m.Name == calledMethodName) as IMethodSymbol;
                if (x != null)
                {
                    var paramNames = x.Parameters.Select(p => p.Type.ToString()).ToArray();

                    var addArgAnysAction = CodeAction.Create("Add Arg.Any<>() Arguments", c =>
                    {
                        var testme = CreateArgsAny(paramNames);
                        var oldInvNode = nodeStack.First(n => n is InvocationExpressionSyntax) as InvocationExpressionSyntax;
                        var newInvNode = oldInvNode.WithArgumentList(testme).WithTrailingTrivia(SyntaxFactory.LineFeed);
                        var newRoot = root.ReplaceNode(oldInvNode, newInvNode);
                        return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
                    });

                    context.RegisterRefactoring(addArgAnysAction);
                }
            }

            //foreach (var checkNode in nodeStack)
            //{
            //    var mg = model.GetMemberGroup(checkNode);
            //    if (mg.Any())
            //    {
            //        var methodGroup = mg[0];
            //        var x = methodGroup.ContainingType.GetMembers().FirstOrDefault(m => m.Name == calledMethodName) as IMethodSymbol;
            //        if (x != null)
            //        {
            //            var paramNames = x.Parameters.Select(p => p.Type.Name).ToArray();

            //            var addArgAnysAction = CodeAction.Create("Add Arg.Any<>() Arguments", c =>
            //            {
            //                var testme = CreateArgsAny(paramNames);
            //                var oldInvNode = nodeStack.First(n => n is InvocationExpressionSyntax) as InvocationExpressionSyntax;
            //                var newInvNode = oldInvNode.WithArgumentList(testme);
            //                var newRoot = root.ReplaceNode(oldInvNode, newInvNode);
            //                return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
            //            });

            //            context.RegisterRefactoring(addArgAnysAction);
            //        }
            //    }
            //}

            //root.ReplaceNode()

            //var x = model.GetSymbolInfo(node);
            // node.Parent.Parent  MemberAccessExpressionSyntax SimpleMemberAccessExpression calculator           

            // For any type declaration node, create a code action to reverse the identifier text.
            //            var action = CodeAction.Create("Reverse type name", c => ReverseTypeNameAsync(context.Document, typeDecl, c));

            // Register this code action.
            //          context.RegisterRefactoring(action);
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
            //var genType = SyntaxFactory.IdentifierName(SyntaxFactory.Identifier(argumentTypeName)) as TypeSyntax;
            var genArg = SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList(new[] { argGenericType }));
            var calledMethodNameExp = SyntaxFactory.GenericName(SyntaxFactory.Identifier("Any"), genArg);
            var methodCallExp = SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName("Arg"), calledMethodNameExp);
            var invocation = SyntaxFactory.InvocationExpression(methodCallExp);
            return SyntaxFactory.Argument(invocation);
        }

        private class CanIMakeThis { }

        //private async Task<Document> AddNSubstituteArgsAny(Document document, InvocationExpressionSyntax nSubMethodSetup, CancellationToken cancellationToken)
        //{

        //}

        //private async Task<Solution> ReverseTypeNameAsync(Document document, TypeDeclarationSyntax typeDecl, CancellationToken cancellationToken)
        //{
        //    // Produce a reversed version of the type declaration's identifier token.
        //    var identifierToken = typeDecl.Identifier;
        //    var newName = new string(identifierToken.Text.ToCharArray().Reverse().ToArray());

        //    // Get the symbol representing the type to be renamed.
        //    var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        //    var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken);

        //    // Produce a new solution that has all references to that type renamed, including the declaration.
        //    var originalSolution = document.Project.Solution;
        //    var optionSet = originalSolution.Workspace.Options;
        //    var newSolution = await Renamer.RenameSymbolAsync(document.Project.Solution, typeSymbol, newName, optionSet, cancellationToken).ConfigureAwait(false);

        //    // Return the new solution with the now-uppercase type name.
        //    return newSolution;
        //}
    }
}
