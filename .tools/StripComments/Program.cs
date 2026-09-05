using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

class Program
{
    static int Main(string[] args)
    {
        var root = new DirectoryInfo(Directory.GetCurrentDirectory()).Parent?.Parent?.Parent?.Parent?.FullName;
        string workspaceRoot = root ?? Directory.GetCurrentDirectory();
        Console.WriteLine($"Workspace root: {workspaceRoot}");
        var csFiles = Directory.GetFiles(workspaceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains(Path.Combine("bin", "")) && !p.Contains(Path.Combine("obj", "")) && !p.Contains(Path.Combine(".tools", "")))
            .ToList();

        int changed = 0;
        foreach (var file in csFiles)
        {
            try
            {
                var text = File.ReadAllText(file);
                var tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Latest));
                var rootNode = tree.GetRoot();

                var triviaKinds = new[] {
                    SyntaxKind.SingleLineCommentTrivia,
                    SyntaxKind.MultiLineCommentTrivia,
                    SyntaxKind.SingleLineDocumentationCommentTrivia,
                    SyntaxKind.MultiLineDocumentationCommentTrivia,
                    SyntaxKind.DocumentationCommentExteriorTrivia
                };

                var trivias = rootNode.DescendantTrivia().Where(t => triviaKinds.Contains(t.Kind())).ToList();
                if (trivias.Count == 0) continue;

                var newRoot = rootNode.ReplaceTrivia(trivias, (oldTrivia, _) =>
                {
                    if (oldTrivia.IsKind(SyntaxKind.SingleLineCommentTrivia))
                    {
                        // preserve end-of-line
                        return SyntaxFactory.EndOfLine(Environment.NewLine);
                    }
                    return SyntaxFactory.Whitespace(" ");
                });

                var newText = newRoot.NormalizeWhitespace().ToFullString();
                File.WriteAllText(file, newText);
                Console.WriteLine($"Stripped comments: {file}");
                changed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed: {file} -> {ex.Message}");
            }
        }

        Console.WriteLine($"Done. Files changed: {changed}");
        return 0;
    }
}
