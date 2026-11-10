<#
.SYNOPSIS
  Builds the Device Check Agent release: installer, portable ZIPs, checksums and latest.json.

.DESCRIPTION
  Runs the tests, then publishes into agent/dist:
    device-check-agent-<ver>-setup.exe                     per-user installer (Inno Setup 6, with -Installer)
    device-check-agent-<ver>-<rid>-portable.zip            DeviceCheck.Agent.exe (tray) with the .NET runtime inside
    device-check-agent-<ver>-<rid>-framework.zip           tray + console, needs .NET 10 Desktop and ASP.NET Core runtimes
    device-check-agent-cli-<ver>-<rid>.zip                 console collector (hardware + storage), runtime inside
    SHA256SUMS.txt, latest.json                            checksums and the manifest the site reads

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File agent/build/publish.ps1 -Installer
  powershell -ExecutionPolicy Bypass -File agent/build/publish.ps1 -Version 0.2.1 -SkipTests
#>
param(
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release',
    [string]$Version,
    [switch]$Installer,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tray = Join-Path $root 'src/DeviceCheck.Agent.UI/DeviceCheck.Agent.UI.csproj'
$cli = Join-Path $root 'src/DeviceCheck.Agent.Cli/DeviceCheck.Agent.Cli.csproj'
$dist = Join-Path $root 'dist'
if (-not $Version) { $Version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version }
$versionArgs = @("-p:Version=$Version")

function Invoke-Dotnet {
    & dotnet @args | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') failed with exit code $LASTEXITCODE" }
}

if (-not $SkipTests) {
    Invoke-Dotnet test (Join-Path $root 'tests/DeviceCheck.Agent.Tests') -c $Configuration --nologo @versionArgs
}

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist | Out-Null

$singleFile = @('--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true')
$framework = @('--self-contained', 'false', '-p:PublishSingleFile=true')
$files = @()

function New-Zip([string]$name, [string]$kind, [string[]]$projects, [string[]]$publishArgs) {
    $out = Join-Path $dist ('publish-' + [IO.Path]::GetFileNameWithoutExtension($name))
    foreach ($proj in $projects) {
        Invoke-Dotnet publish $proj -c $Configuration -r $Runtime -o $out --nologo '-p:DebugType=none' @versionArgs @publishArgs
    }
    Copy-Item (Join-Path $root 'README.md') $out
    $zip = Join-Path $dist $name
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -CompressionLevel Optimal
    return @{ kind = $kind; path = $zip }
}

$files += New-Zip "device-check-agent-$Version-$Runtime-portable.zip" 'portable' @($tray) $singleFile
$files += New-Zip "device-check-agent-$Version-$Runtime-framework.zip" 'framework' @($tray, $cli) $framework
$files += New-Zip "device-check-agent-cli-$Version-$Runtime.zip" 'cli' @($cli) $singleFile

if ($Installer) {
    # Installer layout: tray + console in one folder sharing one self-contained runtime (no single-file extraction).
    $layout = Join-Path $dist 'installer-root'
    foreach ($proj in @($tray, $cli)) {
        Invoke-Dotnet publish $proj -c $Configuration -r $Runtime -o $layout --nologo '-p:DebugType=none' --self-contained true @versionArgs
    }
    Copy-Item (Join-Path $root 'README.md') $layout
    $iscc = @(
        (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source,
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) not found. Install it or run without -Installer.' }
    & $iscc /Q "/DAppVersion=$Version" "/DSourceDir=$layout" "/DOutputDir=$dist" (Join-Path $root 'installer/DeviceCheckAgent.iss')
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }
    $files = @(@{ kind = 'installer'; path = (Join-Path $dist "device-check-agent-$Version-setup.exe") }) + $files
}

# Checksums + manifest for the download page (latest.json).
$entries = foreach ($f in $files) {
    $item = Get-Item $f.path
    [ordered]@{
        kind = $f.kind
        name = $item.Name
        size_bytes = $item.Length
        sha256 = (Get-FileHash $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
($entries | ForEach-Object { "$($_.sha256)  $($_.name)" }) -join "`n" | Set-Content -Encoding ascii -NoNewline (Join-Path $dist 'SHA256SUMS.txt')
$manifest = [ordered]@{
    schema_version = '1.0'
    version = $Version
    released_at = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    runtime = $Runtime
    release_url = "https://github.com/BlkDem/device-check/releases/tag/v$Version"
    files = @($entries)
}
[IO.File]::WriteAllText((Join-Path $dist 'latest.json'), ($manifest | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding $false))

foreach ($e in $entries) { '{0,-58} {1,8:N1} MB' -f $e.name, ($e.size_bytes / 1MB) }
