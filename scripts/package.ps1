param(
    [string]$Version = "v1.0.0",
    [string]$OutputRoot = "dist"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $RepoRoot

try {
    $StageDir = Join-Path $OutputRoot "CrackMeasure-win-x64"
    $ZipPath  = Join-Path $OutputRoot ("CrackMeasure-$Version-win-x64.zip")

    if (Test-Path $StageDir) { Remove-Item -Recurse -Force $StageDir }
    if (Test-Path $ZipPath)  { Remove-Item -Force $ZipPath }
    New-Item -ItemType Directory -Force -Path $StageDir | Out-Null

    Write-Host "[1/3] Publishing CrackMeasure (win-x64 self-contained single-file)..."
    dotnet publish "src/CrackMeasure/CrackMeasure.csproj" `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=false `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $StageDir
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host "[2/3] Copying samples and documentation..."
    Copy-Item -Recurse -Force "samples" (Join-Path $StageDir "samples")
    Copy-Item -Force "README.md" (Join-Path $StageDir "README.md")
    Copy-Item -Force "LICENSE"   (Join-Path $StageDir "LICENSE")

    Write-Host "[3/3] Creating release archive $ZipPath..."
    Compress-Archive -Path $StageDir -DestinationPath $ZipPath -CompressionLevel Optimal

    Write-Host "Done! Output directory: $StageDir"
    Write-Host "Release archive: $ZipPath"
}
finally {
    Pop-Location
}
