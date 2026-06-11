param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [switch]$NoSingleFile
)

$ErrorActionPreference = "Stop"

$RepoRoot = $PSScriptRoot
$Project = Join-Path $RepoRoot "StageManager\StageManager.csproj"
$Output = Join-Path $RepoRoot "publish\StageManager-$Runtime"

if (Test-Path -LiteralPath $Output) {
    Remove-Item -LiteralPath $Output -Recurse -Force
}

$PublishArgs = @(
    "publish", $Project,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "-o", $Output
)

if (-not $NoSingleFile) {
    $PublishArgs += @(
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true"
    )
}

dotnet @PublishArgs

Write-Host "Published to $Output"
Write-Host "Run: $(Join-Path $Output 'StageManager.exe')"
