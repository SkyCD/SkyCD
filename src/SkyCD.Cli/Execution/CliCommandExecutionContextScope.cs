using System.Threading;

namespace SkyCD.Cli.Execution;

internal static class CliCommandExecutionContextScope
{
    private static readonly AsyncLocal<CliCommandExecutionContext?> Context = new();

    public static CliCommandExecutionContext? Current
    {
        get => Context.Value;
        set => Context.Value = value;
    }
}
