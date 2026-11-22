using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SkyCD.Plugin.Feature.Loans.Models;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.Loans.Views;

public partial class ContactsWindow : Window
{
    private static FilePickerFileType VCardFileType => new(LocalizationService.Current.Get(LoansKeys.VCardFiles))
    {
        Patterns = ["*.vcf", "*.vcard"],
        MimeTypes = ["text/vcard", "text/x-vcard"]
    };

    public ContactsWindow() => InitializeComponent();

    private void CloseClicked(object? sender, RoutedEventArgs args) => Close();

    private async void ImportClicked(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not ContactsDialogViewModel viewModel)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.Current.Get(LoansKeys.ImportContacts),
            AllowMultiple = true,
            FileTypeFilter = [VCardFileType]
        });
        if (files.Count == 0)
        {
            return;
        }

        var texts = new List<string>();
        try
        {
            foreach (var file in files)
            {
                await using var stream = await file.OpenReadAsync();
                using var reader = new StreamReader(stream);
                texts.Add(await reader.ReadToEndAsync());
            }
        }
        catch (Exception exception)
        {
            viewModel.Error = LocalizationService.Current.Format(LoansKeys.ContactsImportFailed, exception.Message);
            return;
        }

        viewModel.Import(string.Join("\n", texts));
    }

    private async void ExportClicked(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not ContactsDialogViewModel viewModel)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationService.Current.Get(LoansKeys.ExportContacts),
            SuggestedFileName = "contacts.vcf",
            DefaultExtension = "vcf",
            FileTypeChoices = [VCardFileType]
        });
        if (file is null)
        {
            return;
        }

        var text = viewModel.Export();
        if (text.Length == 0)
        {
            return;
        }

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(text);
        }
        catch (Exception exception)
        {
            viewModel.Error = LocalizationService.Current.Format(LoansKeys.ContactsExportFailed, exception.Message);
        }
    }
}
