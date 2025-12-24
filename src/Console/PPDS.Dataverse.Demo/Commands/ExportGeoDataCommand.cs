using System.CommandLine;
using System.Diagnostics;
using System.Text.RegularExpressions;
using PPDS.Dataverse.Pooling;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Exports geographic reference data to a portable ZIP package.
///
/// This command demonstrates the ppds-migrate CLI export workflow:
///   1. Generate schema: ppds-migrate schema generate -e ppds_state,ppds_city,ppds_zipcode
///   2. Export data: ppds-migrate export --schema schema.xml --output data.zip
///
/// The resulting package can be:
///   - Stored in artifact repositories (Azure Artifacts, Git LFS, S3)
///   - Versioned alongside solution exports
///   - Imported to other environments using import-geo-data command
///
/// Usage:
///   dotnet run -- export-geo-data --output geo-v1.0.zip
///   dotnet run -- export-geo-data --output artifacts/geo-data.zip --env Dev --verbose
/// </summary>
public static class ExportGeoDataCommand
{
    private static readonly string CliPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "..",
            "sdk", "src", "PPDS.Migration.Cli", "bin", "Debug", "net8.0", "ppds-migrate.exe"));

    private static readonly string DefaultSchemaPath = Path.Combine(AppContext.BaseDirectory, "migration", "geo-schema.xml");
    private static readonly string DefaultOutputPath = Path.Combine(AppContext.BaseDirectory, "geo-export.zip");

    public static Command Create()
    {
        var command = new Command("export-geo-data", "Export geographic data to a portable ZIP package");

        var outputOption = new Option<string?>(
            aliases: ["--output", "-o"],
            description: $"Output ZIP file path (default: geo-export.zip)");

        var envOption = new Option<string?>(
            aliases: ["--environment", "--env", "-e"],
            description: "Source environment name (default: Dev)");

        var verboseOption = new Option<bool>(
            ["--verbose", "-v"],
            "Show detailed output including CLI commands");

        command.AddOption(outputOption);
        command.AddOption(envOption);
        command.AddOption(verboseOption);

        command.SetHandler(async (string? output, string? environment, bool verbose) =>
        {
            Environment.ExitCode = await ExecuteAsync(output, environment, verbose);
        }, outputOption, envOption, verboseOption);

        return command;
    }

    public static async Task<int> ExecuteAsync(
        string? outputPath = null,
        string? environment = null,
        bool verbose = false)
    {
        var output = outputPath ?? DefaultOutputPath;
        var env = environment ?? "Dev";

        Console.WriteLine("+==============================================================+");
        Console.WriteLine("|       Export Geographic Data                                 |");
        Console.WriteLine("+==============================================================+");
        Console.WriteLine();

        // Verify CLI exists
        if (!File.Exists(CliPath))
        {
            CommandBase.WriteError($"CLI not found: {CliPath}");
            Console.WriteLine("Build the CLI first: dotnet build ../sdk/src/PPDS.Migration.Cli");
            return 1;
        }

        // Create connection pool to verify source data
        using var host = CommandBase.CreateHost(env);
        var pool = CommandBase.GetConnectionPool(host);

        if (pool == null)
        {
            CommandBase.WriteError($"{env} environment not configured. See docs/guides/LOCAL_DEVELOPMENT_GUIDE.md");
            return 1;
        }

        // Ensure migration directory exists
        var schemaDir = Path.GetDirectoryName(DefaultSchemaPath);
        if (!string.IsNullOrEmpty(schemaDir) && !Directory.Exists(schemaDir))
        {
            Directory.CreateDirectory(schemaDir);
        }

        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            Console.WriteLine($"  Environment: {env}");
            Console.WriteLine($"  Output: {Path.GetFullPath(output)}");
            Console.WriteLine();

            // ===================================================================
            // STEP 1: Verify Source Data
            // ===================================================================
            Console.WriteLine("+-----------------------------------------------------------------+");
            Console.WriteLine("| Step 1: Verify Source Data                                      |");
            Console.WriteLine("+-----------------------------------------------------------------+");

            await using var client = await pool.GetClientAsync();
            var summary = await QueryGeoSummary(client);

            Console.WriteLine($"  States: {summary.StateCount}");
            Console.WriteLine($"  Cities: {summary.CityCount}");
            Console.WriteLine($"  ZIP Codes: {summary.ZipCodeCount:N0}");
            Console.WriteLine($"  Total: {summary.TotalCount:N0} records");
            Console.WriteLine();

            if (summary.TotalCount == 0)
            {
                CommandBase.WriteError($"No geo data found in {env}. Run load-geo-data first.");
                return 1;
            }

            // ===================================================================
            // STEP 2: Generate Schema
            // ===================================================================
            Console.WriteLine("+-----------------------------------------------------------------+");
            Console.WriteLine("| Step 2: Generate Schema (ppds-migrate schema generate)         |");
            Console.WriteLine("+-----------------------------------------------------------------+");

            var geoEntities = "ppds_state,ppds_city,ppds_zipcode";
            Console.Write($"  Generating schema for: {geoEntities}... ");

            var schemaResult = await RunCliAsync(
                $"schema generate -e {geoEntities} -o \"{DefaultSchemaPath}\" --env {env}", verbose);
            if (schemaResult != 0)
            {
                CommandBase.WriteError("Schema generation failed");
                return 1;
            }
            CommandBase.WriteSuccess("Done");
            Console.WriteLine($"  Schema file: {DefaultSchemaPath}");
            Console.WriteLine();

            // ===================================================================
            // STEP 3: Export Data
            // ===================================================================
            Console.WriteLine("+-----------------------------------------------------------------+");
            Console.WriteLine("| Step 3: Export Data (ppds-migrate export)                      |");
            Console.WriteLine("+-----------------------------------------------------------------+");

            Console.Write("  Exporting data package... ");
            var exportResult = await RunCliAsync(
                $"export --schema \"{DefaultSchemaPath}\" --output \"{output}\" --env {env}", verbose);
            if (exportResult != 0)
            {
                CommandBase.WriteError("Export failed");
                return 1;
            }

            var fileInfo = new FileInfo(output);
            CommandBase.WriteSuccess($"Done ({fileInfo.Length / 1024} KB)");
            Console.WriteLine();

            stopwatch.Stop();

            // ===================================================================
            // RESULT
            // ===================================================================
            Console.WriteLine("+==============================================================+");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("|              Export Complete                                  |");
            Console.ResetColor();
            Console.WriteLine("+==============================================================+");
            Console.WriteLine();
            Console.WriteLine($"  Package: {Path.GetFullPath(output)}");
            Console.WriteLine($"  Size: {fileInfo.Length / 1024} KB");
            Console.WriteLine($"  Records: {summary.TotalCount:N0}");
            Console.WriteLine($"  Time: {stopwatch.Elapsed.TotalSeconds:F2}s");
            Console.WriteLine();
            Console.WriteLine("  Next steps:");
            Console.WriteLine($"    Import to QA:   dotnet run -- import-geo-data --data \"{output}\" --env QA");
            Console.WriteLine($"    Import to Prod: ppds-migrate import --data \"{output}\" --mode Upsert --env Prod");

            return 0;
        }
        catch (Exception ex)
        {
            CommandBase.WriteError($"Error: {ex.Message}");
            if (verbose)
            {
                Console.WriteLine(ex.StackTrace);
            }
            return 1;
        }
    }

    private static async Task<int> RunCliAsync(string arguments, bool verbose = false)
    {
        if (verbose)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"\n    > ppds-migrate {RedactSecrets(arguments)}");
            Console.ResetColor();
        }

        var psi = new ProcessStartInfo
        {
            FileName = CliPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return 1;

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var output = await outputTask;
        var error = await errorTask;

        if (verbose && !string.IsNullOrEmpty(output))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            foreach (var line in output.Split('\n').Take(15))
            {
                Console.WriteLine($"    {line}");
            }
            if (output.Split('\n').Length > 15)
            {
                Console.WriteLine($"    ... ({output.Split('\n').Length - 15} more lines)");
            }
            Console.ResetColor();
        }

        if (!string.IsNullOrEmpty(error) && process.ExitCode != 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n    CLI Error: {error}");
            Console.ResetColor();
        }

        return process.ExitCode;
    }

    private static async Task<GeoSummary> QueryGeoSummary(IPooledClient client)
    {
        var summary = new GeoSummary();

        var stateQuery = new Microsoft.Xrm.Sdk.Query.QueryExpression("ppds_state")
        {
            ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet(false),
            PageInfo = new Microsoft.Xrm.Sdk.Query.PagingInfo { Count = 5000, PageNumber = 1 }
        };
        var stateResult = await client.RetrieveMultipleAsync(stateQuery);
        summary.StateCount = stateResult.Entities.Count;

        var cityQuery = new Microsoft.Xrm.Sdk.Query.QueryExpression("ppds_city")
        {
            ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet(false),
            PageInfo = new Microsoft.Xrm.Sdk.Query.PagingInfo { Count = 5000, PageNumber = 1 }
        };
        var cityResult = await client.RetrieveMultipleAsync(cityQuery);
        summary.CityCount = cityResult.Entities.Count;

        var zipQuery = new Microsoft.Xrm.Sdk.Query.QueryExpression("ppds_zipcode")
        {
            ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet(false),
            PageInfo = new Microsoft.Xrm.Sdk.Query.PagingInfo { Count = 5000, PageNumber = 1 }
        };
        var totalZips = 0;
        while (true)
        {
            var zipResult = await client.RetrieveMultipleAsync(zipQuery);
            totalZips += zipResult.Entities.Count;
            if (!zipResult.MoreRecords) break;
            zipQuery.PageInfo.PageNumber++;
            zipQuery.PageInfo.PagingCookie = zipResult.PagingCookie;
        }
        summary.ZipCodeCount = totalZips;

        return summary;
    }

    private static string RedactSecrets(string arguments)
    {
        return Regex.Replace(
            arguments,
            @"(ClientSecret|Password|Secret|Key)=([^;""]+)",
            "$1=***REDACTED***",
            RegexOptions.IgnoreCase);
    }

    private class GeoSummary
    {
        public int StateCount { get; set; }
        public int CityCount { get; set; }
        public int ZipCodeCount { get; set; }
        public int TotalCount => StateCount + CityCount + ZipCodeCount;
    }
}
