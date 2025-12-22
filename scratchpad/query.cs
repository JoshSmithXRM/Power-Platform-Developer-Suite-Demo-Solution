#!/usr/bin/env dotnet run
// =============================================================================
// Query Scratchpad - .NET 10 Single-File C# Script
// =============================================================================
//
// Quick Dataverse queries without a project file.
// Edit the query section below and run: dotnet run query.cs
//
// =============================================================================

#:package Microsoft.PowerPlatform.Dataverse.Client@1.1.27

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

var connectionString = Environment.GetEnvironmentVariable("DATAVERSE_CONNECTION")
    ?? "YOUR_CONNECTION_STRING_HERE";

if (connectionString == "YOUR_CONNECTION_STRING_HERE")
{
    Console.WriteLine("Set DATAVERSE_CONNECTION environment variable first.");
    Console.WriteLine("  $env:DATAVERSE_CONNECTION = \"AuthType=ClientSecret;Url=...\"");
    return;
}

using var client = new ServiceClient(connectionString);

if (!client.IsReady)
{
    Console.WriteLine($"Connection failed: {client.LastError}");
    return;
}

Console.WriteLine($"Connected to {client.ConnectedOrgFriendlyName}");
Console.WriteLine();

// =============================================================================
// EDIT YOUR QUERY HERE
// =============================================================================

// Example 1: Simple query
var accounts = client.RetrieveMultiple(new QueryExpression("account")
{
    ColumnSet = new ColumnSet("name", "telephone1", "createdon"),
    TopCount = 10,
    Orders = { new OrderExpression("createdon", OrderType.Descending) }
});

Console.WriteLine($"Recent Accounts ({accounts.Entities.Count}):");
Console.WriteLine(new string('-', 60));
foreach (var account in accounts.Entities)
{
    var name = account.GetAttributeValue<string>("name") ?? "(no name)";
    var phone = account.GetAttributeValue<string>("telephone1") ?? "";
    var created = account.GetAttributeValue<DateTime?>("createdon")?.ToString("yyyy-MM-dd") ?? "";
    Console.WriteLine($"  {name,-35} {phone,-15} {created}");
}

Console.WriteLine();

// Example 2: FetchXML query
var fetchXml = @"
<fetch top='5'>
  <entity name='systemuser'>
    <attribute name='fullname' />
    <attribute name='internalemailaddress' />
    <filter>
      <condition attribute='isdisabled' operator='eq' value='0' />
    </filter>
    <order attribute='fullname' />
  </entity>
</fetch>";

var users = client.RetrieveMultiple(new FetchExpression(fetchXml));

Console.WriteLine($"Active Users ({users.Entities.Count}):");
Console.WriteLine(new string('-', 60));
foreach (var user in users.Entities)
{
    var name = user.GetAttributeValue<string>("fullname") ?? "(no name)";
    var email = user.GetAttributeValue<string>("internalemailaddress") ?? "";
    Console.WriteLine($"  {name,-30} {email}");
}
