# Solution Export Pipeline

This GitHub Actions workflow automatically exports the PPDSDemo solution from Dataverse and commits the unpacked source files to the repository.

## Overview

| Property | Value |
|----------|-------|
| **Workflow File** | `.github/workflows/export-solution.yml` |
| **Schedule** | Nightly at 2 AM UTC |
| **Manual Trigger** | Yes (workflow_dispatch) |
| **Environment** | DemoDev |

## What It Does

1. **Authenticates** to Dataverse using Service Principal credentials
2. **Exports** the PPDSDemo solution (unmanaged) as a zip file
3. **Unpacks** the solution into source-control-friendly XML format
4. **Commits** any changes back to the main branch (or creates a PR)

## Prerequisites

### 1. Azure App Registration (Service Principal)

Create an App Registration in Azure AD:

1. Go to **Azure Portal** > **Azure Active Directory** > **App registrations**
2. Click **New registration**
   - Name: `Power Platform GitHub Actions` (or similar)
   - Supported account types: Single tenant
3. After creation, note these values:
   - **Application (client) ID** → `POWERPLATFORM_CLIENT_ID`
   - **Directory (tenant) ID** → `POWERPLATFORM_TENANT_ID`
4. Go to **Certificates & secrets** > **New client secret**
   - Copy the secret value immediately → `POWERPLATFORM_CLIENT_SECRET`

### 2. Grant Dataverse Permissions

Add the App Registration as an Application User in Power Platform:

1. Go to **Power Platform Admin Center** (admin.powerplatform.microsoft.com)
2. Select your environment
3. **Settings** > **Users + permissions** > **Application users**
4. Click **New app user** > **Add an app**
5. Search for your App Registration name and select it
6. Assign the **System Administrator** security role (or a custom role with solution export permissions)

### 3. Configure GitHub Environment

Create a GitHub Environment with the required secrets and variables:

1. Go to your GitHub repo > **Settings** > **Environments**
2. Create environment: `DemoDev`
3. Add **Environment Variables** (non-sensitive):
   | Variable | Value |
   |----------|-------|
   | `POWERPLATFORM_TENANT_ID` | Your Azure AD Tenant ID |
   | `POWERPLATFORM_CLIENT_ID` | Your App Registration Client ID |
   | `POWERPLATFORM_ENVIRONMENT_URL` | Your Dataverse URL (e.g., `https://org7a4a0326.crm.dynamics.com`) |

4. Add **Environment Secret** (sensitive):
   | Secret | Value |
   |--------|-------|
   | `POWERPLATFORM_CLIENT_SECRET` | Your App Registration Client Secret |

## Usage

### Automatic (Scheduled)

The workflow runs automatically every night at 2 AM UTC. Changes are committed directly to the main branch.

### Manual Trigger

Trigger manually from GitHub UI or CLI:

**GitHub UI:**
1. Go to **Actions** > **Export Solution from Dataverse**
2. Click **Run workflow**
3. Choose options:
   - `solution_name`: Solution to export (default: `PPDSDemo`)
   - `create_pr`: Create a PR instead of committing directly

**GitHub CLI:**
```bash
# Export and commit directly
gh workflow run "Export Solution from Dataverse" \
  --field solution_name=PPDSDemo \
  --field create_pr=false

# Export and create a PR for review
gh workflow run "Export Solution from Dataverse" \
  --field solution_name=PPDSDemo \
  --field create_pr=true

# Watch the run
gh run watch
```

## Workflow Steps

| Step | Description |
|------|-------------|
| Checkout repository | Clone the repo |
| Install Power Platform Tools | Install PAC CLI |
| Who Am I | Verify authentication |
| Export Solution | Download solution zip from Dataverse |
| Unpack Solution | Convert zip to source files |
| Clean up export zip | Remove temporary zip file |
| Check for changes | Detect if anything changed |
| Commit changes to main | Push changes (scheduled runs) |
| Create Pull Request | Open PR (manual runs with `create_pr=true`) |

## Exported Components

The pipeline exports and unpacks these solution components:

```
solutions/PPDSDemo/src/
├── Other/
│   ├── Solution.xml           # Solution metadata
│   └── Customizations.xml     # Component definitions
├── PluginAssemblies/          # Plugin DLLs and metadata
├── SdkMessageProcessingSteps/ # Plugin step registrations
├── WebResources/              # JavaScript, HTML, CSS, images
├── Entities/                  # Table definitions
├── OptionSets/                # Global option sets
└── Workflows/                 # Cloud flows, classic workflows
```

## Troubleshooting

### Authentication Failed

```
Error: PAC is not installed
```
**Solution:** Ensure the `actions-install` step is present before any PAC commands.

### Export Failed

```
Error: Solution not found
```
**Solution:** Verify the solution exists in Dataverse and the Service Principal has access.

### Unpack Failed - Managed Solution Not Found

```
Error: Assumed Managed solution file not found
```
**Solution:** Set `solution-type: 'Unmanaged'` in the unpack step (not `'Both'`).

### No Changes Detected

If the workflow runs but doesn't commit, it means the solution in Dataverse matches what's in source control. This is expected behavior.

## Related Documentation

- [Deploy-Components.ps1](../../tools/Deploy-Components.ps1) - Deploy components to Dataverse
- [PAC CLI Setup](../tools/pac-cli.md) - Local PAC CLI configuration
- [GitHub Actions for Power Platform](https://learn.microsoft.com/en-us/power-platform/alm/devops-github-actions) - Microsoft documentation
