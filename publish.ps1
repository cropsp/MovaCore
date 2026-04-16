# Publish Script for MovaCore (Native AOT)
# This script automates the creation of a standalone, optimized Native AOT executable.

$projectName = "MovaCore"
$runtime = "win-x64"
$configuration = "Release"

Write-Host "🚀 Starting Native AOT build process for $projectName..." -ForegroundColor Cyan

# Check if dotnet is installed
if (-not (Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
    Write-Host "❌ Error: .NET SDK is not installed. Please install it from https://dotnet.microsoft.com/" -ForegroundColor Red
    exit 1
}

# Run the publish command
# Native AOT is enabled in the .csproj via <PublishAot>true</PublishAot>
dotnet publish "$projectName.csproj" `
    -c $configuration `
    -r $runtime

if ($LASTEXITCODE -eq 0) {
    Write-Host "✅ Native AOT Build completed successfully!" -ForegroundColor Green
    Write-Host "📍 Output location: bin\$configuration\net8.0-windows\$runtime\publish\" -ForegroundColor Yellow
    Write-Host "📦 Your single executable MovaCore.exe is ready." -ForegroundColor Cyan
} else {
    Write-Host "❌ Build failed. Please check the logs above." -ForegroundColor Red
}
