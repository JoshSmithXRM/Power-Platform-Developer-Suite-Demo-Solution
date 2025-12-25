namespace PPDS.Dataverse.Demo.Infrastructure;

/// <summary>
/// Result of a CLI invocation.
/// Captures exit code, output streams, and provides success/failure semantics.
/// </summary>
public record CliResult
{
    /// <summary>
    /// The process exit code.
    /// </summary>
    public required int ExitCode { get; init; }

    /// <summary>
    /// Standard output content.
    /// </summary>
    public string StandardOutput { get; init; } = "";

    /// <summary>
    /// Standard error content.
    /// </summary>
    public string StandardError { get; init; } = "";

    /// <summary>
    /// The command that was executed (redacted for logging).
    /// </summary>
    public string CommandLine { get; init; } = "";

    /// <summary>
    /// Whether the command succeeded (ExitCode == 0).
    /// </summary>
    public bool Success => ExitCode == 0;

    /// <summary>
    /// Whether the command failed (ExitCode != 0).
    /// </summary>
    public bool Failed => ExitCode != 0;

    /// <summary>
    /// Creates a success result.
    /// </summary>
    public static CliResult Ok(string output = "", string commandLine = "") => new()
    {
        ExitCode = 0,
        StandardOutput = output,
        CommandLine = commandLine
    };

    /// <summary>
    /// Creates a failure result.
    /// </summary>
    public static CliResult Fail(int exitCode, string error = "", string commandLine = "") => new()
    {
        ExitCode = exitCode,
        StandardError = error,
        CommandLine = commandLine
    };
}
