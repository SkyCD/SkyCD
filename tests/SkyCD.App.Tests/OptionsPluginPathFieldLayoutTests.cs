using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #1104/#1304: the Plugins → Settings folder field commits on Enter and lost focus only, and the
/// inline hint replaces an error dialog for a folder that is missing.
/// </summary>
public sealed class OptionsPluginPathFieldLayoutTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    [Fact]
    public void FolderField_CommitsOnLostFocusAndEnter()
    {
        var field = GetFolderField();

        Assert.Equal("{Binding PluginPath, UpdateSourceTrigger=PropertyChanged}", (string?)field.Attribute("Text"));
        Assert.Equal("OnPluginPathFieldLostFocus", (string?)field.Attribute("LostFocus"));

        var keyBinding = Assert.Single(field.Descendants(Avalonia + "KeyBinding"));
        Assert.Equal("Enter", (string?)keyBinding.Attribute("Gesture"));
        Assert.Equal("{Binding RescanPluginPathCommand}", (string?)keyBinding.Attribute("Command"));
    }

    [Fact]
    public void FolderField_ShowsTheBusyStateWhileTheCommittedScanRuns()
    {
        var busy = Assert.Single(GetFieldColumn().Elements(Avalonia + "TextBlock"),
            textBlock => (string?)textBlock.Attribute("Text") ==
                         "{i18n:Translate {x:Static i18n:UiKeys.PluginScanInProgress}}");

        Assert.Equal("{Binding IsPluginPathScanning}", (string?)busy.Attribute("IsVisible"));
    }

    [Fact]
    public void FolderField_ShowsTheMissingFolderHintBeneathIt()
    {
        var hint = Assert.Single(GetFieldColumn().Elements(Avalonia + "TextBlock"),
            textBlock => (string?)textBlock.Attribute("Text") == "{Binding PluginPathHint}");

        Assert.Equal("{Binding HasPluginPathHint}", (string?)hint.Attribute("IsVisible"));
        Assert.Equal("Wrap", (string?)hint.Attribute("TextWrapping"));
    }

    [Fact]
    public void FolderField_KeepsLiteralTypographyAndSpacingOutOfTheMarkup()
    {
        var markup = GetFieldColumn().ToString();

        Assert.DoesNotMatch(new Regex("(FontSize|Spacing|RowSpacing|ColumnSpacing)=\"[0-9.]+\""), markup);
        Assert.DoesNotMatch(new Regex("(Margin|Padding)=\"[0-9.]+\""), markup);
    }

    private static XElement GetFolderField() => Assert.Single(GetFieldColumn().Descendants(Avalonia + "TextBox"),
        textBox => ((string?)textBox.Attribute("AutomationProperties.Name"))?.Contains("UiKeys.PluginPath") == true);

    private static XElement GetFieldColumn()
    {
        var document = XDocument.Parse(ReadRepoFile("src", "SkyCD.App", "Views", "OptionsWindow.axaml"));
        var section = Assert.Single(document.Descendants(),
            element => (string?)element.Attribute("IsVisible") == "{Binding ShowPluginsSettingsSection}");
        return Assert.Single(section.Elements(Avalonia + "StackPanel"));
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine([root, .. parts]));
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
