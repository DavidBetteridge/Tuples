using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace TupleClient.Generators;

[Generator]
public class RemoteEvalGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find all methods with [RemoteEval] attribute
        var methodDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsCandidateMethod(s),
                transform: static (ctx, _) => GetMethodInfo(ctx))
            .Where(static m => m is not null);

        // Find all structs with [TupleDefinition] attribute
        var structDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsCandidateStruct(s),
                transform: static (ctx, _) => GetStructInfo(ctx))
            .Where(static s => s is not null);

        // Combine methods and structs with compilation
        var combinedData = context.CompilationProvider
            .Combine(methodDeclarations.Collect())
            .Combine(structDeclarations.Collect());

        // Generate source
        context.RegisterSourceOutput(combinedData,
            static (spc, source) => Execute(source.Left.Left, source.Left.Right!, source.Right!, spc));
    }

    private static bool IsCandidateMethod(SyntaxNode node)
    {
        return node is MethodDeclarationSyntax method &&
               method.AttributeLists.Count > 0;
    }

    private static bool IsCandidateStruct(SyntaxNode node)
    {
        return node is StructDeclarationSyntax structDecl &&
               structDecl.AttributeLists.Count > 0;
    }

    private static MethodInfo? GetMethodInfo(GeneratorSyntaxContext context)
    {
        var methodSyntax = (MethodDeclarationSyntax)context.Node;

        // Check if it has the RemoteEval attribute
        foreach (var attributeList in methodSyntax.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var name = attribute.Name.ToString();
                if (name == "RemoteEval" || name == "RemoteEvalAttribute" ||
                    name == "TupleClient.RemoteEval" || name == "TupleClient.RemoteEvalAttribute")
                {
                    // Get the containing class/type
                    var containingType = methodSyntax.Parent as TypeDeclarationSyntax;
                    if (containingType == null) return null;

                    // Get namespace
                    var namespaceDecl = containingType.Parent as BaseNamespaceDeclarationSyntax;
                    var namespaceName = namespaceDecl?.Name.ToString() ?? "";

                    // Extract the method body
                    var body = methodSyntax.Body;
                    if (body == null) return null;

                    // Get the body text without the braces
                    var bodyText = body.Statements.ToFullString();

                    return new MethodInfo(
                        namespaceName,
                        containingType.Identifier.Text,
                        methodSyntax.Identifier.Text,
                        bodyText.Trim());
                }
            }
        }

        return null;
    }

    private static StructInfo? GetStructInfo(GeneratorSyntaxContext context)
    {
        var structSyntax = (StructDeclarationSyntax)context.Node;

        // Check if it has the TupleDefinition attribute
        foreach (var attributeList in structSyntax.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var name = attribute.Name.ToString();
                if (name == "TupleDefinition" || name == "TupleDefinitionAttribute" ||
                    name == "TupleClient.TupleDefinition" || name == "TupleClient.TupleDefinitionAttribute")
                {
                    var structName = structSyntax.Identifier.Text;
                    
                    // Extract the struct definition without the attribute
                    var modifiers = structSyntax.Modifiers.ToString();
                    var members = structSyntax.Members.ToFullString();
                    
                    // Build the struct definition string
                    var structDefinition = $"{modifiers} struct {structName}\n{{\n{members}}}";

                    return new StructInfo(structName, structDefinition.Trim());
                }
            }
        }

        return null;
    }

    private static void Execute(Compilation compilation, ImmutableArray<MethodInfo?> methods, ImmutableArray<StructInfo?> structs, SourceProductionContext context)
    {
        var validMethods = methods.Where(m => m is not null).Cast<MethodInfo>().ToList();
        var validStructs = structs.Where(s => s is not null).Cast<StructInfo>().ToList();

        if (validMethods.Count == 0 && validStructs.Count == 0)
            return;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace TupleClient;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Contains the extracted code strings from methods marked with [RemoteEval]");
        sb.AppendLine("/// and struct definitions marked with [TupleDefinition].");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public static class RemoteCode");
        sb.AppendLine("{");

        // Generate TupleDefinitions property containing all struct definitions
        if (validStructs.Count > 0)
        {
            var allStructDefinitions = string.Join("\n\n", validStructs.Select(s => s.StructDefinition));
            var escapedStructs = EscapeForVerbatimString(allStructDefinitions);

            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// All struct definitions marked with [TupleDefinition].");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    public static string TupleDefinitions => @\"{escapedStructs}\";");
            sb.AppendLine();
        }

        // Generate method code properties
        foreach (var method in validMethods)
        {
            var propertyName = method.MethodName;
            var escapedCode = EscapeForVerbatimString(method.BodyText);

            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// Code extracted from {method.ContainingType}.{method.MethodName}");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    public static string {propertyName} => @\"{escapedCode}\";");
            sb.AppendLine();
        }

        sb.AppendLine("}");

        context.AddSource("RemoteCode.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static string EscapeForVerbatimString(string text)
    {
        // In verbatim strings, only double quotes need to be escaped (by doubling them)
        return text.Replace("\"", "\"\"");
    }

    private class MethodInfo
    {
        public string Namespace { get; }
        public string ContainingType { get; }
        public string MethodName { get; }
        public string BodyText { get; }

        public MethodInfo(string ns, string containingType, string methodName, string bodyText)
        {
            Namespace = ns;
            ContainingType = containingType;
            MethodName = methodName;
            BodyText = bodyText;
        }
    }

    private class StructInfo
    {
        public string StructName { get; }
        public string StructDefinition { get; }

        public StructInfo(string structName, string structDefinition)
        {
            StructName = structName;
            StructDefinition = structDefinition;
        }
    }
}
