# CLAUDE.md Guide for Dynamics 365 Projects

How to create and maintain an effective CLAUDE.md for AI-assisted Dynamics 365 / Dataverse development.

---

## What is CLAUDE.md?

CLAUDE.md is a context file that AI assistants read at the start of every conversation. It establishes:
- **Rules** - What must never or always happen
- **Context** - Project-specific settings and conventions
- **Patterns** - Code templates and architectural decisions
- **Navigation** - Where to find detailed documentation

Think of it as onboarding documentation for an AI developer joining your team.

---

## Core Principles

### 1. Brevity Over Completeness
The CLAUDE.md should be **under 200 lines**. AI assistants read this on every request - bloat wastes tokens and dilutes important rules.

**Put in CLAUDE.md:** Rules, quick reference, critical patterns
**Put in docs/:** Detailed explanations, full examples, edge cases

### 2. Rules Need Rationale
Every rule should have a brief "why". Without context, rules get misapplied or ignored at the wrong time.

```markdown
# Bad - No context
| Hardcoded GUIDs | Don't use them |

# Good - Clear rationale
| Hardcoded GUIDs | Breaks across environments; use config or queries |
```

### 3. Show, Don't Just Tell
Include actual code for critical patterns. "Use try/catch" is less useful than a complete error handling template.

### 4. Scannable Format
Use tables for rules, code blocks for patterns, and headers for navigation. AI assistants parse structured content better than prose.

---

## Recommended Structure

```
CLAUDE.md
├── Solution Context        # Project-specific values (prefix, solution name)
├── NEVER                   # Non-negotiable prohibitions with rationale
├── ALWAYS                  # Required patterns with rationale
├── Critical Patterns       # Code templates (error handling, common operations)
├── When to Use What        # Architectural decision guidance
├── Naming Conventions      # Consistent naming rules
├── Solution Structure      # Folder layout diagram
├── Common Commands         # Frequently used CLI commands
├── Git Workflow            # Branching and merge strategy
└── Reference Documentation # Links to detailed docs
```

---

## Section Templates

### Solution Context
Project-specific values that vary between projects.

```markdown
## Solution Context

| Property | Value |
|----------|-------|
| Solution Name | `YourSolutionName` |
| Publisher Prefix | `pub` |
| Schema Prefix | `pub_` |
| Entity Binding | Early-bound / Late-bound |
| Plugin Framework | PPDS.Plugins / Manual registration |
```

**Customize for your project:**
- Solution unique name (matches Dataverse)
- Publisher prefix assigned to your organization
- Whether you use early-bound or late-bound entities
- Plugin registration approach

### NEVER Rules
Platform constraints and project policies that must never be violated.

```markdown
## NEVER (Non-Negotiable)

| Rule | Why |
|------|-----|
| `Console.WriteLine` in plugins | Sandbox blocks it; use `ITracingService` |
| Hardcoded GUIDs | Breaks across environments; use config or queries |
| `Xrm.Page` in JavaScript | Deprecated since v9; use `formContext` |
| Sync plugins in Pre-Create | Entity doesn't exist yet; use Post-Create |
```

**Dynamics-specific NEVERs to consider:**
- Console.WriteLine (sandbox restriction)
- Hardcoded GUIDs (environment portability)
- Xrm.Page (deprecation)
- alert() (UCI blocking)
- Static state in plugins (sandbox recycling)
- External assemblies (sandbox whitelist)
- Thread.Sleep in sync plugins (timeout risk)
- Direct SQL access (unsupported, breaks upgrade)

### ALWAYS Rules
Required patterns that ensure consistency and correctness.

```markdown
## ALWAYS (Required Patterns)

| Rule | Why |
|------|-----|
| `ITracingService` for debugging | Only way to get runtime output in sandbox |
| try/catch with `InvalidPluginExecutionException` | Platform requires this type for user-facing errors |
| Check `InputParameters.Contains("Target")` | Not all messages have Target; prevents null ref |
```

**Dynamics-specific ALWAYS to consider:**
- ITracingService for all debug output
- InvalidPluginExecutionException for user errors
- InputParameters validation
- formContext from executionContext
- Namespace patterns in JavaScript
- Early-bound or late-bound consistency
- Environment-specific deployment settings

### Error Handling Pattern
Include actual code, not just instructions.

```markdown
## Error Handling Pattern

\`\`\`csharp
public void Execute(IServiceProvider serviceProvider)
{
    var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
    var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));

    try
    {
        tracingService.Trace("Plugin started: {0}", context.MessageName);
        // Plugin logic here
        tracingService.Trace("Plugin completed successfully");
    }
    catch (InvalidPluginExecutionException)
    {
        throw; // Re-throw business exceptions as-is
    }
    catch (Exception ex)
    {
        tracingService.Trace("Error: {0}", ex.ToString());
        throw new InvalidPluginExecutionException(
            $"An error occurred. Contact support with timestamp: {DateTime.UtcNow:O}", ex);
    }
}
\`\`\`
```

### When to Use What
Architectural decision guidance prevents wrong tool selection.

```markdown
## When to Use What

| Scenario | Use | Why |
|----------|-----|-----|
| Sync validation | Plugin (Pre-Operation) | Runs in transaction, can cancel |
| Post-save automation | Plugin (Post-Op Async) | Non-blocking, retries |
| User-triggered flows | Power Automate | Visible to makers, easy to modify |
| Long-running (>2 min) | Azure Function | No platform timeouts |
| External integration | Custom API + Azure | Clean contract, scalable |
```

**Decision criteria to document:**
- Sync vs async requirements
- Transaction boundaries
- Timeout constraints (2 min plugin limit)
- Who needs to modify it (developers vs makers)
- Retry and error handling needs
- Performance and scale requirements

### Reference Documentation
Link to detailed docs, organized by purpose.

```markdown
## Reference Documentation

### Strategy (Why)
- [ALM_OVERVIEW.md](docs/strategy/ALM_OVERVIEW.md) - Philosophy
- [BRANCHING_STRATEGY.md](docs/strategy/BRANCHING_STRATEGY.md) - Git workflow

### Reference (How)
- [PLUGIN_COMPONENTS_REFERENCE.md](docs/reference/PLUGIN_COMPONENTS_REFERENCE.md) - Patterns
- [WEBRESOURCE_PATTERNS.md](docs/reference/WEBRESOURCE_PATTERNS.md) - JavaScript

### Guides (Step-by-Step)
- [GETTING_STARTED_GUIDE.md](docs/guides/GETTING_STARTED_GUIDE.md) - Setup
```

---

## Documentation Hierarchy

Organize supporting documentation in a three-tier structure:

```
docs/
├── strategy/      # WHY - Architectural decisions, rationale
│   ├── ALM_OVERVIEW.md
│   ├── BRANCHING_STRATEGY.md
│   └── ENVIRONMENT_STRATEGY.md
│
├── reference/     # HOW - Technical patterns, detailed examples
│   ├── PLUGIN_COMPONENTS_REFERENCE.md
│   ├── WEBRESOURCE_PATTERNS.md
│   └── TESTING_PATTERNS.md
│
└── guides/        # STEPS - Procedural instructions
    ├── GETTING_STARTED_GUIDE.md
    ├── ENVIRONMENT_SETUP_GUIDE.md
    └── DEPLOYMENT_GUIDE.md
```

**Strategy docs** explain decisions and alternatives. Use when evaluating approaches.
**Reference docs** provide detailed patterns and examples. Use when implementing.
**Guide docs** give step-by-step procedures. Use when performing specific tasks.

---

## What NOT to Include

### Don't Include in CLAUDE.md

1. **Marketing/Overview Content**
   - "This is a demo solution for..." belongs in README.md
   - CLAUDE.md is for working on the code, not understanding what the repo is

2. **External Documentation Links**
   - AI assistants can search for Microsoft docs
   - Links become outdated and add noise

3. **Obvious Things**
   - "Write clean code" - too vague to be useful
   - "Follow best practices" - not actionable

4. **Tool-Specific Instructions**
   - "Use the Grep tool instead of bash grep" - these are AI assistant meta-instructions
   - If needed, put in a separate `.claude/instructions.md` file

5. **Full Tutorials**
   - Keep code examples minimal in CLAUDE.md
   - Full tutorials go in docs/guides/

### Don't Make These Mistakes

1. **Listing Rules Without Why**
   - Every rule needs brief rationale or it gets ignored

2. **Contradictory Guidance**
   - "Use early-bound" in one place, late-bound example in another

3. **Stale References**
   - Links to docs that don't exist or have moved

4. **Too Many "Critical" Patterns**
   - If everything is critical, nothing is. Limit to 3-5 actual critical patterns.

---

## Maintenance

### When to Update CLAUDE.md

- **New platform constraint discovered** - Add to NEVER
- **Team convention established** - Add to ALWAYS or Naming
- **Repeated mistakes** - Add explicit rule to prevent
- **Docs restructured** - Update Reference section

### When to Add New Docs

- **Pattern needs full example** - Create reference doc
- **Decision needs explanation** - Create strategy doc
- **Process needs steps** - Create guide doc

### Review Checklist

- [ ] Under 200 lines
- [ ] All rules have rationale
- [ ] Code examples are complete and correct
- [ ] All doc references point to existing files
- [ ] No demo/placeholder content
- [ ] Solution Context matches actual project values

---

## Quick Start Template

Minimal CLAUDE.md for a new Dynamics 365 project:

```markdown
# CLAUDE.md

**Rules for AI assistants working on this Dynamics 365 project.**

## Solution Context

| Property | Value |
|----------|-------|
| Solution Name | `MySolution` |
| Publisher Prefix | `pub` |
| Schema Prefix | `pub_` |

## NEVER

| Rule | Why |
|------|-----|
| `Console.WriteLine` in plugins | Sandbox blocks it |
| Hardcoded GUIDs | Breaks across environments |
| `Xrm.Page` | Deprecated; use `formContext` |

## ALWAYS

| Rule | Why |
|------|-----|
| `ITracingService` for debugging | Required for sandbox output |
| try/catch with `InvalidPluginExecutionException` | Platform requirement |
| Conventional commits | `feat:`, `fix:`, `chore:` |

## Naming Conventions

Dataverse has two name formats:
- **Logical Name**: Always lowercase (`pub_customer`, `pub_firstname`)
- **Schema Name**: PascalCase with lowercase prefix (`pub_Customer`, `pub_FirstName`)

| Component | Logical Name | Schema Name |
|-----------|--------------|-------------|
| Tables | `pub_customer` | `pub_Customer` |
| Columns | `pub_firstname` | `pub_FirstName` |
| Plugins | `{Entity}{Message}Plugin` | `AccountCreatePlugin` |
```

Expand from this base as your project grows.
