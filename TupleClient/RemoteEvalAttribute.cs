namespace TupleClient;

/// <summary>
/// Marks a method for remote evaluation. The source generator will extract
/// the method body and make it available as a string for serialization.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RemoteEvalAttribute : Attribute
{
}
