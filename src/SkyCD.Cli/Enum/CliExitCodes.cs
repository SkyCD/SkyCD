namespace SkyCD.Cli;

public enum CliExitCodes
{
    Success = 0,
    InvalidArguments = 2,
    CommandFailed = 3,
    ConfigurationError = 4,
    Cancelled = 130
}
