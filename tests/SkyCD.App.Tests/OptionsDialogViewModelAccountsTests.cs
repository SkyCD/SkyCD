using System.Linq;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #1306: accounts moved entirely into Add To List, so Options must no longer carry the
/// duplicate Accounts page; a stored selection from older versions falls back to the default page.
/// </summary>
public sealed class OptionsDialogViewModelAccountsTests
{
    private const string AccountsCategoryId = "accounts";
    private const int LegacyAccountsTabIndex = 13;

    [Fact]
    public void AccountsCategory_IsNotListed()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);

        Assert.DoesNotContain(viewModel.SettingCategories,
            category => category.Id == AccountsCategoryId);
        Assert.DoesNotContain(viewModel.SidebarEntries,
            entry => entry.Category?.Id == AccountsCategoryId);
    }

    [Fact]
    public void AccountsSearch_ShowsNoAccountsCategory()
    {
        var viewModel = new OptionsDialogViewModel(["English"])
        {
            SettingsSearchText = "Accounts"
        };

        Assert.DoesNotContain(viewModel.FilteredSettingCategories,
            category => category.Id == AccountsCategoryId);
        Assert.DoesNotContain(viewModel.SidebarEntries,
            entry => entry.Category?.Id == AccountsCategoryId);
    }

    [Fact]
    public void StoredAccountsSelection_RestoresTheDefaultPage()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);

        viewModel.RestoreSettingCategory(AccountsCategoryId, legacyTabIndex: LegacyAccountsTabIndex);

        Assert.Equal("plugins", viewModel.SelectedSettingCategory?.Id);
        Assert.DoesNotContain(viewModel.SettingCategories,
            category => category.BuiltInIndex == LegacyAccountsTabIndex);
    }
}
