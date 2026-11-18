using System;
using System.Collections.Generic;
using System.Globalization;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public class PropertiesDialogViewModelTests
{
    [Fact]
    public void Constructor_InitializesAllProperties()
    {
        var infoItems = new PropertiesCollection(new Dictionary<string, object?> { ["Size"] = "1024 KB" });
        var vm = new PropertiesDialogViewModel(
            objectKey: "file123",
            name: "document.pdf",
            iconGlyph: "folder",
            comments: "Important document",
            infoProperties: infoItems);

        Assert.Equal("file123", vm.ObjectKey);
        Assert.Equal("document.pdf", vm.Name);
        Assert.Equal("folder", vm.IconGlyph);
        Assert.Equal("Important document", vm.Comments);
        Assert.Single(vm.InfoProperties);
    }

    [Fact]
    public void DialogAccepted_DefaultsToFalse()
    {
        var vm = new PropertiesDialogViewModel("key", "name", "icon", "comments", new PropertiesCollection());

        Assert.False(vm.DialogAccepted);
    }

    [Fact]
    public void HasInfoTab_IsTrueWhenInfoPropertiesNotEmpty()
    {
        var infoItems = new PropertiesCollection(new Dictionary<string, object?> { ["Property"] = "Value" });
        var vm = new PropertiesDialogViewModel("key", "name", "icon", "comments", infoItems);

        Assert.True(vm.HasInfoTab);
    }

    [Fact]
    public void HasInfoTab_IsFalseWhenInfoPropertiesEmpty()
    {
        var vm = new PropertiesDialogViewModel("key", "name", "icon", "comments", new PropertiesCollection());

        Assert.False(vm.HasInfoTab);
    }

    [Fact]
    public void Comments_CanBeModified()
    {
        var vm = new PropertiesDialogViewModel("key", "name", "icon", "initial", new PropertiesCollection());

        vm.Comments = "updated comments";

        Assert.Equal("updated comments", vm.Comments);
    }

    [Fact]
    public void Name_CanBeModified()
    {
        var vm = new PropertiesDialogViewModel("key", "name", "icon", "initial", new PropertiesCollection());

        vm.Name = "renamed";

        Assert.Equal("renamed", vm.Name);
    }

    [Fact]
    public void ConfirmCommand_SetsDialogAcceptedTrue()
    {
        var vm = new PropertiesDialogViewModel("key", "name", "icon", "comments", new PropertiesCollection());

        vm.ConfirmCommand.Execute(null);

        Assert.True(vm.DialogAccepted);
    }

    [Fact]
    public void Constructor_NormalizesEmptyValuesToUnknown()
    {
        var vm = new PropertiesDialogViewModel(
            "key",
            "name",
            "icon",
            "comments",
            new PropertiesCollection(new Dictionary<string, object?> { ["Size"] = string.Empty }));

        Assert.Equal("Unknown", vm.InfoProperties["Size"]);
    }

    [Fact]
    public void Constructor_SortsInfoPropertiesAscendingByProperty()
    {
        var vm = new PropertiesDialogViewModel(
            "key",
            "name",
            "icon",
            "comments",
            new PropertiesCollection(new Dictionary<string, object?>
            {
                ["Zeta"] = "1",
                ["Alpha"] = "2",
                ["Middle"] = "3"
            }));

        Assert.Equal(["Alpha", "Middle", "Zeta"], vm.InfoProperties.Keys);
    }

    [Theory]
    [InlineData("2016-06-05T16:49:14.9800000+03:00", "2016-06-05T13:49:14Z", "en-US")]
    [InlineData("2016-06-05T16:49:14Z", "2016-06-05T16:49:14Z", "lt-LT")]
    [InlineData("2016-01-15T06:35:06.1-05:30", "2016-01-15T12:05:06Z", "en-US")]
    public void Constructor_ConvertsOffsetTimestampsToLocalTime(string value, string utcValue, string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var utc = DateTime.ParseExact(utcValue, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        var expected = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local).ToString("G", culture);
        var vm = CreateWithCulture(value, culture);

        Assert.Equal(expected, vm.InfoProperties["Value"]);
        Assert.Equal(value, vm.GetProperties()["Value"]);
    }

    [Theory]
    [InlineData("2016-06-05T16:49:14.9800000", "en-US", "6/5/2016 4:49:14 PM")]
    [InlineData("2016-06-05T16:49:14", "lt-LT", "2016-06-05 16:49:14")]
    public void Constructor_FormatsOffsetlessTimestampsWithoutShifting(string value, string cultureName, string expected)
    {
        var vm = CreateWithCulture(value, CultureInfo.GetCultureInfo(cultureName));

        Assert.Equal(expected, vm.InfoProperties["Value"]);
        Assert.Equal(value, vm.GetProperties()["Value"]);
    }

    [Theory]
    [InlineData("2016-02-30T16:49:14Z")]
    [InlineData("0000-01-01T00:00:00Z")]
    [InlineData("2016-06-05T16:49:14+15:00")]
    [InlineData("2016-06-05T16:49:14.12345678Z")]
    [InlineData("2016-06-05T16:49:14.Z")]
    [InlineData("2016-06-05T16:49:14z")]
    [InlineData("2016-06-05 16:49:14")]
    [InlineData("2016-06-05")]
    [InlineData("2016-06-05T16:49:14Z\n")]
    [InlineData("1024")]
    [InlineData("arbitrary text")]
    public void Constructor_LeavesInvalidAndNonTimestampValuesUnchanged(string value)
    {
        var vm = CreateWithCulture(value, CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal(value, vm.InfoProperties["Value"]);
        Assert.Equal(value, vm.GetProperties()["Value"]);
    }

    [Fact]
    public void Constructor_FormatsTypedDatesAndPreservesTheirValues()
    {
        var culture = CultureInfo.GetCultureInfo("lt-LT");
        var utc = new DateTime(2016, 6, 5, 13, 49, 14, DateTimeKind.Utc);
        var local = new DateTime(2016, 6, 5, 16, 49, 14, DateTimeKind.Local);
        var unspecified = new DateTime(2016, 6, 5, 16, 49, 14, DateTimeKind.Unspecified);
        var offset = new DateTimeOffset(2016, 6, 5, 16, 49, 14, TimeSpan.FromHours(3));
        var values = new PropertiesCollection { ["Utc"] = utc, ["Local"] = local, ["WallClock"] = unspecified, ["Offset"] = offset };
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = culture;
            var vm = new PropertiesDialogViewModel("key", "name", "icon", "", values);

            Assert.Equal(TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local).ToString("G", culture), vm.InfoProperties["Utc"]);
            Assert.Equal(local.ToString("G", culture), vm.InfoProperties["Local"]);
            Assert.Equal("2016-06-05 16:49:14", vm.InfoProperties["WallClock"]);
            Assert.Equal(TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local).ToString("G", culture), vm.InfoProperties["Offset"]);
            Assert.Equal(utc, vm.GetProperties()["Utc"]);
            Assert.Equal(local, vm.GetProperties()["Local"]);
            Assert.Equal(unspecified, vm.GetProperties()["WallClock"]);
            Assert.Equal(offset, vm.GetProperties()["Offset"]);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void CustomPropertyEditing_PreservesOriginalTimestampUntilEdited()
    {
        const string timestamp = "2016-06-05T16:49:14.9800000+03:00";
        var source = new PropertiesCollection { ["Created"] = timestamp };
        var vm = new PropertiesDialogViewModel("key", "name", "icon", "", source);
        vm.ApplyCustomPropertyDefaults([new CustomPropertyDocument { Name = "Created", DefaultValue = "default" }]);

        Assert.Equal(timestamp, vm.CustomProperties[0].DefaultValue);
        Assert.Equal(timestamp, vm.GetProperties()["Created"]);
        vm.CustomProperties[0].DefaultValue = "edited";
        Assert.Equal("edited", vm.GetProperties()["Created"]);
        Assert.Equal(timestamp, source["Created"]);
    }

    private static PropertiesDialogViewModel CreateWithCulture(object value, CultureInfo culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = culture;
            return new PropertiesDialogViewModel("key", "name", "icon", "", new PropertiesCollection { ["Value"] = value });
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Constructor_LocalizesBooleanValuesForLithuanian()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("lt-LT");
            var vm = new PropertiesDialogViewModel(
                "key",
                "name",
                "icon",
                "comments",
                new PropertiesCollection(new Dictionary<string, object?> { ["Flag"] = true }));

            Assert.Equal("Taip", vm.InfoProperties["Flag"]);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}