$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot "windows\KeyTool.Windows\KeyTool.Windows.csproj"
$output = Join-Path $projectRoot "dist\windows\win-x64"
$archive = Join-Path $projectRoot "dist\windows\KeyTool-Windows-x64.zip"

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $output

if (Test-Path $archive) {
    Remove-Item $archive
}
Compress-Archive -Path (Join-Path $output "*") -DestinationPath $archive

Write-Host "Built $output"
