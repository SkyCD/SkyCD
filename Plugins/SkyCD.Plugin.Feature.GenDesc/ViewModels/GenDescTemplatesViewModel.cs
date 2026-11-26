using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Plugin.Feature.GenDesc.Exceptions;
using SkyCD.Plugin.Feature.GenDesc.Localization;
using SkyCD.Plugin.Feature.GenDesc.Models;
using SkyCD.Plugin.Feature.GenDesc.Parsing;
using SkyCD.Plugin.OnlineMetadata.Models;
using SkyCD.SDK.Capabilities.Ui;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.GenDesc.ViewModels;

/// <summary>
/// Edits the GenDesc templates: a kind filter, the template list, the header fields, the body with a live
/// syntax view and preview, and import/export of individual template files.
/// </summary>
public sealed partial class GenDescTemplatesViewModel : ObservableObject
{
    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant);
    private readonly IHostFileDialogApi? fileDialogs;
    private readonly string directory;
    private TemplateStore store;
    private bool suppressPreview;

    public GenDescTemplatesViewModel(IHostFileDialogApi? fileDialogs, string? directory = null)
    {
        this.fileDialogs = fileDialogs;
        this.directory = directory ?? GenDescTemplatePaths.DefaultDirectory;
        KindOptions =
        [
            new KindOption(TemplateInfo.NfoKind, LocalizationService.Current.Get(GenDescKeys.NfoKind)),
            new KindOption(TemplateInfo.DescriptionKind, LocalizationService.Current.Get(GenDescKeys.DescriptionKind))
        ];
        selectedKindOption = KindOptions[1];
        Tokens =
        [
            "Title ?? Name",
            "Year",
            "has(\"Genre\")",
            "join(\"Genre\", \", \")",
            "Plot",
            "FileName",
            "Size",
            "MimeType",
            "CategoryTitle",
            "upper(Title)",
            "filesize(SizeBytes)",
            "date(LastWriteTime, \"yyyy-MM-dd\")"
        ];
        selectedToken = Tokens[0];
        store = GenDescTemplatePaths.LoadAndSeed(directory);
        RefreshList();
    }

    public IReadOnlyList<KindOption> KindOptions { get; }

    public IReadOnlyList<string> Tokens { get; }

    public string TemplateDirectory => directory;

    public ObservableCollection<TemplateInfo> Templates { get; } = [];

    [ObservableProperty]
    private KindOption selectedKindOption;

    [ObservableProperty]
    private TemplateInfo? selectedTemplate;

    [ObservableProperty]
    private string editorId = string.Empty;

    [ObservableProperty]
    private string editorCategory = TemplateInfo.GeneralCategory;

    [ObservableProperty]
    private string editorLanguage = "en";

    [ObservableProperty]
    private string editorSite = TemplateInfo.DefaultSite;

    [ObservableProperty]
    private string editorVersion = "1";

    [ObservableProperty]
    private string editorMime = string.Empty;

    [ObservableProperty]
    private string editorExtensions = string.Empty;

    [ObservableProperty]
    private string editorBody = string.Empty;

    [ObservableProperty]
    private string selectedToken;

    [ObservableProperty]
    private string previewText = string.Empty;

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private string errorText = string.Empty;

    [ObservableProperty]
    private string previewError = string.Empty;

    partial void OnSelectedKindOptionChanged(KindOption value) => RefreshList();

    partial void OnSelectedTemplateChanged(TemplateInfo? value)
    {
        if (value is null)
        {
            return;
        }

        suppressPreview = true;
        EditorId = value.Id;
        EditorCategory = value.Category;
        EditorLanguage = value.Language;
        EditorSite = value.Site;
        EditorVersion = value.Version;
        EditorMime = value.MimeTypes;
        EditorExtensions = value.Extensions;
        EditorBody = value.Body;
        suppressPreview = false;
        StatusText = string.Empty;
        ErrorText = string.Empty;
        UpdatePreview();
    }

    partial void OnEditorBodyChanged(string value) => UpdatePreview();

    [RelayCommand]
    private void New()
    {
        SelectedTemplate = null;
        var id = UniqueId("custom-template");
        EditorId = id;
        EditorCategory = TemplateInfo.GeneralCategory;
        EditorLanguage = "en";
        EditorSite = TemplateInfo.DefaultSite;
        EditorVersion = "1";
        EditorMime = string.Empty;
        EditorExtensions = string.Empty;
        EditorBody = "[b]{{ default(\"Title\", Name) }}{{ if Year }} ({{ Year }}){{ end }}[/b]\n\n{{ if has(\"Plot\") }}{{ Plot }}{{ end }}\n";
        StatusText = string.Empty;
        ErrorText = string.Empty;
        UpdatePreview();
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (SelectedTemplate is not { } template)
        {
            ErrorText = LocalizationService.Current.Get(GenDescKeys.MissingSelection);
            return;
        }

        SelectedTemplate = null;
        EditorId = UniqueId(template.Id + "-copy");
        EditorCategory = template.Category;
        EditorLanguage = template.Language;
        EditorSite = template.Site;
        EditorVersion = template.Version;
        EditorMime = template.MimeTypes;
        EditorExtensions = template.Extensions;
        EditorBody = template.Body;
        StatusText = string.Empty;
        ErrorText = string.Empty;
        UpdatePreview();
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedTemplate is not { } template)
        {
            ErrorText = LocalizationService.Current.Get(GenDescKeys.MissingSelection);
            return;
        }

        try
        {
            store.Delete(template);
            RefreshList();
            StatusText = LocalizationService.Current.Get(GenDescKeys.Deleted);
        }
        catch (Exception exception) when (exception is TemplateStorageException or IOException or UnauthorizedAccessException)
        {
            ErrorText = exception.Message;
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (!TryBuild(out var template))
        {
            return;
        }

        try
        {
            store.Save(template!);
            ErrorText = string.Empty;
            RefreshList(template!.Id);
            StatusText = LocalizationService.Current.Get(GenDescKeys.Saved);
        }
        catch (Exception exception) when (exception is TemplateStorageException or IOException or UnauthorizedAccessException)
        {
            ErrorText = exception.Message;
        }
    }

    [RelayCommand]
    private void Reload()
    {
        store = GenDescTemplatePaths.LoadAndSeed(directory);
        StatusText = string.Empty;
        ErrorText = string.Empty;
        RefreshList();
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (fileDialogs is null)
        {
            ErrorText = LocalizationService.Current.Get(GenDescKeys.NoPicker);
            return;
        }

        var path = await fileDialogs.ShowOpenAsync(new OpenFileRequest(
            LocalizationService.Current.Get(GenDescKeys.Import),
            [new FileDialogFilter("GenDesc templates", ["*.tpl"])]));
        if (path is null)
        {
            return;
        }

        try
        {
            var content = await File.ReadAllTextAsync(path);
            var template = TemplateInfo.Parse(content, Path.GetFileNameWithoutExtension(path),
                SelectedKindOption.Value);
            suppressPreview = true;
            SelectedTemplate = null;
            EditorId = template.Id;
            EditorCategory = template.Category;
            EditorLanguage = template.Language;
            EditorSite = template.Site;
            EditorVersion = template.Version;
            EditorMime = template.MimeTypes;
            EditorExtensions = template.Extensions;
            EditorBody = template.Body;
            suppressPreview = false;
            StatusText = LocalizationService.Current.Format(GenDescKeys.Imported, path);
            ErrorText = string.Empty;
            UpdatePreview();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TemplateSyntaxException)
        {
            ErrorText = exception.Message;
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (fileDialogs is null)
        {
            ErrorText = LocalizationService.Current.Get(GenDescKeys.NoPicker);
            return;
        }

        if (!TryBuild(out var template))
        {
            return;
        }

        var path = await fileDialogs.ShowSaveAsync(new SaveFileRequest(
            LocalizationService.Current.Get(GenDescKeys.Export),
            template!.Id + ".tpl",
            ".tpl",
            [new FileDialogFilter("GenDesc templates", ["*.tpl"])]));
        if (path is null)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(path, template.ToFileContent());
            StatusText = LocalizationService.Current.Format(GenDescKeys.Exported, path);
            ErrorText = string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErrorText = exception.Message;
        }
    }

    [RelayCommand]
    private void InsertToken()
    {
        if (string.IsNullOrWhiteSpace(SelectedToken))
        {
            return;
        }

        var separator = EditorBody.EndsWith('\n') || EditorBody.Length == 0 ? string.Empty : "\n";
        EditorBody += separator + "{{ " + SelectedToken + " }}\n";
    }

    private void RefreshList(string? selectId = null)
    {
        var wantedId = selectId ?? SelectedTemplate?.Id;
        Templates.Clear();
        foreach (var template in store.ForKind(SelectedKindOption.Value))
        {
            Templates.Add(template);
        }

        SelectedTemplate = Templates.FirstOrDefault(template =>
            string.Equals(template.Id, wantedId, StringComparison.OrdinalIgnoreCase));
        if (SelectedTemplate is null && Templates.Count > 0)
        {
            SelectedTemplate = Templates[0];
        }

        if (store.LoadErrors.Count > 0)
        {
            ErrorText = LocalizationService.Current.Format(GenDescKeys.LoadErrors, store.LoadErrors.Count) +
                        " " + string.Join("; ", store.LoadErrors);
        }
    }

    private bool TryBuild(out TemplateInfo? template)
    {
        template = null;
        ErrorText = string.Empty;
        var id = EditorId.Trim().ToLowerInvariant();
        if (!IdPattern.IsMatch(id))
        {
            ErrorText = LocalizationService.Current.Get(GenDescKeys.InvalidId);
            return false;
        }

        if (string.IsNullOrWhiteSpace(EditorBody))
        {
            ErrorText = LocalizationService.Current.Get(GenDescKeys.EmptyBody);
            return false;
        }

        var mimeError = ValidateMimeTypes(EditorMime);
        if (mimeError is not null)
        {
            ErrorText = mimeError;
            return false;
        }

        var extensionError = ValidateExtensions(EditorExtensions);
        if (extensionError is not null)
        {
            ErrorText = extensionError;
            return false;
        }

        try
        {
            // Scriban reports unknown helpers at render time, so trial-render with empty facts to reject
            // them here instead of failing the first real generation or preview.
            TemplateEngine.Compile(EditorBody).Render(new DescriptionFacts());
        }
        catch (TemplateSyntaxException exception)
        {
            ErrorText = exception.Message;
            return false;
        }

        template = new TemplateInfo(
            id,
            SelectedKindOption.Value,
            string.IsNullOrWhiteSpace(EditorCategory) ? TemplateInfo.GeneralCategory : EditorCategory.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(EditorLanguage) ? "en" : EditorLanguage.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(EditorSite) ? TemplateInfo.DefaultSite : EditorSite.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(EditorVersion) ? "1" : EditorVersion.Trim(),
            NormalizeTokens(EditorMime),
            NormalizeTokens(NormalizeExtensionDots(EditorExtensions)),
            EditorBody);
        return true;
    }

    private void UpdatePreview()
    {
        if (suppressPreview)
        {
            return;
        }

        try
        {
            PreviewText = TemplateEngine.Render(EditorBody, SampleFacts());
            PreviewError = string.Empty;
        }
        catch (TemplateSyntaxException exception)
        {
            PreviewText = string.Empty;
            PreviewError = LocalizationService.Current.Format(GenDescKeys.PreviewFailed, exception.Message);
        }
    }

    private DescriptionFacts SampleFacts()
    {
        var facts = new DescriptionFacts();
        facts.Force("Name", "Example.Movie.2024.1080p.BluRay.x264-GROUP");
        facts.Force("Title", "Example Movie");
        facts.Force("Year", "2024");
        facts.Force("Genre", "Action|Drama");
        facts.Force("Runtime", "1h 58m");
        facts.Force("Director", "Jane Director");
        facts.Force("Cast", "Actor One|Actor Two");
        facts.Force("Rating", "7.8/10");
        facts.Force("Plot", "A short sample plot used to preview the template layout.");
        facts.Force("FileName", "Example.Movie.2024.1080p.BluRay.x264-GROUP.mkv");
        facts.Force("Extension", ".mkv");
        facts.Force("MimeType", "video/x-matroska");
        facts.Force("SizeBytes", "9126805504");
        facts.Force("Size", "8.5 GB");
        facts.Force("Category", string.IsNullOrWhiteSpace(EditorCategory) ? TemplateInfo.GeneralCategory : EditorCategory);
        facts.Force("CategoryTitle", GenDescCategory.TitleFor(EditorCategory));
        return facts;
    }

    private string UniqueId(string candidate)
    {
        var id = candidate;
        var suffix = 2;
        while (store.Find(id) is not null || Templates.Any(template =>
                   string.Equals(template.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            id = candidate + "-" + suffix++;
        }

        return id;
    }

    private static string NormalizeTokens(string value) => string.Join(' ', SplitTokens(value));

    private static string NormalizeExtensionDots(string value) => string.Join(' ', SplitTokens(value)
        .Select(token => token.StartsWith('.') ? token : "." + token));

    private static string[] SplitTokens(string value) =>
        value.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private string? ValidateMimeTypes(string value)
    {
        foreach (var mime in value.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (mime == "*/*" || mime.EndsWith("/*", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = mime.Split('/');
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            {
                return LocalizationService.Current.Get(GenDescKeys.InvalidMime) + " (" + mime + ")";
            }
        }

        return null;
    }

    private string? ValidateExtensions(string value)
    {
        foreach (var extension in value.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = extension.StartsWith('.') ? extension : "." + extension;
            if (normalized.Length < 2 || normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                normalized.Contains('/') || normalized.Contains('\\'))
            {
                return LocalizationService.Current.Get(GenDescKeys.InvalidExtension) + " (" + extension + ")";
            }
        }

        return null;
    }
}
