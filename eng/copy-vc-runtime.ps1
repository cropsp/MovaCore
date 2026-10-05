<#
.SYNOPSIS
Copies the Visual C++ runtime DLLs that the Whisper native libraries import into every runtime folder of a publish
output, so that dictation also works on PCs without the VC++ Redistributable installed.

.DESCRIPTION
A Native AOT exe does not bring the VC++ runtime, but whisper.dll and the ggml DLLs import msvcp140, vcruntime140
(vcruntime140_1 on x64) and vcomp140 (OpenMP). Whisper.net loads each runtime DLL by its full path, so Windows looks
for those imports in the DLL's own folder and in System32, not next to MovaCore.exe: the copies go into each
runtimes\... folder that holds whisper.dll. They come from the Redist folder of the newest Visual Studio, as
Microsoft allows for app-local deployment.

.EXAMPLE
./eng/copy-vc-runtime.ps1 -PublishDir out/win-x64 -Rid win-x64
#>
param(
    [Parameter(Mandatory)] [string] $PublishDir,
    [Parameter(Mandatory)] [ValidateSet('win-x64', 'win-arm64')] [string] $Rid
)
$ErrorActionPreference = 'Stop'

$arch = if ($Rid -eq 'win-arm64') { 'arm64' } else { 'x64' }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products * -property installationPath
if (-not $vs) { throw 'Visual Studio (with the C++ tools) was not found' }

# VC\Redist\MSVC\<version>\<arch>\Microsoft.VC14x.CRT and ...OpenMP; the folder names change with VS versions
$redist = Get-ChildItem (Join-Path $vs 'VC\Redist\MSVC') -Directory |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' -and (Test-Path (Join-Path $_.FullName $arch)) } |
    Sort-Object { [version]$_.Name } -Descending |
    Select-Object -First 1
if (-not $redist) { throw "No VC++ redistributable files for $arch under $vs" }
$archDir = Join-Path $redist.FullName $arch
$sources = Get-ChildItem $archDir -Directory | Where-Object { $_.Name -match '^Microsoft\.VC\d+\.(CRT|OpenMP)$' }

$wanted = 'msvcp140.dll', 'vcruntime140.dll', 'vcruntime140_1.dll', 'vcomp140.dll'
$files = foreach ($name in $wanted) {
    $sources | ForEach-Object { Join-Path $_.FullName $name } | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not ($files | Where-Object { (Split-Path $_ -Leaf) -eq 'msvcp140.dll' })) { throw "msvcp140.dll not found in $archDir" }

$targets = Get-ChildItem (Join-Path $PublishDir 'runtimes') -Recurse -Filter whisper.dll | ForEach-Object { $_.DirectoryName }
if (-not $targets) { throw "No runtimes\...\whisper.dll in $PublishDir" }
foreach ($target in $targets) {
    foreach ($file in $files) { Copy-Item $file $target -Force }
    Write-Host "VC++ runtime $($redist.Name) ($arch) -> $target"
}
