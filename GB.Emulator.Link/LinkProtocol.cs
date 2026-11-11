using System.Buffers.Binary;
using System.Text.Json;

namespace GB.Emulator.Link;

public sealed record LobbyPlayer(Guid Id, string Name, string Game, string RomHash, Guid Partner, Guid InviteFrom, Guid InviteTo, bool Automated = false);
public sealed record LinkLobbyState(Guid Self, Guid Session, LobbyPlayer[] Players, long Transfers = 0);
internal sealed record LinkPacket(string Kind, int Version = 1, string? Name = null, string? Game = null, string? RomHash = null,
    Guid Target = default, Guid Session = default, long Transfer = 0, byte Value = 0, bool InternalClock = false,
    LinkLobbyState? State = null, string? Error = null, string? AccessToken = null, bool AutoMatch = true);

internal static class LinkProtocol
{
    internal const int MaximumPacket = 32_768;
    internal static async Task<LinkPacket?> ReadAsync(Stream stream, CancellationToken token)
    {
        byte[] header = new byte[4];
        int first = await stream.ReadAsync(header.AsMemory(0, 1), token);
        if (first == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(1), token);
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is < 2 or > MaximumPacket) throw new InvalidDataException("Invalid link packet length.");
        byte[] body = new byte[length]; await stream.ReadExactlyAsync(body, token);
        var packet = JsonSerializer.Deserialize<LinkPacket>(body) ?? throw new InvalidDataException("Missing link packet.");
        if (packet.Version != 1) throw new InvalidDataException("Incompatible link protocol version.");
        return packet;
    }
    internal static async Task WriteAsync(Stream stream, LinkPacket packet, CancellationToken token)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(packet);
        if (body.Length > MaximumPacket) throw new InvalidDataException("Link packet exceeds its limit.");
        byte[] frame = new byte[4 + body.Length]; BinaryPrimitives.WriteInt32BigEndian(frame, body.Length);
        body.CopyTo(frame, 4);
        await stream.WriteAsync(frame, token);
    }
    public static string GameKey(string title) => new string(title.Where(c => c is >= ' ' and <= '~').Take(16).ToArray()).Trim().ToUpperInvariant() is { Length: > 0 } key ? key : "UNTITLED";
}
