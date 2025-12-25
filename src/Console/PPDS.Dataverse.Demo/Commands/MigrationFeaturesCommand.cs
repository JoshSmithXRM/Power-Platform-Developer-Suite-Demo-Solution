using System.CommandLine;
using System.IO.Compression;
using System.Xml.Linq;
using PPDS.Dataverse.Demo.Infrastructure;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Demonstrates new ppds-migrate CLI features:
/// - M2M relationship support (systemuserroles)
/// - Attribute filtering (--include-attributes, --exclude-attributes, --exclude-patterns)
/// - User mapping (--user-mapping)
/// - Plugin disable/enable (disableplugins schema attribute)
///
/// Usage:
///   dotnet run -- demo-features
///   dotnet run -- demo-features --feature m2m --verbose
///   dotnet run -- demo-features --feature filtering --env Dev
/// </summary>
public static class MigrationFeaturesCommand
{
    private static readonly string SchemaPath = Path.Combine(
        AppContext.BaseDirectory, "migration", "schema-features.xml");

    private static readonly string UserMappingPath = Path.Combine(
        AppContext.BaseDirectory, "migration", "user-mapping.xml");

    private static readonly string OutputPath = Path.Combine(
        AppContext.BaseDirectory, "features-export.zip");

    public static Command Create()
    {
        var command = new Command("demo-features", "Demonstrate new migration CLI features (M2M, filtering, user mapping, plugin disable)");

        var featureOption = new Option<string>(
            "--feature",
            description: "Specific feature to demo: m2m, filtering, user-mapping, plugin-disable, or all",
            getDefaultValue: () => "all");

        // Use standardized options from GlobalOptionsExtensions
        var envOption = GlobalOptionsExtensions.CreateEnvironmentOption();
        var verboseOption = GlobalOptionsExtensions.CreateVerboseOption();
        var debugOption = GlobalOptionsExtensions.CreateDebugOption();

        command.AddOption(featureOption);
        command.AddOption(envOption);
        command.AddOption(verboseOption);
        command.AddOption(debugOption);

        command.SetHandler(async (string feature, string? environment, bool verbose, bool debug) =>
        {
            var options = new GlobalOptions
            {
                Environment = environment,
                Verbose = verbose,
                Debug = debug
            };
            Environment.ExitCode = await ExecuteAsync(feature, options);
        }, featureOption, envOption, verboseOption, debugOption);

        return command;
    }

    public static async Task<int> ExecuteAsync(string feature, GlobalOptions options)
    {
        ConsoleWriter.Header("ppds-migrate Feature Demonstration");

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

        using var host = CommandBase.CreateHost(options);
        var pool = CommandBase.GetConnectionPool(host);
        if (pool == null) return 1;

        var envName = CommandBase.ResolveEnvironment(host, options);
        options = options with { Environment = envName };

        Console.WriteLine($"  Environment: {envName}");
        Console.WriteLine();

        try
        {
            var features = feature.ToLowerInvariant();

            if (features == "all" || features == "m2m")
            {
                await DemoM2MRelationships(cli, options);
            }

            if (features == "all" || features == "filtering")
            {
                DemoAttributeFiltering();
            }

            if (features == "all" || features == "user-mapping")
            {
                DemoUserMapping();
            }

            if (features == "all" || features == "plugin-disable")
            {
                DemoPluginDisable();
            }

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

    private static async Task DemoM2MRelationships(IMigrationCli cli, GlobalOptions options)
    {
        ConsoleWriter.Section("Feature 1: Many-to-Many (M2M) Relationship Support");
        Console.WriteLine();
        Console.WriteLine("  M2M relationships link entities without foreign keys.");
        Console.WriteLine("  Example: Users <-> Roles via systemuserroles intersect table.");
        Console.WriteLine();

        // Show schema M2M configuration
        Console.WriteLine("  Schema Configuration:");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  -------------------------------------------------------------");
        Console.WriteLine("  <relationships>");
        Console.WriteLine("    <relationship");
        Console.WriteLine("      name=\"systemuserroles\"");
        Console.WriteLine("      manyToMany=\"true\"");
        Console.WriteLine("      m2mTargetEntity=\"role\"");
        Console.WriteLine("      m2mTargetEntityPrimaryKey=\"roleid\" />");
        Console.WriteLine("  </relationships>");
        Console.ResetColor();
        Console.WriteLine();

        // Export with M2M
        Console.Write("  Exporting with M2M relationships... ");

        var exportResult = await cli.ExportAsync(SchemaPath, OutputPath, options);

        if (exportResult.Success)
        {
            ConsoleWriter.Success("Done");

            // Inspect M2M data
            if (File.Exists(OutputPath))
            {
                InspectM2MData(OutputPath);
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Skipped (export requires connection)");
            Console.ResetColor();
        }

        // Show CMT-compatible data format
        Console.WriteLine();
        Console.WriteLine("  Data Format (CMT-compatible):");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  -------------------------------------------------------------");
        Console.WriteLine("  <m2mrelationships>");
        Console.WriteLine("    <m2mrelationship");
        Console.WriteLine("      sourceid=\"user-guid\"");
        Console.WriteLine("      targetentityname=\"role\"");
        Console.WriteLine("      targetentitynameidfield=\"roleid\"");
        Console.WriteLine("      m2mrelationshipname=\"systemuserroles\">");
        Console.WriteLine("      <targetids>");
        Console.WriteLine("        <targetid>role-guid-1</targetid>");
        Console.WriteLine("        <targetid>role-guid-2</targetid>");
        Console.WriteLine("      </targetids>");
        Console.WriteLine("    </m2mrelationship>");
        Console.WriteLine("  </m2mrelationships>");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void DemoAttributeFiltering()
    {
        ConsoleWriter.Section("Feature 2: Attribute Filtering");
        Console.WriteLine();
        Console.WriteLine("  Control which attributes are included in the schema.");
        Console.WriteLine("  Filtering happens during SCHEMA GENERATION, not export.");
        Console.WriteLine();

        Console.WriteLine("  Command: ppds-migrate schema generate");
        Console.WriteLine();

        // --include-attributes
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  --include-attributes name,accountid,parentaccountid");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("    Include ONLY these specific attributes in schema");
        Console.ResetColor();
        Console.WriteLine();

        // --exclude-attributes
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  --exclude-attributes createdon,modifiedon,createdby,modifiedby");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("    Exclude these audit fields from schema");
        Console.ResetColor();
        Console.WriteLine();

        // --exclude-patterns
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  --exclude-patterns *versionnumber,*utcconversion*,override*");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("    Exclude attributes matching wildcard patterns");
        Console.ResetColor();
        Console.WriteLine();

        // Example workflow
        Console.WriteLine("  Workflow:");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  -------------------------------------------------------------");
        Console.WriteLine("  # 1. Generate schema with filtering");
        Console.WriteLine("  ppds-migrate schema generate -e account,contact \\");
        Console.WriteLine("    --exclude-attributes createdon,modifiedon,createdby,modifiedby \\");
        Console.WriteLine("    --exclude-patterns *versionnumber -o filtered-schema.xml");
        Console.WriteLine();
        Console.WriteLine("  # 2. Export uses the filtered schema");
        Console.WriteLine("  ppds-migrate export --schema filtered-schema.xml --output data.zip");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void DemoUserMapping()
    {
        ConsoleWriter.Section("Feature 3: User Mapping");
        Console.WriteLine();
        Console.WriteLine("  Map user GUIDs between source and target environments.");
        Console.WriteLine("  Critical for cross-tenant migrations where user IDs differ.");
        Console.WriteLine();

        Console.WriteLine("  Usage:");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("    ppds-migrate import --data data.zip --user-mapping user-mapping.xml");
        Console.ResetColor();
        Console.WriteLine();

        // Show user mapping file format
        Console.WriteLine("  User Mapping File Format (XML):");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  -------------------------------------------------------------");
        Console.WriteLine("  <usermappings>");
        Console.WriteLine("    <usermapping");
        Console.WriteLine("      sourceUserId=\"source-env-user-guid\"");
        Console.WriteLine("      targetUserId=\"target-env-user-guid\"");
        Console.WriteLine("      comment=\"Admin User\" />");
        Console.WriteLine("  </usermappings>");
        Console.ResetColor();
        Console.WriteLine();

        // Show sample file location
        Console.WriteLine($"  Sample file: migration/user-mapping.xml");

        if (File.Exists(UserMappingPath))
        {
            var xml = XDocument.Load(UserMappingPath);
            var mappings = xml.Descendants("usermapping").Count();
            Console.WriteLine($"  Contains: {mappings} example mappings");
        }

        Console.WriteLine();
        Console.WriteLine("  When to use:");
        Console.WriteLine("    - Migrating between tenants (different Azure AD)");
        Console.WriteLine("    - Restoring to environment with recreated users");
        Console.WriteLine("    - Remapping owner/createdby/modifiedby fields");
        Console.WriteLine();
    }

    private static void DemoPluginDisable()
    {
        ConsoleWriter.Section("Feature 4: Plugin Disable/Enable");
        Console.WriteLine();
        Console.WriteLine("  Control plugin execution during import via schema attribute.");
        Console.WriteLine();

        Console.WriteLine("  Schema Configuration:");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  -------------------------------------------------------------");
        Console.WriteLine("  <!-- Disable plugins for faster bulk import -->");
        Console.WriteLine("  <entity name=\"account\" disableplugins=\"true\">");
        Console.WriteLine();
        Console.WriteLine("  <!-- Keep plugins enabled for validation -->");
        Console.WriteLine("  <entity name=\"contact\" disableplugins=\"false\">");
        Console.ResetColor();
        Console.WriteLine();

        Console.WriteLine("  Benefits of disableplugins=\"true\":");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("    + Faster bulk imports (no plugin overhead)");
        Console.WriteLine("    + Bypass validation plugins for historical data");
        Console.WriteLine("    + Avoid integration plugin side effects");
        Console.ResetColor();
        Console.WriteLine();

        Console.WriteLine("  Risks:");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("    ! Business logic not enforced during import");
        Console.WriteLine("    ! Integrations not triggered (may need manual sync)");
        Console.WriteLine("    ! Audit/compliance plugins skipped");
        Console.ResetColor();
        Console.WriteLine();

        Console.WriteLine("  Best Practice:");
        Console.WriteLine("    1. Disable plugins for initial bulk load");
        Console.WriteLine("    2. Re-enable after migration");
        Console.WriteLine("    3. Run validation/sync jobs post-migration");
        Console.WriteLine();

        // Show which entities have plugins disabled in schema
        if (File.Exists(SchemaPath))
        {
            var xml = XDocument.Load(SchemaPath);
            Console.WriteLine("  Schema entities:");
            foreach (var entity in xml.Descendants("entity"))
            {
                var name = entity.Attribute("name")?.Value ?? "unknown";
                var disabled = entity.Attribute("disableplugins")?.Value ?? "false";
                var status = disabled == "true" ? "DISABLED" : "enabled";
                var color = disabled == "true" ? ConsoleColor.Yellow : ConsoleColor.Green;

                Console.Write($"    {name}: ");
                Console.ForegroundColor = color;
                Console.WriteLine(status);
                Console.ResetColor();
            }
        }

        Console.WriteLine();
    }

    private static void InspectM2MData(string zipPath)
    {
        try
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

            Console.WriteLine();
            Console.WriteLine("  M2M Data in Export:");

            foreach (var entity in doc.Descendants("entity"))
            {
                var name = entity.Attribute("name")?.Value ?? "unknown";
                var m2mRels = entity.Element("m2mrelationships");

                if (m2mRels != null)
                {
                    var relCount = m2mRels.Elements("m2mrelationship").Count();
                    if (relCount > 0)
                    {
                        Console.WriteLine($"    {name}: {relCount} M2M associations");

                        // Show first few
                        foreach (var rel in m2mRels.Elements("m2mrelationship").Take(3))
                        {
                            var sourceId = rel.Attribute("sourceid")?.Value?[..8] ?? "?";
                            var targetIds = rel.Element("targetids")?.Elements("targetid").Count() ?? 0;
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.WriteLine($"      {sourceId}... -> {targetIds} targets");
                            Console.ResetColor();
                        }

                        if (relCount > 3)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.WriteLine($"      ... and {relCount - 3} more");
                            Console.ResetColor();
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"    Could not inspect M2M data: {ex.Message}");
            Console.ResetColor();
        }
    }
}
