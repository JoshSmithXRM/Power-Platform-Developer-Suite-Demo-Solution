# Plugin Deployment Tooling - Design Document

This document describes the design for automated plugin deployment and step registration tooling.

---

## Problem Statement

Currently, plugin development requires:
1. Writing plugin code
2. **Manually** registering steps via Plugin Registration Tool
3. No automated deployment path from code to environment
4. Step configuration disconnected from plugin code

**Goal:** Enable developers to define plugin step configuration in code and deploy with a single command.

---

## Solution Overview

```
┌─────────────────────────────────────────────────────────────┐
│  Developer writes plugin with [PluginStep] attributes       │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│  Build: dotnet build                                        │
│  Extract: Reflect on DLL → Generate registrations.json      │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│  Deploy Assembly: pac plugin push                           │
│  Register Steps: Web API → SdkMessageProcessingStep         │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│  Nightly Export: Captures registrations in solution         │
│  ALM Process: Deploys to QA/Prod via solution               │
└─────────────────────────────────────────────────────────────┘
```

---

## Architecture

### Component Overview

| Component | Purpose | Location |
|-----------|---------|----------|
| Registration Attributes | Define step config in code | `src/Shared/PPDSDemo.Sdk/` |
| Extraction Tool | Read attributes → JSON | `tools/Extract-PluginRegistrations.ps1` |
| Deployment Script | Deploy assembly + steps | `tools/Deploy-Plugins.ps1` |
| CI Workflow | Auto-deploy on develop push | `.github/workflows/ci-plugin-deploy.yml` |

### Supported Plugin Types

| Type | PAC CLI Flag | Location |
|------|--------------|----------|
| Classic Plugin Assembly | `--type Assembly` | `src/Plugins/` |
| Plugin Package (NuGet) | `--type Nuget` | `src/PluginPackages/` |

---

## Attribute Schema

### PluginStepAttribute

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class PluginStepAttribute : Attribute
{
    // Required
    public string Message { get; set; }           // Create, Update, Delete, etc.
    public string EntityLogicalName { get; set; } // account, contact, etc.
    public PluginStage Stage { get; set; }        // PreValidation, PreOperation, PostOperation

    // Optional
    public PluginMode Mode { get; set; } = PluginMode.Synchronous;
    public string FilteringAttributes { get; set; }  // Comma-separated
    public int ExecutionOrder { get; set; } = 1;
    public string Name { get; set; }              // Step name (auto-generated if not specified)
    public string Configuration { get; set; }     // Unsecure configuration
}
```

### PluginImageAttribute

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class PluginImageAttribute : Attribute
{
    public string Name { get; set; }              // PreImage, PostImage
    public PluginImageType ImageType { get; set; } // PreImage, PostImage, Both
    public string Attributes { get; set; }        // Comma-separated attributes
    public string EntityAlias { get; set; }       // Alias for the image

    // Link to specific step (if multiple steps on same plugin)
    public string MessageName { get; set; }       // Optional: which step this image belongs to
}
```

### Enumerations

```csharp
public enum PluginStage
{
    PreValidation = 10,
    PreOperation = 20,
    PostOperation = 40
}

public enum PluginMode
{
    Synchronous = 0,
    Asynchronous = 1
}

public enum PluginImageType
{
    PreImage = 0,
    PostImage = 1,
    Both = 2
}
```

### Example Usage

```csharp
[PluginStep(
    Message = "Update",
    EntityLogicalName = "account",
    Stage = PluginStage.PostOperation,
    Mode = PluginMode.Asynchronous,
    FilteringAttributes = "name,telephone1,revenue")]
[PluginImage(
    Name = "PreImage",
    ImageType = PluginImageType.PreImage,
    Attributes = "name,telephone1,revenue")]
public class AccountAuditLogPlugin : PluginBase
{
    // Plugin implementation
}
```

---

## Registration JSON Schema

Generated from attributes, stored in repository for review and deployment.

```json
{
  "$schema": "./plugin-registration.schema.json",
  "version": "1.0",
  "generatedAt": "2025-12-13T18:30:00Z",
  "assemblies": [
    {
      "name": "PPDSDemo.Plugins",
      "type": "Assembly",
      "path": "src/Plugins/PPDSDemo.Plugins/bin/Release/net462/PPDSDemo.Plugins.dll",
      "plugins": [
        {
          "typeName": "PPDSDemo.Plugins.AccountPreCreatePlugin",
          "steps": [
            {
              "name": "PPDSDemo.Plugins.AccountPreCreatePlugin: Create of account",
              "message": "Create",
              "entity": "account",
              "stage": "PreOperation",
              "mode": "Synchronous",
              "executionOrder": 1,
              "filteringAttributes": null,
              "configuration": null,
              "images": []
            }
          ]
        }
      ]
    },
    {
      "name": "PPDSDemo.PluginPackage",
      "type": "Nuget",
      "path": "src/PluginPackages/PPDSDemo.PluginPackage/bin/Release/PPDSDemo.PluginPackage.1.0.0.nupkg",
      "plugins": [
        {
          "typeName": "PPDSDemo.PluginPackage.AccountAuditLogPlugin",
          "steps": [
            {
              "name": "AccountAuditLogPlugin: Update of account",
              "message": "Update",
              "entity": "account",
              "stage": "PostOperation",
              "mode": "Asynchronous",
              "executionOrder": 1,
              "filteringAttributes": "name,telephone1,revenue",
              "configuration": null,
              "images": [
                {
                  "name": "PreImage",
                  "imageType": "PreImage",
                  "attributes": "name,telephone1,revenue",
                  "entityAlias": "PreImage"
                }
              ]
            }
          ]
        }
      ]
    }
  ]
}
```

---

## Deployment Script Design

### Deploy-Plugins.ps1

```powershell
<#
.SYNOPSIS
    Deploys plugin assemblies and registers steps to Dataverse.

.PARAMETER Environment
    Target environment: Dev (default), QA, Prod

.PARAMETER Project
    Specific project to deploy. If not specified, deploys all.

.PARAMETER Force
    Remove orphaned steps not in configuration.

.PARAMETER WhatIf
    Show what would be deployed without making changes.

.EXAMPLE
    .\Deploy-Plugins.ps1
    Deploys all plugins to Dev environment.

.EXAMPLE
    .\Deploy-Plugins.ps1 -Environment QA -Project PPDSDemo.Plugins
    Deploys specific plugin assembly to QA.

.EXAMPLE
    .\Deploy-Plugins.ps1 -Force
    Deploys and removes orphaned steps.
#>
```

### Deployment Flow

```
1. Load registrations.json
2. Authenticate to target environment (pac auth)
3. For each assembly/package:
   a. Build if needed (dotnet build)
   b. Deploy assembly (pac plugin push)
   c. For each plugin type:
      i.  Get or create PluginType record
      ii. For each step:
          - Check if step exists (by name)
          - Create or update SdkMessageProcessingStep
          - For each image:
            - Create or update SdkMessageProcessingStepImage
4. If -Force: Remove steps in Dataverse not in config
5. Report summary
```

### API Entities

| Entity | Purpose |
|--------|---------|
| `pluginassembly` | The deployed DLL/NuGet package |
| `plugintype` | Each plugin class in the assembly |
| `sdkmessageprocessingstep` | Step registration (message, entity, stage) |
| `sdkmessageprocessingstepimage` | Pre/Post images for steps |
| `sdkmessage` | Message definitions (Create, Update, etc.) |
| `sdkmessagefilter` | Entity-specific message filters |

---

## CI/CD Integration

### Workflow: ci-plugin-deploy.yml

Triggers on push to `develop` when plugin files change.

```yaml
name: 'CI: Deploy Plugins to Dev'

on:
  push:
    branches: [develop]
    paths:
      - 'src/Plugins/**'
      - 'src/PluginPackages/**'

jobs:
  deploy-plugins:
    runs-on: ubuntu-latest
    environment: development

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.x'

      - name: Build plugins
        run: |
          dotnet build src/Plugins/PPDSDemo.Plugins -c Release
          dotnet build src/PluginPackages/PPDSDemo.PluginPackage -c Release

      - name: Setup PAC CLI
        uses: ./.github/actions/setup-pac-cli

      - name: Authenticate to Dev
        uses: ./.github/actions/pac-auth
        with:
          environment-url: ${{ vars.DEV_ENV_URL }}
          tenant-id: ${{ secrets.AZURE_TENANT_ID }}
          client-id: ${{ secrets.AZURE_CLIENT_ID }}
          client-secret: ${{ secrets.AZURE_CLIENT_SECRET }}

      - name: Deploy plugins
        shell: pwsh
        run: |
          ./tools/Deploy-Plugins.ps1 -Environment Dev -Verbose
```

### Integration with Existing ALM

```
develop branch push (plugin code changes)
    ↓
CI: Deploy Plugins to Dev (new workflow)
    ↓
Plugin assemblies + steps now in Dev environment
    ↓
Nightly Export (existing ci-export.yml)
    ↓
Solution exported with plugin registrations
    ↓
Committed to develop branch
    ↓
CD: Deploy to QA (existing cd-qa.yml)
    ↓
Solution deployed to QA with all registrations
```

---

## Safety Considerations

### Orphaned Step Handling

| Mode | Behavior |
|------|----------|
| Default | Warn about steps in Dataverse not in config |
| `-Force` | Delete orphaned steps |

### Production Deployment

- Plugins deploy to Dev via CI
- QA/Prod receive plugins via solution deployment (ALM)
- Direct plugin deployment to Prod should require confirmation

### Rollback Strategy

- Assembly versions tracked in Dataverse
- Previous versions retained (can rollback via PRT if needed)
- Solution history provides deployment audit trail

---

## File Structure

```
tools/
├── Deploy-Plugins.ps1           # Main deployment script
├── Extract-PluginRegistrations.ps1  # Generate JSON from attributes
├── Remove-PluginSteps.ps1       # Cleanup orphaned steps
└── lib/
    └── PluginDeployment.psm1    # Shared functions

src/
├── Shared/
│   └── PPDSDemo.Sdk/
│       ├── Attributes/
│       │   ├── PluginStepAttribute.cs
│       │   └── PluginImageAttribute.cs
│       └── Enums/
│           ├── PluginStage.cs
│           ├── PluginMode.cs
│           └── PluginImageType.cs
├── Plugins/
│   └── PPDSDemo.Plugins/
│       └── registrations.json   # Generated, committed
└── PluginPackages/
    └── PPDSDemo.PluginPackage/
        └── registrations.json   # Generated, committed

.github/
└── workflows/
    └── ci-plugin-deploy.yml     # Deploy on develop push
```

---

## See Also

- [PAC Plugin Command Reference](https://learn.microsoft.com/en-us/power-platform/developer/cli/reference/plugin)
- [Register a Plug-in](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/register-plug-in)
- [SdkMessageProcessingStep Entity](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/reference/entities/sdkmessageprocessingstep)
- [PIPELINE_STRATEGY.md](../strategy/PIPELINE_STRATEGY.md)
