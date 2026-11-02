namespace GB.Sdk;

/// <summary>Native byte access to the target address space. Access timing is the caller's responsibility.</summary>
public static class GameMemory
{
    /// <summary>
    /// Reads one byte from the target address space, including hardware side effects.
    /// Host execution is explicitly unsupported; this body is never copied into a ROM.
    /// </summary>
    [GameIntrinsic]
    public static byte ReadByte(ushort address) =>
        throw new PlatformNotSupportedException("GameMemory.ReadByte requires the Game Boy compiler backend.");

    /// <summary>Writes a native byte; this method cannot execute on the host.</summary>
    [GameIntrinsic]
    public static void WriteByte(ushort address, byte value) =>
        throw new PlatformNotSupportedException("GameMemory.WriteByte requires the Game Boy compiler backend.");
}

// An intrinsic marker alone does not authorize a call: the validator must resolve the
// SDK assembly and exact method symbol/signature before treating it as an intrinsic.
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
internal sealed class GameIntrinsicAttribute : Attribute
{
}
