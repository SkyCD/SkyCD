using System;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace SkyCD.TestLogger;

[FriendlyName("ansi")]
[ExtensionUri("logger://SkyCD/Ansi/v1")]
public sealed class AnsiTestLogger : ITestLogger
{
    private const string Reset = "\u001b[0m";
    private const string Red = "\u001b[31m";
    private const string Green = "\u001b[32m";
    private const string Yellow = "\u001b[33m";
    private const string Dim = "\u001b[90m";

    public void Initialize(TestLoggerEvents events, string testResultsDirectory)
    {
        events.TestResult += TestResultHandler;
        events.TestRunMessage += TestRunMessageHandler;
        events.TestRunComplete += TestRunCompleteHandler;
    }

    private static void TestResultHandler(object? sender, TestResultEventArgs e)
    {
        var result = e.Result;
        var name = result.TestCase.DisplayName ?? result.TestCase.FullyQualifiedName;
        var duration = FormatDuration(result.Duration);

        switch (result.Outcome)
        {
            case TestOutcome.Passed:
                Console.WriteLine($"  {Green}Passed{Reset} {name} {Dim}{duration}{Reset}");
                break;

            case TestOutcome.Failed:
                Console.WriteLine($"  {Red}Failed{Reset} {name} {Dim}{duration}{Reset}");
                WriteFailureDetails(result);
                break;

            case TestOutcome.Skipped:
                Console.WriteLine($"  {Yellow}Skipped{Reset} {name} {Dim}{duration}{Reset}");
                break;

            default:
                Console.WriteLine($"  {Yellow}{result.Outcome}{Reset} {name} {Dim}{duration}{Reset}");
                break;
        }
    }

    private static void WriteFailureDetails(TestResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            Console.WriteLine($"  {Red}Error Message:{Reset}");
            WriteIndented(result.ErrorMessage, "    ");
        }

        if (!string.IsNullOrWhiteSpace(result.ErrorStackTrace))
        {
            Console.WriteLine($"  {Red}Stack Trace:{Reset}");
            WriteIndented(result.ErrorStackTrace, "    ");
        }

        foreach (var message in result.Messages)
        {
            if (message.Category == TestResultMessage.StandardErrorCategory)
            {
                Console.WriteLine($"  {Red}Standard Error:{Reset}");
                WriteIndented(message.Text, "    ");
            }
            else if (message.Category == TestResultMessage.StandardOutCategory)
            {
                Console.WriteLine("  Standard Output:");
                WriteIndented(message.Text, "    ");
            }
        }
    }

    private static void TestRunMessageHandler(object? sender, TestRunMessageEventArgs e)
    {
        switch (e.Level)
        {
            case TestMessageLevel.Error:
                Console.WriteLine($"{Red}{e.Message}{Reset}");
                break;

            case TestMessageLevel.Warning:
                Console.WriteLine($"{Yellow}{e.Message}{Reset}");
                break;
        }
    }

    private static void TestRunCompleteHandler(object? sender, TestRunCompleteEventArgs e)
    {
        Console.WriteLine();

        if (e.IsAborted)
        {
            Console.WriteLine($"Test Run {Red}Aborted{Reset}.");
        }
        else if (e.IsCanceled)
        {
            Console.WriteLine($"Test Run {Yellow}Canceled{Reset}.");
        }

        var stats = e.TestRunStatistics;
        if (stats is null)
        {
            return;
        }

        var failed = stats[TestOutcome.Failed];
        if (!e.IsAborted && !e.IsCanceled)
        {
            var outcome = failed == 0 ? $"{Green}Successful{Reset}" : $"{Red}Failed{Reset}";
            Console.WriteLine($"Test Run {outcome}.");
        }

        Console.WriteLine($"Total tests: {stats.ExecutedTests}");
        Console.WriteLine($"     Passed: {Green}{stats[TestOutcome.Passed]}{Reset}");
        Console.WriteLine($"     Failed: {Red}{failed}{Reset}");
        Console.WriteLine($"    Skipped: {Yellow}{stats[TestOutcome.Skipped]}{Reset}");
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalMilliseconds < 1
            ? "[< 1 ms]"
            : $"[{duration.TotalMilliseconds:0} ms]";

    private static void WriteIndented(string? text, string indent)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            Console.WriteLine(indent + line);
        }
    }
}
