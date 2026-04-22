namespace SkyCD.Cli;

public static class CliExitCodes
{
    public const int Success = 0;
    public const int InvalidArguments = 2;
    public const int CommandFailed = 3;
    public const int ConfigurationError = 4;
    public const int Cancelled = 130;
}
