using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // Configure HttpClient for Web API calls
        services.AddHttpClient("WebApi", client =>
        {
            var baseUrl = context.Configuration["WebApiBaseUrl"]
                ?? throw new InvalidOperationException("WebApiBaseUrl configuration is required");
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // For production with managed identity, add auth handler
        // services.AddTransient<ManagedIdentityAuthHandler>();
    })
    .Build();

host.Run();
