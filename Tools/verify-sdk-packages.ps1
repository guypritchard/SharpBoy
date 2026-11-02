param(
    [string]$RgbdsBin = $env:GB_RGBDS_BIN,
    [string]$PackageVersion = '0.1.0-alpha.1'
)

$ErrorActionPreference = 'Stop'
if (-not $RgbdsBin) { throw 'Supply -RgbdsBin or set GB_RGBDS_BIN (RGBDS 0.9.0).' }
$RgbdsBin = (Resolve-Path $RgbdsBin).Path
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$root = Join-Path ([IO.Path]::GetTempPath()) ("sharpboy-sdk-" + [guid]::NewGuid().ToString('N'))
$feed = Join-Path $root 'feed'
$toolPath = Join-Path $root 'tools'
$hive = Join-Path $root 'template-hive'
$previousPath = $env:PATH
$installed = $false

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') failed ($LASTEXITCODE)" }
}

function Assert-PackageEntries([string]$package, [string[]]$required, [string[]]$forbidden) {
    $zip = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        $entries = @($zip.Entries | ForEach-Object FullName)
        foreach ($name in $required) {
            if (-not @($entries | Where-Object { $_ -like $name }).Count) { throw "$package missing $name" }
        }
        foreach ($name in $forbidden) {
            if (@($entries | Where-Object { $_ -like $name }).Count) { throw "$package unexpectedly contains $name" }
        }
    } finally { $zip.Dispose() }
}

try {
    New-Item -ItemType Directory -Path $feed -Force | Out-Null
    foreach ($project in 'GB.Sdk', 'GB.Compiler.Cli', 'GB.Sdk.Templates') {
        Invoke-Dotnet pack (Join-Path $repo "$project/$project.csproj") -c Release -o $feed "-p:Version=$PackageVersion"
    }
    $sdk = Join-Path $feed "GB.Sdk.$PackageVersion.nupkg"
    $cli = Join-Path $feed "GB.Compiler.Cli.$PackageVersion.nupkg"
    $templates = Join-Path $feed "GB.Sdk.Templates.$PackageVersion.nupkg"
    Assert-PackageEntries $sdk @('lib/net10.0/GB.Sdk.dll', 'lib/net10.0/GB.Sdk.xml') @('*Samples*', '*background.2bpp', '*sprite.2bpp')
    Assert-PackageEntries $cli @('tools/net10.0/any/GB.Compiler.Cli.dll', 'tools/net10.0/any/GB.Compiler.dll', 'tools/net10.0/any/GB.Sdk.dll', 'tools/net10.0/any/Microsoft.CodeAnalysis.dll', 'tools/net10.0/any/Microsoft.CodeAnalysis.CSharp.dll') @('*Samples*')
    $templateEntries = @('content/background/Assets/background.2bpp', 'content/background/Assets/background.tilemap', 'content/sprites/Assets/background.2bpp', 'content/sprites/Assets/background.tilemap', 'content/sprites/Assets/sprite.2bpp')
    foreach ($name in 'blank', 'background', 'sprites') {
        foreach ($file in '.template.config/template.json', 'TemplateProject.csproj', 'Game.cs', 'README.md', 'build.ps1', 'rom-title.txt') {
            $templateEntries += "content/$name/$file"
        }
    }
    Assert-PackageEntries $templates $templateEntries @('lib/*.dll', 'tools/*.dll')

    Invoke-Dotnet tool install GB.Compiler.Cli --version $PackageVersion --tool-path $toolPath --add-source $feed
    $env:PATH = "$toolPath$([IO.Path]::PathSeparator)$previousPath"
    & (Join-Path $toolPath 'gbc') --rgbds-bin $RgbdsBin --check-rgbds
    if ($LASTEXITCODE -ne 0) { throw 'Installed gbc cannot find RGBDS 0.9.0.' }

    # The custom hive prevents altering the user template cache. Uninstall in finally
    # even when a generated project or ROM fails to compile.
    Invoke-Dotnet new install $templates --debug:custom-hive $hive
    $installed = $true
    foreach ($item in @(
        @{ ShortName = 'gb-dmg'; Name = 'BlankSmoke'; Title = 'DMG & TEST'; Size = 32768 },
        @{ ShortName = 'gb-dmg-background'; Name = 'BackgroundSmoke'; Title = 'BackgroundSmoke'; Size = 32768 },
        @{ ShortName = 'gb-dmg-sprites'; Name = 'SpritesSmoke'; Title = 'SpritesSmoke'; Size = 32768 }
    )) {
        $destination = Join-Path $root $item.Name
        Invoke-Dotnet new $item.ShortName --debug:custom-hive $hive --name $item.Name --output $destination --romTitle $item.Title --sdkVersion $PackageVersion
        $project = Join-Path $destination "$($item.Name).csproj"
        Invoke-Dotnet restore $project --source $feed
        Invoke-Dotnet build $project -c Release --no-restore
        Push-Location $destination
        try {
            & ./build.ps1 -RgbdsBin $RgbdsBin
            if ($LASTEXITCODE -ne 0) { throw "ROM build failed for $($item.Name)" }
        } finally { Pop-Location }
        $base = Join-Path $destination "out/$($item.Name)"
        foreach ($suffix in '.gb', '.asm', '.map', '.sym', '.memory.txt') {
            if (-not (Test-Path "$base$suffix")) { throw "Missing $base$suffix" }
        }
        if ((Get-Item "$base.gb").Length -ne $item.Size) { throw "Incorrect ROM length for $($item.Name)" }
        $rom = [IO.File]::ReadAllBytes("$base.gb")
        $titleBytes = [Text.Encoding]::ASCII.GetBytes($item.Title)
        if ($titleBytes.Length -gt 16) { throw "ROM title too long for $($item.Name)" }
        for ($i = 0; $i -lt $titleBytes.Length; $i++) {
            if ($rom[0x134 + $i] -ne $titleBytes[$i]) { throw "Incorrect ROM title for $($item.Name) at byte $i" }
        }
        if ($titleBytes.Length -lt 16 -and $rom[0x134 + $titleBytes.Length] -ne 0) {
            throw "ROM title is not zero-terminated for $($item.Name)"
        }
        Write-Host "$($item.ShortName): generated, restored, built, ROM $($item.Size) bytes"
    }
    Write-Host 'All three isolated SDK packages passed consumer verification.'
} finally {
    $env:PATH = $previousPath
    if ($installed) {
        & dotnet new uninstall GB.Sdk.Templates --debug:custom-hive $hive
    }
    if (Test-Path $toolPath) {
        & dotnet tool uninstall GB.Compiler.Cli --tool-path $toolPath
    }
    if (Test-Path $root) { Remove-Item $root -Recurse -Force }
}
