<#
.SYNOPSIS
    Example script showing how to extract plugin registrations using PPDS.Tools.

.DESCRIPTION
    This script demonstrates the intended usage of PPDS.Tools cmdlets to extract
    plugin step registrations from compiled assemblies via .NET reflection.

    The extracted registrations are saved to registrations.json files that can be
    used for deployment with Deploy-Plugins.ps1.

.PARAMETER Project
    Specific project name to extract. If not specified, extracts all.

.PARAMETER Build
    Build projects before extraction.

.EXAMPLE
    .\Extract-PluginRegistrations.ps1
    Extracts registrations from all plugin projects.

.EXAMPLE
    .\Extract-PluginRegistrations.ps1 -Project PPDSDemo.Plugins -Build
    Builds and extracts registrations from a specific project.

.NOTES
    Prerequisites:
    - Install PPDS.Tools: Install-Module PPDS.Tools -Scope CurrentUser
    - Build plugin projects first (or use -Build parameter)
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Project,

    [Parameter()]
    [switch]$Build
)

$ErrorActionPreference = "Stop"

# Import PPDS.Tools module
if (-not (Get-Module -ListAvailable PPDS.Tools)) {
    Write-Error "PPDS.Tools module not found. Install with: Install-Module PPDS.Tools -Scope CurrentUser"
    exit 1
}
Import-Module PPDS.Tools -Force

$repoRoot = Split-Path $PSScriptRoot -Parent

Write-Host "Plugin Registration Extractor" -ForegroundColor Cyan
Write-Host "Repository: $repoRoot"
Write-Host ""

# Build if requested
if ($Build) {
    Write-Host "Building projects..."
    dotnet build "$repoRoot" -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed"
        exit 1
    }
    Write-Host "Build successful" -ForegroundColor Green
    Write-Host ""
}

# Define plugin assemblies to process
$pluginAssemblies = @(
    @{
        Name = "PPDSDemo.Plugins"
        Path = "$repoRoot/src/Plugins/PPDSDemo.Plugins"
        DllPath = "$repoRoot/src/Plugins/PPDSDemo.Plugins/bin/Release/net462/PPDSDemo.Plugins.dll"
        Type = "Assembly"
    },
    @{
        Name = "PPDSDemo.PluginPackage"
        Path = "$repoRoot/src/PluginPackages/PPDSDemo.PluginPackage"
        DllPath = "$repoRoot/src/PluginPackages/PPDSDemo.PluginPackage/bin/Release/net462/PPDSDemo.PluginPackage.dll"
        Type = "Nuget"
    }
)

# Filter to specific project if requested
if ($Project) {
    $pluginAssemblies = $pluginAssemblies | Where-Object { $_.Name -eq $Project }
    if (-not $pluginAssemblies) {
        Write-Error "Project not found: $Project"
        exit 1
    }
}

# Extract registrations for each assembly
foreach ($assembly in $pluginAssemblies) {
    Write-Host "Processing: $($assembly.Name) ($($assembly.Type))" -ForegroundColor Yellow

    if (-not (Test-Path $assembly.DllPath)) {
        Write-Warning "DLL not found: $($assembly.DllPath). Build the project first."
        continue
    }

    # Extract plugin registrations using PPDS.Tools
    $outputPath = Join-Path $assembly.Path "registrations.json"

    Get-DataversePluginRegistrations `
        -AssemblyPath $assembly.DllPath `
        -OutputPath $outputPath `
        -AssemblyType $assembly.Type

    Write-Host "  Generated: $outputPath" -ForegroundColor Green
}

Write-Host ""
Write-Host "Extraction complete. Generated files should be committed to source control." -ForegroundColor Cyan
