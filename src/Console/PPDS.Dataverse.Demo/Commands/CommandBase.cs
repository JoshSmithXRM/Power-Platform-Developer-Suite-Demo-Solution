using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PPDS.Dataverse.DependencyInjection;
using PPDS.Dataverse.Pooling;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Base class for commands that need Dataverse connectivity.
/// </summary>
public abstract class CommandBase
{
    /// <summary>
    /// Creates and configures the host with Dataverse connection pool for a specific environment.
    /// </summary>
    /// <param name="environment">Environment name (e.g., "Dev", "QA"). Uses DefaultEnvironment if not specified.</param>
    public static IHost CreateHost(string? environment = null)
    {
        return Host.CreateDefaultBuilder([])
            .ConfigureServices((context, services) =>
            {
                services.AddDataverseConnectionPool(context.Configuration, environment: environment);
            })
            .Build();
    }

    /// <summary>
    /// Creates a host configured for bulk operations for a specific environment.
    /// </summary>
    /// <param name="environment">Environment name (e.g., "Dev", "QA"). Uses DefaultEnvironment if not specified.</param>
    /// <param name="parallelism">Optional max parallel batches. If null, uses SDK default.</param>
    /// <param name="verbose">Enable debug-level logging for PPDS.Dataverse namespace.</param>
    public static IHost CreateHostForBulkOperations(string? environment = null, int? parallelism = null, bool verbose = false)
    {
        return Host.CreateDefaultBuilder([])
            .ConfigureLogging(logging =>
            {
                if (verbose)
                {
                    logging.AddFilter("PPDS.Dataverse", LogLevel.Debug);
                    logging.AddSimpleConsole(options =>
                    {
                        options.SingleLine = true;
                        options.TimestampFormat = "HH:mm:ss.fff ";
                    });
                }
            })
            .ConfigureServices((context, services) =>
            {
                services.AddDataverseConnectionPool(context.Configuration, environment: environment);

                // Apply overrides
                services.Configure<DataverseOptions>(options =>
                {
                    options.Pool.DisableAffinityCookie = true;
                    if (parallelism.HasValue)
                    {
                        options.BulkOperations.MaxParallelBatches = parallelism.Value;
                    }
                });
            })
            .Build();
    }

    /// <summary>
    /// Validates the connection pool is enabled and returns it.
    /// </summary>
    public static IDataverseConnectionPool? GetConnectionPool(IHost host)
    {
        var pool = host.Services.GetRequiredService<IDataverseConnectionPool>();

        if (!pool.IsEnabled)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Connection pool is not enabled.");
            Console.WriteLine();
            Console.ResetColor();
            Console.WriteLine("Configure using .NET User Secrets:");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  cd src/Console/PPDS.Dataverse.Demo");
            Console.WriteLine("  dotnet user-secrets set \"Dataverse:Environments:Dev:Url\" \"https://YOUR-ORG.crm.dynamics.com\"");
            Console.WriteLine("  dotnet user-secrets set \"Dataverse:Environments:Dev:Connections:0:ClientId\" \"your-client-id\"");
            Console.WriteLine("  dotnet user-secrets set \"Dataverse:Environments:Dev:Connections:0:ClientSecret\" \"your-client-secret\"");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("See docs/guides/LOCAL_DEVELOPMENT_GUIDE.md for details.");
            Console.WriteLine();
            return null;
        }

        return pool;
    }

    /// <summary>
    /// Writes a success message in green.
    /// </summary>
    public static void WriteSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>
    /// Writes an error message in red.
    /// </summary>
    public static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>
    /// Writes an info message in cyan.
    /// </summary>
    public static void WriteInfo(string message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>
    /// Builds a connection string from environment configuration.
    /// Used by commands that need to pass connection strings to external tools (e.g., CLI).
    /// </summary>
    /// <param name="config">The configuration instance.</param>
    /// <param name="environment">Environment name (e.g., "Dev", "QA").</param>
    /// <returns>Tuple of (connectionString, displayName). ConnectionString is null if config is incomplete.</returns>
    public static (string? ConnectionString, string DisplayName) BuildConnectionString(
        IConfiguration config,
        string environment)
    {
        var envSection = config.GetSection($"Dataverse:Environments:{environment}");
        if (!envSection.Exists())
        {
            return (null, environment);
        }

        var displayName = envSection["Name"] ?? environment;
        var url = envSection["Url"];
        var clientId = envSection["Connections:0:ClientId"];
        var clientSecret = envSection["Connections:0:ClientSecret"];

        // Check for environment variable-based secret
        var clientSecretVariable = envSection["Connections:0:ClientSecretVariable"];
        if (!string.IsNullOrEmpty(clientSecretVariable) && string.IsNullOrEmpty(clientSecret))
        {
            clientSecret = Environment.GetEnvironmentVariable(clientSecretVariable);
        }

        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            return (null, displayName);
        }

        var connectionString = $"AuthType=ClientSecret;Url={url};ClientId={clientId};ClientSecret={clientSecret}";
        return (connectionString, displayName);
    }

    /// <summary>
    /// Gets the environment URL from configuration.
    /// </summary>
    public static string? GetEnvironmentUrl(IConfiguration config, string environment)
    {
        return config[$"Dataverse:Environments:{environment}:Url"];
    }
}
