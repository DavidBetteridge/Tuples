namespace TupleClient;

/// <summary>
/// Represents a wildcard pattern matcher for tuple operations.
/// Use this instead of the string "*" to avoid ambiguity when a tuple value might actually be "*".
/// </summary>
public readonly struct Wildcard
{
    /// <summary>
    /// The singleton wildcard instance. Use this in pattern matching operations.
    /// </summary>
    public static readonly Wildcard Any = new Wildcard();

    /// <summary>
    /// Converts the wildcard to its string representation used in the tuple protocol.
    /// </summary>
    public override string ToString() => "*";

    /// <summary>
    /// Implicit conversion to string for use in tuple pattern arrays.
    /// </summary>
    public static implicit operator string(Wildcard _) => "*";
}
