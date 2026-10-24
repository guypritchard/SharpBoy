using GB.Emulator.Core;

namespace GB.Emulator.Hosting;

/// <summary>File dialogs, browser uploads and remote storage can supply cartridges through the same boundary.</summary>
public interface ICartridgeSource
{
    string? Name { get; }
    Task<Cartridge> LoadAsync(CancellationToken cancellationToken);
}

public sealed class FileCartridgeSource(string path) : ICartridgeSource
{
    public string Name { get; } = Path.GetFullPath(path);
    public async Task<Cartridge> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Cartridge cartridge = await CartridgeLoader.Load(this.Name);
        cancellationToken.ThrowIfCancellationRequested();
        return cartridge;
    }
}
