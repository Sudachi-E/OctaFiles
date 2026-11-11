#requires -Version 5.1
<#
.SYNOPSIS
Publishes the unpackaged app layout and packs it into a per-user setup.exe with NSIS.
.DESCRIPTION
Runs a self-contained, portable publish of Files.App, then writes
artifacts\installer\OctaFilesSetup-<version>-x64.exe. No package registration is performed.
#>
param(
    [string]$Configuration = 'Release',
    [string]$Platform = 'x64',
    [string]$Version = '4.3.3.0',
    [string]$PayloadDir,
    [string]$NsisPath,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifacts = Join-Path $repo 'artifacts\installer'
if (-not $PayloadDir) { $PayloadDir = Join-Path $artifacts 'payload' }

$appProject = Join-Path $repo 'src\Files.App\Files.App.csproj'
$payloadExe = Join-Path $PayloadDir 'OctaFiles.exe'

# Newest source file that a published payload must reflect.
function Get-NewestSourceFile {
    $roots = 'src\Files.App', 'src\Files.App.Server', 'src\Files.App.Controls', 'src\Files.Shared', 'src\Files.Storage' |
        ForEach-Object { Join-Path $repo $_ }
    Get-ChildItem -Path $roots -Recurse -File -Force -Include *.cs, *.xaml, *.csproj, *.props, *.targets, *.resw, *.manifest |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
}

if ($SkipBuild) {
    if (-not (Test-Path -LiteralPath $payloadExe)) { throw "OctaFiles.exe not found in $PayloadDir" }

    $newestSource = Get-NewestSourceFile
    if ($newestSource -and $newestSource.LastWriteTimeUtc -gt (Get-Item -LiteralPath $payloadExe).LastWriteTimeUtc) {
        throw "Refusing to pack a stale payload: $($newestSource.FullName) is newer than $payloadExe. Rerun without -SkipBuild."
    }
}
else {
    if (Test-Path $PayloadDir) { Remove-Item $PayloadDir -Recurse -Force }

    # Generated XAML survives incremental builds after its page is deleted and would be published again.
    foreach ($project in 'Files.App', 'Files.App.Controls', 'Files.App.Server') {
        foreach ($relative in "bin\$Platform\$Configuration", "obj\$Platform\$Configuration") {
            $path = Join-Path $repo "src\$project\$relative"
            if (Test-Path -LiteralPath $path) { Remove-Item $path -Recurse -Force }
        }
    }

    & dotnet publish $appProject `
        -c $Configuration `
        -p:Platform=$Platform `
        -p:FilesPortable=true `
        -p:PublishAot=false `
        -p:SelfContained=true `
        -o $PayloadDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish exited with $LASTEXITCODE." }
}

if (-not $NsisPath) {
    $command = Get-Command makensis.exe -ErrorAction SilentlyContinue
    $candidates = @(
        $(if ($command) { $command.Source }),
        (Join-Path $repo 'artifacts\tools\NSIS\makensis.exe'),
        $(if (${env:ProgramFiles(x86)}) { Join-Path ${env:ProgramFiles(x86)} 'NSIS\makensis.exe' }),
        $(if ($env:ProgramFiles) { Join-Path $env:ProgramFiles 'NSIS\makensis.exe' })
    )
    $NsisPath = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } |
        Select-Object -First 1
}
if (-not $NsisPath) { throw 'makensis.exe not found.' }

$files = @(Get-ChildItem -LiteralPath $PayloadDir -File -Recurse -Force)
$sizeKB = [long][Math]::Ceiling(($files | Measure-Object -Property Length -Sum).Sum / 1KB)
$output = Join-Path $artifacts "OctaFilesSetup-$Version-x64.exe"

$scratch = Join-Path $artifacts '.setup-tmp'
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$savedTemp = $env:TEMP
$savedTmp = $env:TMP
try {
    $env:TEMP = $scratch
    $env:TMP = $scratch
    & $NsisPath /NOCONFIG /INPUTCHARSET UTF8 /WX /V3 "/DPAYLOAD=$PayloadDir" "/DOUTPUT=$output" `
        "/DVERSION=$Version" "/DESTIMATED_SIZE=$sizeKB" (Join-Path $PSScriptRoot 'OctaFilesSetup.nsi')
    if ($LASTEXITCODE -ne 0) { throw "makensis exited with $LASTEXITCODE." }
}
finally {
    $env:TEMP = $savedTemp
    $env:TMP = $savedTmp
}

$result = Get-Item -LiteralPath $output
Write-Output "INSTALLER: $($result.FullName)"
Write-Output "SIZE: $($result.Length) bytes ($([Math]::Round($result.Length / 1MB, 2)) MiB)"
