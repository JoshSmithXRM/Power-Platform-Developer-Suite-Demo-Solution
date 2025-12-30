# Azure Integration Implementation Spec

**Issue:** [#37 - Azure integration components](https://github.com/joshsmithxrm/ppds-demo/issues/37)

**Purpose:** Reference implementation for Dataverse → Azure patterns + test bed for VS Code extension PRT feature.

---

## Quick Reference

| Decision | Choice |
|----------|--------|
| Web API framework | Controller-based ASP.NET Core 8 |
| Azure hosting | App Service (shared plan with Functions) |
| Function → API auth | Managed Identity |
| Plugin → API auth | API Key in Secure Configuration |
| Service endpoints | Registered via PRT (not solution-deployed) |

---

## 1. Project Structure

Create the following structure:

```
src/
├── Api/
│   └── PPDSDemo.Api/
│       ├── Controllers/
│       │   ├── WebhookController.cs
│       │   ├── CustomApiController.cs
│       │   ├── ProductsController.cs
│       │   └── DiagnosticsController.cs
│       ├── Services/
│       │   ├── IAccountService.cs
│       │   ├── AccountService.cs
│       │   ├── IProductService.cs
│       │   └── ProductService.cs
│       ├── Models/
│       │   ├── RemoteExecutionContext.cs
│       │   ├── ProcessAccountRequest.cs
│       │   ├── ProcessAccountResponse.cs
│       │   ├── Product.cs
│       │   └── PoolTestResult.cs
│       ├── Authentication/
│       │   └── ApiKeyAuthHandler.cs
│       ├── Program.cs
│       ├── appsettings.json
│       └── PPDSDemo.Api.csproj
│
├── Functions/
│   └── PPDSDemo.Functions/
│       ├── WebhookTrigger.cs
│       ├── ServiceBusProcessor.cs
│       ├── Program.cs
│       ├── host.json
│       ├── local.settings.json
│       └── PPDSDemo.Functions.csproj
│
├── Plugins/
│   └── PPDSDemo.Plugins/
│       └── Plugins/
│           ├── ProcessAccountPlugin.cs        # NEW
│           └── ExternalProductDataProvider.cs # NEW
│
└── Shared/
    └── PPDSDemo.Shared/
        └── (existing - may add shared models if needed)

infra/
├── main.bicep
├── dev.parameters.json
├── qa.parameters.json
└── prod.parameters.json

.github/workflows/
└── deploy-azure.yml  # NEW - consumes ppds-alm templates
```

---

## 2. Web API (PPDSDemo.Api)

### 2.1 Project Setup

```xml
<!-- PPDSDemo.Api.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="PPDS.Dataverse" Version="1.*" />
    <PackageReference Include="Microsoft.Identity.Web" Version="3.*" />
  </ItemGroup>
</Project>
```

### 2.2 Models

```csharp
// Models/RemoteExecutionContext.cs
// Simplified model for Dataverse webhook payload
public record RemoteExecutionContext
{
    public string MessageName { get; init; } = "";
    public int Stage { get; init; }
    public string PrimaryEntityName { get; init; } = "";
    public Guid PrimaryEntityId { get; init; }
    public int Depth { get; init; }
    public Guid UserId { get; init; }
    public Guid OrganizationId { get; init; }
    public Dictionary<string, object>? InputParameters { get; init; }
}

// Models/ProcessAccountRequest.cs
public record ProcessAccountRequest
{
    public Guid AccountId { get; init; }
    public string Action { get; init; } = "";  // "validate", "enrich", "sync"
}

// Models/ProcessAccountResponse.cs
public record ProcessAccountResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
}

// Models/Product.cs
public record Product
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Sku { get; init; } = "";
    public decimal Price { get; init; }
    public string Category { get; init; } = "";
    public bool InStock { get; init; }
}

// Models/PoolTestResult.cs
public record PoolTestResult
{
    public int OperationCount { get; init; }
    public bool Parallel { get; init; }
    public long TotalMs { get; init; }
    public double AverageMs { get; init; }
    public double MinMs { get; init; }
    public double MaxMs { get; init; }
    public int ErrorCount { get; init; }
}
```

### 2.3 Controllers

#### WebhookController

```csharp
// Controllers/WebhookController.cs
[ApiController]
[Route("api/webhook")]
public class WebhookController : ControllerBase
{
    private readonly IAccountService _accountService;
    private readonly ILogger<WebhookController> _logger;

    // POST /api/webhook/account-created
    // Called by Azure Function (forwarding from Dataverse webhook)
    [HttpPost("account-created")]
    public async Task<IActionResult> AccountCreated([FromBody] RemoteExecutionContext context)
    {
        // 1. Log receipt
        // 2. Use connection pool to create Note on Account
        // 3. Return success
    }

    // POST /api/webhook/account-updated
    // Called by Azure Function (processing Service Bus message)
    [HttpPost("account-updated")]
    public async Task<IActionResult> AccountUpdated([FromBody] RemoteExecutionContext context)
    {
        // 1. Log receipt
        // 2. Use connection pool to update ppds_lastazuresync field
        // 3. Return success
    }
}
```

#### CustomApiController

```csharp
// Controllers/CustomApiController.cs
[ApiController]
[Route("api/custom")]
public class CustomApiController : ControllerBase
{
    private readonly IAccountService _accountService;

    // POST /api/custom/process-account
    // Called by ProcessAccountPlugin
    [HttpPost("process-account")]
    public async Task<ActionResult<ProcessAccountResponse>> ProcessAccount(
        [FromBody] ProcessAccountRequest request)
    {
        // 1. Validate request
        // 2. Use connection pool to retrieve Account
        // 3. Perform action based on request.Action
        // 4. Return ProcessAccountResponse
    }
}
```

#### ProductsController

```csharp
// Controllers/ProductsController.cs
[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;

    // GET /api/products
    // Used by Virtual Table RetrieveMultiple
    [HttpGet]
    public ActionResult<IEnumerable<Product>> GetAll([FromQuery] string? filter)
    {
        // Return filtered/all products from in-memory store
    }

    // GET /api/products/{id}
    // Used by Virtual Table Retrieve
    [HttpGet("{id:guid}")]
    public ActionResult<Product> Get(Guid id)
    {
        // Return single product or 404
    }

    // POST /api/products
    // Used by Virtual Table Create
    [HttpPost]
    public ActionResult<Product> Create([FromBody] Product product)
    {
        // Add to in-memory store, return created product
    }

    // PUT /api/products/{id}
    // Used by Virtual Table Update
    [HttpPut("{id:guid}")]
    public ActionResult<Product> Update(Guid id, [FromBody] Product product)
    {
        // Update in-memory store
    }

    // DELETE /api/products/{id}
    // Used by Virtual Table Delete
    [HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id)
    {
        // Remove from in-memory store
    }
}
```

#### DiagnosticsController

```csharp
// Controllers/DiagnosticsController.cs
[ApiController]
[Route("api/diagnostics")]
public class DiagnosticsController : ControllerBase
{
    private readonly IDataverseConnectionPool _pool;

    // GET /api/diagnostics/health
    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "healthy", timestamp = DateTime.UtcNow });

    // GET /api/diagnostics/pool-test?operations=100&parallel=true
    [HttpGet("pool-test")]
    public async Task<ActionResult<PoolTestResult>> PoolTest(
        [FromQuery] int operations = 100,
        [FromQuery] bool parallel = true)
    {
        // 1. Run 'operations' WhoAmI calls
        // 2. Measure timing (parallel or sequential based on flag)
        // 3. Return PoolTestResult with stats
    }
}
```

### 2.4 Services

```csharp
// Services/IProductService.cs
public interface IProductService
{
    IEnumerable<Product> GetAll(string? filter = null);
    Product? GetById(Guid id);
    Product Create(Product product);
    Product? Update(Guid id, Product product);
    bool Delete(Guid id);
}

// Services/ProductService.cs
// In-memory implementation with sample data
public class ProductService : IProductService
{
    private readonly ConcurrentDictionary<Guid, Product> _products = new();

    public ProductService()
    {
        // Seed with sample products
        SeedProducts();
    }

    private void SeedProducts()
    {
        var samples = new[]
        {
            new Product { Id = Guid.NewGuid(), Name = "Widget Pro", Sku = "WGT-001", Price = 29.99m, Category = "Widgets", InStock = true },
            new Product { Id = Guid.NewGuid(), Name = "Gadget Plus", Sku = "GDG-002", Price = 49.99m, Category = "Gadgets", InStock = true },
            new Product { Id = Guid.NewGuid(), Name = "Thingamajig", Sku = "THG-003", Price = 19.99m, Category = "Things", InStock = false },
            // Add more...
        };
        foreach (var p in samples) _products[p.Id] = p;
    }
    // ... implement interface methods
}

// Services/IAccountService.cs
public interface IAccountService
{
    Task CreateProcessingNoteAsync(Guid accountId, string noteText);
    Task UpdateLastAzureSyncAsync(Guid accountId);
    Task<ProcessAccountResponse> ProcessAccountAsync(ProcessAccountRequest request);
}

// Services/AccountService.cs
public class AccountService : IAccountService
{
    private readonly IDataverseConnectionPool _pool;
    private readonly ILogger<AccountService> _logger;

    // Uses connection pool to interact with Dataverse
}
```

### 2.5 Authentication

```csharp
// Authentication/ApiKeyAuthHandler.cs
// For Plugin → API authentication
// Validates X-API-Key header against configured key

// Program.cs will configure dual auth:
// 1. Azure AD (for Functions with managed identity)
// 2. API Key (for Plugins)
```

### 2.6 Program.cs Configuration

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add Dataverse connection pool
builder.Services.AddDataverseConnectionPool(builder.Configuration);

// Add services
builder.Services.AddSingleton<IProductService, ProductService>();
builder.Services.AddScoped<IAccountService, AccountService>();

// Add authentication (dual: Azure AD + API Key)
builder.Services.AddAuthentication()
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"))
    .AddScheme<ApiKeyAuthOptions, ApiKeyAuthHandler>("ApiKey", null);

builder.Services.AddControllers();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
```

---

## 3. Azure Functions (PPDSDemo.Functions)

### 3.1 Project Setup

```xml
<!-- PPDSDemo.Functions.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <AzureFunctionsVersion>v4</AzureFunctionsVersion>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Azure.Functions.Worker" Version="1.*" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Sdk" Version="1.*" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.Http" Version="3.*" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.ServiceBus" Version="5.*" />
    <PackageReference Include="Azure.Identity" Version="1.*" />
  </ItemGroup>
</Project>
```

### 3.2 WebhookTrigger

```csharp
// WebhookTrigger.cs
public class WebhookTrigger
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebhookTrigger> _logger;

    public WebhookTrigger(IHttpClientFactory httpClientFactory, ILogger<WebhookTrigger> logger)
    {
        _httpClient = httpClientFactory.CreateClient("WebApi");
        _logger = logger;
    }

    [Function("AccountCreatedWebhook")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "webhook")] HttpRequestData req)
    {
        // 1. Read RemoteExecutionContext from request body
        // 2. Forward to Web API /api/webhook/account-created
        // 3. Return response
    }
}
```

### 3.3 ServiceBusProcessor

```csharp
// ServiceBusProcessor.cs
public class ServiceBusProcessor
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ServiceBusProcessor> _logger;

    [Function("AccountUpdatedProcessor")]
    public async Task Run(
        [ServiceBusTrigger("account-updates", Connection = "ServiceBusConnection")] string message)
    {
        // 1. Deserialize RemoteExecutionContext from message
        // 2. Forward to Web API /api/webhook/account-updated
        // 3. Log result
    }
}
```

### 3.4 Program.cs

```csharp
// Program.cs
var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        // Configure HttpClient with managed identity for Web API calls
        services.AddHttpClient("WebApi", client =>
        {
            client.BaseAddress = new Uri(context.Configuration["WebApiBaseUrl"]!);
        })
        .AddHttpMessageHandler<ManagedIdentityAuthHandler>();

        services.AddTransient<ManagedIdentityAuthHandler>();
    })
    .Build();

host.Run();
```

---

## 4. Plugins

### 4.1 ProcessAccountPlugin (Custom API)

```csharp
// Plugins/ProcessAccountPlugin.cs
using System;
using System.Net.Http;
using Microsoft.Xrm.Sdk;
using PPDS.Plugins;

namespace PPDSDemo.Plugins.Plugins
{
    /// <summary>
    /// Implements the ppds_ProcessAccount Custom API.
    /// Calls Azure Web API to process the account.
    /// </summary>
    [PluginStep(
        Message = "ppds_ProcessAccount",
        Stage = PluginStage.MainOperation,
        Mode = PluginMode.Synchronous)]
    public class ProcessAccountPlugin : PluginBase
    {
        private readonly string _apiKey;
        private readonly string _apiBaseUrl;

        public ProcessAccountPlugin(string unsecureConfig, string secureConfig)
        {
            // secureConfig format: "apiKey|baseUrl"
            // Example: "my-secret-key|https://api-ppds-demo.azurewebsites.net"
            var parts = secureConfig?.Split('|') ?? Array.Empty<string>();
            _apiKey = parts.Length > 0 ? parts[0] : "";
            _apiBaseUrl = parts.Length > 1 ? parts[1] : "";
        }

        protected override void ExecutePlugin(LocalPluginContext context)
        {
            // 1. Get input parameters
            var accountRef = context.PluginExecutionContext.InputParameters["AccountId"] as EntityReference;
            var action = context.PluginExecutionContext.InputParameters["Action"] as string;

            if (accountRef == null || string.IsNullOrEmpty(action))
            {
                throw new InvalidPluginExecutionException("AccountId and Action are required.");
            }

            context.Trace($"Processing account {accountRef.Id} with action: {action}");

            // 2. Call Web API
            using var client = new HttpClient();
            client.BaseAddress = new Uri(_apiBaseUrl);
            client.DefaultRequestHeaders.Add("X-API-Key", _apiKey);

            var request = new { AccountId = accountRef.Id, Action = action };
            var content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(request),
                System.Text.Encoding.UTF8,
                "application/json");

            var response = client.PostAsync("/api/custom/process-account", content).Result;
            var responseBody = response.Content.ReadAsStringAsync().Result;

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidPluginExecutionException($"API call failed: {response.StatusCode}");
            }

            // 3. Parse response and set output parameters
            var result = System.Text.Json.JsonSerializer.Deserialize<ProcessAccountResponse>(responseBody);

            context.PluginExecutionContext.OutputParameters["Success"] = result?.Success ?? false;
            context.PluginExecutionContext.OutputParameters["Message"] = result?.Message ?? "";

            context.Trace($"API response: Success={result?.Success}, Message={result?.Message}");
        }

        private record ProcessAccountResponse(bool Success, string Message);
    }
}
```

### 4.2 ExternalProductDataProvider (Virtual Table)

```csharp
// Plugins/ExternalProductDataProvider.cs
using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using PPDS.Plugins;

namespace PPDSDemo.Plugins.Plugins
{
    /// <summary>
    /// Data Provider plugin for ppds_ExternalProduct virtual table.
    /// Routes CRUD operations to Azure Web API.
    /// </summary>
    public class ExternalProductDataProvider : PluginBase
    {
        private readonly string _apiKey;
        private readonly string _apiBaseUrl;

        public ExternalProductDataProvider(string unsecureConfig, string secureConfig)
        {
            var parts = secureConfig?.Split('|') ?? Array.Empty<string>();
            _apiKey = parts.Length > 0 ? parts[0] : "";
            _apiBaseUrl = parts.Length > 1 ? parts[1] : "";
        }

        protected override void ExecutePlugin(LocalPluginContext context)
        {
            var message = context.PluginExecutionContext.MessageName;

            switch (message)
            {
                case "Retrieve":
                    HandleRetrieve(context);
                    break;
                case "RetrieveMultiple":
                    HandleRetrieveMultiple(context);
                    break;
                case "Create":
                    HandleCreate(context);
                    break;
                case "Update":
                    HandleUpdate(context);
                    break;
                case "Delete":
                    HandleDelete(context);
                    break;
                default:
                    throw new InvalidPluginExecutionException($"Unsupported message: {message}");
            }
        }

        private void HandleRetrieve(LocalPluginContext context)
        {
            var target = context.PluginExecutionContext.InputParameters["Target"] as EntityReference;
            // GET /api/products/{id}
            // Set OutputParameters["BusinessEntity"]
        }

        private void HandleRetrieveMultiple(LocalPluginContext context)
        {
            var query = context.PluginExecutionContext.InputParameters["Query"];
            // GET /api/products
            // Set OutputParameters["BusinessEntityCollection"]
        }

        private void HandleCreate(LocalPluginContext context)
        {
            var target = context.PluginExecutionContext.InputParameters["Target"] as Entity;
            // POST /api/products
            // Set OutputParameters["id"]
        }

        private void HandleUpdate(LocalPluginContext context)
        {
            var target = context.PluginExecutionContext.InputParameters["Target"] as Entity;
            // PUT /api/products/{id}
        }

        private void HandleDelete(LocalPluginContext context)
        {
            var target = context.PluginExecutionContext.InputParameters["Target"] as EntityReference;
            // DELETE /api/products/{id}
        }

        private HttpClient CreateClient()
        {
            var client = new HttpClient { BaseAddress = new Uri(_apiBaseUrl) };
            client.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
            return client;
        }
    }
}
```

**Note:** The Data Provider plugin requires multiple step registrations (one per message). This will be done via PRT.

---

## 5. Dataverse Solution Components

### 5.1 Custom Field on Account

| Property | Value |
|----------|-------|
| Display Name | Last Azure Sync |
| Schema Name | ppds_lastazuresync |
| Type | DateTime |
| Format | DateAndTime |
| Behavior | UserLocal |

Add to Account entity via solution.

### 5.2 Custom API Definition

| Property | Value |
|----------|-------|
| Unique Name | ppds_ProcessAccount |
| Display Name | Process Account |
| Binding Type | Unbound (Global) |
| Is Function | No |
| Plugin Type | PPDSDemo.Plugins.Plugins.ProcessAccountPlugin |

**Request Parameters:**

| Name | Type | Required |
|------|------|----------|
| AccountId | EntityReference (account) | Yes |
| Action | String | Yes |

**Response Properties:**

| Name | Type |
|------|------|
| Success | Boolean |
| Message | String |

### 5.3 Virtual Table (ppds_ExternalProduct)

| Property | Value |
|----------|-------|
| Display Name | External Product |
| Plural Name | External Products |
| Schema Name | ppds_ExternalProduct |
| Table Type | Virtual |
| Data Source | Custom Data Provider |

**Columns:**

| Display Name | Schema Name | Type |
|--------------|-------------|------|
| External Product | ppds_externalproductid | Primary Key (GUID) |
| Name | ppds_name | Primary Name (String, 100) |
| SKU | ppds_sku | String (50) |
| Price | ppds_price | Decimal (precision 2) |
| Category | ppds_category | String (100) |
| In Stock | ppds_instock | Boolean |

**Data Provider Registration:**
- Plugin: PPDSDemo.Plugins.Plugins.ExternalProductDataProvider
- Steps: Retrieve, RetrieveMultiple, Create, Update, Delete

---

## 6. Infrastructure (infra/)

### 6.1 main.bicep

```bicep
// References ppds-alm modules (once available)
// For now, can use direct Azure resources

targetScope = 'resourceGroup'

param location string = resourceGroup().location
param environment string
param appNamePrefix string = 'ppds-demo'

// Use ppds-alm modules when available:
// module functionApp 'br:ghcr.io/joshsmithxrm/ppds-alm/modules/function-app:v1' = { ... }

// Or define directly for now
```

### 6.2 Parameter Files

```json
// dev.parameters.json
{
  "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
  "contentVersion": "1.0.0.0",
  "parameters": {
    "environment": { "value": "dev" },
    "appNamePrefix": { "value": "ppds-demo" },
    "serviceBusQueueName": { "value": "account-updates" }
  }
}
```

---

## 7. GitHub Workflow

```yaml
# .github/workflows/deploy-azure.yml
name: Deploy Azure Resources

on:
  workflow_dispatch:
    inputs:
      environment:
        description: 'Target environment'
        required: true
        type: choice
        options:
          - dev
          - qa
          - prod

jobs:
  deploy:
    runs-on: ubuntu-latest
    environment: ${{ inputs.environment }}
    steps:
      - uses: actions/checkout@v4

      - uses: azure/login@v2
        with:
          creds: ${{ secrets.AZURE_CREDENTIALS }}

      - uses: azure/arm-deploy@v2
        with:
          resourceGroupName: ppds-demo-${{ inputs.environment }}
          template: infra/main.bicep
          parameters: infra/${{ inputs.environment }}.parameters.json
```

---

## 8. Implementation Order

Execute in this order to minimize blockers:

### Phase 1: Foundation
1. Create `src/Api/PPDSDemo.Api/` project structure
2. Implement models
3. Implement `ProductService` (in-memory, no external deps)
4. Implement `ProductsController`
5. Implement `DiagnosticsController`
6. Test locally with `dotnet run`

### Phase 2: Dataverse Integration
7. Add PPDS.Dataverse reference
8. Configure connection pool in appsettings
9. Implement `AccountService`
10. Implement `WebhookController`
11. Implement `CustomApiController`
12. Test with local Dataverse connection

### Phase 3: Azure Functions
13. Create `src/Functions/PPDSDemo.Functions/` project
14. Implement `WebhookTrigger`
15. Implement `ServiceBusProcessor`
16. Test locally with Azure Functions Core Tools

### Phase 4: Plugins
17. Create `ProcessAccountPlugin`
18. Create `ExternalProductDataProvider`
19. Build plugin assembly

### Phase 5: Dataverse Solution
20. Add `ppds_lastazuresync` field to Account
21. Create Custom API definition
22. Create Virtual Table definition
23. Export/pack solution

### Phase 6: Infrastructure
24. Create `infra/` folder with Bicep and parameter files
25. Create GitHub workflow

### Phase 7: Registration & Testing
26. Deploy to Azure
27. Register service endpoints via PRT (webhook + service bus)
28. Register plugin steps via PRT
29. End-to-end testing

---

## 9. Code Patterns to Follow

Reference these existing files for consistency:

| Pattern | Reference File |
|---------|----------------|
| Plugin base class | `src/Plugins/PPDSDemo.Plugins/PluginBase.cs` |
| Plugin step attribute | `src/Plugins/PPDSDemo.Plugins/Plugins/AccountPreCreatePlugin.cs` |
| Connection pool usage | `src/Console/PPDSDemo.Console/` (DI patterns) |
| Error handling | `PluginBase.cs` try/catch with tracing |

---

## 10. Testing Checklist

After implementation, verify:

- [ ] `GET /api/products` returns product list
- [ ] `GET /api/diagnostics/health` returns healthy
- [ ] `GET /api/diagnostics/pool-test` executes without errors
- [ ] Webhook fires on Account Create
- [ ] Service Bus message received on Account Update
- [ ] Custom API callable from Dataverse
- [ ] Virtual Table shows products in model-driven app

---

## Related Issues

| Repository | Issue | Purpose |
|------------|-------|---------|
| ppds-alm | #10 | Azure integration Bicep modules |
| ppds-sdk | #48 | Connection pool metrics API |
| ppds-sdk | #49 | Service endpoint CLI commands |
