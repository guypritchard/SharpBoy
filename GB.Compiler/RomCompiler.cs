using System.Text;
using System.Text.RegularExpressions;

namespace GB.Compiler;

public sealed record RomBuildOptions(string OutputPath, string Title = "SHARPBOY", string? RgbdsBinDirectory = null,
    bool KeepIntermediates = false, CartridgeProfile? Profile = null, GameAssetBundle? Assets = null, Sm83Limits? Limits = null);

public sealed record RomBuildResult(IReadOnlyList<string> Diagnostics, IReadOnlyDictionary<string, string> Artifacts,
    string? WorkDirectory = null)
{
    public bool Success => Diagnostics.Count == 0;
    /// <summary>Non-fatal post-publication issues; artifacts remain valid.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>Filesystem boundary for artifact replacement and work-directory cleanup.</summary>
public interface IRomArtifactFileSystem
{
    bool Exists(string path);
    void Copy(string source, string destination, bool overwrite = false);
    void Move(string source, string destination, bool overwrite);
    void Delete(string path);
    void DeleteDirectory(string path);
}

public sealed class SystemRomArtifactFileSystem : IRomArtifactFileSystem
{
    public bool Exists(string path) => File.Exists(path);
    public void Copy(string source, string destination, bool overwrite = false) => File.Copy(source, destination, overwrite);
    public void Move(string source, string destination, bool overwrite) => File.Move(source, destination, overwrite);
    public void Delete(string path) => File.Delete(path);
    public void DeleteDirectory(string path) => Directory.Delete(path, true);
}

/// <summary>Build/tool/validation failures never publish; caught publication failures roll back the previous set.
/// Sibling replacement is not atomic across OS/process termination: a crash during replacement may expose a mixed set.</summary>
public sealed class RomCompiler(IToolRunner? runner = null, IRomArtifactFileSystem? artifactFileSystem = null)
{
    private readonly IToolRunner tools = runner ?? new SystemToolRunner();
    private readonly IRomArtifactFileSystem files = artifactFileSystem ?? new SystemRomArtifactFileSystem();

    public async Task<RomBuildResult> CompileAsync(IEnumerable<string> sourcePaths, RomBuildOptions options,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        RomBuildResult Fail(string message, string? work = null) => new([.. errors, message],
            new Dictionary<string, string>(), work);
        if (options is null || string.IsNullOrWhiteSpace(options.OutputPath) ||
            !options.OutputPath.EndsWith(".gb", StringComparison.OrdinalIgnoreCase))
            return Fail("GBC003: --out must specify a .gb file.");
        if (string.IsNullOrWhiteSpace(options.Title) || options.Title.Length > 16 ||
            options.Title.Any(c => c < ' ' || c > '~'))
            return Fail("GBC003: title must contain 1-16 printable ASCII characters.");
        var profile = options.Profile ?? CartridgeProfile.RomOnly;
        var paths = sourcePaths?.ToArray() ?? [];
        if (paths.Length == 0 || paths.Any(p => !p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) ||
            paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
            return Fail("GBC003: provide distinct, explicit .cs source files.");
        var sources = new List<(string Path, string Text)>();
        foreach (var path in paths)
        {
            try { sources.Add((Path.GetFullPath(path), await File.ReadAllTextAsync(path, cancellationToken))); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { return Fail($"{path}: error GBC001: Cannot read source file: {ex.Message}"); }
        }
        var frontend = GameFrontend.Analyze(sources);
        if (!frontend.Success) return new(frontend.Diagnostics.Select(d => d.ToString()).ToArray(), new Dictionary<string, string>());
        IrModule module;
        try { module = GameIrCompiler.Lower(frontend); }
        catch (IrVerificationException ex) { return Fail($"GBC004: IR: {ex.Message}"); }
        var backend = Sm83Backend.Emit(module, limits: options.Limits, profile: profile);
        if (!backend.Success)
            return new(backend.Diagnostics.Select(d => $"{d.Source?.Path}({d.Source?.Line},{d.Source?.Column}): {d}").ToArray(),
                new Dictionary<string, string>());
        var assets = options.Assets ?? new GameAssetBundle();
        var assetIssues = assets.Validate(backend.Layout!);
        if (assetIssues.Count != 0) return new(assetIssues, new Dictionary<string, string>());
        var output = Path.GetFullPath(options.OutputPath);
        var targetDir = Path.GetDirectoryName(output)!;
        var baseName = Path.GetFileNameWithoutExtension(output);
        string? workDir = null;
        bool retainWorkDirectory = false;
        async Task<RomBuildResult> BuildAsync()
        {
          try
          {
            Directory.CreateDirectory(targetDir);
            workDir = Path.Combine(targetDir, $".{baseName}.build-{Guid.NewGuid():N}");
            Directory.CreateDirectory(workDir);
            var probe = await new RgbdsProbe(tools, options.RgbdsBinDirectory).CheckAsync(cancellationToken);
            if (probe.Count != 0) return new(probe, new Dictionary<string, string>(), options.KeepIntermediates ? workDir : null);
            var runtime = RuntimeAssembly.Create(backend.Layout!);
            // Bank-copy code must be emitted while the generated ROM0 section is still current;
            // switching to the ROMX asset sections first would place executable code in bank 1.
            var assembly = runtime + Environment.NewLine + backend.Assembly +
                (profile.IsMbc1 ? Environment.NewLine + RuntimeAssembly.Banking() : "") +
                Environment.NewLine + assets.Assembly() +
                (profile.IsMbc1 ? Environment.NewLine + profile.Assembly() : "");
            await File.WriteAllTextAsync(Path.Combine(workDir, "game.asm"), assembly, cancellationToken);
             await File.WriteAllTextAsync(Path.Combine(workDir, "game.memory.txt"), backend.MemoryReport! +
                 assets.Report() + profile.Report(assets.BankOneBytes), cancellationToken);

            async Task<string?> Run(string tool, params string[] args)
            {
                var executable = RgbdsProbe.Resolve(tool, options.RgbdsBinDirectory);
                try
                {
                    var result = await tools.RunAsync(executable, args, workDir, cancellationToken);
                    return result.ExitCode == 0 ? null :
                        $"GBT003: {tool} {string.Join(' ', args)} exited {result.ExitCode}: {result.StandardError.Trim()} {result.StandardOutput.Trim()} (work: {workDir}).";
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or System.ComponentModel.Win32Exception or NotSupportedException)
                { return $"GBT003: {tool} failed: {ex.Message} (work: {workDir})."; }
            }
            var error = await Run("rgbasm", "-o", "game.o", "game.asm") ??
                await Run("rgblink", "-o", "game.gb", "-m", "game.map", "-n", "game.sym", "game.o") ??
                await Run("rgbfix", "-v", "-p", "0", "-m", profile.IsMbc1 ? "1" : "0", "-r", "0",
                    "-t", options.Title, "game.gb");
            if (error is not null) return Fail(error, options.KeepIntermediates ? workDir : null);
            foreach (var name in new[] { "game.gb", "game.asm", "game.map", "game.sym", "game.memory.txt" })
                if (!File.Exists(Path.Combine(workDir, name)))
                    return Fail($"GBC005: tool did not produce {name} (work: {workDir}).", options.KeepIntermediates ? workDir : null);
            var validation = RomValidator.Validate(await File.ReadAllBytesAsync(Path.Combine(workDir, "game.gb")),
                 await File.ReadAllTextAsync(Path.Combine(workDir, "game.sym")), backend.Layout!, profile, assets);
            if (validation.Count != 0) return new(validation, new Dictionary<string, string>(), options.KeepIntermediates ? workDir : null);
            var basePath = Path.Combine(targetDir, baseName);
            var artifacts = new Dictionary<string, string>();
            var staged = new List<(string Source, string Destination, string? Backup)>();
            foreach (var suffix in new[] { ".gb", ".asm", ".map", ".sym", ".memory.txt" })
            {
                var src = Path.Combine(workDir, "stage" + suffix);
                var dst = basePath + suffix;
                var backup = files.Exists(dst) ? Path.Combine(workDir, "backup" + suffix) : null;
                staged.Add((src, dst, backup));
            }
            try
            {
                // Finish every staging copy and snapshot the entire prior generation before touching any destination.
                foreach (var (src, dst, backup) in staged)
                {
                    files.Copy(Path.Combine(workDir, "game" + Path.GetFileName(src)["stage".Length..]), src);
                    if (backup is not null) files.Copy(dst, backup);
                }
                try
                {
                    foreach (var (src, dst, _) in staged) files.Move(src, dst, true);
                }
                catch (Exception publicationError) when (publicationError is IOException or UnauthorizedAccessException)
                {
                    var rollbackErrors = new List<string>();
                    foreach (var (_, dst, backup) in staged)
                    {
                        try
                        {
                            if (backup is null) files.Delete(dst);
                            else files.Move(backup, dst, true);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        { rollbackErrors.Add($"{dst}: {ex.Message}"); }
                    }
                    if (rollbackErrors.Count != 0)
                    {
                        retainWorkDirectory = true;
                        return Fail($"GBC006: Cannot publish artifacts: {publicationError.Message}; rollback incomplete: {string.Join("; ", rollbackErrors)} (backups: {workDir}).", workDir);
                    }
                    return Fail($"GBC006: Cannot publish artifacts: {publicationError.Message}", options.KeepIntermediates ? workDir : null);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { return Fail($"GBC006: Cannot publish artifacts: {ex.Message}", options.KeepIntermediates ? workDir : null); }
            foreach (var (_, dst, _) in staged) artifacts[Path.GetExtension(dst) == ".txt" ? "memory" : Path.GetExtension(dst)[1..]] = dst;
            return new([], artifacts, options.KeepIntermediates ? workDir : null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { return Fail($"GBC005: Build I/O failed: {ex.Message}", options.KeepIntermediates ? workDir : null); }
        }
        RomBuildResult? result = null;
        try { result = await BuildAsync(); }
        finally
        {
            if (workDir is not null && !options.KeepIntermediates && !retainWorkDirectory && Directory.Exists(workDir))
                try { files.DeleteDirectory(workDir); }
                catch (Exception ex)
                {
                    if (result is not null)
                    {
                        var warning = $"GBC007: Could not remove build work directory {workDir}: {ex.Message}. Remove it manually.";
                        result = result.Success ? result with { Warnings = [.. result.Warnings, warning] }
                            : result with { Diagnostics = [.. result.Diagnostics, warning], WorkDirectory = workDir };
                    }
                }
        }
        return result!;
    }
}

public static class RuntimeAssembly
{
    public static string Create(MemoryLayout layout)
    {
        using var stream = typeof(RuntimeAssembly).Assembly.GetManifestResourceStream("GB.Compiler.Runtime.Startup.asm")
            ?? throw new InvalidOperationException("Missing embedded startup runtime.");
        using var reader = new StreamReader(stream);
        var template = reader.ReadToEnd();
        var init = new StringBuilder();
        foreach (var slot in layout.Allocations.Where(a => a.Name.StartsWith("global ", StringComparison.Ordinal)))
            for (var i = 0; i < slot.Bytes; i++)
            {
                init.AppendLine($"    ld a, ${(byte)(slot.InitialBits!.Value >> (8 * i)):X2}");
                init.AppendLine($"    ld [${slot.Address + i:X4}], a ; {slot.Name} byte {i}");
            }
        template = template.Replace("; GB_GLOBAL_INITIALIZERS", init.ToString(), StringComparison.Ordinal);
        if (layout.UsesBanking)
        {
            template = template.Replace("; GB_MBC1_INITIALIZERS", "    xor a\n    ld [$0000], a ; RAM disabled\n    ld [$6000], a ; mode 0\n    ld [$4000], a ; upper bank bits 0\n    inc a\n    ld [$2000], a ; bank 1\n    ld [gb_current_bank], a", StringComparison.Ordinal);
        }
        string Include(string name)
        {
            using var stream = typeof(RuntimeAssembly).Assembly.GetManifestResourceStream($"GB.Compiler.Runtime.{name}.asm")
                ?? throw new InvalidOperationException($"Missing embedded {name} runtime.");
            using var reader = new StreamReader(stream);
            return Environment.NewLine + reader.ReadToEnd();
        }
        return (layout.UsesBackground ? "DEF GB_HAS_BACKGROUND EQU 1\n" : "") +
            (layout.UsesSpriteInit ? "DEF GB_HAS_SPRITE_INIT EQU 1\n" : "") +
            template + (layout.UsesDisplay ? Include("Display") : "") +
            (layout.UsesSprites ? Include("Sprites") : "");
    }

    public static string Banking()
    {
        using var stream = typeof(RuntimeAssembly).Assembly.GetManifestResourceStream("GB.Compiler.Runtime.Banking.asm")
            ?? throw new InvalidOperationException("Missing embedded MBC1 runtime.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

public static class RomValidator
{
    private static readonly byte[] Logo = [0xCE, 0xED, 0x66, 0x66, 0xCC, 0x0D, 0x00, 0x0B,
        0x03, 0x73, 0x00, 0x83, 0x00, 0x0C, 0x00, 0x0D, 0x00, 0x08, 0x11, 0x1F,
        0x88, 0x89, 0x00, 0x0E, 0xDC, 0xCC, 0x6E, 0xE6, 0xDD, 0xDD, 0xD9, 0x99,
        0xBB, 0xBB, 0x67, 0x63, 0x6E, 0x0E, 0xEC, 0xCC, 0xDD, 0xDC, 0x99, 0x9F,
        0xBB, 0xB9, 0x33, 0x3E];

    public static IReadOnlyList<string> Validate(byte[] rom, string symbols, MemoryLayout layout, CartridgeProfile? profile = null, GameAssetBundle? assets = null)
    {
        profile ??= CartridgeProfile.RomOnly;
        assets ??= new GameAssetBundle();
        var issues = new List<string>();
        issues.AddRange(assets.Validate(layout));
        if (issues.Count != 0) return issues;
        if (rom.Length != profile.Banks * 0x4000) return [$"GBR001: ROM must be exactly {profile.Banks * 0x4000} bytes ({profile.Banks * 16} KiB)."];
        if (!rom.AsSpan(0x104, 48).SequenceEqual(Logo)) issues.Add("GBR002: Nintendo logo mismatch.");
        if (rom[0x100] != 0 || rom[0x101] != 0xC3 || rom[0x102] != 0x50 || rom[0x103] != 0x01)
            issues.Add("GBR003: entry jump/header boundaries are invalid.");
        if (rom[0x147] != (profile.IsMbc1 ? 1 : 0) || rom[0x148] != profile.RomSizeCode || rom[0x149] != 0)
            issues.Add("GBR004: mapper, ROM capacity or RAM header mismatch.");
        int header = 0;
        for (var i = 0x134; i <= 0x14C; i++) header = (header - rom[i] - 1) & 0xFF;
        if (rom[0x14D] != header) issues.Add("GBR005: header checksum mismatch.");
        int global = 0;
        for (var i = 0; i < rom.Length; i++) if (i is not (0x14E or 0x14F)) global = (global + rom[i]) & 0xFFFF;
        if (rom[0x14E] != (global >> 8) || rom[0x14F] != (global & 0xFF))
            issues.Add("GBR006: global checksum mismatch.");
        var addresses = new Dictionary<string, (int Bank, int Address)>(StringComparer.Ordinal);
        // RGBDS .sym expands local labels to their qualified parent.local name.
        // Keep that scope in the key: .loop under two different routines is not
        // a duplicate, and a local must never satisfy a required global symbol.
        foreach (Match match in Regex.Matches(symbols, @"(?m)^[ \t]*(?:(?<bank>[0-9a-fA-F]{2}):)?(?<address>[0-9a-fA-F]{4})[ \t]+(?<name>gb_[A-Za-z0-9_]+(?:\.[A-Za-z_][A-Za-z0-9_]*)?)[ \t]*\r?$"))
        {
            var name = match.Groups["name"].Value;
            if (addresses.ContainsKey(name)) issues.Add($"GBR007: duplicate linker symbol {name}.");
            addresses[name] = (match.Groups["bank"].Success ? Convert.ToInt32(match.Groups["bank"].Value, 16) : -1,
                Convert.ToInt32(match.Groups["address"].Value, 16));
        }
        foreach (var (name, expected) in new[] { ("gb_start", 0x150), ("gb_stack_top", layout.StackTop),
            ("gb_stack_bottom", layout.StackBottom) })
            if (!addresses.TryGetValue(name, out var address) || address.Address != expected ||
                (name == "gb_start" && address.Bank != 0) ||
                (name == "gb_stack_bottom" && address.Bank != (expected < 0xD000 ? 0 : 1)))
                issues.Add($"GBR007: linker symbol {name} must be ${expected:X4}.");
        if (!addresses.TryGetValue("gb_entry", out var entry) || entry.Bank != 0 || entry.Address < 0x150 || entry.Address >= 0x4000)
            issues.Add("GBR007: generated entry must be in ROM0 after the header.");
        foreach (var slot in layout.Allocations)
            if (!addresses.TryGetValue($"gb_ram_{slot.Address:X4}", out var address) ||
                address.Address != slot.Address || address.Bank != (slot.Address < 0xD000 ? 0 : 1) ||
                address.Address + slot.Bytes > layout.StackBottom || address.Address < layout.WramStart)
                issues.Add($"GBR007: linker WRAM slot ${slot.Address:X4} exceeds the planned range.");
        if (layout.UsesBackground)
        {
            foreach (var (name, bank, location) in new[] { ("gb_bg_tiles", 1, 0x4000),
                ("gb_bg_map", 1, 0x4000 + assets.BackgroundTiles.Length),
                ("gb_bg_end", 1, 0x4000 + assets.BackgroundTiles.Length + assets.BackgroundMap.Length) })
                if (!addresses.TryGetValue(name, out var symbol) || symbol.Bank != bank || symbol.Address != location)
                    issues.Add($"GBR007: background linker symbol {name} must be bank {bank} ${location:X4}.");
            if (!rom.AsSpan(0x4000, assets.BackgroundTiles.Length).SequenceEqual(assets.BackgroundTiles.AsSpan()) ||
                !rom.AsSpan(0x4000 + assets.BackgroundTiles.Length, assets.BackgroundMap.Length).SequenceEqual(assets.BackgroundMap.AsSpan()))
                issues.Add("GBR008: embedded background tile/map bytes mismatch.");
        }
        if (layout.UsesDisplay)
        {
            foreach (var name in new[] { "gb_display_init", "gb_wait_next_vblank" })
                if ((name != "gb_display_init" || layout.UsesBackground) &&
                    (!addresses.TryGetValue(name, out var symbol) || symbol.Bank != 0 || symbol.Address < 0x150 || symbol.Address >= 0x4000))
                    issues.Add($"GBR007: native LCD helper {name} must be in ROM0.");
        }
        if (layout.UsesSpriteInit)
        {
            var address = assets.SpriteAddress;
            foreach (var (name, expected) in new[] { ("gb_sprite_tile", address),
                ("gb_sprite_end", address + assets.SpriteTiles.Length) })
                if (!addresses.TryGetValue(name, out var symbol) || symbol.Bank != 1 || symbol.Address != expected)
                    issues.Add($"GBR007: sprite linker symbol {name} must be bank 1 ${expected:X4}.");
            if (!rom.AsSpan(address, assets.SpriteTiles.Length).SequenceEqual(assets.SpriteTiles.AsSpan()))
                issues.Add("GBR008: embedded sprite tile bytes mismatch.");
        }
        if (layout.UsesSprites)
        {
            foreach (var name in new[] { "gb_sprite_init", "gb_wait_oam_safe" })
                if ((name != "gb_sprite_init" || layout.UsesSpriteInit) &&
                    (!addresses.TryGetValue(name, out var symbol) || symbol.Bank != 0 || symbol.Address < 0x150 || symbol.Address >= 0x4000))
                    issues.Add($"GBR007: native OBJ helper {name} must be in ROM0.");
        }
        if (profile.IsMbc1)
        {
            foreach (var name in profile.NeedsCapacityGuard
                ? new[] { "gb_bank_copy", "gb_current_bank", "gb_bank_copy_target", "gb_capacity_guard" }
                : new[] { "gb_bank_copy", "gb_current_bank", "gb_bank_copy_target" })
                if (!addresses.TryGetValue(name, out var symbol) || (name == "gb_bank_copy" &&
                    (symbol.Bank != 0 || symbol.Address < 0x150 || symbol.Address >= 0x4000)) ||
                    (name == "gb_current_bank" && symbol.Address != 0xC000) ||
                    (name == "gb_bank_copy_target" && symbol.Address != 0xC001) ||
                    (name == "gb_capacity_guard" && (symbol.Bank != profile.Banks - 1 || symbol.Address != 0x7FFF)))
                    issues.Add($"GBR007: invalid MBC1 linker symbol {name}.");
            foreach (var asset in profile.Assets)
            {
                var position = asset.Bank * 0x4000 + asset.Address - 0x4000;
                if (!addresses.TryGetValue($"gb_asset_{asset.Name}", out var symbol) || symbol.Bank != asset.Bank ||
                    symbol.Address != asset.Address || !addresses.TryGetValue($"gb_asset_{asset.Name}_end", out var end) ||
                    end.Bank != asset.Bank || end.Address != asset.Address + asset.Data.Length ||
                    !rom.AsSpan(position, asset.Data.Length).SequenceEqual(asset.Data.AsSpan()))
                    issues.Add($"GBR008: asset {asset.Name} linker placement or bytes mismatch.");
            }
        }
        foreach (var symbol in addresses.Where(p => p.Key.StartsWith("gb_fn_", StringComparison.Ordinal) ||
             p.Key is "gb_entry" or "gb_bank_copy" or "gb_display_init" or "gb_wait_next_vblank" or "gb_sprite_init" or "gb_wait_oam_safe"))
            if (symbol.Value.Bank != 0 || symbol.Value.Address < 0x150 || symbol.Value.Address >= 0x4000)
                issues.Add($"GBR007: executable {symbol.Key} must reside in ROM0.");
        return issues;
    }
}
