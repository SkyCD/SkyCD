using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #999: the design-token gate is the rule contributors and agents actually hit, so the
/// fixture markup below is checked by the real script rather than by a copy of its expressions.
/// </summary>
public sealed class DesignTokenGateTests
{
    private const string ViolatingFixture = """
        <UserControl xmlns="https://github.com/avaloniaui">
            <StackPanel Padding="8,6">
                <Border Margin="16 12" />
                <TextBox Margin="0,0,0,4" Padding="0" />
                <ListBox.Styles>
                    <Style Selector="ListBoxItem">
                        <Setter Property="Padding" Value="14,10" />
                    </Style>
                </ListBox.Styles>
            </StackPanel>
        </UserControl>
        """;

    private const string CompliantFixture = """
        <UserControl xmlns="https://github.com/avaloniaui">
            <StackPanel Padding="{DynamicResource SkyRowPadding}">
                <Border Margin="{DynamicResource SkyInsetLg}" />
                <TextBox Margin="{DynamicResource SkyGapBottomXs}" Padding="0" />
                <ListBox.Styles>
                    <Style Selector="ListBoxItem">
                        <Setter Property="Padding" Value="{DynamicResource SkyControlPadding}" />
                    </Style>
                </ListBox.Styles>
            </StackPanel>
        </UserControl>
        """;

    [Fact]
    public void Gate_FailsOnLiteralInsets()
    {
        using var workspace = new TestWorkspace();
        WriteRepository(workspace, ViolatingFixture);

        var (exitCode, output) = RunGate(workspace.Root);

        Assert.Equal(1, exitCode);
        Assert.Contains("uses a literal inset value '8,6'", output, StringComparison.Ordinal);
        Assert.Contains("uses a literal inset value '16 12'", output, StringComparison.Ordinal);
        Assert.Contains("uses a literal inset value '14,10'", output, StringComparison.Ordinal);
        Assert.DoesNotContain("literal inset value '0'", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate_PassesForTokenisedInsetsAndZeroResets()
    {
        using var workspace = new TestWorkspace();
        WriteRepository(workspace, CompliantFixture);

        var (exitCode, output) = RunGate(workspace.Root);

        Assert.Equal(0, exitCode);
        Assert.Contains("Design token check passed", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The script resolves the token dictionary and the scanned roots from the repository root it is
    /// given, so a fixture only has to mirror those two paths.
    /// </summary>
    private static void WriteRepository(TestWorkspace workspace, string xaml)
    {
        var tokens = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "SkyCD.App", "Styles", "DesignTokens.axaml"));
        workspace.CreateFile(Path.Combine("src", "SkyCD.App", "Styles", "DesignTokens.axaml"), tokens);
        workspace.CreateFile(Path.Combine("src", "Fixtures", "Views", "Insets.axaml"), xaml);
    }

    private static (int ExitCode, string Output) RunGate(string repositoryRoot)
    {
        var script = Path.Combine(FindRepoRoot(), "tools", "scripts", "ci", "check-design-tokens.ps1");
        var startInfo = new ProcessStartInfo("pwsh")
        {
            Arguments = $"-NoProfile -NonInteractive -File \"{script}\" -RepositoryRoot \"{repositoryRoot}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = Start(startInfo);
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, output + error);
    }

    /// <summary>
    /// The gate is a PowerShell script and CI runs it in the style job, so a machine without pwsh on PATH
    /// cannot run it; the test reports that instead of passing on assertions that never executed.
    /// </summary>
    private static Process Start(ProcessStartInfo startInfo)
    {
        try
        {
            return Process.Start(startInfo) ?? throw new InvalidOperationException("pwsh did not return a process.");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                "pwsh is not on PATH; install PowerShell 7 to run the design-token gate.",
                exception);
        }
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SkyCD.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root with SkyCD.slnx was not found.");
    }
}
