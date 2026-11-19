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

    [Theory]
    [InlineData("Name")]
    [InlineData("name")]
    [InlineData("NaMe")]
    [InlineData("RealDirectoryName")]
    [InlineData("realdirectoryname")]
    [InlineData("rEaLdIrEcToRyNaMe")]
    [InlineData("Comment")]
    [InlineData("cOmMeNt")]
    public void Constructor_HidesGeneralTabPropertiesWithoutChangingStoredValues(string key)
    {
        var source = new PropertiesCollection { [key] = "original" };
        var vm = new PropertiesDialogViewModel("key", "display name", "icon", "original", source);

        Assert.Empty(vm.InfoProperties);
        Assert.False(vm.HasInfoTab);
        Assert.Equal("original", vm.GetProperties()[key]);
        Assert.Equal("original", source[key]);
    }

    [Fact]
    public void Constructor_RetainsOtherPropertiesAndTheirFormatting()
    {
        var source = new PropertiesCollection
        {
            ["Name"] = "item",
            ["RealDirectoryName"] = "directory",
            ["Comment"] = "general comment",
            ["Comments"] = "imported comments",
            ["FullName"] = @"C:\item",
            ["Size"] = 1024,
            ["Created"] = "2016-06-05T16:49:14",
            ["Attributes"] = "ReadOnly"
        };
        var culture = CultureInfo.GetCultureInfo("en-US");
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            var vm = new PropertiesDialogViewModel("key", "item", "icon", "general comment", source);

            Assert.True(vm.HasInfoTab);
            Assert.Equal(["Attributes", "Comments", "Created", "FullName", "Size"], vm.InfoProperties.Keys);
            Assert.Equal("ReadOnly", vm.InfoProperties["Attributes"]);
            Assert.Equal("imported comments", vm.InfoProperties["Comments"]);
            Assert.Equal(new DateTime(2016, 6, 5, 16, 49, 14).ToString("G", culture), vm.InfoProperties["Created"]);
            Assert.Equal(@"C:\item", vm.InfoProperties["FullName"]);
            Assert.Equal("1 KB (1,024 bytes)", vm.InfoProperties["Size"]);
            Assert.Equal("item", vm.GetProperties()["Name"]);
            Assert.Equal("directory", vm.GetProperties()["RealDirectoryName"]);
            Assert.Equal("general comment", vm.GetProperties()["Comment"]);
            Assert.Equal(1024, vm.GetProperties()["Size"]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Theory]
    [InlineData("Name", "nAmE")]
    [InlineData("RealDirectoryName", "realdirectoryname")]
    [InlineData("Comment", "comment")]
    public void CustomPropertyDefaults_UseHiddenRawValuesAndPreserveEditSemantics(string storedKey, string configuredKey)
    {
        var source = new PropertiesCollection { [storedKey] = "original" };
        var vm = new PropertiesDialogViewModel("key", "display name", "icon", "original", source);
        vm.ApplyCustomPropertyDefaults([new CustomPropertyDocument { Name = configuredKey, DefaultValue = "fallback" }]);

        var custom = Assert.Single(vm.CustomProperties);
        Assert.Equal(storedKey, custom.Name);
        Assert.Equal("original", custom.DefaultValue);
        Assert.Equal("original", vm.GetProperties()[storedKey]);
        custom.DefaultValue = "edited";
        Assert.Equal("edited", vm.GetProperties()[storedKey]);
        Assert.Equal("original", source[storedKey]);
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
    [InlineData(0L)]
    [InlineData(512L)]
    [InlineData(1024L)]
    [InlineData(1536L)]
    [InlineData(1048576L)]
    [InlineData(503206458L)]
    public void Constructor_FormatsSizesLikeBrowserAndPreservesRawValues(long bytes)
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            var document = new CatalogDocument { Size = bytes, Properties = new PropertiesCollection { ["Size"] = bytes.ToString(CultureInfo.InvariantCulture) } };
            var vm = new PropertiesDialogViewModel("key", "name", "icon", "", document.Properties);

            Assert.Equal($"{document.DisplaySize} ({bytes.ToString("N0", culture)} bytes)", vm.InfoProperties["Size"]);
            Assert.Equal(bytes.ToString(CultureInfo.InvariantCulture), vm.GetProperties()["Size"]);
            Assert.Equal(bytes.ToString(CultureInfo.InvariantCulture), document.Properties["Size"]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Theory]
    [InlineData("Size")]
    [InlineData("sIzE")]
    [InlineData("FileSize")]
    [InlineData("sizeBytes")]
    [InlineData("SIZEBYTES")]
    [InlineData("DedLogicalSize")]
    [InlineData("DedAggregateSize")]
    [InlineData("TotalFileSize")]
    public void Constructor_RecognizesSizeKeys(string key)
    {
        var vm = CreateWithCulture("1536", CultureInfo.GetCultureInfo("en-US"), key);

        Assert.Equal("1.5 KB (1,536 bytes)", vm.InfoProperties[key]);
        Assert.Equal("1536", vm.GetProperties()[key]);
    }

    [Theory]
    [InlineData(1536)]
    [InlineData(1536L)]
    [InlineData(1536UL)]
    public void Constructor_FormatsTypedIntegerSizesWithoutChangingTheirTypes(object value)
    {
        var vm = CreateWithCulture(value, CultureInfo.GetCultureInfo("en-US"), "Size");

        Assert.Equal("1.5 KB (1,536 bytes)", vm.InfoProperties["Size"]);
        Assert.Equal(value, vm.GetProperties()["Size"]);
        Assert.Equal(value.GetType(), vm.GetProperties()["Size"]!.GetType());
    }

    [Theory]
    [InlineData(9223372036854775808UL)]
    [InlineData(ulong.MaxValue)]
    [InlineData("18446744073709551615")]
    public void Constructor_FormatsSizesBeyondLongMaxWithoutOverflow(object value)
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        var vm = CreateWithCulture(value, culture, "Size");
        var bytes = ulong.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
        var display = Assert.IsType<string>(vm.InfoProperties["Size"]);

        Assert.EndsWith($" ({bytes.ToString("N0", culture)} bytes)", display, StringComparison.Ordinal);
        Assert.DoesNotContain("-", display, StringComparison.Ordinal);
        Assert.Equal(value, vm.GetProperties()["Size"]);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("1e3")]
    [InlineData("1.5 MB")]
    [InlineData("1,536")]
    [InlineData("1-5")]
    [InlineData(" 1536")]
    [InlineData("1536 ")]
    [InlineData("1536\0")]
    [InlineData("18446744073709551616")]
    [InlineData("arbitrary text")]
    public void Constructor_LeavesInvalidSizeValuesUnchanged(string value)
    {
        var vm = CreateWithCulture(value, CultureInfo.GetCultureInfo("en-US"), "Size");

        Assert.Equal(value, vm.InfoProperties["Size"]);
        Assert.Equal(value, vm.GetProperties()["Size"]);
    }

    [Fact]
    public void Constructor_LeavesNegativeAndFloatingPointSizesUnchanged()
    {
        var values = new PropertiesCollection { ["FileSize"] = -1L, ["Size"] = 1536.0 };
        var vm = new PropertiesDialogViewModel("key", "name", "icon", "", values);

        Assert.Equal("-1", vm.InfoProperties["FileSize"]);
        Assert.Equal(1536.0.ToString(), vm.InfoProperties["Size"]);
        Assert.Equal(-1L, vm.GetProperties()["FileSize"]);
        Assert.Equal(1536.0, vm.GetProperties()["Size"]);
    }

    [Theory]
    [InlineData("SizeInBytes")]
    [InlineData("sizeBytesExtra")]
    public void Constructor_LeavesOtherNumericPropertiesUnchanged(string key)
    {
        var vm = CreateWithCulture("1536", CultureInfo.GetCultureInfo("en-US"), key);

        Assert.Equal("1536", vm.InfoProperties[key]);
        Assert.Equal("1536", vm.GetProperties()[key]);
    }

    [Fact]
    public void Constructor_RendersAttributeMaskInsteadOfRawNumber()
    {
        var vm = CreateWithCulture("1536", CultureInfo.GetCultureInfo("en-US"), "Attributes");

        Assert.Equal("Sparse file, Reparse point", vm.InfoProperties["Attributes"]);
        Assert.Equal("1536", vm.GetProperties()["Attributes"]);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("lt-LT")]
    public void Constructor_GroupsExactByteCountWithUiCulture(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var vm = CreateWithCulture("503206458", culture, "Size");

        Assert.EndsWith($" ({503206458L.ToString("N0", culture)} bytes)", Assert.IsType<string>(vm.InfoProperties["Size"]), StringComparison.Ordinal);
    }

    [Fact]
    public void CustomPropertyEditing_PreservesRawSizeUntilEdited()
    {
        var source = new PropertiesCollection { ["Size"] = 1536L };
        var vm = new PropertiesDialogViewModel("key", "name", "icon", "", source);
        vm.ApplyCustomPropertyDefaults([new CustomPropertyDocument { Name = "Size", DefaultValue = "default" }]);

        Assert.Equal("1536", vm.CustomProperties[0].DefaultValue);
        Assert.Equal(1536L, vm.GetProperties()["Size"]);
        vm.CustomProperties[0].DefaultValue = "2048";
        Assert.Equal("2048", vm.GetProperties()["Size"]);
        Assert.Equal(1536L, source["Size"]);
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
    [InlineData("2016-06-05T16:49:14.9800000", "en-US")]
    [InlineData("2016-06-05T16:49:14", "lt-LT")]
    public void Constructor_FormatsOffsetlessTimestampsWithoutShifting(string value, string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var expected = new DateTime(2016, 6, 5, 16, 49, 14).ToString("G", culture);
        var vm = CreateWithCulture(value, culture);

        Assert.Equal(expected, vm.InfoProperties["Value"]);
        Assert.Equal(value, vm.GetProperties()["Value"]);
    }

    [Fact]
    public void Constructor_RespectsCultureSpecificSpacing()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone();
        culture.DateTimeFormat.ShortDatePattern = "M/d/yyyy";
        culture.DateTimeFormat.LongTimePattern = "h:mm:ss'\u202f'tt";

        var vm = CreateWithCulture("2016-06-05T16:49:14.9800000", culture);

        Assert.Equal("6/5/2016 4:49:14\u202fPM", vm.InfoProperties["Value"]);
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

    private static PropertiesDialogViewModel CreateWithCulture(object value, CultureInfo culture, string key = "Value")
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = culture;
            return new PropertiesDialogViewModel("key", "name", "icon", "", new PropertiesCollection { [key] = value });
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

    [Fact]
    public void Constructor_RendersAttributeMaskAsFlagNames()
    {
        var vm = CreateWithCulture("5242912", CultureInfo.GetCultureInfo("en"), "Attributes");

        Assert.Equal("Archive, Unpinned, Recall on data access", vm.InfoProperties["Attributes"]);
        Assert.Equal("5242912", vm.GetProperties()["Attributes"]);
    }

    [Fact]
    public void Constructor_LocalizesAttributeMaskForLithuanian()
    {
        var vm = CreateWithCulture("5242912", CultureInfo.GetCultureInfo("lt-LT"), "Attributes");

        Assert.Equal("Archyvinis, Neprisegtas, Atsisiunčiama skaitant duomenis", vm.InfoProperties["Attributes"]);
    }

    [Fact]
    public void Constructor_RendersNumericAttributeValueAsFlagNames()
    {
        var vm = CreateWithCulture(5242912, CultureInfo.GetCultureInfo("en"), "Attributes");

        Assert.Equal("Archive, Unpinned, Recall on data access", vm.InfoProperties["Attributes"]);
    }

    [Fact]
    public void Constructor_RendersZeroAttributesAsNone()
    {
        var vm = CreateWithCulture("0", CultureInfo.GetCultureInfo("en"), "Attributes");

        Assert.Equal("None", vm.InfoProperties["Attributes"]);
    }

    [Theory]
    [InlineData("ReadOnly, Archive")]
    [InlineData("Archive, ReparsePoint")]
    public void Constructor_LeavesReadableAttributeTextUnchanged(string stored)
    {
        var vm = CreateWithCulture(stored, CultureInfo.GetCultureInfo("en"), "Attributes");

        Assert.Equal(stored, vm.InfoProperties["Attributes"]);
    }

    [Fact]
    public void Confirm_KeepsStoredAttributeValueRaw()
    {
        var vm = CreateWithCulture("5242912", CultureInfo.GetCultureInfo("en"), "Attributes");

        vm.ConfirmCommand.Execute(null);

        Assert.True(vm.DialogAccepted);
        Assert.Equal("Archive, Unpinned, Recall on data access", vm.InfoProperties["Attributes"]);
        Assert.Equal("5242912", vm.GetProperties()["Attributes"]);
    }
}