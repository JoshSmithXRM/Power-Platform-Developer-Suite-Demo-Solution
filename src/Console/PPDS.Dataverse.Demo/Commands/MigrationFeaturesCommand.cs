using System.CommandLine;
using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using PPDS.Dataverse.Demo.Infrastructure;
using PPDS.Migration.Export;
using PPDS.Migration.Models;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Demonstrates PPDS.Migration library features.
/// </summary>
public static class MigrationFeaturesCommand
{
    private static readonly string SchemaPath = Path.Combine(AppContext.BaseDirectory, "migration", "schema-features.xml");
    private static readonly string UserMappingPath = Path.Combine(AppContext.BaseDirectory, "migration", "user-mapping.xml");
    private static readonly string OutputPath = Path.Combine(AppContext.BaseDirectory, "features-export.zip");

    public static Command Create()
    {
        var featureOption = new Option<string>("--feature")
        {
            DefaultValueFactory = _ => "all"
        };
        var envOption = GlobalOptionsExtensions.CreateEnvironmentOption();
        var verboseOption = GlobalOptionsExtensions.CreateVerboseOption();
        var debugOption = GlobalOptionsExtensions.CreateDebugOption();

        var command = new Command("demo-features", "Demonstrate migration features")
        {
            featureOption,
            envOption,
            verboseOption,
            debugOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var options = new GlobalOptions
            {
                Environment = parseResult.GetValue(envOption),
                Verbose = parseResult.GetValue(verboseOption),
                Debug = parseResult.GetValue(debugOption)
            };
            return await ExecuteAsync(parseResult.GetValue(featureOption)!, options);
        });

        return command;
    }

    public static async Task<int> ExecuteAsync(string feature, GlobalOptions options)
    {
        ConsoleWriter.Header("PPDS.Migration Feature Demonstration");
        using var host = HostFactory.CreateHostForMigration(options);
        var pool = HostFactory.GetConnectionPool(host, options.Environment);
        if (pool == null) return 1;
        var exporter = host.Services.GetRequiredService<IExporter>();
        var envName = HostFactory.ResolveEnvironment(host, options);
        Console.WriteLine($"  Environment: {envName}");
        Console.WriteLine();
        try
        {
            var features = feature.ToLowerInvariant();
            if (features == "all" || features == "m2m") await DemoM2MRelationships(exporter);
            if (features == "all" || features == "filtering") DemoAttributeFiltering();
            if (features == "all" || features == "user-mapping") DemoUserMapping();
            if (features == "all" || features == "plugin-disable") DemoPluginDisable();
            Console.WriteLine();
            ConsoleWriter.ResultBanner("FEATURE DEMO COMPLETE", success: true);
            return 0;
        }
        catch (Exception ex)
        {
            ConsoleWriter.Exception(ex, options.Debug);
            return 1;
        }
    }

    private static async Task DemoM2MRelationships(IExporter exporter)
    {
        ConsoleWriter.Section("Feature 1: M2M Relationship Support");
        Console.WriteLine("  M2M relationships link entities without foreign keys.");
        Console.WriteLine();
        if (File.Exists(SchemaPath))
        {
            Console.Write("  Exporting with M2M... ");
            try
            {
                var result = await exporter.ExportAsync(SchemaPath, OutputPath, new ExportOptions(), null, CancellationToken.None);
                if (result.Success) ConsoleWriter.Success("Done");
                else Console.WriteLine("Export failed");
            }
            catch { Console.WriteLine("Skipped (requires connection)"); }
        }
        Console.WriteLine();
    }

    private static void DemoAttributeFiltering()
    {
        ConsoleWriter.Section("Feature 2: Attribute Filtering");
        Console.WriteLine("  Control which attributes are included via SchemaGeneratorOptions.");
        Console.WriteLine();
    }

    private static void DemoUserMapping()
    {
        ConsoleWriter.Section("Feature 3: User Mapping");
        Console.WriteLine("  Use ImportOptions.StripOwnerFields = true for cross-env migrations.");
        Console.WriteLine();
    }

    private static void DemoPluginDisable()
    {
        ConsoleWriter.Section("Feature 4: Plugin Disable/Enable");
        Console.WriteLine("  Use disableplugins attribute in schema for bulk imports.");
        Console.WriteLine();
    }
}
