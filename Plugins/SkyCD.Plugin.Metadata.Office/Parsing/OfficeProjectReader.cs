using System;
using System.IO;
using System.Linq;
using System.Threading;
using OfficeIMO.Project;
using SkyCD.Plugin.Metadata.Office.Models;

namespace SkyCD.Plugin.Metadata.Office.Parsing;

/// <summary>
/// Reads the properties, the schedule window and the task tree size of a Project plan.
/// </summary>
/// <remarks>
/// Both stored forms are covered: the compound <c>.mpp</c> and <c>.mpt</c> files and the text interchange
/// format <c>.mpx</c>. Resource assignments, cost tables and timephased series are decoded by the library but
/// never published here, and the task counts come from the lists the load already built, so a plan with
/// thousands of tasks costs the same read as a small one.
/// </remarks>
internal static class OfficeProjectReader
{
    public static OfficeMetadata Read(
        string container,
        byte[] data,
        OfficePluginOptions options,
        string? extension,
        CancellationToken cancellationToken) =>
        OfficeProtection.Read(container,
            () => ReadOpened(container, data, options, extension, cancellationToken));

    private static OfficeMetadata ReadOpened(
        string container,
        byte[] data,
        OfficePluginOptions options,
        string? extension,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var stream = new MemoryStream(data, writable: false);
        using var document = ProjectDocument.Load(stream, OfficeLoadBounds.Plan(options));

        var protectedPlan = OfficeProtection.FromDiagnostics(
            container,
            OfficeGuard.Try(() => document.ReadDiagnostics?.Select(diagnostic => diagnostic.ToString())));

        var native = OfficeGuard.Try(() => document.NativeInfo);

        var metadata = new OfficeMetadata
        {
            Container = container,
            DocumentType = OfficeFamilyResolver.DocumentType(OfficeFamily.Plan),
            IsTemplate = OfficeFamilyResolver.IsTemplate(extension) ||
                native?.IsTemplate == true,
            Title = Title(document),
            Author = OfficeGuard.Try(() => OfficeText.Normalize(document.Author)),
            Subject = OfficeGuard.Try(() => OfficeText.Normalize(document.Subject)),
            Company = OfficeGuard.Try(() => OfficeText.Normalize(document.Company)),
            Manager = OfficeGuard.Try(() => OfficeText.Normalize(document.Manager)),
            ApplicationVersion = OfficeGuard.Try(() => OfficeText.Normalize(native?.ProducerVersion)),
            FormatProfile = OfficeGuard.Try(() => OfficeText.Normalize(native?.Generation)),
            PlanStartAt = OfficeGuard.TryValue(() => OfficeText.FromDateTime(document.Settings?.StartDate)),
            PlanFinishAt = OfficeGuard.TryValue(() => OfficeText.FromDateTime(document.Settings?.FinishDate)),
            TaskCount = OfficeGuard.TryValue(() => (long?)document.AllTasks.Count()),
            TopLevelTaskCount = OfficeGuard.TryCount(() => document.Tasks.Count)
        };

        return protectedPlan ?? metadata;
    }

    private static string? Title(ProjectDocument document) =>
        OfficeText.Normalize(OfficeGuard.Try(() => document.Title))
            ?? OfficeText.Normalize(OfficeGuard.Try(() => document.Name));
}
