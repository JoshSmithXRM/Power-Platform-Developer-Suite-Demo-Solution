# Plugin Deployment Tooling - Implementation Progress

Tracking document for the plugin deployment feature implementation.

---

## Overview

| Item | Status |
|------|--------|
| Design Document | Complete |
| Feature Branch | `feature/plugin-deployment-tooling` |
| Target | Develop branch |

---

## Implementation Phases

### Phase 1: SDK Attributes

Create the shared attribute library for plugin step configuration.

| Task | Status | Notes |
|------|--------|-------|
| Create `PPDSDemo.Sdk` project | Complete | Shared library with strong naming |
| Implement `PluginStepAttribute` | Complete | Message, Entity, Stage, Mode, FilteringAttributes, etc. |
| Implement `PluginImageAttribute` | Complete | ImageType, Name, Attributes, StepId linking |
| Implement enums (Stage, Mode, ImageType) | Complete | PluginStage, PluginMode, PluginImageType |
| Add reference from Plugins project | Complete | |
| Add reference from PluginPackage project | Complete | |
| Update existing plugins with attributes | Complete | AccountPreCreatePlugin, ContactPostUpdatePlugin, AccountAuditLogPlugin |

### Phase 2: Extraction Tooling

Build the tool to extract registrations from compiled assemblies.

| Task | Status | Notes |
|------|--------|-------|
| Create `Extract-PluginRegistrations.ps1` | Not Started | |
| Implement DLL reflection logic | Not Started | Load assembly, find attributes |
| Generate JSON schema | Not Started | `plugin-registration.schema.json` |
| Generate registrations.json | Not Started | |
| Test with classic plugins | Not Started | |
| Test with plugin packages | Not Started | |

### Phase 3: Deployment Script

Build the deployment script using PAC CLI + Web API.

| Task | Status | Notes |
|------|--------|-------|
| Create `Deploy-Plugins.ps1` | Not Started | |
| Create `lib/PluginDeployment.psm1` | Not Started | Shared functions |
| Implement PAC auth integration | Not Started | |
| Implement `pac plugin push` wrapper | Not Started | Assembly + NuGet support |
| Implement PluginType lookup/create | Not Started | Web API |
| Implement SdkMessageProcessingStep create/update | Not Started | Web API |
| Implement SdkMessageProcessingStepImage create/update | Not Started | Web API |
| Implement orphan detection | Not Started | |
| Implement `-Force` cleanup | Not Started | |
| Implement `-WhatIf` mode | Not Started | |
| Add environment parameter (Dev/QA/Prod) | Not Started | |

### Phase 4: CI/CD Integration

Create the GitHub Actions workflow for automated deployment.

| Task | Status | Notes |
|------|--------|-------|
| Create `ci-plugin-deploy.yml` workflow | Not Started | |
| Configure environment secrets | Not Started | DEV_ENV_URL, credentials |
| Test workflow trigger on develop push | Not Started | |
| Verify nightly export captures registrations | Not Started | |

### Phase 5: Documentation & Cleanup

| Task | Status | Notes |
|------|--------|-------|
| Update CLAUDE.md with plugin patterns | Not Started | |
| Add usage examples to design doc | Not Started | |
| Create developer guide | Not Started | How to add new plugins |
| PR review and merge | Not Started | |

---

## Current Focus

**Phase 1: SDK Attributes**

Starting with the attribute library as it's the foundation for everything else.

---

## Decisions Log

| Date | Decision | Rationale |
|------|----------|-----------|
| 2025-12-13 | Use attributes over JSON config | Keeps config with code, proven pattern (Spkl) |
| 2025-12-13 | Generate + preserve JSON | Enables PR review, debugging, documentation |
| 2025-12-13 | PAC CLI for assembly push | Leverage existing tooling |
| 2025-12-13 | Web API for step registration | PAC CLI doesn't support step registration |
| 2025-12-13 | Default warn, -Force to delete | Safety first for orphaned steps |
| 2025-12-13 | Dev default, support all envs | Most common use case is local dev |

---

## Blockers & Risks

| Risk | Mitigation |
|------|------------|
| PAC CLI version compatibility | Pin version in setup action |
| Web API authentication | Use existing pac-auth action |
| Step name collisions | Use consistent naming convention |
| Reflection across .NET versions | Test with both net462 and net6.0 |

---

## Notes

_Implementation notes will be added as work progresses._
