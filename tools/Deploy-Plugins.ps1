<#
.SYNOPSIS
    Example script showing how to deploy plugins using PPDS.Tools.

.DESCRIPTION
    This script demonstrates the intended usage of PPDS.Tools cmdlets to deploy
    plugin assemblies and register steps to Dataverse environments.

    Supports both classic plugin assemblies and plugin packages (NuGet).

.PARAMETER Environment
    Target environment label: Dev (default), QA, Prod.
    Loads credentials from corresponding .env file.

.PARAMETER Project
    Specific project to deploy. If not specified, deploys all.

.PARAMETER Force
    Remove orphaned steps that exist in Dataverse but not in configuration.

.PARAMETER WhatIf
    Show what would be deployed without making changes.

.PARAMETER Build
    Build projects before deployment.

.PARAMETER Interactive
    Use interactive browser-based OAuth login.

.EXAMPLE
    .\Deploy-Plugins.ps1
    Deploys all plugins using .env.dev credentials.

.EXAMPLE
    .\Deploy-Plugins.ps1 -Interactive
    Deploys using browser-based login.

.EXAMPLE
    .\Deploy-Plugins.ps1 -Environment QA -Project PPDSDemo.Plugins
    Deploys specific plugin assembly to QA.

.EXAMPLE
    .\Deploy-Plugins.ps1 -WhatIf
    Shows what would be deployed without making changes.

.NOTES
    Prerequisites:
    - Install PPDS.Tools: Install-Module PPDS.Tools -Scope CurrentUser
    - Extract registrations first: .\Extract-PluginRegistrations.ps1
    - Configure environment credentials in .env.dev, .env.qa, or .env.prod
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter()]
    [ValidateSet("Dev", "QA", "Prod")]
    [string]$Environment = "Dev",

    [Parameter()]
    [string]$Project,

    [Parameter()]
    [switch]$Force,

    [Parameter()]
    [switch]$Build,

    [Parameter()]
    [switch]$Interactive
)

$ErrorActionPreference = "Stop"

# Import PPDS.Tools module
if (-not (Get-Module -ListAvailable PPDS.Tools)) {
    Write-Error "PPDS.Tools module not found. Install with: Install-Module PPDS.Tools -Scope CurrentUser"
    exit 1
}
Import-Module PPDS.Tools -Force

$repoRoot = Split-Path $PSScriptRoot -Parent

Write-Host "Plugin Deployment Tool" -ForegroundColor Cyan
Write-Host "Repository: $repoRoot"
Write-Host "Environment: $Environment"
if ($WhatIfPreference) {
    Write-Host "Mode: WhatIf (no changes will be made)" -ForegroundColor Yellow
}
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

# Connect to Dataverse
Write-Host "Connecting to Dataverse..."

$envFile = Join-Path $repoRoot ".env.$($Environment.ToLower())"
$connectionParams = @{}

if ($Interactive) {
    $connectionParams["Interactive"] = $true
} elseif (Test-Path $envFile) {
    $connectionParams["EnvFile"] = $envFile
} else {
    Write-Warning "No .env.$($Environment.ToLower()) file found. Using PAC CLI authentication."
}

try {
    $connection = Connect-DataverseEnvironment @connectionParams
    Write-Host "Connected to: $($connection.OrganizationName)" -ForegroundColor Green
} catch {
    Write-Error "Failed to connect: $($_.Exception.Message)"
    exit 1
}

Write-Host ""

# Define plugin assemblies
$pluginAssemblies = @(
    @{
        Name = "PPDSDemo.Plugins"
        Path = "$repoRoot/src/Plugins/PPDSDemo.Plugins"
        DllPath = "$repoRoot/src/Plugins/PPDSDemo.Plugins/bin/Release/net462/PPDSDemo.Plugins.dll"
        RegistrationsPath = "$repoRoot/src/Plugins/PPDSDemo.Plugins/registrations.json"
        Type = "Assembly"
    },
    @{
        Name = "PPDSDemo.PluginPackage"
        Path = "$repoRoot/src/PluginPackages/PPDSDemo.PluginPackage"
        DllPath = "$repoRoot/src/PluginPackages/PPDSDemo.PluginPackage/bin/Release/net462/PPDSDemo.PluginPackage.dll"
        NupkgPath = "$repoRoot/src/PluginPackages/PPDSDemo.PluginPackage/bin/Release/*.nupkg"
        RegistrationsPath = "$repoRoot/src/PluginPackages/PPDSDemo.PluginPackage/registrations.json"
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

# Deploy each assembly
foreach ($assembly in $pluginAssemblies) {
    Write-Host "Deploying: $($assembly.Name) ($($assembly.Type))" -ForegroundColor Yellow

    # Check prerequisites
    if (-not (Test-Path $assembly.RegistrationsPath)) {
        Write-Warning "No registrations.json found. Run Extract-PluginRegistrations.ps1 first."
        continue
    }

    $deployPath = if ($assembly.Type -eq "Nuget") {
        (Get-ChildItem $assembly.NupkgPath | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
    } else {
        $assembly.DllPath
    }

    if (-not (Test-Path $deployPath)) {
        Write-Warning "Assembly not found: $deployPath. Build the project first."
        continue
    }

    # Deploy using PPDS.Tools
    $deployParams = @{
        RegistrationFile = $assembly.RegistrationsPath
        AssemblyPath = $deployPath
        Connection = $connection
    }

    if ($Force) {
        $deployParams["RemoveOrphans"] = $true
    }

    Deploy-DataversePlugins @deployParams -WhatIf:$WhatIfPreference

    Write-Host "  Deployed successfully" -ForegroundColor Green
}

Write-Host ""
Write-Host "Deployment complete." -ForegroundColor Cyan
