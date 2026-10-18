using GB.Sdk;
using Microsoft.CodeAnalysis;

namespace GB.Compiler;

internal enum DisplayIntrinsicKind { None, WriteByte, InitializeBackground, WaitForNextVBlank, SetScroll, CopyToBuffer, ReadBufferByte, InitializeSprites, SetSprite, HideSprite }

internal static class DisplayIntrinsic
{
    internal static DisplayIntrinsicKind Identify(IMethodSymbol method, IAssemblySymbol sdk)
    {
        var marker = sdk.GetTypeByMetadataName("GB.Sdk.GameIntrinsicAttribute");
        if (!SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, sdk) ||
            !SymbolEqualityComparer.Default.Equals(method.GetAttributes().FirstOrDefault(a =>
                SymbolEqualityComparer.Default.Equals(a.AttributeClass, marker))?.AttributeClass?.ContainingAssembly, sdk) ||
            !method.IsStatic || method.IsGenericMethod ||
            method.Parameters.Any(p => p.RefKind != RefKind.None || p.IsOptional || p.IsParams)) return DisplayIntrinsicKind.None;
        bool Signature(params SpecialType[] types) => method.Parameters.Select(p => p.Type.SpecialType).SequenceEqual(types);
        if (SymbolEqualityComparer.Default.Equals(method.ContainingType, sdk.GetTypeByMetadataName(typeof(GameMemory).FullName!)) &&
            method.ReturnsVoid && method.Name == nameof(GameMemory.WriteByte) && Signature(SpecialType.System_UInt16, SpecialType.System_Byte))
            return DisplayIntrinsicKind.WriteByte;
        if (SymbolEqualityComparer.Default.Equals(method.ContainingType, sdk.GetTypeByMetadataName(typeof(GameBanking).FullName!)) &&
            method.ReturnsVoid && method.Name == nameof(GameBanking.CopyToBuffer) && Signature(SpecialType.System_Byte,
                SpecialType.System_UInt16, SpecialType.System_UInt16, SpecialType.System_UInt16))
            return DisplayIntrinsicKind.CopyToBuffer;
        if (SymbolEqualityComparer.Default.Equals(method.ContainingType, sdk.GetTypeByMetadataName(typeof(GameBanking).FullName!)) &&
            method.Name == nameof(GameBanking.ReadBufferByte) && method.ReturnType.SpecialType == SpecialType.System_Byte &&
            Signature(SpecialType.System_UInt16)) return DisplayIntrinsicKind.ReadBufferByte;
        if (!method.ReturnsVoid) return DisplayIntrinsicKind.None;
        if (SymbolEqualityComparer.Default.Equals(method.ContainingType, sdk.GetTypeByMetadataName(typeof(GameSprites).FullName!)))
            return method.Name switch
            {
                nameof(GameSprites.Initialize) when Signature() => DisplayIntrinsicKind.InitializeSprites,
                nameof(GameSprites.Set) when Signature(SpecialType.System_Byte, SpecialType.System_Byte,
                    SpecialType.System_Byte, SpecialType.System_Byte) => DisplayIntrinsicKind.SetSprite,
                nameof(GameSprites.Hide) when Signature(SpecialType.System_Byte) => DisplayIntrinsicKind.HideSprite,
                _ => DisplayIntrinsicKind.None
            };
        if (!SymbolEqualityComparer.Default.Equals(method.ContainingType, sdk.GetTypeByMetadataName(typeof(GameDisplay).FullName!)))
            return DisplayIntrinsicKind.None;
        return method.Name switch
        {
            nameof(GameDisplay.InitializeBackground) when Signature() => DisplayIntrinsicKind.InitializeBackground,
            nameof(GameDisplay.WaitForNextVBlank) when Signature() => DisplayIntrinsicKind.WaitForNextVBlank,
            nameof(GameDisplay.SetScroll) when Signature(SpecialType.System_Byte, SpecialType.System_Byte) => DisplayIntrinsicKind.SetScroll,
            _ => DisplayIntrinsicKind.None
        };
    }
}
