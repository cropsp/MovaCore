# Publish Script for MovaCore (Native AOT)
# This script automates the creation of a standalone, optimized Native AOT executable.

$projectName = "MovaCore"
$runtime = "win-x64"
$configuration = "Release"

Write-Host "Starting Native AOT build process for $projectName..."

# Check if dotnet is installed
$dotnetExe = "dotnet"
if (-not (Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
    $defaultPath = "C:\Program Files\dotnet\dotnet.exe"
    if (Test-Path $defaultPath) {
        $dotnetExe = $defaultPath
        Write-Host "Using dotnet from: $defaultPath"
    } else {
        Write-Host "Error: .NET SDK is not installed. Please install it from https://dotnet.microsoft.com/"
        exit 1
    }
}

# Run the publish command
# Native AOT is enabled in the .csproj via <PublishAot>true</PublishAot>
& $dotnetExe publish "$projectName.csproj" -c $configuration -r $runtime

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed. Please check the logs above."
    exit 1
}

# Speech recognition needs the VC++ runtime next to the Whisper DLLs (as CI does it, see .github/workflows/ci.yml)
$publishDir = "bin\$configuration\net10.0-windows\$runtime\publish"
& "$PSScriptRoot\eng\copy-vc-runtime.ps1" -PublishDir $publishDir -Rid $runtime
& "$PSScriptRoot\eng\check-native-deps.ps1" -PublishDir $publishDir
if ($LASTEXITCODE -ne 0) { exit 1 }

Write-Host "Native AOT Build completed successfully!"
Write-Host "Output location: $publishDir\"
Write-Host "Ship the whole folder: MovaCore.exe, uiohook.dll (the keyboard hook) and runtimes\ (speech recognition)."
Write-Host "Native AOT cannot embed native libraries."
