using System.CommandLine;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk.Query;
using PPDS.Dataverse.Demo.Infrastructure;
using PPDS.Dataverse.Pooling;

namespace PPDS.Dataverse.Demo.Commands;

/// <summary>
/// Generates a user mapping file for cross-environment migration.
/// Matches users by Azure AD Object ID since systemuserid differs across environments.
/// </summary>
public static class GenerateUserMappingCommand
{
    private static readonly string OutputPath = Path.Combine(AppContext.BaseDirectory, "user-mapping.xml");

    public static Command Create()
    {
        var command = new Command("generate-user-mapping", "Generate user mapping file for cross-environment migration");

        var outputOption = new Option<string>(
            "--output",
            () => OutputPath,
            "Output path for the user mapping XML file");

        var analyzeOnlyOption = new Option<bool>(
            "--analyze",
            "Analyze user differences without generating mapping file");

        // Use standardized options from GlobalOptionsExtensions
        var verboseOption = GlobalOptionsExtensions.CreateVerboseOption();
        var debugOption = GlobalOptionsExtensions.CreateDebugOption();

        command.AddOption(outputOption);
        command.AddOption(analyzeOnlyOption);
        command.AddOption(verboseOption);
        command.AddOption(debugOption);

        command.SetHandler(async (string output, bool analyzeOnly, bool verbose, bool debug) =>
        {
            var options = new GlobalOptions
            {
                Verbose = verbose,
                Debug = debug
            };
            Environment.ExitCode = await ExecuteAsync(output, analyzeOnly, options);
        }, outputOption, analyzeOnlyOption, verboseOption, debugOption);

        return command;
    }

    public static async Task<int> ExecuteAsync(string outputPath, bool analyzeOnly, GlobalOptions options)
    {
        ConsoleWriter.Header("Generate User Mapping: Dev → QA");

        // Create pools for both environments
        var devOptions = options with { Environment = "Dev" };
        var qaOptions = options with { Environment = "QA" };

        using var devHost = HostFactory.CreateHostForMigration(devOptions);
        using var qaHost = HostFactory.CreateHostForMigration(qaOptions);

        var devPool = HostFactory.GetConnectionPool(devHost, "Dev");
        var qaPool = HostFactory.GetConnectionPool(qaHost, "QA");

        if (devPool == null)
        {
            ConsoleWriter.Error("Dev environment not configured. See docs/guides/LOCAL_DEVELOPMENT_GUIDE.md");
            return 1;
        }

        if (qaPool == null)
        {
            ConsoleWriter.Error("QA environment not configured. See docs/guides/LOCAL_DEVELOPMENT_GUIDE.md");
            return 1;
        }

        try
        {
            // Connect to both environments
            Console.WriteLine("  Connecting to environments...");
            await using var devClient = await devPool.GetClientAsync();
            await using var qaClient = await qaPool.GetClientAsync();

            Console.WriteLine("    Dev: Connected");
            Console.WriteLine("    QA: Connected");
            Console.WriteLine();

            // Query users from both environments
            Console.WriteLine("  Querying users...");
            var devUsers = await QueryUsersAsync(devClient);
            var qaUsers = await QueryUsersAsync(qaClient);

            Console.WriteLine($"    Dev: {devUsers.Count} users");
            Console.WriteLine($"    QA: {qaUsers.Count} users");
            Console.WriteLine();

            // Build lookup by AAD Object ID
            var qaUsersByAadId = qaUsers
                .Where(u => u.AadObjectId.HasValue)
                .ToDictionary(u => u.AadObjectId!.Value, u => u);

            // Build lookup by domain name as fallback
            var qaUsersByDomain = qaUsers
                .Where(u => !string.IsNullOrEmpty(u.DomainName))
                .GroupBy(u => u.DomainName!.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First());

            // Match users
            var mappings = new List<UserMappingInfo>();
            var unmapped = new List<UserInfo>();

            foreach (var devUser in devUsers)
            {
                UserInfo? qaUser = null;

                // Try AAD Object ID first
                if (devUser.AadObjectId.HasValue &&
                    qaUsersByAadId.TryGetValue(devUser.AadObjectId.Value, out qaUser))
                {
                    mappings.Add(new UserMappingInfo
                    {
                        Source = devUser,
                        Target = qaUser,
                        MatchedBy = "AadObjectId"
                    });
                }
                // Fallback to domain name
                else if (!string.IsNullOrEmpty(devUser.DomainName) &&
                         qaUsersByDomain.TryGetValue(devUser.DomainName.ToLowerInvariant(), out qaUser))
                {
                    mappings.Add(new UserMappingInfo
                    {
                        Source = devUser,
                        Target = qaUser,
                        MatchedBy = "DomainName"
                    });
                }
                else
                {
                    unmapped.Add(devUser);
                }
            }

            // Report results
            Console.WriteLine("  Mapping Results:");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"    Matched: {mappings.Count}");
            Console.ResetColor();

            var byAad = mappings.Count(m => m.MatchedBy == "AadObjectId");
            var byDomain = mappings.Count(m => m.MatchedBy == "DomainName");
            Console.WriteLine($"      By AAD Object ID: {byAad}");
            Console.WriteLine($"      By Domain Name: {byDomain}");

            if (unmapped.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"    Unmapped: {unmapped.Count}");
                Console.ResetColor();
            }
            Console.WriteLine();

            // Show sample mappings
            Console.WriteLine("  Sample Mappings (first 5):");
            foreach (var mapping in mappings.Take(5))
            {
                Console.WriteLine($"    {mapping.Source.FullName}");
                Console.WriteLine($"      Dev: {mapping.Source.SystemUserId}");
                Console.WriteLine($"      QA:  {mapping.Target.SystemUserId} (matched by {mapping.MatchedBy})");
            }
            Console.WriteLine();

            // Show unmapped users
            if (unmapped.Count > 0)
            {
                Console.WriteLine("  Unmapped Users (first 10):");
                foreach (var user in unmapped.Take(10))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"    {user.FullName} ({user.DomainName ?? "no domain"})");
                    Console.ResetColor();
                }
                Console.WriteLine();
            }

            if (analyzeOnly)
            {
                Console.WriteLine("  [ANALYZE ONLY] No mapping file generated.");
                return 0;
            }

            // Generate mapping file
            Console.WriteLine($"  Generating mapping file: {outputPath}");
            GenerateMappingFile(outputPath, mappings);
            ConsoleWriter.Success($"  Generated {mappings.Count} mappings");
            Console.WriteLine();

            Console.WriteLine("  Usage:");
            Console.WriteLine($"    ppds-migrate import --data <file> --user-mapping \"{outputPath}\"");

            return 0;
        }
        catch (Exception ex)
        {
            ConsoleWriter.Exception(ex, options.Debug);
            return 1;
        }
    }

    private static async Task<List<UserInfo>> QueryUsersAsync(IPooledClient client)
    {
        var query = new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet(
                "systemuserid",
                "fullname",
                "domainname",
                "internalemailaddress",
                "azureactivedirectoryobjectid",
                "isdisabled",
                "accessmode"
            ),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    // Exclude disabled users
                    new ConditionExpression("isdisabled", ConditionOperator.Equal, false)
                }
            }
        };

        var results = await client.RetrieveMultipleAsync(query);
        return results.Entities.Select(e => new UserInfo
        {
            SystemUserId = e.Id,
            FullName = e.GetAttributeValue<string>("fullname") ?? "(no name)",
            DomainName = e.GetAttributeValue<string>("domainname"),
            Email = e.GetAttributeValue<string>("internalemailaddress"),
            AadObjectId = e.GetAttributeValue<Guid?>("azureactivedirectoryobjectid"),
            AccessMode = e.GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>("accessmode")?.Value ?? 0
        }).ToList();
    }

    private static void GenerateMappingFile(string path, List<UserMappingInfo> mappings)
    {
        var doc = new XDocument(
            new XElement("mappings",
                new XAttribute("useCurrentUserAsDefault", "true"),
                mappings.Select(m => new XElement("mapping",
                    new XAttribute("sourceId", m.Source.SystemUserId),
                    new XAttribute("sourceName", m.Source.FullName),
                    new XAttribute("targetId", m.Target.SystemUserId),
                    new XAttribute("targetName", m.Target.FullName)
                ))
            )
        );

        doc.Save(path);
    }

    private class UserInfo
    {
        public Guid SystemUserId { get; set; }
        public string FullName { get; set; } = "";
        public string? DomainName { get; set; }
        public string? Email { get; set; }
        public Guid? AadObjectId { get; set; }
        public int AccessMode { get; set; }
    }

    private class UserMappingInfo
    {
        public UserInfo Source { get; set; } = null!;
        public UserInfo Target { get; set; } = null!;
        public string MatchedBy { get; set; } = "";
    }
}
