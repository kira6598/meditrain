[CmdletBinding()]
param(
    [string]$Database,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repositoryRoot 'src\QuanLyDaoTao.Desktop\QuanLyDaoTao.Desktop.csproj'
$runArguments = @('run', '--project', $projectPath, '--configuration', $Configuration)
if (-not [string]::IsNullOrWhiteSpace($Database)) {
    $databasePath = [System.IO.Path]::GetFullPath($Database)
    $runArguments += @('--', '--database', $databasePath)
}

& dotnet @runArguments
if ($LASTEXITCODE -ne 0) {
    throw "Khong chay duoc ung dung (dotnet exit code: $LASTEXITCODE)."
}
