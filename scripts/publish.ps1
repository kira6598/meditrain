[CmdletBinding()]
param(
    [switch]$SkipZip
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repositoryRoot 'src\QuanLyDaoTao.Desktop\QuanLyDaoTao.Desktop.csproj'
$outputDirectory = Join-Path $repositoryRoot 'artifacts\publish'
$archivePath = Join-Path $repositoryRoot 'artifacts\MediTrain-win-x64.zip'

& dotnet publish $projectPath --configuration Release --runtime win-x64 --self-contained true --output $outputDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Publish that bai (dotnet exit code: $LASTEXITCODE)."
}

Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md') -Destination (Join-Path $outputDirectory 'README.md') -Force
if (-not $SkipZip) {
    Compress-Archive -Path (Join-Path $outputDirectory '*') -DestinationPath $archivePath -Force
    Write-Output "ZIP: $archivePath"
}
Write-Output "EXE: $(Join-Path $outputDirectory 'QuanLyDaoTao.Desktop.exe')"
