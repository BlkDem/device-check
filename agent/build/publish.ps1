<#
.SYNOPSIS
  Builds the portable Device Check Agent ZIPs.

.DESCRIPTION
  Runs the tests, then publishes into agent/dist:
    device-check-agent-<ver>-<rid>-self-contained.zip      DeviceCheck.Agent.exe (tray app) with the .NET runtime inside - runs anywhere
    device-check-agent-<ver>-<rid>-framework.zip           DeviceCheck.Agent.exe + DeviceCheck.Agent.Cli.exe - needs .NET 10
                                                            Desktop Runtime and ASP.NET Core Runtime installed
    device-check-agent-cli-<ver>-<rid>-self-contained.zip  DeviceCheck.Agent.Cli.exe (console collector) with the runtime inside

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File agent/build/publish.ps1
  powershell -ExecutionPolicy Bypass -File agent/build/publish.ps1 -Runtime win-arm64 -SkipTests
#>
param(
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tray = Join-Path $root 'src/DeviceCheck.Agent.UI/DeviceCheck.Agent.UI.csproj'
$cli = Join-Path $root 'src/DeviceCheck.Agent.Cli/DeviceCheck.Agent.Cli.csproj'
$dist = Join-Path $root 'dist'
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') failed with exit code $LASTEXITCODE" }
}

if (-not $SkipTests) {
    Invoke-Dotnet test (Join-Path $root 'tests/DeviceCheck.Agent.Tests') -c $Configuration --nologo
}

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist | Out-Null

$selfContained = @('--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true')
$framework = @('--self-contained', 'false', '-p:PublishSingleFile=true')

$packages = @(
    @{ Zip = "device-check-agent-$version-$Runtime-self-contained.zip";     Projects = @($tray);       Args = $selfContained },
    @{ Zip = "device-check-agent-$version-$Runtime-framework.zip";          Projects = @($tray, $cli); Args = $framework },
    @{ Zip = "device-check-agent-cli-$version-$Runtime-self-contained.zip"; Projects = @($cli);        Args = $selfContained }
)

foreach ($p in $packages) {
    $out = Join-Path $dist ('publish-' + [IO.Path]::GetFileNameWithoutExtension($p.Zip))
    foreach ($proj in $p.Projects) {
        Invoke-Dotnet publish $proj -c $Configuration -r $Runtime -o $out --nologo -p:DebugType=none @($p.Args)
    }
    Copy-Item (Join-Path $root 'README.md') $out
    $zip = Join-Path $dist $p.Zip
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -CompressionLevel Optimal
    '{0,-70} {1,8:N1} MB' -f $p.Zip, ((Get-Item $zip).Length / 1MB)
}
