using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace SkyCD.Plugin.Feature.Split.Localization;

/// <summary>English and Lithuanian catalogs for the Split menu entry and dialog.</summary>
internal static class SplitLocalization
{
    private static readonly Dictionary<string, string> English = new()
    {
        ["ui.MenuTitle"] = "S_plit...",
        ["ui.DialogTitle"] = "Split",
        ["ui.SourceCatalog"] = "Source catalog",
        ["ui.EntrySummary"] = "{0} entries",
        ["ui.OutputFolder"] = "Output folder",
        ["ui.FileNamePattern"] = "Part file name",
        ["ui.PatternHint"] = "Tokens: #N or {n} is the part number, {total} the part count, {source} the catalog name.",
        ["ui.EntriesPerPart"] = "Maximum entries per part",
        ["ui.PartSize"] = "Maximum part size",
        ["ui.SavingFormat"] = "Saving format",
        ["ui.Action"] = "Split",
        ["ui.NoEntries"] = "The open catalog has no entries to split.",
        ["ui.NoOpenCatalog"] = "Split needs an open catalog. Open one to split it.",
        ["ui.NoWritableFormat"] = "No catalog format that can be written and reopened is available. Check plugin settings.",
        ["ui.HostUnavailable"] = "Split needs a host window and the host save service, which this host does not provide.",
        ["ui.EntriesRequired"] = "Enter the maximum entries per part as a positive whole number.",
        ["ui.EntriesInvalid"] = "The maximum entries per part must be a positive whole number, not '{0}'.",
        ["ui.SizeInvalid"] = "The part size must be a positive number, not '{0}'.",
        ["ui.SizeUnitRequired"] = "Choose the unit of the part size.",
        ["ui.SizeTooLarge"] = "The part size is too large.",
        ["ui.OutputFolderRequired"] = "Choose the output folder the parts are written to.",
        ["ui.PatternExtensionMismatch"] = "The pattern extension '{0}' does not match the '{1}' format. Use one of: {2}.",
        ["ui.ProgressStatus"] = "Wrote part {0} of {1} ({2} of {3} entries).",
        ["ui.Outcome"] = "Split '{0}' ({1} entries) into {2} part file(s) in '{3}'.",
        ["ui.Cancelled"] = "Split cancelled. No part files were kept.",
        ["ui.Failed"] = "Split failed. No part files were kept."
    };

    private static readonly Dictionary<string, string> Lithuanian = new()
    {
        ["ui.MenuTitle"] = "S_kaidyti...",
        ["ui.DialogTitle"] = "Skaidymas",
        ["ui.SourceCatalog"] = "Šaltinio katalogas",
        ["ui.EntrySummary"] = "{0} elementų",
        ["ui.OutputFolder"] = "Išvesties aplankas",
        ["ui.FileNamePattern"] = "Dalies failo pavadinimas",
        ["ui.PatternHint"] = "Žymos: #N arba {n} – dalies numeris, {total} – dalių skaičius, {source} – katalogo pavadinimas.",
        ["ui.EntriesPerPart"] = "Didžiausias elementų skaičius dalyje",
        ["ui.PartSize"] = "Didžiausias dalies dydis",
        ["ui.SavingFormat"] = "Įrašymo formatas",
        ["ui.Action"] = "Skaidyti",
        ["ui.NoEntries"] = "Atvirame kataloge nėra elementų, kuriuos būtų galima išskaidyti.",
        ["ui.NoOpenCatalog"] = "Skaidymui reikia atidaryto katalogo. Atidarykite jį, kad galėtumėte išskaidyti.",
        ["ui.NoWritableFormat"] = "Nėra katalogo formato, kurį būtų galima įrašyti ir vėl atidaryti. Patikrinkite papildinių nuostatas.",
        ["ui.HostUnavailable"] = "Skaidymui reikia pagrindinės programos lango ir įrašymo paslaugos, kurių ši aplinka neteikia.",
        ["ui.EntriesRequired"] = "Įveskite didžiausią elementų skaičių dalyje kaip teigiamą sveikąjį skaičių.",
        ["ui.EntriesInvalid"] = "Didžiausias elementų skaičius dalyje turi būti teigiamas sveikasis skaičius, o ne '{0}'.",
        ["ui.SizeInvalid"] = "Dalies dydis turi būti teigiamas skaičius, o ne '{0}'.",
        ["ui.SizeUnitRequired"] = "Pasirinkite dalies dydžio matavimo vienetą.",
        ["ui.SizeTooLarge"] = "Dalies dydis per didelis.",
        ["ui.OutputFolderRequired"] = "Pasirinkite išvesties aplanką, į kurį bus įrašytos dalys.",
        ["ui.PatternExtensionMismatch"] = "Šablono plėtinys '{0}' neatitinka '{1}' formato. Naudokite vieną iš: {2}.",
        ["ui.ProgressStatus"] = "Įrašyta {0} dalis iš {1} ({2} iš {3} elementų).",
        ["ui.Outcome"] = "Katalogas '{0}' ({1} elementų) išskaidytas į {2} dalies failus aplanke '{3}'.",
        ["ui.Cancelled"] = "Skaidymas atšauktas. Jokie dalies failai nebuvo palikti.",
        ["ui.Failed"] = "Skaidymas nepavyko. Jokie dalies failai nebuvo palikti."
    };

    public static IReadOnlyDictionary<string, string> Translations => new Dictionary<string, string>
    {
        ["en"] = Serialize(English),
        ["lt"] = Serialize(Lithuanian)
    };

    private static string Serialize(Dictionary<string, string> values) =>
        new SerializerBuilder().Build().Serialize(values);
}
