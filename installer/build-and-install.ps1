#requires -Version 5.1
<#
.SYNOPSIS
Builds the per-user setup from current source and reinstalls it silently.
.DESCRIPTION
Closes a running Octa Files, publishes and packs with installer\build-setup.ps1, then runs the
resulting setup with /S so %LOCALAPPDATA%\Programs\Octa Files matches the working tree.
Fails when the installed OctaFiles.dll differs from the packed payload.
#>
param(
    [string]$Configuration = 'Release',
    [string]$Platform = 'x64',
    [string]$Version = '4.3.3.0',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifacts = Join-Path $repo 'artifacts\installer'
$payloadDll = Join-Path $artifacts 'payload\OctaFiles.dll'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\Octa Files'
$installedDll = Join-Path $installDir 'OctaFiles.dll'

# build-setup.ps1 resolves dotnet from PATH, which can find a global SDK that does not satisfy global.json.
$dotnet = Join-Path $repo '.dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { throw "Repo-local SDK not found at $dotnet." }
$env:PATH = (Split-Path -LiteralPath $dotnet) + ';' + $env:PATH

# NSIS is built with AllowSkipFiles off, so a running instance makes a silent install fail.
foreach ($running in Get-Process -Name OctaFiles -ErrorAction SilentlyContinue) {
    Write-Output "CLOSING: OctaFiles ($($running.Id))"
    if (-not $running.CloseMainWindow() -or -not $running.WaitForExit(10000)) {
        $running.Kill()
        [void]$running.WaitForExit(10000)
    }
}

$buildArgs = @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass',
    '-File', (Join-Path $PSScriptRoot 'build-setup.ps1'),
    '-Configuration', $Configuration, '-Platform', $Platform, '-Version', $Version
)
if ($SkipBuild) { $buildArgs += '-SkipBuild' }

& powershell.exe @buildArgs
if ($LASTEXITCODE -ne 0) { throw "build-setup.ps1 exited with $LASTEXITCODE." }

$setup = Get-ChildItem -LiteralPath $artifacts -Filter 'OctaFilesSetup-*-x64.exe' -File |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1
if (-not $setup) { throw "No setup exe found in $artifacts." }
if ((Get-Item -LiteralPath $payloadDll).LastWriteTimeUtc -gt $setup.LastWriteTimeUtc) { throw "Refusing to install a stale setup: $($setup.FullName)." }

Write-Output "INSTALLING: $($setup.FullName)"
$install = Start-Process -FilePath $setup.FullName -ArgumentList '/S' -Wait -PassThru
if ($install.ExitCode -ne 0) { throw "Setup exited with $($install.ExitCode)." }

if (-not (Test-Path -LiteralPath $installedDll -PathType Leaf)) { throw "Installed OctaFiles.dll not found at $installedDll." }

# NSIS preserves payload timestamps, so compare content instead of LastWriteTime.
if ((Get-FileHash -LiteralPath $installedDll -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $payloadDll -Algorithm SHA256).Hash) {
    throw "Installed OctaFiles.dll does not match the payload; the install did not replace it."
}

$result = Get-Item -LiteralPath $installedDll
Write-Output "INSTALLED: $($result.FullName)"
Write-Output "UPDATED: $($result.LastWriteTime)"
