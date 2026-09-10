using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TupleClient.Generators;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class TuplePatternAnalyzer : DiagnosticAnalyzer
{
    public const string CountMismatchDiagnosticId = "TUPLE001";
    public const string TypeMismatchDiagnosticId = "TUPLE002";
    private const string Category = "Usage";

    private static readonly LocalizableString CountMismatchTitle = "Tuple pattern parameter count mismatch";
    private static readonly LocalizableString CountMismatchMessageFormat = 
        "The generic type '{0}' has {1} properties, but {2} pattern parameters were provided";
    private static readonly LocalizableString CountMismatchDescription = 
        "When using generic tuple methods like InAsync<T>, RdAsync<T>, etc., the number of pattern parameters must match the number of properties in the tuple struct.";

    private static readonly LocalizableString TypeMismatchTitle = "Tuple pattern parameter type mismatch";
    private static readonly LocalizableString TypeMismatchMessageFormat = 
        "Parameter {0} has type '{1}' but property '{2}' expects type '{3}'. Use Wildcard.Any for wildcard matching.";
    private static readonly LocalizableString TypeMismatchDescription = 
        "When using generic tuple methods, each pattern parameter type must match the corresponding property type in the tuple struct, or use Wildcard.Any for wildcard matching.";

    private static readonly DiagnosticDescriptor CountMismatchRule = new DiagnosticDescriptor(
        CountMismatchDiagnosticId,
        CountMismatchTitle,
        CountMismatchMessageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: CountMismatchDescription);

    private static readonly DiagnosticDescriptor TypeMismatchRule = new DiagnosticDescriptor(
        TypeMismatchDiagnosticId,
        TypeMismatchTitle,
        TypeMismatchMessageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: TypeMismatchDescription);

    // Methods that need validation
    private static readonly string[] TargetMethods = 
    {
        "InAsync",
        "RdAsync", 
        "InpAsync",
        "RdpAsync"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => 
        ImmutableArray.Create(CountMismatchRule, TypeMismatchRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Check if this is a method we care about
        if (!TryGetGenericMethodInfo(invocation, context.SemanticModel, out var methodName, out var typeArgument))
            return;

        if (!TargetMethods.Contains(methodName))
            return;

        // Get the type symbol for the generic argument
        var typeSymbol = context.SemanticModel.GetTypeInfo(typeArgument).Type as INamedTypeSymbol;
        if (typeSymbol == null)
            return;

        // Check if it's a struct
        if (!typeSymbol.IsValueType || typeSymbol.TypeKind != TypeKind.Struct)
            return;

        // Get the properties in the struct (in declaration order)
        var properties = typeSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public && !p.IsStatic)
            .ToArray();

        // Get the pattern arguments (excluding TimeSpan if present)
        var patternArguments = GetPatternArguments(invocation, context.SemanticModel);

        // Check if counts match
        if (patternArguments.Length != properties.Length)
        {
            var diagnostic = Diagnostic.Create(
                CountMismatchRule,
                invocation.GetLocation(),
                typeSymbol.Name,
                properties.Length,
                patternArguments.Length);

            context.ReportDiagnostic(diagnostic);
            return; // Don't check types if count is wrong
        }

        // Check each parameter type
        for (int i = 0; i < patternArguments.Length; i++)
        {
            var argument = patternArguments[i];
            var property = properties[i];
            var argumentType = context.SemanticModel.GetTypeInfo(argument.Expression).Type;

            if (argumentType == null)
                continue;

            // Allow Wildcard type for any property
            if (IsWildcardType(argumentType))
                continue;

            // Check if the argument type is compatible with the property type
            if (!IsTypeCompatible(argumentType, property.Type, context.Compilation))
            {
                var diagnostic = Diagnostic.Create(
                    TypeMismatchRule,
                    argument.GetLocation(),
                    i + 1,
                    argumentType.ToDisplayString(),
                    property.Name,
                    property.Type.ToDisplayString());

                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static bool TryGetGenericMethodInfo(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        out string methodName,
        out TypeSyntax typeArgument)
    {
        methodName = "";
        typeArgument = null!;

        // Handle member access expressions like client.InAsync<T>(...)
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            if (memberAccess.Name is GenericNameSyntax genericName)
            {
                methodName = genericName.Identifier.Text;
                if (genericName.TypeArgumentList.Arguments.Count == 1)
                {
                    typeArgument = genericName.TypeArgumentList.Arguments[0];
                    return true;
                }
            }
        }
        // Handle simple invocations like InAsync<T>(...)
        else if (invocation.Expression is GenericNameSyntax genericName)
        {
            methodName = genericName.Identifier.Text;
            if (genericName.TypeArgumentList.Arguments.Count == 1)
            {
                typeArgument = genericName.TypeArgumentList.Arguments[0];
                return true;
            }
        }

        return false;
    }

    private static ArgumentSyntax[] GetPatternArguments(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        return invocation.ArgumentList.Arguments
            .Where(arg =>
            {
                var typeInfo = semanticModel.GetTypeInfo(arg.Expression);
                var type = typeInfo.Type;
                // Skip TimeSpan arguments (used for timeout overloads)
                return type == null || type.ToDisplayString() != "System.TimeSpan";
            })
            .ToArray();
    }

    private static bool IsWildcardType(ITypeSymbol type)
    {
        // Check if the type is TupleClient.Wildcard
        return type.Name == "Wildcard" && 
               (type.ContainingNamespace?.ToDisplayString() == "TupleClient" || 
                type.ToDisplayString() == "TupleClient.Wildcard");
    }

    private static bool IsTypeCompatible(ITypeSymbol argumentType, ITypeSymbol propertyType, Compilation compilation)
    {
        // Direct match
        if (SymbolEqualityComparer.Default.Equals(argumentType, propertyType))
            return true;

        // Check if argument type is implicitly convertible to property type
        var conversion = compilation.ClassifyConversion(argumentType, propertyType);
        if (conversion.IsImplicit)
            return true;

        // For numeric types, allow compatible conversions (e.g., int literal to int property)
        if (IsNumericType(argumentType) && IsNumericType(propertyType))
        {
            // Allow same underlying numeric type
            var argSpecialType = GetUnderlyingNumericType(argumentType);
            var propSpecialType = GetUnderlyingNumericType(propertyType);
            if (argSpecialType == propSpecialType)
                return true;
        }

        return false;
    }

    private static bool IsNumericType(ITypeSymbol type)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
                return true;
            default:
                return false;
        }
    }

    private static SpecialType GetUnderlyingNumericType(ITypeSymbol type)
    {
        return type.SpecialType;
    }
}
