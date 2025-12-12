# Project TODO

Tracking document for documentation and implementation tasks.

---

## Phase 1: Documentation Foundation (Current)

### Strategy Documents

| Document | Status | Description |
|----------|--------|-------------|
| `docs/README.md` | ✅ Complete | Navigation hub, links to all documentation |
| `docs/strategy/ALM_OVERVIEW.md` | ✅ Complete | High-level ALM philosophy and principles |
| `docs/strategy/ENVIRONMENT_STRATEGY.md` | ✅ Complete | Dev/QA/Prod environments, mapping to branches |
| `docs/strategy/BRANCHING_STRATEGY.md` | ✅ Complete | develop/main workflow, PR process |
| `docs/strategy/PIPELINE_STRATEGY.md` | ✅ Complete | CI/CD approach, PAC CLI, extensibility |
| `docs/strategy/SOLUTION_STRATEGY.md` | Pending | Solution architecture (future - when needed) |

### Configuration

| Task | Status | Description |
|------|--------|-------------|
| Update CLAUDE.md | ✅ Complete | Add documentation style guide section |
| Create develop branch | Pending | Implement branching strategy |
| Update workflows for develop/main | Pending | Modify CI/CD for new branching |

---

## Phase 2: Implementation Guides (After Branching Implementation)

| Document | Status | Description |
|----------|--------|-------------|
| `docs/guides/GETTING_STARTED.md` | Pending | New developer onboarding |
| `docs/guides/MAKING_CHANGES.md` | Pending | Day-to-day development workflow |
| `docs/guides/DEPLOYING_CHANGES.md` | Pending | How deployments work |
| `docs/guides/ENVIRONMENT_SETUP.md` | Pending | Configuring environments and secrets |

---

## Phase 3: Pipeline Documentation (As We Expand)

| Document | Status | Description |
|----------|--------|-------------|
| `docs/pipelines/PIPELINE_OVERVIEW.md` | Pending | Architecture and design |
| `docs/pipelines/CUSTOMIZATION.md` | Pending | How to extend/modify |
| `docs/pipelines/templates/README.md` | Pending | Reusable pipeline templates |

---

## Phase 4: Reference Material (As Needed)

| Document | Status | Description |
|----------|--------|-------------|
| `docs/reference/NAMING_CONVENTIONS.md` | Pending | Standards for everything |
| `docs/reference/PAC_CLI_REFERENCE.md` | Pending | Common PAC CLI commands |
| `docs/reference/TROUBLESHOOTING.md` | Pending | Common issues and solutions |

---

## Future Considerations

- Multi-solution strategy documentation (when solution layering is needed)
- Environment provisioning automation guide
- Personal Developer Environment (PDE) guide
- Power Platform Pipelines integration guide (hybrid approach)
- Data migration patterns
- Canvas app source control considerations

---

## Completed

| Task | Date | Notes |
|------|------|-------|
| Initial CI/CD pipeline | 2025-12 | PAC CLI based, export from Dev, deploy to QA |
| Dependabot configuration | 2025-12 | Weekly GitHub Actions updates |

