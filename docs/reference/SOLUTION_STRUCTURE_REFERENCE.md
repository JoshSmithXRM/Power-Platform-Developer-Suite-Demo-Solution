# Solution Structure Reference

Standards for Power Platform solution folder structure in source control.

---

## Core Principle

**Source control contains UNMANAGED solution only. Managed is a build artifact.**

Per [Microsoft ALM guidance](https://learn.microsoft.com/en-us/power-platform/alm/solution-concepts-alm):
- Unmanaged solutions are your **source code**
- Managed solutions are **build artifacts** generated during CI/CD
- Never store both in source control

---

## Correct Folder Structure

```
solutions/
└── PPDSDemo/
    ├── PPDSDemo.cdsproj              # MSBuild project file
    ├── .gitignore                    # Excludes bin/, obj/, *.zip
    ├── version.txt                   # Solution version tracking
    ├── config/                       # Deployment settings per environment
    │   ├── dev.deploymentsettings.json
    │   ├── qa.deploymentsettings.json
    │   └── prod.deploymentsettings.json
    └── src/                          # Unpacked solution (UNMANAGED ONLY)
        ├── Other/
        │   ├── Solution.xml          # Solution manifest
        │   └── Customizations.xml    # Solution customizations
        ├── Entities/                 # Tables
        │   └── ppds_tablename/
        │       ├── Entity.xml
        │       ├── FormXml/
        │       ├── SavedQueries/
        │       └── RibbonDiff.xml
        ├── OptionSets/               # Global option sets
        │   └── ppds_optionsetname.xml
        ├── WebResources/             # JavaScript, HTML, CSS, images
        │   └── ppds_/
        │       └── scripts/
        ├── PluginAssemblies/         # Plugin DLLs and metadata
        │   └── AssemblyName-GUID/
        │       ├── AssemblyName.dll
        │       └── AssemblyName.dll.data.xml
        ├── SdkMessageProcessingSteps/  # Plugin step registrations
        │   └── {step-guid}.xml
        └── environmentvariabledefinitions/  # Environment variables
            └── ppds_variablename/
                └── environmentvariabledefinition.xml
```

---

## Anti-Patterns (DO NOT DO)

### Managed/Unmanaged Subfolders

```
src/
├── Managed/      ← WRONG - delete this
├── Unmanaged/    ← WRONG - delete this
└── Other/        ← Correct location
```

This happens when you:
- Export with `--managed` and `--unmanaged` separately and unpack both
- Use incorrect PAC CLI options

### Duplicate Component Folders

```
src/
├── Managed/PluginAssemblies/     ← Duplicate
├── Unmanaged/PluginAssemblies/   ← Duplicate
└── PluginAssemblies/             ← Which is canonical?
```

---

## PAC CLI Commands

### Export Solution (from Dataverse)

```bash
# Export UNMANAGED from dev environment
pac solution export --name PPDSDemo --path solutions/exports/PPDSDemo.zip --overwrite
```

### Unpack Solution (to source control)

```bash
# Unpack to src/ folder - flat structure, no managed/unmanaged split
pac solution unpack \
    --zipfile solutions/exports/PPDSDemo.zip \
    --folder solutions/PPDSDemo/src \
    --allowDelete \
    --allowWrite
```

### Pack Solution (build artifact)

```bash
# Pack UNMANAGED (for dev/test import)
pac solution pack \
    --zipfile solutions/exports/PPDSDemo.zip \
    --folder solutions/PPDSDemo/src

# Pack MANAGED (for production deployment)
pac solution pack \
    --zipfile solutions/exports/PPDSDemo_managed.zip \
    --folder solutions/PPDSDemo/src \
    --managed
```

### Using MSBuild/dotnet

```bash
# Build unmanaged (Debug)
dotnet build solutions/PPDSDemo/PPDSDemo.cdsproj -c Debug

# Build managed (Release)
dotnet build solutions/PPDSDemo/PPDSDemo.cdsproj -c Release
```

---

## cdsproj Configuration

The `.cdsproj` file controls solution packaging:

```xml
<PropertyGroup>
  <SolutionRootPath>src</SolutionRootPath>

  <!-- Uncomment to override default behavior -->
  <!-- <SolutionPackageType>Managed</SolutionPackageType> -->
  <!-- Options: Managed, Unmanaged, Both -->
</PropertyGroup>
```

| Build Config | Default Output |
|--------------|----------------|
| Debug | Unmanaged |
| Release | Managed |

To generate both managed and unmanaged:
```xml
<SolutionPackageType>Both</SolutionPackageType>
```

---

## CI/CD Integration

### Export from Dev (nightly or on-demand)

```yaml
- name: Export solution
  run: |
    pac auth create --url ${{ secrets.DEV_URL }} ...
    pac solution export --name PPDSDemo --path ./PPDSDemo.zip
    pac solution unpack --zipfile ./PPDSDemo.zip --folder solutions/PPDSDemo/src --allowDelete --allowWrite
```

### Build for Deployment

```yaml
- name: Build managed solution
  run: |
    dotnet build solutions/PPDSDemo/PPDSDemo.cdsproj -c Release
    # Output: solutions/PPDSDemo/bin/Release/PPDSDemo.zip (managed)
```

### Deploy to Target Environment

```yaml
- name: Import managed solution
  run: |
    pac auth create --url ${{ secrets.TARGET_URL }} ...
    pac solution import --path solutions/PPDSDemo/bin/Release/PPDSDemo.zip --activate-plugins
```

---

## Migration: Fixing Incorrect Structure

If your solution has `Managed/` and `Unmanaged/` subfolders:

1. **Delete the subfolders**
   ```bash
   rm -rf solutions/PPDSDemo/src/Managed
   rm -rf solutions/PPDSDemo/src/Unmanaged
   ```

2. **Clean the root src/ folder** (may have duplicates)
   ```bash
   rm -rf solutions/PPDSDemo/src/*
   ```

3. **Fresh export from dev**
   ```bash
   pac solution export --name PPDSDemo --path solutions/exports/PPDSDemo.zip --overwrite
   ```

4. **Unpack correctly**
   ```bash
   pac solution unpack --zipfile solutions/exports/PPDSDemo.zip --folder solutions/PPDSDemo/src --allowDelete --allowWrite
   ```

5. **Verify structure** - should have no Managed/Unmanaged subfolders

6. **Commit the corrected structure**

---

## See Also

- [TOOLS_REFERENCE.md](TOOLS_REFERENCE.md) - PowerShell script authentication
- [PAC_CLI_REFERENCE.md](PAC_CLI_REFERENCE.md) - PAC CLI commands
- [Microsoft: Solution concepts](https://learn.microsoft.com/en-us/power-platform/alm/solution-concepts-alm)
- [Microsoft: Organize solutions](https://learn.microsoft.com/en-us/power-platform/alm/organize-solutions)
