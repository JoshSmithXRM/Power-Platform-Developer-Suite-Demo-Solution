#!/usr/bin/env dotnet run
// =============================================================================
// WhoAmI Scratchpad - .NET 10 Single-File C# Script
// =============================================================================
//
// Run with:   dotnet run whoami.cs
// Or:         dotnet whoami.cs (if .NET 10+ with file association)
//
// Set connection string via environment variable:
//   $env:DATAVERSE_CONNECTION = "AuthType=ClientSecret;Url=https://org.crm.dynamics.com;ClientId=...;ClientSecret=..."
//
// Or edit the fallback below for quick testing.
// =============================================================================

#:package Microsoft.PowerPlatform.Dataverse.Client@1.1.27

using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;

// Get connection string from environment or use fallback
var connectionString = Environment.GetEnvironmentVariable("DATAVERSE_CONNECTION")
    ?? "YOUR_CONNECTION_STRING_HERE";  // Edit this for quick testing

if (connectionString == "YOUR_CONNECTION_STRING_HERE")
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("No connection string configured!");
    Console.WriteLine();
    Console.ResetColor();
    Console.WriteLine("Options:");
    Console.WriteLine("  1. Set environment variable:");
    Console.WriteLine("     $env:DATAVERSE_CONNECTION = \"AuthType=ClientSecret;Url=...\"");
    Console.WriteLine();
    Console.WriteLine("  2. Edit this script and replace YOUR_CONNECTION_STRING_HERE");
    Console.WriteLine();
    return;
}

Console.WriteLine("Connecting to Dataverse...");
Console.WriteLine();

try
{
    using var client = new ServiceClient(connectionString);

    if (!client.IsReady)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Connection failed: {client.LastError}");
        Console.ResetColor();
        return;
    }

    // Execute WhoAmI
    var response = (WhoAmIResponse)client.Execute(new WhoAmIRequest());

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("Connected Successfully!");
    Console.ResetColor();
    Console.WriteLine();
    Console.WriteLine($"  Organization ID:   {response.OrganizationId}");
    Console.WriteLine($"  Business Unit ID:  {response.BusinessUnitId}");
    Console.WriteLine($"  User ID:           {response.UserId}");
    Console.WriteLine();
    Console.WriteLine($"  Environment:       {client.ConnectedOrgUriActual}");
    Console.WriteLine($"  Organization:      {client.ConnectedOrgFriendlyName}");
    Console.WriteLine($"  Version:           {client.ConnectedOrgVersion}");
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Error: {ex.Message}");
    Console.ResetColor();
}
