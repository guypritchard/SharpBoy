namespace GB.Sdk;

/// <summary>Reserved native memory-write API; no target implementation exists yet.</summary>
public static class GameMemory
{
    /// <summary>
    /// Reserved for a future symbol-verified raw byte write intrinsic.
    /// Host execution is explicitly unsupported; this body is never copied into a ROM.
    /// </summary>
    [GameIntrinsic]
    public static void WriteByte(ushort address, byte value) =>
        throw new PlatformNotSupportedException("GameMemory.WriteByte requires a future Game Boy compiler backend.");
}

// An intrinsic marker alone does not authorize a call: the validator must resolve the
// SDK assembly and exact method symbol/signature before treating it as an intrinsic.
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
internal sealed class GameIntrinsicAttribute : Attribute
{
}
