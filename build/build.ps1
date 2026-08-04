<#
.SYNOPSIS
    Restores, builds, tests and publishes AI GIS Converter.

.PARAMETER Configuration
    Debug or Release. Defaults to Release.

.PARAMETER EnableAutoCadProvider
    Compiles the AutoCAD .NET provider. Requires -AutoCadSdkPath.

.PARAMETER AutoCadSdkPath
    Folder containing acmgd.dll, acdbmgd.dll and accoremgd.dll (ObjectARX / RealDWG SDK).

.EXAMPLE
    .\build.ps1 -Configuration Release
.EXAMPLE
    .\build.ps1 -EnableAutoCadProvider -AutoCadSdkPath 'C:\ObjectARX 2025\inc'
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $EnableAutoCadProvider,
    [string] $AutoCadSdkPath = '',
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'AiGisConverter.sln'

$msbuildArgs = @("-c:$Configuration")
if ($EnableAutoCadProvider) {
    if ([string]::IsNullOrWhiteSpace($AutoCadSdkPath)) {
        throw 'AutoCadSdkPath is required when EnableAutoCadProvider is set.'
    }
    $msbuildArgs += "-p:EnableAutoCadProvider=true"
    $msbuildArgs += "-p:AutoCadSdkPath=$AutoCadSdkPath"
}

Write-Host 'Restoring...' -ForegroundColor Cyan
dotnet restore $solution

Write-Host 'Building...' -ForegroundColor Cyan
dotnet build $solution @msbuildArgs --no-restore

if (-not $SkipTests) {
    Write-Host 'Testing...' -ForegroundColor Cyan
    dotnet test $solution @msbuildArgs --no-build --collect:"XPlat Code Coverage"
}

Write-Host 'Publishing...' -ForegroundColor Cyan
dotnet publish (Join-Path $root 'src\AiGisConverter.Presentation\AiGisConverter.Presentation.csproj') `
    @msbuildArgs -r win-x64 --self-contained false -o (Join-Path $root 'artifacts\publish')

Write-Host 'Done.' -ForegroundColor Green
