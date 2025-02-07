using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace pkNX.Structures.FlatBuffers.SourceGen;

/// <summary>
/// A source generator that adds the ExpandableObject attribute to all classes and structs that have a FlatBuffer attribute.
/// </summary>
[Generator]
public class ExpandableAttributeGenerator : IIncrementalGenerator
{
    private static bool IsFlatBufferTableAttribute(AttributeSyntax a)
        => a.Name.ToString() is "FlatBufferTable" or "FlatBufferStruct";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var declarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node switch
                {
                    ClassDeclarationSyntax => true,
                    RecordDeclarationSyntax => true,
                    StructDeclarationSyntax => true,
                    _ => false,
                },
                transform: static (ctx, _) => ctx.Node)
            .Where(static node => (node switch
            {
                MemberDeclarationSyntax c => c.AttributeLists,
                _ => throw new InvalidOperationException("Unexpected node type."),
            }).SelectMany(x => x.Attributes).Any(IsFlatBufferTableAttribute))
            .Collect();

        context.RegisterSourceOutput(declarations, static (spc, source) =>
        {
            var sb = new StringBuilder();
            var newAttribute = GetExpandableObjectAttribute();

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("using System.ComponentModel;");
            sb.AppendLine();

            // Group declarations by their namespace (or global if none).
            var groups = source.GroupBy(GetNamespace);
            foreach (var group in groups)
            {
                var ns = group.Key;
                if (!string.IsNullOrEmpty(ns))
                {
                    sb.AppendLine($"namespace {ns}");
                    sb.AppendLine("{");
                }

                foreach (var node in group)
                {
                    var type = (TypeDeclarationSyntax)node;
                    var keyword = type.Keyword.Text; // "class" or "struct"
                    var identifier = type.Identifier.Text;

                    // [Attribute] class;
                    sb.AppendLine($"    {newAttribute} partial {keyword} {identifier};");
                }

                if (!string.IsNullOrEmpty(ns))
                {
                    sb.AppendLine("}"); // close namespace
                    sb.AppendLine();
                }
            }

            spc.AddSource("ExpandableAttributes.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        });
    }

    private static AttributeListSyntax GetExpandableObjectAttribute() => SyntaxFactory.AttributeList(
        SyntaxFactory.SingletonSeparatedList(
            SyntaxFactory.Attribute(
                SyntaxFactory.ParseName("TypeConverter"),
                SyntaxFactory.AttributeArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.AttributeArgument(
                            SyntaxFactory.ParseExpression("typeof(ExpandableObjectConverter)")
                        )
                    )
                )
            )
        )
    );

    /// <summary>
    /// Gets the full namespace of a type declaration by walking its ancestors.
    /// Returns an empty string for the global namespace.
    /// </summary>
    private static string GetNamespace(SyntaxNode node)
    {
        // Look for both regular and file-scoped namespace declarations.
        var namespaceDeclaration = node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
        return namespaceDeclaration != null ? namespaceDeclaration.Name.ToString() : "";
    }
}
