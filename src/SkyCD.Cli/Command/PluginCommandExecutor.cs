using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommandDotNet;
using SkyCD.Cli.Extensions;
using SkyCD.Cli.Exceptions;
using SkyCD.Cli.Execution;

namespace SkyCD.Cli.Command;

internal static class PluginCommandExecutor
{
    internal static async Task<CliExitCodes> ExecutePluginCommandAsync(
        Lock consoleRedirectLock,
        TextWriter stdout,
        TextWriter stderr,
        JsonSerializerOptions jsonOptions,
        RegisteredCliContribution command,
        IReadOnlyList<string> pluginArgs,
        bool jsonOutput,
        CancellationToken cancellationToken)
    {
        var executionResult = await ExecuteWithTimeoutAsync(
            token => InvokePluginCommandAsync(consoleRedirectLock, stdout, stderr, command, pluginArgs, token),
            cancellationToken);

        if (!executionResult.Success)
        {
            await stderr.WriteLineAsync(executionResult.Error ?? "Plugin command failed.");
            return CliExitCodes.CommandFailed;
        }

        if (!string.IsNullOrWhiteSpace(executionResult.Output))
        {
            await stdout.WriteLineAsync(executionResult.Output);
        }
        else if (jsonOutput)
        {
            await stdout.WriteJsonAsync(new
            {
                success = true,
                command = command.CommandPath
            }, jsonOptions);
        }

        return executionResult.ExitCode;
    }

    private static async Task<PluginCommandExecutionResult> InvokePluginCommandAsync(
        Lock consoleRedirectLock,
        TextWriter stdout,
        TextWriter stderr,
        RegisteredCliContribution command,
        IReadOnlyList<string> pluginArgs,
        CancellationToken cancellationToken)
    {
        try
        {
            var runnerType = typeof(AppRunner<>).MakeGenericType(command.CommandInstance.GetType());
            var runner = Activator.CreateInstance(runnerType, new AppSettings(), new Resources())
                         ?? throw new CliRunnerCreationException(command.CommandPath);

            var runMethod = runner.GetType().GetMethod("Run", [typeof(string[])])
                           ?? throw new CliRunnerMethodResolutionException(command.CommandPath);

            var canonicalPluginArgs = CanonicalizePluginCommandTokens(command.CommandInstance.GetType(), pluginArgs);
            var normalizedArgs = NormalizeSystemRunnerArgs(canonicalPluginArgs);
            var exitCode = await Task.Run(() =>
            {
                lock (consoleRedirectLock)
                {
                    var previousOut = System.Console.Out;
                    var previousError = System.Console.Error;
                    try
                    {
                        System.Console.SetOut(TextWriter.Synchronized(stdout));
                        System.Console.SetError(TextWriter.Synchronized(stderr));
                        return (int)(runMethod.Invoke(runner, [normalizedArgs]) ?? (int)CliExitCodes.Success);
                    }
                    finally
                    {
                        System.Console.SetOut(previousOut);
                        System.Console.SetError(previousError);
                    }
                }
            }, cancellationToken);

            var mappedExitCode = System.Enum.IsDefined(typeof(CliExitCodes), exitCode)
                ? (CliExitCodes)exitCode
                : CliExitCodes.InvalidArguments;
            return mappedExitCode == CliExitCodes.Success
                ? new PluginCommandExecutionResult(true, null, null, mappedExitCode)
                : new PluginCommandExecutionResult(false, null, $"Plugin command returned {mappedExitCode}.", mappedExitCode);
        }
        catch (TargetInvocationException exception)
        {
            return new PluginCommandExecutionResult(false, null, exception.InnerException?.Message ?? exception.Message, CliExitCodes.CommandFailed);
        }
        catch (Exception exception)
        {
            return new PluginCommandExecutionResult(false, null, exception.Message, CliExitCodes.CommandFailed);
        }
    }

    private static async Task<PluginCommandExecutionResult> ExecuteWithTimeoutAsync(
        Func<CancellationToken, Task<PluginCommandExecutionResult>> executor,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            return await executor(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PluginCommandExecutionResult(false, null, "Plugin CLI handler timed out after 5 seconds.", CliExitCodes.CommandFailed);
        }
        catch (Exception exception)
        {
            return new PluginCommandExecutionResult(false, null, exception.Message, CliExitCodes.CommandFailed);
        }
    }

    private static string[] NormalizeSystemRunnerArgs(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return [];
        }

        var normalized = new string[args.Count];
        for (var index = 0; index < args.Count; index++)
        {
            normalized[index] = args[index].Equals("/?", StringComparison.OrdinalIgnoreCase)
                ? "--help"
                : args[index];
        }

        return normalized;
    }

    private static IReadOnlyList<string> CanonicalizePluginCommandTokens(Type rootCommandType, IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return args;
        }

        var canonical = args.ToArray();
        var currentType = rootCommandType;

        for (var index = 0; index < canonical.Length; index++)
        {
            var token = canonical[index];
            if (token.StartsWith("-", StringComparison.Ordinal))
            {
                break;
            }

            var subcommands = GetSubcommandTypes(currentType)
                .Select(subcommandType => new
                {
                    Name = GetDeclaredCommandName(subcommandType),
                    Type = subcommandType
                })
                .Where(static item => !string.IsNullOrWhiteSpace(item.Name))
                .ToList();

            if (subcommands.Count == 0)
            {
                break;
            }

            var match = subcommands.FirstOrDefault(item =>
                item.Name.Equals(token, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                break;
            }

            canonical[index] = match.Name;
            currentType = match.Type;
        }

        return canonical;
    }

    private static IEnumerable<Type> GetSubcommandTypes(Type commandType)
    {
        return commandType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(static property => property.GetCustomAttribute<SubcommandAttribute>() is not null)
            .Select(static property => property.PropertyType);
    }

    private static string GetDeclaredCommandName(Type commandType)
    {
        var attributeData = commandType.CustomAttributes.FirstOrDefault(attribute =>
            attribute.AttributeType == typeof(CommandAttribute));
        if (attributeData is null)
        {
            return string.Empty;
        }

        if (attributeData.ConstructorArguments.Count > 0
            && attributeData.ConstructorArguments[0].ArgumentType == typeof(string)
            && attributeData.ConstructorArguments[0].Value is string ctorValue
            && !string.IsNullOrWhiteSpace(ctorValue))
        {
            return ctorValue.Trim();
        }

        var commandAttribute = commandType.GetCustomAttribute<CommandAttribute>();
        var namedName = commandAttribute?.Name;
        return string.IsNullOrWhiteSpace(namedName) ? string.Empty : namedName.Trim();
    }

    private sealed record PluginCommandExecutionResult(
        bool Success,
        string? Output,
        string? Error,
        CliExitCodes ExitCode);
}
