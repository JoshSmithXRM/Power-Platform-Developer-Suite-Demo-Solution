# PPDS Demo

Reference implementation for Dynamics 365 / Dataverse projects.

## Solution Context

| Property | Value |
|----------|-------|
| Solution Name | `PPDSDemo` |
| Publisher Prefix | `ppds` |
| Schema Prefix | `ppds_` |
| Entity Binding | Early-bound (see `src/Entities/`) |
| Plugin Framework | PPDS.Plugins (attribute-based registration) |

## NEVER

- Store `IPooledClient` in fields - get per operation, dispose with `await using`
- Hardcode parallelism values - query `RecommendedDegreesOfParallelism` dynamically
- Use `Console.WriteLine` in plugins - sandbox blocks it; use `ITracingService`
- Run sync plugins in Pre-Create - entity doesn't exist yet; use Post-Create
- Set alternate key in both `KeyAttributes` AND `Attributes` - causes duplicate key error

## ALWAYS

- Use `ITracingService` for debugging - only way to get runtime output in sandbox
- Wrap plugin exceptions in `InvalidPluginExecutionException` - platform requirement
- Dispose pooled clients with `await using` - returns connection to pool
- Use bulk APIs for 10+ records - 200x throughput vs single requests
- Use early-bound entities for type safety - compile-time checking prevents runtime errors

## Configuration

See `README.md#configuration` for User Secrets setup (single and multi-environment).

## Key Files

- `src/Plugins/` - Plugin implementations with `PluginStepAttribute`
- `src/Services/` - Connection pool and bulk operation patterns
- `docs/reference/PLUGIN_COMPONENTS_REFERENCE.md` - Plugin patterns and error handling
- `solutions/PPDSDemo/` - Solution with deployment settings

## Commands

| Command | Purpose |
|---------|---------|
| `dotnet build src/Plugins -c Release` | Build plugins |
| `pac solution export --name PPDSDemo` | Export solution |
| `pac modelbuilder build` | Regenerate early-bound entities |

## Git Workflow

| Flow | Strategy |
|------|----------|
| `feature/*` → `develop` | Squash |
| `develop` → `main` | Regular merge |
| `hotfix/*` → `main` | Regular merge, cherry-pick to develop |

## See Also

- `docs/guides/` - Setup and configuration guides
- `docs/reference/` - Plugin, web resource, and solution patterns
