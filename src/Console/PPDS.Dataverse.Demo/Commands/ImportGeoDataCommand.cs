using System.CommandLine;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PPDS.Dataverse.Pooling;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Imports geographic reference data from a portable ZIP package.
///
/// This command demonstrates the ppds-migrate CLI import workflow:
///   ppds-migrate import --data geo-export.zip --mode Upsert --env QA
///
/// The package should have been created by:
///   - export-geo-data command
///   - ppds-migrate export command
///
/// Supports:
///   - Upsert mode (default) - idempotent via alternate keys
///   - Clean-first option - removes existing data before import
///
/// Usage:
///   dotnet run -- import-geo-data --data geo-v1.0.zip --env QA
///   dotnet run -- import-geo-data --data artifacts/geo-data.zip --env Prod --clean-first
/// </summary>
public static class ImportGeoDataCommand
{
    private static readonly string CliPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "..",
            "sdk", "src", "PPDS.Migration.Cli", "bin", "Debug", "net8.0", "ppds-migrate.exe"));

    public static Command Create()
    {
        var command = new Command("import-geo-data", "Import geographic data from a ZIP package");

        var dataOption = new Option<string>(
            aliases: ["--data", "-d"],
            description: "Input ZIP file path (required)")
        {
            IsRequired = true
        };

        var envOption = new Option<string>(
            aliases: ["--environment", "--env", "-e"],
            description: "Target environment name (required)")
        {
            IsRequired = true
        };

        var cleanFirstOption = new Option<bool>(
            "--clean-first",
            "Run clean-geo-data before import");

        var verboseOption = new Option<bool>(
            ["--verbose", "-v"],
            "Show detailed output including CLI commands");

        command.AddOption(dataOption);
        command.AddOption(envOption);
        command.AddOption(cleanFirstOption);
        command.AddOption(verboseOption);

        command.SetHandler(async (string data, string environment, bool cleanFirst, bool verbose) =>
        {
            Environment.ExitCode = await ExecuteAsync(data, environment, cleanFirst, verbose);
        }, dataOption, envOption, cleanFirstOption, verboseOption);

        return command;
    }

    public static async Task<int> ExecuteAsync(
        string dataPath,
        string environment,
        bool cleanFirst = false,
        bool verbose = false)
    {
        Console.WriteLine("+==============================================================+");
        Console.WriteLine("|       Import Geographic Data                                 |");
        Console.WriteLine("+==============================================================+");
        Console.WriteLine();

        // Verify CLI exists
        if (!File.Exists(CliPath))
        {
            CommandBase.WriteError($"CLI not found: {CliPath}");
            Console.WriteLine("Build the CLI first: dotnet build ../sdk/src/PPDS.Migration.Cli");
            return 1;
        }

        // Verify data package exists
        if (!File.Exists(dataPath))
        {
            CommandBase.WriteError($"Data package not found: {dataPath}");
            Console.WriteLine("Create a package first: dotnet run -- export-geo-data --output geo-export.zip");
            return 1;
        }

        // Create connection pool to verify target
        using var host = CommandBase.CreateHost(environment);
        var pool = CommandBase.GetConnectionPool(host);

        if (pool == null)
        {
            CommandBase.WriteError($"{environment} environment not configured.");
            Console.WriteLine();
            Console.WriteLine($"Configure {environment} environment in User Secrets:");
            Console.WriteLine($"  dotnet user-secrets set \"Dataverse:Environments:{environment}:Url\" \"https://{environment.ToLower()}.crm.dynamics.com\"");
            Console.WriteLine($"  dotnet user-secrets set \"Dataverse:Environments:{environment}:Connections:0:ClientId\" \"...\"");
            Console.WriteLine($"  dotnet user-secrets set \"Dataverse:Environments:{environment}:Connections:0:ClientSecret\" \"...\"");
            return 1;
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            Console.WriteLine($"  Environment: {environment}");
            Console.WriteLine($"  Package: {Path.GetFullPath(dataPath)}");
            Console.WriteLine($"  Size: {new FileInfo(dataPath).Length / 1024} KB");
            Console.WriteLine();

            // ===================================================================
            // STEP 1: Inspect Package
            // ===================================================================
            Console.WriteLine("+-----------------------------------------------------------------+");
            Console.WriteLine("| Step 1: Inspect Package                                         |");
            Console.WriteLine("+-----------------------------------------------------------------+");

            var packageSummary = InspectPackage(dataPath);
            Console.WriteLine($"  States: {packageSummary.StateCount}");
            Console.WriteLine($"  Cities: {packageSummary.CityCount}");
            Console.WriteLine($"  ZIP Codes: {packageSummary.ZipCodeCount:N0}");
            Console.WriteLine($"  Total: {packageSummary.TotalCount:N0} records");
            Console.WriteLine();

            // ===================================================================
            // STEP 2: Check Target (before)
            // ===================================================================
            Console.WriteLine("+-----------------------------------------------------------------+");
            Console.WriteLine("| Step 2: Check Target (before import)                            |");
            Console.WriteLine("+-----------------------------------------------------------------+");

            await using var client = await pool.GetClientAsync();
            var beforeSummary = await QueryGeoSummary(client);

            Console.WriteLine($"  States: {beforeSummary.StateCount}");
            Console.WriteLine($"  Cities: {beforeSummary.CityCount}");
            Console.WriteLine($"  ZIP Codes: {beforeSummary.ZipCodeCount:N0}");
            Console.WriteLine($"  Total: {beforeSummary.TotalCount:N0} records");
            Console.WriteLine();

            // ===================================================================
            // STEP 3: Clean Target (optional)
            // ===================================================================
            if (cleanFirst)
            {
                Console.WriteLine("+-----------------------------------------------------------------+");
                Console.WriteLine("| Step 3: Clean Target                                            |");
                Console.WriteLine("+-----------------------------------------------------------------+");

                if (beforeSummary.TotalCount > 0)
                {
                    Console.WriteLine($"  Removing {beforeSummary.TotalCount:N0} existing records...");
                    var cleanResult = await CleanGeoDataCommand.ExecuteAsync(
                        zipOnly: false,
                        confirm: true,
                        parallelism: null,
                        ratePreset: null, // Uses Conservative default for deletes
                        verbose: verbose,
                        environment: environment);

                    if (cleanResult != 0)
                    {
                        CommandBase.WriteError("Clean failed");
                        return 1;
                    }
                }
                else
                {
                    Console.WriteLine("  Target is empty, nothing to clean.");
                }
                Console.WriteLine();
            }

            // ===================================================================
            // STEP 4: Import Data
            // ===================================================================
            var importStep = cleanFirst ? "4" : "3";
            Console.WriteLine("+-----------------------------------------------------------------+");
            Console.WriteLine($"| Step {importStep}: Import Data (ppds-migrate import)");
            Console.WriteLine("+-----------------------------------------------------------------+");

            Console.Write("  Importing data package... ");
            var importResult = await RunCliAsync(
                $"import --data \"{dataPath}\" --mode Upsert --env {environment}", verbose);
            if (importResult != 0)
            {
                CommandBase.WriteError("Import failed");
                return 1;
            }
            CommandBase.WriteSuccess("Done");
            Console.WriteLine();

            // ===================================================================
            // STEP 5: Verify Target (after)
            // ===================================================================
            var verifyStep = cleanFirst ? "5" : "4";
            Console.WriteLine("+-----------------------------------------------------------------+");
            Console.WriteLine($"| Step {verifyStep}: Verify Target (after import)");
            Console.WriteLine("+-----------------------------------------------------------------+");

            // Need a fresh client for accurate counts
            await using var verifyClient = await pool.GetClientAsync();
            var afterSummary = await QueryGeoSummary(verifyClient);

            Console.WriteLine($"  States: {afterSummary.StateCount}");
            Console.WriteLine($"  Cities: {afterSummary.CityCount}");
            Console.WriteLine($"  ZIP Codes: {afterSummary.ZipCodeCount:N0}");
            Console.WriteLine($"  Total: {afterSummary.TotalCount:N0} records");
            Console.WriteLine();

            // Compare package vs target
            Console.WriteLine("  Verification (package vs target):");
            var passed = true;

            var stateMatch = packageSummary.StateCount == afterSummary.StateCount;
            Console.Write($"    States: {packageSummary.StateCount} vs {afterSummary.StateCount} ");
            WritePassFail(stateMatch);
            passed &= stateMatch;

            var cityMatch = packageSummary.CityCount == afterSummary.CityCount;
            Console.Write($"    Cities: {packageSummary.CityCount} vs {afterSummary.CityCount} ");
            WritePassFail(cityMatch);
            passed &= cityMatch;

            var zipMatch = packageSummary.ZipCodeCount == afterSummary.ZipCodeCount;
            Console.Write($"    ZIP Codes: {packageSummary.ZipCodeCount:N0} vs {afterSummary.ZipCodeCount:N0} ");
            WritePassFail(zipMatch);
            passed &= zipMatch;

            Console.WriteLine();

            stopwatch.Stop();

            // ===================================================================
            // RESULT
            // ===================================================================
            if (passed)
            {
                Console.WriteLine("+==============================================================+");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("|              Import Complete                                  |");
                Console.ResetColor();
                Console.WriteLine("+==============================================================+");
                Console.WriteLine();
                Console.WriteLine($"  Environment: {environment}");
                Console.WriteLine($"  Records: {afterSummary.TotalCount:N0}");
                Console.WriteLine($"  Time: {stopwatch.Elapsed.TotalSeconds:F2}s");
                return 0;
            }
            else
            {
                Console.WriteLine("+==============================================================+");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("|         Import Complete (with verification warnings)         |");
                Console.ResetColor();
                Console.WriteLine("+==============================================================+");
                Console.WriteLine();
                Console.WriteLine("  Some counts don't match. This may be expected if:");
                Console.WriteLine("    - Target had existing data (use --clean-first)");
                Console.WriteLine("    - Some records failed validation");
                return 1;
            }
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

    private static PackageSummary InspectPackage(string zipPath)
    {
        var summary = new PackageSummary();

        using var archive = ZipFile.OpenRead(zipPath);
        var dataEntry = archive.GetEntry("data.xml");
        if (dataEntry == null)
        {
            return summary;
        }

        using var stream = dataEntry.Open();
        var doc = XDocument.Load(stream);

        foreach (var entity in doc.Descendants("entity"))
        {
            var entityName = entity.Attribute("name")?.Value ?? "";
            var records = entity.Descendants("record").Count();

            switch (entityName)
            {
                case "ppds_state":
                    summary.StateCount = records;
                    break;
                case "ppds_city":
                    summary.CityCount = records;
                    break;
                case "ppds_zipcode":
                    summary.ZipCodeCount = records;
                    break;
            }
        }

        return summary;
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

    private static void WritePassFail(bool passed)
    {
        if (passed)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[PASS]");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[FAIL]");
        }
        Console.ResetColor();
    }

    private static string RedactSecrets(string arguments)
    {
        return Regex.Replace(
            arguments,
            @"(ClientSecret|Password|Secret|Key)=([^;""]+)",
            "$1=***REDACTED***",
            RegexOptions.IgnoreCase);
    }

    private class PackageSummary
    {
        public int StateCount { get; set; }
        public int CityCount { get; set; }
        public int ZipCodeCount { get; set; }
        public int TotalCount => StateCount + CityCount + ZipCodeCount;
    }

    private class GeoSummary
    {
        public int StateCount { get; set; }
        public int CityCount { get; set; }
        public int ZipCodeCount { get; set; }
        public int TotalCount => StateCount + CityCount + ZipCodeCount;
    }
}
