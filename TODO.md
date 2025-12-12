# Project TODO

Tracking document for documentation and implementation tasks.

---

## Phase 1: Documentation Foundation

### Strategy Documents

| Document | Status | Description |
|----------|--------|-------------|
| `docs/README.md` | ✅ Complete | Navigation hub, links to all documentation |
| `docs/strategy/ALM_OVERVIEW.md` | ✅ Complete | High-level ALM philosophy and principles |
| `docs/strategy/ENVIRONMENT_STRATEGY.md` | ✅ Complete | Dev/QA/Prod environments, mapping to branches |
| `docs/strategy/BRANCHING_STRATEGY.md` | ✅ Complete | develop/main workflow, PR process, merge strategy |
| `docs/strategy/PIPELINE_STRATEGY.md` | ✅ Complete | CI/CD approach, PAC CLI, extensibility |

### Configuration

| Task | Status | Description |
|------|--------|-------------|
| Update CLAUDE.md | ✅ Complete | Add documentation style guide section |

---

## Phase 1.5: Branching Implementation (Current)

| Task | Status | Description |
|------|--------|-------------|
| Update BRANCHING_STRATEGY.md | Pending | Add squash/merge strategy section |
| Create working branch | Pending | Move current work off main |
| Reset main to initial state | Pending | Clean slate for proper flow |
| Create develop branch | Pending | From reset main |
| Update workflows for develop/main | Pending | Modify CI/CD for new branching |
| PR working branch → develop | Pending | Showcase PR workflow |
| PR develop → main | Pending | Showcase release workflow |

---

## Phase 2: Implementation Guides

| Document | Status | Description |
|----------|--------|-------------|
| `docs/guides/GETTING_STARTED_GUIDE.md` | Pending | New developer onboarding |
| `docs/guides/MAKING_CHANGES_GUIDE.md` | Pending | Day-to-day development workflow |
| `docs/guides/DEPLOYING_CHANGES_GUIDE.md` | Pending | How deployments work |
| `docs/guides/ENVIRONMENT_SETUP_GUIDE.md` | Pending | Configuring GitHub environments and secrets |
| `docs/guides/CREATING_NEW_SOLUTION_GUIDE.md` | Pending | Adding solutions to this pattern |

---

## Phase 3: Pipeline Expansion

| Document/Task | Status | Description |
|---------------|--------|-------------|
| `docs/pipelines/PIPELINE_OVERVIEW.md` | Pending | Detailed pipeline architecture |
| `docs/pipelines/CUSTOMIZATION_GUIDE.md` | Pending | How to extend/modify pipelines |
| `.github/workflows/deploy-to-prod.yml` | Pending | Production deployment workflow |
| `.github/workflows/validate-pr.yml` | Pending | PR validation pipeline |
| `docs/pipelines/templates/README.md` | Pending | Reusable pipeline templates |
| Multi-solution deployment template | Pending | Ordered deployment for dependent solutions |

---

## Phase 4: Reference Material

| Document | Status | Description |
|----------|--------|-------------|
| `docs/reference/NAMING_CONVENTIONS_REFERENCE.md` | Pending | Standards for branches, solutions, environments |
| `docs/reference/PAC_CLI_REFERENCE.md` | Pending | Common PAC CLI commands with examples |
| `docs/reference/TROUBLESHOOTING_REFERENCE.md` | Pending | Common issues and solutions |

---

## Phase 5: Strategy Documents (Future)

| Document | Status | Description |
|----------|--------|-------------|
| `docs/strategy/SOLUTION_STRATEGY.md` | Pending | Multi-solution architecture, layering, dependencies |
| `docs/strategy/SECURITY_STRATEGY.md` | Pending | Service principals, roles, least privilege |
| `docs/strategy/DATA_STRATEGY.md` | Pending | Reference data migration, test data |

---

## Future Considerations (Not Scheduled)

These are topics we may document when the need arises:

- **Personal Developer Environments (PDE)** - Per-developer isolated environments
- **Power Platform Pipelines integration** - Hybrid approach with native Pipelines
- **Environment provisioning automation** - Automated environment creation/teardown
- **Canvas app source control** - Handling canvas app merge challenges
- **Solution Checker integration** - Automated quality gates in CI/CD
- **Approval gates** - Manual approval workflows for production
- **Rollback automation** - Quick rollback procedures on failure
- **ALM Accelerator comparison** - When to use ALM Accelerator vs this pattern

---

## Completed

| Task | Date | Notes |
|------|------|-------|
| Initial CI/CD pipeline | 2025-12 | PAC CLI based, export from Dev, deploy to QA |
| Dependabot configuration | 2025-12 | Weekly GitHub Actions updates |

