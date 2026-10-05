<#
.SYNOPSIS
Fails if a DLL or exe in a publish output imports a DLL that a clean Windows PC may lack.

.DESCRIPTION
Every import must be a part of Windows (in System32, or an api-ms-win-* / ext-ms-win-* API set), vulkan-1.dll (which
comes with the graphics driver), or a file in the importing file's own folder. The VC++ runtime counts as missing even
though the build machine has it in System32: users may not. Catches a runtime folder shipped without its VC++ DLLs,
which the smoke test cannot see on a CI runner.

.EXAMPLE
./eng/check-native-deps.ps1 -PublishDir out/win-x64
#>
param([Parameter(Mandatory)] [string] $PublishDir)
$ErrorActionPreference = 'Stop'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products * -property installationPath
$dumpbin = Get-ChildItem (Join-Path $vs 'VC\Tools\MSVC\*\bin\Hostx64\x64\dumpbin.exe') | Select-Object -Last 1
if (-not $dumpbin) { throw 'dumpbin.exe was not found' }

$system32 = Join-Path $env:SystemRoot 'System32'
$notPartOfWindows = '^(msvcp140.*|vcruntime140.*|vcomp140|concrt140|mfc140.*|vccorlib140)\.dll$'
$problems = @()

foreach ($binary in Get-ChildItem $PublishDir -Recurse -Include *.dll, *.exe) {
    $output = & $dumpbin.FullName /nologo /dependents $binary.FullName
    $imports = $output | Where-Object { $_ -match '^\s+\S+\.(dll|DLL|drv|sys)\s*$' } | ForEach-Object { $_.Trim() }
    foreach ($import in $imports) {
        $local = Test-Path (Join-Path $binary.DirectoryName $import)
        $windows = ($import -match '^(api|ext)-ms-win-') -or ($import -ieq 'vulkan-1.dll') -or
            (($import -notmatch $notPartOfWindows) -and (Test-Path (Join-Path $system32 $import)))
        if (-not ($local -or $windows)) {
            $problems += "$($binary.FullName.Substring((Resolve-Path $PublishDir).Path.Length + 1)) imports $import"
        }
    }
    Write-Host "$($binary.Name): $($imports -join ', ')"
}

if ($problems) {
    $problems | ForEach-Object { Write-Host "::error::$_, which is neither part of Windows nor next to it" }
    exit 1
}
Write-Host 'Every native import is part of Windows or ships next to its importer'
