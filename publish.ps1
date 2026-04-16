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

if ($LASTEXITCODE -eq 0) {
    Write-Host "Native AOT Build completed successfully!"
    Write-Host "Output location: bin\$configuration\net8.0-windows10.0.17763.0\$runtime\publish\"
    Write-Host "Your single executable MovaCore.exe is ready."
} else {
    Write-Host "Build failed. Please check the logs above."
}
