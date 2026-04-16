# Publish Script for MovaCore
# This script automates the creation of a self-contained, unpackaged build for Windows.
# Note: WinUI 3 Unpackaged builds require the entire publish folder to be distributed (usually as a ZIP).

$projectName = "MovaCore"
$runtime = "win-x64"
$configuration = "Release"

Write-Host "🚀 Starting build process for $projectName..." -ForegroundColor Cyan

# Check if dotnet is installed
if (-not (Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
    Write-Host "❌ Error: .NET SDK is not installed. Please install it from https://dotnet.microsoft.com/" -ForegroundColor Red
    exit 1
}

# Run the publish command
# -c Release: Build in release mode
# -r win-x64: Target Windows 64-bit
# --self-contained true: Include the .NET runtime in the output
# -p:PublishSingleFile=true: Bundle everything into a single .exe
# -p:PublishReadyToRun=true: Optimize for faster startup
dotnet publish "$projectName.csproj" `
    -c $configuration `
    -r $runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:PublishTrimmed=false

if ($LASTEXITCODE -eq 0) {
    Write-Host "✅ Build completed successfully!" -ForegroundColor Green
    Write-Host "📍 Output location: bin\$configuration\net8.0-windows10.0.19041.0\$runtime\publish\" -ForegroundColor Yellow
} else {
    Write-Host "❌ Build failed. Please check the logs above." -ForegroundColor Red
}
