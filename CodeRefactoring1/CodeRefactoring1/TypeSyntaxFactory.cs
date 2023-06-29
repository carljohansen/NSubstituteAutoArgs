using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using System.Threading.Tasks;

namespace CodeRefactoring1
{
    public static class TypeSyntaxFactory
    {
        public static async Task<TypeSyntax> CreateTypeSyntax(string typeName)
        {
            var options = new CSharpParseOptions(kind: SourceCodeKind.Script);
            var parsedTree = CSharpSyntaxTree.ParseText($"typeof({typeName})", options);
            var treeRoot = await parsedTree.GetRootAsync();
            var typeNameNode = treeRoot.DescendantNodes().OfType<TypeSyntax>().FirstOrDefault();
            return typeNameNode;
        }
    }
}
