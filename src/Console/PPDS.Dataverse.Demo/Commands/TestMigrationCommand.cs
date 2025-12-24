using System.CommandLine;
using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using PPDS.Dataverse.BulkOperations;
using PPDS.Dataverse.Demo.Models;
using PPDS.Dataverse.Pooling;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// End-to-end test of ppds-migrate CLI.
/// Seeds data, exports, cleans, imports, and verifies relationships are restored.
/// </summary>
public static class TestMigrationCommand
{
    private static readonly string CliPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "..",
            "sdk", "src", "PPDS.Migration.Cli", "bin", "Debug", "net8.0", "ppds-migrate.exe"));

    private static readonly string SchemaPath = Path.Combine(AppContext.BaseDirectory, "test-schema.xml");
    private static readonly string DataPath = Path.Combine(AppContext.BaseDirectory, "test-export.zip");

    public static Command Create()
    {
        var command = new Command("test-migration", "End-to-end test of ppds-migrate export/import");

        var skipSeedOption = new Option<bool>("--skip-seed", "Skip seeding (use existing data)");
        var skipCleanOption = new Option<bool>("--skip-clean", "Skip cleaning after export");
        var envOption = new Option<string?>(
            aliases: ["--environment", "--env", "-e"],
            description: "Target environment name (e.g., 'Dev', 'QA'). Uses DefaultEnvironment from config if not specified.");

        command.AddOption(skipSeedOption);
        command.AddOption(skipCleanOption);
        command.AddOption(envOption);

        command.SetHandler(async (bool skipSeed, bool skipClean, string? environment) =>
        {
            Environment.ExitCode = await ExecuteAsync(skipSeed, skipClean, environment);
        }, skipSeedOption, skipCleanOption, envOption);

        return command;
    }

    public static async Task<int> ExecuteAsync(bool skipSeed, bool skipClean, string? environment = null)
    {
        Console.WriteLine("+==========================================================+");
        Console.WriteLine("|         ppds-migrate End-to-End Test                     |");
        Console.WriteLine("+==========================================================+");
        Console.WriteLine();

        // Verify CLI exists
        if (!File.Exists(CliPath))
        {
            CommandBase.WriteError($"CLI not found: {CliPath}");
            Console.WriteLine("Build the CLI first: dotnet build ../sdk/src/PPDS.Migration.Cli");
            return 1;
        }

        using var host = CommandBase.CreateHost(environment);
        var pool = CommandBase.GetConnectionPool(host);
        if (pool == null) return 1;

        var bulkExecutor = host.Services.GetRequiredService<IBulkOperationExecutor>();
        var envName = CommandBase.ResolveEnvironment(host, environment);

        Console.WriteLine($"  Environment: {envName}");
        Console.WriteLine();

        try
        {
            // ===================================================================
            // PHASE 1: Seed test data with relationships
            // ===================================================================
            if (!skipSeed)
            {
                Console.WriteLine("+---------------------------------------------------------+");
                Console.WriteLine("| Phase 1: Seed Test Data                                 |");
                Console.WriteLine("+---------------------------------------------------------+");

                var accounts = SampleData.GetAccounts();
                var accountParentUpdates = SampleData.GetAccountParentUpdates();
                var contacts = SampleData.GetContacts();

                // Create accounts
                Console.Write("  Creating accounts... ");
                var accountResult = await bulkExecutor.UpsertMultipleAsync("account", accounts,
                    new BulkOperationOptions { ContinueOnError = true });
                if (accountResult.CreatedCount.HasValue && accountResult.UpdatedCount.HasValue)
                {
                    CommandBase.WriteSuccess($"{accountResult.SuccessCount} upserted ({accountResult.CreatedCount} created, {accountResult.UpdatedCount} updated)");
                }
                else
                {
                    CommandBase.WriteSuccess($"{accountResult.SuccessCount} upserted");
                }

                // Set parent relationships
                Console.Write("  Setting parent relationships... ");
                var parentResult = await bulkExecutor.UpdateMultipleAsync("account", accountParentUpdates,
                    new BulkOperationOptions { ContinueOnError = true });
                CommandBase.WriteSuccess($"{parentResult.SuccessCount} updated");

                // Create contacts
                Console.Write("  Creating contacts... ");
                var contactResult = await bulkExecutor.UpsertMultipleAsync("contact", contacts,
                    new BulkOperationOptions { ContinueOnError = true });
                if (contactResult.CreatedCount.HasValue && contactResult.UpdatedCount.HasValue)
                {
                    CommandBase.WriteSuccess($"{contactResult.SuccessCount} upserted ({contactResult.CreatedCount} created, {contactResult.UpdatedCount} updated)");
                }
                else
                {
                    CommandBase.WriteSuccess($"{contactResult.SuccessCount} upserted");
                }

                Console.WriteLine();
            }

            // Verify source data before export
            Console.WriteLine("  Verifying source data...");
            var sourceData = await QueryTestData(pool);
            PrintDataSummary("  Source", sourceData);
            Console.WriteLine();

            // ===================================================================
            // PHASE 2: Generate schema and export
            // ===================================================================
            Console.WriteLine("+---------------------------------------------------------+");
            Console.WriteLine("| Phase 2: Generate Schema & Export                       |");
            Console.WriteLine("+---------------------------------------------------------+");

            // Generate schema
            Console.Write("  Generating schema... ");
            var schemaResult = await RunCliAsync(
                $"schema generate -e account,contact -o \"{SchemaPath}\" --env {envName} --secrets-id ppds-dataverse-demo");
            if (schemaResult != 0)
            {
                CommandBase.WriteError("Schema generation failed");
                return 1;
            }
            CommandBase.WriteSuccess("Done");

            // Export data
            Console.Write("  Exporting data... ");
            var exportResult = await RunCliAsync(
                $"export --schema \"{SchemaPath}\" --output \"{DataPath}\" --env {envName} --secrets-id ppds-dataverse-demo");
            if (exportResult != 0)
            {
                CommandBase.WriteError("Export failed");
                return 1;
            }
            CommandBase.WriteSuccess($"Done ({new FileInfo(DataPath).Length / 1024} KB)");

            // Inspect exported data
            Console.WriteLine("  Inspecting exported data...");
            InspectExportedData(DataPath);
            Console.WriteLine();

            // ===================================================================
            // PHASE 3: Clean data
            // ===================================================================
            if (!skipClean)
            {
                Console.WriteLine("+---------------------------------------------------------+");
                Console.WriteLine("| Phase 3: Clean Test Data                                |");
                Console.WriteLine("+---------------------------------------------------------+");

                // Delete contacts first (foreign key constraint)
                var contactIds = SampleData.GetContacts().Select(c => c.Id).ToList();
                Console.Write($"  Deleting {contactIds.Count} contacts... ");
                var deleteContactResult = await bulkExecutor.DeleteMultipleAsync("contact", contactIds,
                    new BulkOperationOptions { ContinueOnError = true });
                CommandBase.WriteSuccess($"{deleteContactResult.SuccessCount} deleted");

                // Delete accounts
                var accountIds = SampleData.GetAccounts().Select(a => a.Id).ToList();
                Console.Write($"  Deleting {accountIds.Count} accounts... ");
                var deleteAccountResult = await bulkExecutor.DeleteMultipleAsync("account", accountIds,
                    new BulkOperationOptions { ContinueOnError = true });
                CommandBase.WriteSuccess($"{deleteAccountResult.SuccessCount} deleted");

                // Verify clean
                var cleanData = await QueryTestData(pool);
                Console.WriteLine($"  Verified: {cleanData.Accounts.Count} accounts, {cleanData.Contacts.Count} contacts remaining");
                Console.WriteLine();
            }

            // ===================================================================
            // PHASE 4: Import data
            // ===================================================================
            Console.WriteLine("+---------------------------------------------------------+");
            Console.WriteLine("| Phase 4: Import Data                                    |");
            Console.WriteLine("+---------------------------------------------------------+");

            Console.Write("  Importing data... ");
            var importResult = await RunCliAsync(
                $"import --data \"{DataPath}\" --mode Upsert --env {envName} --secrets-id ppds-dataverse-demo");
            if (importResult != 0)
            {
                CommandBase.WriteError("Import failed");
                return 1;
            }
            CommandBase.WriteSuccess("Done");
            Console.WriteLine();

            // ===================================================================
            // PHASE 5: Verify imported data
            // ===================================================================
            Console.WriteLine("+---------------------------------------------------------+");
            Console.WriteLine("| Phase 5: Verify Import                                  |");
            Console.WriteLine("+---------------------------------------------------------+");

            var importedData = await QueryTestData(pool);
            PrintDataSummary("  Imported", importedData);
            Console.WriteLine();

            // Compare
            var passed = true;
            Console.WriteLine("  Comparison:");

            // Account count
            var accountMatch = sourceData.Accounts.Count == importedData.Accounts.Count;
            Console.WriteLine($"    Accounts: {sourceData.Accounts.Count} → {importedData.Accounts.Count} " +
                (accountMatch ? "✓" : "✗"));
            passed &= accountMatch;

            // Contact count
            var contactMatch = sourceData.Contacts.Count == importedData.Contacts.Count;
            Console.WriteLine($"    Contacts: {sourceData.Contacts.Count} → {importedData.Contacts.Count} " +
                (contactMatch ? "✓" : "✗"));
            passed &= contactMatch;

            // Parent account relationships
            var sourceParents = sourceData.Accounts.Count(a => a.ParentAccountId.HasValue);
            var importedParents = importedData.Accounts.Count(a => a.ParentAccountId.HasValue);
            var parentMatch = sourceParents == importedParents;
            Console.WriteLine($"    Parent Account refs: {sourceParents} → {importedParents} " +
                (parentMatch ? "✓" : "✗"));
            passed &= parentMatch;

            // Contact company relationships
            var sourceCompany = sourceData.Contacts.Count(c => c.ParentCustomerId.HasValue);
            var importedCompany = importedData.Contacts.Count(c => c.ParentCustomerId.HasValue);
            var companyMatch = sourceCompany == importedCompany;
            Console.WriteLine($"    Contact→Account refs: {sourceCompany} → {importedCompany} " +
                (companyMatch ? "✓" : "✗"));
            passed &= companyMatch;

            Console.WriteLine();

            // ===================================================================
            // RESULT
            // ===================================================================
            if (passed)
            {
                Console.WriteLine("+==========================================================+");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("|                    TEST PASSED ✓                         |");
                Console.ResetColor();
                Console.WriteLine("+==========================================================+");
                return 0;
            }
            else
            {
                Console.WriteLine("+==========================================================+");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("|                    TEST FAILED ✗                         |");
                Console.ResetColor();
                Console.WriteLine("+==========================================================+");
                return 1;
            }
        }
        catch (Exception ex)
        {
            CommandBase.WriteError($"Error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static async Task<int> RunCliAsync(string arguments)
    {
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

        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static async Task<TestData> QueryTestData(IDataverseConnectionPool pool)
    {
        var result = new TestData();

        await using var client = await pool.GetClientAsync();

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
        Console.WriteLine($"{prefix}: {withParent} accounts with parent, {withCompany} contacts with company");
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

    private static void InspectExportedData(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);

        // Check schema format
        var schemaEntry = archive.GetEntry("data_schema.xml");
        if (schemaEntry != null)
        {
            using var schemaStream = schemaEntry.Open();
            var schemaDoc = XDocument.Load(schemaStream);
            var root = schemaDoc.Root;

            Console.WriteLine("    Schema format:");
            var dateMode = root?.Attribute("dateMode")?.Value;
            Console.WriteLine($"      dateMode: {dateMode ?? "(missing)"}");

            var importOrder = root?.Element("entityImportOrder");
            if (importOrder != null)
            {
                var entities = importOrder.Elements("entityName").Select(e => e.Value).ToList();
                Console.WriteLine($"      entityImportOrder: {string.Join(", ", entities)}");
            }
            else
            {
                Console.WriteLine("      entityImportOrder: (missing)");
            }
        }

        // Check data format
        var dataEntry = archive.GetEntry("data.xml");
        if (dataEntry == null)
        {
            Console.WriteLine("    No data.xml found in archive");
            return;
        }

        using var stream = dataEntry.Open();
        var doc = XDocument.Load(stream);

        Console.WriteLine("    Data format:");

        // Check if field values are element content (CMT) or attributes
        var firstField = doc.Descendants("field").FirstOrDefault();
        if (firstField != null)
        {
            var hasValueAttr = firstField.Attribute("value") != null;
            var hasContent = !string.IsNullOrEmpty(firstField.Value);
            Console.WriteLine($"      Field format: {(hasContent ? "element content (CMT)" : hasValueAttr ? "attribute" : "unknown")}");

            // Show sample field
            var name = firstField.Attribute("name")?.Value;
            var value = hasContent ? firstField.Value : firstField.Attribute("value")?.Value;
            Console.WriteLine($"      Sample: <field name=\"{name}\">{(hasContent ? value : $" value=\"{value}\"")}");
        }

        // Check for lookup fields
        var lookupFields = new[] { "parentaccountid", "parentcustomerid", "primarycontactid" };

        foreach (var entity in doc.Descendants("entity"))
        {
            var entityName = entity.Attribute("name")?.Value ?? "unknown";
            var records = entity.Descendants("record").ToList();

            Console.WriteLine($"    {entityName}: {records.Count} records");

            foreach (var lookupField in lookupFields)
            {
                var withValue = records.Count(r =>
                {
                    var field = r.Elements("field")
                        .FirstOrDefault(f => f.Attribute("name")?.Value == lookupField);
                    if (field == null) return false;
                    // Check both element content and attribute for value
                    var value = !string.IsNullOrEmpty(field.Value) ? field.Value : field.Attribute("value")?.Value;
                    return !string.IsNullOrEmpty(value) && value != Guid.Empty.ToString();
                });

                if (withValue > 0)
                {
                    Console.WriteLine($"      {lookupField}: {withValue} with values");
                }
            }
        }
    }
}
