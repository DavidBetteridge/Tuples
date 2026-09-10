namespace TupleClient;

/// <summary>
/// Marks a struct as a tuple definition that should be published to the expression runner.
/// The source generator will extract the struct definition and make it available as a string
/// for compilation in the remote execution context.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false)]
public sealed class TupleDefinitionAttribute : Attribute
{
}
