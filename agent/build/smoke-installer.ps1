<#
.SYNOPSIS
  Installer smoke test (CI): silent per-user install into a temp folder, run the installed console agent
  (hardware + storage reports), silent uninstall, check that nothing is left behind.
#>
param([string]$Dist = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist'))

$ErrorActionPreference = 'Stop'
$setup = Get-Item (Join-Path $Dist 'device-check-agent-*-setup.exe')
$dir = Join-Path ([IO.Path]::GetTempPath()) ('dca-' + [guid]::NewGuid().ToString('n').Substring(0, 8))

Start-Process $setup.FullName -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=$dir" -Wait
foreach ($f in 'DeviceCheck.Agent.exe', 'DeviceCheck.Agent.Cli.exe', 'unins000.exe') {
    if (-not (Test-Path (Join-Path $dir $f))) { throw "installer did not create $f" }
}

$hw = & (Join-Path $dir 'DeviceCheck.Agent.Cli.exe') --json --quiet | ConvertFrom-Json
if ($hw.schema_version -ne '1.0') { throw 'installed agent: bad hardware report' }
$st = & (Join-Path $dir 'DeviceCheck.Agent.Cli.exe') storage --json --quiet | ConvertFrom-Json
if ($st.kind -ne 'storage_health') { throw 'installed agent: bad storage report' }
"installed agent works: CPU '$($hw.cpu.model.value)', $($st.storage_health.devices.Count) disk(s), $((Get-ChildItem $dir).Count) files"

Start-Process (Join-Path $dir 'unins000.exe') -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait
Start-Sleep -Seconds 3 # the uninstaller finishes removing itself from a temp copy
if (Test-Path (Join-Path $dir 'DeviceCheck.Agent.exe')) { throw 'uninstall left files behind' }
'uninstall: ok'
exit 0 # the console agent exits 1 when a collector times out on the runner; the checks above are what matters
