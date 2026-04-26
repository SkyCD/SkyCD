namespace SkyCD.Cli;

public static class CliEntryPoint
{
    public static CliRunResult TryRun(string[] args, TextWriter? stdout = null, TextWriter? stderr = null)
    {
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler? handler = null;
        handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        System.Console.CancelKeyPress += handler;
        try
        {
            var host = new CliHost(stdout ?? System.Console.Out, stderr ?? System.Console.Error);
            return host.TryRunAsync(args, cts.Token).GetAwaiter().GetResult();
        }
        finally
        {
            System.Console.CancelKeyPress -= handler;
        }
    }
}
