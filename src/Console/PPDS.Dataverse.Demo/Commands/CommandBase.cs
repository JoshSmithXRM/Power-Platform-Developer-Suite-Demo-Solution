using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PPDS.Dataverse.DependencyInjection;
using PPDS.Dataverse.Pooling;
using PPDS.Dataverse.Resilience;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Base class for commands providing Dataverse connectivity via connection pool.
///
/// Configuration is read from appsettings.json with environment variable overrides:
/// - Dataverse:DefaultEnvironment - which environment to use when --env not specified
/// - Dataverse:Environments:{Name}:Url - environment URL
/// - Dataverse:Environments:{Name}:Connections:N:ClientId - app registration
/// - Dataverse:Environments:{Name}:Connections:N:ClientSecret - secret (or env var name)
/// </summary>
public abstract class CommandBase
{
    /// <summary>
    /// Creates a host with Dataverse connection pool configured.
    /// </summary>
    /// <param name="environment">Environment name (e.g., "Dev", "QA"). If null, uses DefaultEnvironment from config.</param>
    public static IHost CreateHost(string? environment = null)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                services.AddDataverseConnectionPool(context.Configuration, environment: environment);
            })
            .Build();
    }

    /// <summary>
    /// Creates a host configured for bulk operations with optional parallelism and verbose logging.
    /// </summary>
    /// <param name="environment">Environment name. If null, uses DefaultEnvironment from config.</param>
    /// <param name="parallelism">Max parallel batches. If null, uses SDK default.</param>
    /// <param name="verbose">Enable debug-level logging for PPDS.Dataverse namespace.</param>
    /// <param name="ratePreset">Adaptive rate control preset. If null, uses config default.</param>
    public static IHost CreateHostForBulkOperations(
        string? environment = null,
        int? parallelism = null,
        bool verbose = false,
        RateControlPreset? ratePreset = null)
    {
        return Host.CreateDefaultBuilder()
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

                services.Configure<DataverseOptions>(options =>
                {
                    options.Pool.DisableAffinityCookie = true;
                    if (parallelism.HasValue)
                    {
                        options.BulkOperations.MaxParallelBatches = parallelism.Value;
                    }
                    if (ratePreset.HasValue)
                    {
                        options.AdaptiveRate.Preset = ratePreset.Value;
                    }
                });
            })
            .Build();
    }

    /// <summary>
    /// Validates the connection pool is enabled and returns it.
    /// Prints setup instructions if not configured.
    /// </summary>
    public static IDataverseConnectionPool? GetConnectionPool(IHost host)
    {
        var pool = host.Services.GetRequiredService<IDataverseConnectionPool>();

        if (!pool.IsEnabled)
        {
            WriteError("Connection pool is not enabled.");
            Console.WriteLine();
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
    /// Gets the default environment name from configuration.
    /// </summary>
    public static string GetDefaultEnvironment(IConfiguration config)
    {
        return config["Dataverse:DefaultEnvironment"] ?? "Dev";
    }

    /// <summary>
    /// Resolves the environment name, falling back to DefaultEnvironment from config.
    /// Use this for display purposes instead of showing "(default)".
    /// </summary>
    public static string ResolveEnvironment(IHost host, string? environment)
    {
        if (environment != null) return environment;
        var config = host.Services.GetRequiredService<IConfiguration>();
        return GetDefaultEnvironment(config);
    }

    /// <summary>
    /// Gets the environment URL from configuration.
    /// </summary>
    public static string? GetEnvironmentUrl(IConfiguration config, string environment)
    {
        return config[$"Dataverse:Environments:{environment}:Url"];
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
}
