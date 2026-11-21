using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public class CustomPropertyItemEditTests
{
    [Fact]
    public void BeginEdit_DraftStartsFromCommittedValueAndDoesNotChangeIt()
    {
        var item = new CustomPropertyItemViewModel { DefaultValue = "saved" };

        item.BeginEdit();
        item.EditValue = "draft";

        Assert.True(item.IsEditing);
        Assert.Equal("saved", item.DefaultValue);
    }

    [Fact]
    public void CommitEdit_CopiesTheDraftIntoTheCommittedValue()
    {
        var item = new CustomPropertyItemViewModel { DefaultValue = "saved" };
        item.BeginEdit();
        item.EditValue = "changed";

        item.CommitEdit();

        Assert.False(item.IsEditing);
        Assert.Equal("changed", item.DefaultValue);
    }

    [Fact]
    public void CancelEdit_DropsTheDraftAndKeepsTheCommittedValue()
    {
        var item = new CustomPropertyItemViewModel { DefaultValue = "saved" };
        item.BeginEdit();
        item.EditValue = "draft";

        item.CancelEdit();
        item.BeginEdit();

        Assert.True(item.IsEditing);
        Assert.Equal("saved", item.DefaultValue);
        Assert.Equal("saved", item.EditValue);
    }

    [Fact]
    public void EditMethods_AreNoOpsOutsideTheirState()
    {
        var item = new CustomPropertyItemViewModel { DefaultValue = "saved" };

        item.CommitEdit();
        item.CancelEdit();

        Assert.False(item.IsEditing);
        Assert.Equal("saved", item.DefaultValue);
    }
}
