namespace GB.Sdk;

/// <summary>
/// Marks the intended game entry point for future symbol-based validation.
/// This marker has no execution behavior; running a game program on the host is not supported.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class GameEntryPointAttribute : Attribute
{
}
