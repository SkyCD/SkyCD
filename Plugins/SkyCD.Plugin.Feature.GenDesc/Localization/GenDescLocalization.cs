using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace SkyCD.Plugin.Feature.GenDesc.Localization;

/// <summary>English and Lithuanian catalogs for the GenDesc menu, dialog and Options page.</summary>
internal static class GenDescLocalization
{
    private static readonly Dictionary<string, string> English = new()
    {
        ["ui.GenerateNfo"] = "Generate .nfo...",
        ["ui.GenerateDescription"] = "Generate description...",
        ["ui.SaveNfoTitle"] = "Save .nfo file",
        ["ui.DescriptionTitle"] = "Generated description",
        ["ui.Copy"] = "Copy",
        ["ui.Close"] = "Close",
        ["ui.Copied"] = "Copied to clipboard.",
        ["ui.NfoSaved"] = "Wrote {0}.",
        ["ui.NoSelection"] = "Select one local file first.",
        ["ui.FileUnavailable"] = "The selected file is no longer available locally.",
        ["ui.HostUnavailable"] = "This host does not provide the required dialog.",
        ["ui.GenerationFailed"] = "Generation failed: {0}",
        ["ui.SyntaxFailed"] = "The template could not be rendered: {0}",
        ["ui.OptionsTitle"] = "GenDesc templates",
        ["ui.OptionsHint"] = "Templates generate .nfo files and site descriptions from metadata captured for a local file. Each template declares the output kind, torrent category, MIME types and extensions it covers.",
        ["ui.CategoriesHint"] = "movie, tv, music, audiobook, game, software, ebook, comic, course, image, font, rom, other",
        ["ui.NfoKind"] = "NFO",
        ["ui.DescriptionKind"] = "Description",
        ["ui.TemplateList"] = "Templates",
        ["ui.Id"] = "ID",
        ["ui.Category"] = "Category",
        ["ui.Language"] = "Language",
        ["ui.Site"] = "Site",
        ["ui.Version"] = "Version",
        ["ui.MimeTypes"] = "MIME types",
        ["ui.Extensions"] = "Extensions",
        ["ui.Body"] = "Template",
        ["ui.Highlighted"] = "Syntax",
        ["ui.Preview"] = "Preview",
        ["ui.Tokens"] = "Facts and functions",
        ["ui.InsertToken"] = "Insert",
        ["ui.New"] = "New",
        ["ui.Duplicate"] = "Duplicate",
        ["ui.Delete"] = "Delete",
        ["ui.Import"] = "Import...",
        ["ui.Export"] = "Export...",
        ["ui.Save"] = "Save",
        ["ui.Reload"] = "Reload",
        ["ui.Saved"] = "Template saved.",
        ["ui.Deleted"] = "Template deleted.",
        ["ui.Imported"] = "Template loaded from {0}.",
        ["ui.Exported"] = "Template exported to {0}.",
        ["ui.InvalidId"] = "The template ID must contain only letters, digits, dots, dashes and underscores.",
        ["ui.InvalidMime"] = "Every MIME type must look like type/subtype.",
        ["ui.InvalidExtension"] = "Every extension must look like .mkv.",
        ["ui.EmptyBody"] = "The template is empty.",
        ["ui.MissingSelection"] = "Select a template first.",
        ["ui.NoPicker"] = "This host does not provide file dialogs.",
        ["ui.LoadErrors"] = "{0} template file(s) could not be read.",
        ["ui.PreviewFailed"] = "Preview: {0}"
    };

    private static readonly Dictionary<string, string> Lithuanian = new()
    {
        ["ui.GenerateNfo"] = "Generuoti .nfo...",
        ["ui.GenerateDescription"] = "Generuoti aprašymą...",
        ["ui.SaveNfoTitle"] = "Išsaugoti .nfo failą",
        ["ui.DescriptionTitle"] = "Sugeneruotas aprašymas",
        ["ui.Copy"] = "Kopijuoti",
        ["ui.Close"] = "Uždaryti",
        ["ui.Copied"] = "Nukopijuota į iškarpinę.",
        ["ui.NfoSaved"] = "Įrašyta {0}.",
        ["ui.NoSelection"] = "Pirmiausia pasirinkite vieną vietinį failą.",
        ["ui.FileUnavailable"] = "Pasirinktas failas nebepasiekiamas vietoje.",
        ["ui.HostUnavailable"] = "Ši aplinka neteikia reikalingo dialogo.",
        ["ui.GenerationFailed"] = "Generavimas nepavyko: {0}",
        ["ui.SyntaxFailed"] = "Šablono atvaizduoti nepavyko: {0}",
        ["ui.OptionsTitle"] = "GenDesc šablonai",
        ["ui.OptionsHint"] = "Šablonai generuoja .nfo failus ir svetainių aprašymus iš vietinio failo metaduomenų. Kiekvienas šablonas nurodo išvesties tipą, torrento kategoriją, MIME tipus ir plėtinius.",
        ["ui.CategoriesHint"] = "movie, tv, music, audiobook, game, software, ebook, comic, course, image, font, rom, other",
        ["ui.NfoKind"] = "NFO",
        ["ui.DescriptionKind"] = "Aprašymas",
        ["ui.TemplateList"] = "Šablonai",
        ["ui.Id"] = "ID",
        ["ui.Category"] = "Kategorija",
        ["ui.Language"] = "Kalba",
        ["ui.Site"] = "Svetainė",
        ["ui.Version"] = "Versija",
        ["ui.MimeTypes"] = "MIME tipai",
        ["ui.Extensions"] = "Plėtiniai",
        ["ui.Body"] = "Šablonas",
        ["ui.Highlighted"] = "Sintaksė",
        ["ui.Preview"] = "Peržiūra",
        ["ui.Tokens"] = "Faktai ir funkcijos",
        ["ui.InsertToken"] = "Įterpti",
        ["ui.New"] = "Naujas",
        ["ui.Duplicate"] = "Kopijuoti",
        ["ui.Delete"] = "Pašalinti",
        ["ui.Import"] = "Importuoti...",
        ["ui.Export"] = "Eksportuoti...",
        ["ui.Save"] = "Išsaugoti",
        ["ui.Reload"] = "Įkelti iš naujo",
        ["ui.Saved"] = "Šablonas išsaugotas.",
        ["ui.Deleted"] = "Šablonas pašalintas.",
        ["ui.Imported"] = "Šablonas įkeltas iš {0}.",
        ["ui.Exported"] = "Šablonas eksportuotas į {0}.",
        ["ui.InvalidId"] = "Šablono ID gali turėti tik raides, skaitmenis, taškus, brūkšnelius ir pabraukimus.",
        ["ui.InvalidMime"] = "Kiekvienas MIME tipas turi būti tipo/potipio formos.",
        ["ui.InvalidExtension"] = "Kiekvienas plėtinys turi būti .mkv formos.",
        ["ui.EmptyBody"] = "Šablonas tuščias.",
        ["ui.MissingSelection"] = "Pirmiausia pasirinkite šabloną.",
        ["ui.NoPicker"] = "Ši aplinka neteikia failų dialogų.",
        ["ui.LoadErrors"] = "Nepavyko perskaityti {0} šablono failų.",
        ["ui.PreviewFailed"] = "Peržiūra: {0}"
    };

    public static IReadOnlyDictionary<string, string> Translations => new Dictionary<string, string>
    {
        ["en"] = Serialize(English),
        ["lt"] = Serialize(Lithuanian)
    };

    private static string Serialize(Dictionary<string, string> values) =>
        new SerializerBuilder().Build().Serialize(values);
}
