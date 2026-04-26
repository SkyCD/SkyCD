namespace SkyCD.Cli;

public sealed class CliRunResult
{
    public required bool Handled { get; init; }

    public CliExitCodes ExitCode { get; init; }
}
