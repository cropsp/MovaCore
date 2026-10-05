<#
.SYNOPSIS
Puts whisper.cpp's Silero voice activity model into models\ of a publish output, where MovaCore looks for it.

.DESCRIPTION
The model (under 1 MB, MIT licensed, converted to ggml by the whisper.cpp project) decides which part of a dictation
is speech. It ships with the app instead of being downloaded at runtime, so dictation works offline from the start. A
copy in -CacheDir is reused. The download is checked against the SHA-256 pinned below; while none is pinned, against
the one Hugging Face reports for the file, and the hash is printed so that it can be pinned.

.EXAMPLE
./eng/get-vad-model.ps1 -PublishDir out/win-x64 -CacheDir vad-model
#>
param(
    [Parameter(Mandatory)] [string] $PublishDir,
    [string] $CacheDir = (Join-Path $PSScriptRoot '..\obj\vad-model')
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue' # Invoke-WebRequest is many times slower with a progress bar

# Keep in step with WhisperSpeechRecognizer.VadModelFileName
$fileName = 'ggml-silero-v6.2.0.bin'
$repository = 'ggml-org/whisper-vad'
$pinnedSha256 = ''

function Get-Sha256([string] $path) { (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() }

New-Item -ItemType Directory -Force $CacheDir | Out-Null
$cached = Join-Path $CacheDir $fileName
if ((Test-Path $cached) -and $pinnedSha256 -and (Get-Sha256 $cached) -ne $pinnedSha256) {
    Write-Warning "The cached $fileName has another hash; downloading it again"
    Remove-Item $cached
}

if (-not (Test-Path $cached)) {
    $download = "$cached.partial"
    Invoke-WebRequest "https://huggingface.co/$repository/resolve/main/$fileName" -OutFile $download
    $sha256 = Get-Sha256 $download
    $expected = $pinnedSha256
    if (-not $expected) {
        # The LFS object ID of a Hugging Face file is its SHA-256
        $files = Invoke-RestMethod "https://huggingface.co/api/models/$repository/tree/main"
        $expected = ($files | Where-Object path -eq $fileName).lfs.oid
        if (-not $expected) { Remove-Item $download; throw "Hugging Face reported no SHA-256 for $fileName" }
        Write-Host "SHA-256 of $fileName (pin it in eng/get-vad-model.ps1): $sha256"
        if ($env:GITHUB_STEP_SUMMARY) { "SHA-256 of ``$fileName``: ``$sha256`` (not pinned yet)" >> $env:GITHUB_STEP_SUMMARY }
    }
    if ($sha256 -ne $expected) { Remove-Item $download; throw "$fileName has SHA-256 $sha256, expected $expected" }

    # whisper.cpp files start with the ggml magic, "lmgg" on disk
    $magic = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($download)[0..3])
    if ($magic -ne 'lmgg') { Remove-Item $download; throw "$fileName is not a ggml file (it starts with '$magic')" }
    Move-Item $download $cached -Force
}

$models = Join-Path $PublishDir 'models'
New-Item -ItemType Directory -Force $models | Out-Null
Copy-Item $cached (Join-Path $models $fileName) -Force
Write-Host "Voice activity model: $(Join-Path $models $fileName)"
