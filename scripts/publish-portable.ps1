[CmdletBinding()]
param([switch]$SkipZip)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repositoryRoot 'src\QuanLyDaoTao.Desktop\QuanLyDaoTao.Desktop.csproj'
$publishDirectory = Join-Path $repositoryRoot 'artifacts\single-file'
$customerDirectory = Join-Path $repositoryRoot 'artifacts\customer'
$customerExe = Join-Path $customerDirectory 'MediTrain.exe'
$zip = Join-Path $repositoryRoot 'artifacts\MediTrain-portable-win-x64.zip'

& dotnet publish $project -c Release -p:PublishProfile=Portable-win-x64
if ($LASTEXITCODE -ne 0) { throw "Publish failed: $LASTEXITCODE" }
$files = @(Get-ChildItem -LiteralPath $publishDirectory -File -Recurse)
if ($files.Count -ne 1 -or $files[0].Name -ne 'QuanLyDaoTao.Desktop.exe') {
    throw 'Portable publish must contain exactly one EXE. Check the publish directory before shipping.'
}
New-Item -ItemType Directory -Path $customerDirectory -Force | Out-Null
Copy-Item -LiteralPath $files[0].FullName -Destination $customerExe -Force
if (-not $SkipZip) { Compress-Archive -LiteralPath $customerExe -DestinationPath $zip -Force }
Write-Output "Send this file to the customer: $customerExe"
if (-not $SkipZip) { Write-Output "Or send the ZIP: $zip" }
