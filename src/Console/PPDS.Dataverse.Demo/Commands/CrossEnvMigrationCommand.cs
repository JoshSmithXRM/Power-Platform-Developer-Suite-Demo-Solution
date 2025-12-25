using System.CommandLine;
using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using PPDS.Dataverse.Demo.Infrastructure;
using PPDS.Dataverse.Demo.Models;
using PPDS.Dataverse.Pooling;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Cross-environment migration workflow: Export from Dev, Import to QA.
///
/// This command demonstrates the complete cross-environment migration workflow:
///   1. Seed test data in source (optional)
///   2. Generate schema and export from source
///   3. Generate user mapping between environments
///   4. Import to target with user mapping
///   5. Verify record counts
///
/// Requires two environment connections in User Secrets:
///   Dataverse:Environments:Dev:* - Source environment
///   Dataverse:Environments:QA:*  - Target environment
///
/// Usage:
///   dotnet run -- migrate-to-qa
///   dotnet run -- migrate-to-qa --skip-seed
///   dotnet run -- migrate-to-qa --dry-run --verbose
/// </summary>
public static class CrossEnvMigrationCommand
{
    private static readonly string SchemaPath = Path.Combine(AppContext.BaseDirectory, "cross-env-schema.xml");
    private static readonly string DataPath = Path.Combine(AppContext.BaseDirectory, "cross-env-export.zip");
    private static readonly string UserMappingPath = Path.Combine(AppContext.BaseDirectory, "user-mapping.xml");

    public static Command Create()
    {
        var command = new Command("migrate-to-qa", "Export from Dev and import to QA environment");

        var skipSeedOption = new Option<bool>(
            "--skip-seed",
            "Skip seeding data in Dev (use existing data)");

        var dryRunOption = new Option<bool>(
            "--dry-run",
            "Export only, don't import to QA");

        var includeM2MOption = new Option<bool>(
            "--include-m2m",
            "Include M2M relationships (systemuserroles, teammembership)");

        // Use standardized options from GlobalOptionsExtensions
        var verboseOption = GlobalOptionsExtensions.CreateVerboseOption();
        var debugOption = GlobalOptionsExtensions.CreateDebugOption();

        command.AddOption(skipSeedOption);
        command.AddOption(dryRunOption);
        command.AddOption(includeM2MOption);
        command.AddOption(verboseOption);
        command.AddOption(debugOption);

        command.SetHandler(async (bool skipSeed, bool dryRun, bool includeM2M, bool verbose, bool debug) =>
        {
            var options = new GlobalOptions
            {
                Verbose = verbose,
                Debug = debug
            };
            Environment.ExitCode = await ExecuteAsync(skipSeed, dryRun, includeM2M, options);
        }, skipSeedOption, dryRunOption, includeM2MOption, verboseOption, debugOption);

        return command;
    }

    public static async Task<int> ExecuteAsync(
        bool skipSeed,
        bool dryRun,
        bool includeM2M,
        GlobalOptions options)
    {
        ConsoleWriter.Header("Cross-Environment Migration: Dev -> QA");

        // Create CLI client with logging if verbose
        var cli = options.EffectiveVerbose
            ? MigrationCli.CreateWithConsoleLogging()
            : new MigrationCli();

        // Verify CLI exists
        if (!cli.Exists)
        {
            ConsoleWriter.Error($"CLI not found: {cli.CliPath}");
            Console.WriteLine("Build the CLI first: dotnet build ../sdk/src/PPDS.Migration.Cli");
            return 1;
        }

        // Create GlobalOptions for each environment
        var devOptions = options with { Environment = "Dev" };
        var qaOptions = options with { Environment = "QA" };

        // Create pools for both environments
        using var devHost = CommandBase.CreateHost(devOptions);
        using var qaHost = CommandBase.CreateHost(qaOptions);

        var devPool = CommandBase.GetConnectionPool(devHost);
        var qaPool = CommandBase.GetConnectionPool(qaHost);

        if (devPool == null)
        {
            ConsoleWriter.Error("Dev environment not configured.");
            ConsoleWriter.ConnectionSetupInstructions("Dev");
            return 1;
        }

        if (qaPool == null)
        {
            ConsoleWriter.Error("QA environment not configured.");
            ConsoleWriter.ConnectionSetupInstructions("QA");
            return 1;
        }

        // Display environment info
        Console.WriteLine("  Environments:");
        Console.WriteLine("    Source: Dev");
        Console.WriteLine("    Target: QA");
        Console.WriteLine();

        if (dryRun)
        {
            ConsoleWriter.Warning("  [DRY RUN] Will export only, no import to QA");
            Console.WriteLine();
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // ===================================================================
            // PHASE 1: Seed test data in Dev (optional)
            // ===================================================================
            if (!skipSeed)
            {
                ConsoleWriter.Section("Phase 1: Seed Test Data in Dev");

                var seedResult = await SeedCommand.ExecuteAsync("Dev");
                if (seedResult != 0)
                {
                    ConsoleWriter.Error("Seed failed");
                    return 1;
                }
                Console.WriteLine();
            }

            // Verify source data
            Console.WriteLine("  Verifying source data in Dev...");
            await using var devClient = await devPool.GetClientAsync();
            var sourceData = await QueryTestData(devClient);
            PrintDataSummary("  Dev", sourceData);
            Console.WriteLine();

            // ===================================================================
            // PHASE 2: Generate schema and export from Dev
            // ===================================================================
            ConsoleWriter.Section("Phase 2: Export from Dev");

            // Generate schema
            Console.Write("  Generating schema... ");
            var entities = new[] { "account", "contact" };

            var schemaArgs = new CliArgs(devOptions)
                .Command("schema generate")
                .WithEntities(entities)
                .WithOutput(SchemaPath)
                .WithRelationships(includeM2M);

            var schemaResult = await cli.RunAsync(schemaArgs);
            if (schemaResult.Failed)
            {
                ConsoleWriter.Error("Schema generation failed");
                return 1;
            }
            ConsoleWriter.Success("Done");

            // Export data
            Console.Write("  Exporting data... ");
            var exportResult = await cli.ExportAsync(SchemaPath, DataPath, devOptions);
            if (exportResult.Failed)
            {
                ConsoleWriter.Error("Export failed");
                return 1;
            }
            ConsoleWriter.Success($"Done ({new FileInfo(DataPath).Length / 1024} KB)");

            // Inspect exported data
            Console.WriteLine("  Exported data summary:");
            InspectExportedData(DataPath);
            Console.WriteLine();

            if (dryRun)
            {
                ConsoleWriter.ResultBanner("DRY RUN COMPLETE - No import performed", success: true);
                Console.WriteLine();
                Console.WriteLine($"  Export file: {DataPath}");
                Console.WriteLine($"  Schema file: {SchemaPath}");
                Console.WriteLine();
                Console.WriteLine("  To import manually:");
                Console.WriteLine($"    ppds-migrate import --data \"{DataPath}\" --env QA --secrets-id ppds-dataverse-demo");
                return 0;
            }

            // ===================================================================
            // PHASE 3: Generate user mapping
            // ===================================================================
            ConsoleWriter.Section("Phase 3: Generate User Mapping");

            var mappingResult = await GenerateUserMappingCommand.ExecuteAsync(UserMappingPath, analyzeOnly: false);
            if (mappingResult != 0)
            {
                ConsoleWriter.Error("User mapping generation failed");
                return 1;
            }
            Console.WriteLine();

            // ===================================================================
            // PHASE 4: Import to QA
            // ===================================================================
            ConsoleWriter.Section("Phase 4: Import to QA");

            if (File.Exists(UserMappingPath))
            {
                Console.WriteLine($"  Using user mapping: {UserMappingPath}");
            }

            Console.Write("  Importing data to QA... ");

            var importOptions = new ImportCliOptions
            {
                Mode = "Upsert",
                UserMappingPath = File.Exists(UserMappingPath) ? UserMappingPath : null
            };

            var importResult = await cli.ImportAsync(DataPath, qaOptions, importOptions);
            if (importResult.Failed)
            {
                ConsoleWriter.Error("Import failed");
                return 1;
            }
            ConsoleWriter.Success("Done");
            Console.WriteLine();

            // ===================================================================
            // PHASE 5: Verify in QA
            // ===================================================================
            ConsoleWriter.Section("Phase 5: Verify in QA");

            await using var qaClient = await qaPool.GetClientAsync();
            var targetData = await QueryTestData(qaClient);
            PrintDataSummary("  QA", targetData);
            Console.WriteLine();

            // Compare
            Console.WriteLine("  Comparison (Dev -> QA):");
            var passed = true;

            // Account count
            var accountMatch = sourceData.Accounts.Count == targetData.Accounts.Count;
            Console.Write($"    Accounts: {sourceData.Accounts.Count} -> {targetData.Accounts.Count} ");
            ConsoleWriter.PassFail(accountMatch);
            passed &= accountMatch;

            // Contact count
            var contactMatch = sourceData.Contacts.Count == targetData.Contacts.Count;
            Console.Write($"    Contacts: {sourceData.Contacts.Count} -> {targetData.Contacts.Count} ");
            ConsoleWriter.PassFail(contactMatch);
            passed &= contactMatch;

            // Parent account relationships
            var sourceParents = sourceData.Accounts.Count(a => a.ParentAccountId.HasValue);
            var targetParents = targetData.Accounts.Count(a => a.ParentAccountId.HasValue);
            var parentMatch = sourceParents == targetParents;
            Console.Write($"    Parent refs: {sourceParents} -> {targetParents} ");
            ConsoleWriter.PassFail(parentMatch);
            passed &= parentMatch;

            // Contact company relationships
            var sourceCompany = sourceData.Contacts.Count(c => c.ParentCustomerId.HasValue);
            var targetCompany = targetData.Contacts.Count(c => c.ParentCustomerId.HasValue);
            var companyMatch = sourceCompany == targetCompany;
            Console.Write($"    Company refs: {sourceCompany} -> {targetCompany} ");
            ConsoleWriter.PassFail(companyMatch);
            passed &= companyMatch;

            Console.WriteLine();

            stopwatch.Stop();

            // ===================================================================
            // RESULT
            // ===================================================================
            if (passed)
            {
                ConsoleWriter.ResultBanner("MIGRATION COMPLETE: Dev -> QA SUCCESS", success: true);
                Console.WriteLine();
                Console.WriteLine($"  Time: {stopwatch.Elapsed.TotalSeconds:F2}s");
                return 0;
            }
            else
            {
                ConsoleWriter.ResultBanner("MIGRATION VERIFICATION FAILED", success: false);
                return 1;
            }
        }
        catch (Exception ex)
        {
            ConsoleWriter.Exception(ex, options.Debug);
            return 1;
        }
    }

    private static async Task<TestData> QueryTestData(IPooledClient client)
    {
        var result = new TestData();

        // Query accounts with parent reference
        var accountQuery = new QueryExpression("account")
        {
            ColumnSet = new ColumnSet("accountid", "name", "parentaccountid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.BeginsWith, "PPDS-")
                }
            }
        };
        var accounts = await client.RetrieveMultipleAsync(accountQuery);
        result.Accounts = accounts.Entities.Select(e => new AccountInfo
        {
            Id = e.Id,
            Name = e.GetAttributeValue<string>("name"),
            ParentAccountId = e.GetAttributeValue<EntityReference>("parentaccountid")?.Id
        }).ToList();

        // Query contacts with company reference
        var contactQuery = new QueryExpression("contact")
        {
            ColumnSet = new ColumnSet("contactid", "fullname", "parentcustomerid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("contactid", ConditionOperator.In,
                        SampleData.GetContacts().Select(c => c.Id).Cast<object>().ToArray())
                }
            }
        };
        var contacts = await client.RetrieveMultipleAsync(contactQuery);
        result.Contacts = contacts.Entities.Select(e => new ContactInfo
        {
            Id = e.Id,
            FullName = e.GetAttributeValue<string>("fullname"),
            ParentCustomerId = e.GetAttributeValue<EntityReference>("parentcustomerid")?.Id
        }).ToList();

        return result;
    }

    private static void PrintDataSummary(string prefix, TestData data)
    {
        Console.WriteLine($"{prefix}: {data.Accounts.Count} accounts, {data.Contacts.Count} contacts");
        var withParent = data.Accounts.Count(a => a.ParentAccountId.HasValue);
        var withCompany = data.Contacts.Count(c => c.ParentCustomerId.HasValue);
        Console.WriteLine($"{prefix}: {withParent} with parent, {withCompany} with company");
    }

    private static void InspectExportedData(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);

        var dataEntry = archive.GetEntry("data.xml");
        if (dataEntry == null)
        {
            Console.WriteLine("    No data.xml found");
            return;
        }

        using var stream = dataEntry.Open();
        var doc = XDocument.Load(stream);

        foreach (var entity in doc.Descendants("entity"))
        {
            var entityName = entity.Attribute("name")?.Value ?? "unknown";
            var records = entity.Descendants("record").Count();
            var m2m = entity.Element("m2mrelationships")?.Elements("m2mrelationship").Count() ?? 0;

            Console.Write($"    {entityName}: {records} records");
            if (m2m > 0)
            {
                Console.Write($", {m2m} M2M associations");
            }
            Console.WriteLine();
        }
    }

    private class TestData
    {
        public List<AccountInfo> Accounts { get; set; } = [];
        public List<ContactInfo> Contacts { get; set; } = [];
    }

    private class AccountInfo
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public Guid? ParentAccountId { get; set; }
    }

    private class ContactInfo
    {
        public Guid Id { get; set; }
        public string? FullName { get; set; }
        public Guid? ParentCustomerId { get; set; }
    }
}
