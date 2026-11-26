using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace SkyCD.Plugin.Feature.MotionDetector.Configuration;

/// <summary>English and Lithuanian strings for the motion detector plugin UI.</summary>
internal static class MotionDetectorTranslations
{
    private static readonly Dictionary<string, string> English = new()
    {
        ["ui.ProviderName"] = "Motion detector",
        ["ui.SourceKind.File"] = "File",
        ["ui.SourceKind.Device"] = "Local device",
        ["ui.SourceKind.JpegUrl"] = "JPEG URL",
        ["ui.SourceKind.MjpegUrl"] = "MJPEG URL",
        ["ui.SourceKind.MmsStream"] = "MMS stream",
        ["ui.Location.File"] = "Video file",
        ["ui.Location.Device"] = "Camera",
        ["ui.Location.Url"] = "Stream URL",
        ["ui.DeviceName"] = "Camera {0}",
        ["ui.OptionsPage"] = "Motion detector",
        ["ui.OptionsHint"] = "The selected detector applies to every motion source. Changing it restarts running sources.",
        ["ui.DetectorLabel"] = "Detector",
        ["ui.DetectorNone"] = "No detector (capture continuously)",
        ["ui.ParametersLabel"] = "Parameters",
        ["ui.Save"] = "Save",
        ["ui.Saved"] = "Motion detector settings saved. Running sources were restarted.",
        ["ui.RestartHint"] = "Changing the detector restarts every running motion source.",
        ["ui.CatalogUnavailable"] = "Open a catalog before configuring motion detection.",
        ["ui.NativeUnavailable"] = "The bundled OpenCV runtime could not be loaded on this platform.",
        ["ui.SourceDirectoryMissing"] = "The source folder '{0}' is not reachable.",
        ["ui.SourceFileMissing"] = "The selected video file no longer exists.",
        ["ui.SourceUrlInvalid"] = "Enter a valid absolute URL, including the scheme.",
        ["ui.SourceOpenFailed"] = "The source could not be opened. Check the device, file or stream URL.",
        ["ui.SourceStreamEnded"] = "The source stopped delivering frames and will be reopened.",
        ["ui.PreviewFailed"] = "A preview frame could not be captured within the time limit.",
        ["ui.DestinationRequired"] = "Choose an absolute physical folder for captured frames.",
        ["ui.DestinationMissing"] = "The destination folder '{0}' could not be used.",
        ["ui.CatalogFolderRequired"] = "Enter a catalog folder name.",
        ["ui.LabelRequired"] = "Enter a source name.",
        ["ui.SourceAdded"] = "Motion source '{0}' saved and started.",
        ["ui.SourceRemoved"] = "Motion source '{0}' removed.",
        ["ui.ImportFailed"] = "Captured files in '{0}' could not be added to the catalog.",
        ["ui.CaptureSuspended"] = "Capture is paused until a catalog is open.",
        ["ui.Detector.frame-difference.Name"] = "Frame difference",
        ["ui.Detector.frame-difference.Description"] =
            "Absolute per-pixel difference against the previous frame, thresholded into a binary mask.",
        ["ui.Detector.running-average.Name"] = "Running average",
        ["ui.Detector.running-average.Description"] =
            "Adaptive mean background; pixels that deviate from the running average count as motion.",
        ["ui.Detector.mog2.Name"] = "Mixture of Gaussians - MOG2",
        ["ui.Detector.mog2.Description"] =
            "OpenCV background subtractor MOG2: a per-pixel Gaussian mixture learned frame by frame.",
        ["ui.Detector.knn.Name"] = "Mixture of Gaussians - KNN",
        ["ui.Detector.knn.Description"] =
            "OpenCV background subtractor KNN: a K-nearest-neighbour background model.",
        ["ui.Detector.farneback.Name"] = "Optical flow - Farnebäck",
        ["ui.Detector.farneback.Description"] =
            "Dense Farnebäck optical flow; areas whose flow magnitude exceeds the threshold count as motion.",
        ["ui.Detector.edge-change.Name"] = "Edge change ratio",
        ["ui.Detector.edge-change.Description"] =
            "Canny edge change ratio between consecutive frames.",
        ["ui.Detector.perceptual-difference.Name"] = "Perceptual difference",
        ["ui.Detector.perceptual-difference.Description"] =
            "A 32x32 grayscale comparison, the cheapest detector for low-CPU cameras.",
        ["ui.Parameter.threshold"] = "Pixel threshold",
        ["ui.Parameter.minAreaPercent"] = "Minimum changed area (%)",
        ["ui.Parameter.learningRate"] = "Background learning rate",
        ["ui.Parameter.history"] = "History (frames)",
        ["ui.Parameter.varianceThreshold"] = "Variance threshold",
        ["ui.Parameter.distanceThreshold"] = "Distance threshold",
        ["ui.Parameter.magnitudeThreshold"] = "Flow magnitude threshold",
        ["ui.Parameter.cannyLow"] = "Canny low threshold",
        ["ui.Parameter.cannyHigh"] = "Canny high threshold",
        ["ui.Parameter.changePercent"] = "Minimum edge change (%)",
        ["ui.Parameter.differencePercent"] = "Minimum perceptual difference (%)"
    };

    private static readonly Dictionary<string, string> Lithuanian = new()
    {
        ["ui.ProviderName"] = "Judesio detektorius",
        ["ui.SourceKind.File"] = "Failas",
        ["ui.SourceKind.Device"] = "Vietinis įrenginys",
        ["ui.SourceKind.JpegUrl"] = "JPEG URL",
        ["ui.SourceKind.MjpegUrl"] = "MJPEG URL",
        ["ui.SourceKind.MmsStream"] = "MMS srautas",
        ["ui.Location.File"] = "Vaizdo failas",
        ["ui.Location.Device"] = "Kamera",
        ["ui.Location.Url"] = "Srauto URL",
        ["ui.DeviceName"] = "Kamera {0}",
        ["ui.OptionsPage"] = "Judesio detektorius",
        ["ui.OptionsHint"] =
            "Pasirinktas detektorius taikomas visiems judesio šaltiniams. Pakeitus jį, veikiantys šaltiniai paleidžiami iš naujo.",
        ["ui.DetectorLabel"] = "Detektorius",
        ["ui.DetectorNone"] = "Be detektoriaus (įrašoma nuolat)",
        ["ui.ParametersLabel"] = "Parametrai",
        ["ui.Save"] = "Išsaugoti",
        ["ui.Saved"] = "Judesio detektoriaus nuostatos išsaugotos. Veikiantys šaltiniai paleisti iš naujo.",
        ["ui.RestartHint"] = "Pakeitus detektorių, visi veikiantys judesio šaltiniai paleidžiami iš naujo.",
        ["ui.CatalogUnavailable"] = "Prieš konfigūruodami judesio aptikimą atverkite katalogą.",
        ["ui.NativeUnavailable"] = "Nepavyko įkelti kartu pateiktos OpenCV vykdymo aplinkos.",
        ["ui.SourceDirectoryMissing"] = "Šaltinio aplankas „{0}“ nepasiekiamas.",
        ["ui.SourceFileMissing"] = "Pasirinktas vaizdo failas jau neegzistuoja.",
        ["ui.SourceUrlInvalid"] = "Įveskite tinkamą absoliutų URL su schema.",
        ["ui.SourceOpenFailed"] = "Šaltinio atidaryti nepavyko. Patikrinkite įrenginį, failą arba srauto URL.",
        ["ui.SourceStreamEnded"] = "Šaltinis nustojo siųsti kadrus; jis bus atidarytas iš naujo.",
        ["ui.PreviewFailed"] = "Per skirtą laiką nepavyko gauti peržiūros kadro.",
        ["ui.DestinationRequired"] = "Pasirinkite absoliutų fizinį aplanką įrašomiems kadrams.",
        ["ui.DestinationMissing"] = "Paskirties aplanko „{0}“ naudoti nepavyko.",
        ["ui.CatalogFolderRequired"] = "Įveskite katalogo aplanko pavadinimą.",
        ["ui.LabelRequired"] = "Įveskite šaltinio pavadinimą.",
        ["ui.SourceAdded"] = "Judesio šaltinis „{0}“ išsaugotas ir paleistas.",
        ["ui.SourceRemoved"] = "Judesio šaltinis „{0}“ pašalintas.",
        ["ui.ImportFailed"] = "Nepavyko į katalogą įtraukti failų iš „{0}“.",
        ["ui.CaptureSuspended"] = "Įrašymas pristabdytas, kol nebus atvertas katalogas.",
        ["ui.Detector.frame-difference.Name"] = "Kadrų skirtumas",
        ["ui.Detector.frame-difference.Description"] =
            "Absoliutus pikselių skirtumas nuo ankstesnio kadro, slenkstinamas į dvejetainę kaukę.",
        ["ui.Detector.running-average.Name"] = "Slenkantis vidurkis",
        ["ui.Detector.running-average.Description"] =
            "Adaptyvus vidutinis fonas; judesiu laikomi pikseliai, nukrypę nuo slenkančio vidurkio.",
        ["ui.Detector.mog2.Name"] = "Gausų mišinys – MOG2",
        ["ui.Detector.mog2.Description"] =
            "OpenCV fono atėmiklis MOG2: kiekvienam pikseliui mokomas Gauso mišinys.",
        ["ui.Detector.knn.Name"] = "Gausų mišinys – KNN",
        ["ui.Detector.knn.Description"] =
            "OpenCV fono atėmiklis KNN: artimiausių kaimynų fono modelis.",
        ["ui.Detector.farneback.Name"] = "Optinis srautas – Farnebäck",
        ["ui.Detector.farneback.Description"] =
            "Tankus Farnebäck optinis srautas; judesiu laikomos sritys, kurių srauto dydis viršija slenkstį.",
        ["ui.Detector.edge-change.Name"] = "Briaunų kaitos santykis",
        ["ui.Detector.edge-change.Description"] =
            "Canny briaunų kaitos santykis tarp gretimų kadrų.",
        ["ui.Detector.perceptual-difference.Name"] = "Suvokiamasis skirtumas",
        ["ui.Detector.perceptual-difference.Description"] =
            "32x32 pilkosios skalės palyginimas – pigiausias detektorius mažo našumo kameroms.",
        ["ui.Parameter.threshold"] = "Pikselių slenkstis",
        ["ui.Parameter.minAreaPercent"] = "Mažiausia pasikeitusi sritis (%)",
        ["ui.Parameter.learningRate"] = "Fono mokymosi sparta",
        ["ui.Parameter.history"] = "Istorija (kadrai)",
        ["ui.Parameter.varianceThreshold"] = "Dispersijos slenkstis",
        ["ui.Parameter.distanceThreshold"] = "Atstumo slenkstis",
        ["ui.Parameter.magnitudeThreshold"] = "Srauto dydžio slenkstis",
        ["ui.Parameter.cannyLow"] = "Canny žemasis slenkstis",
        ["ui.Parameter.cannyHigh"] = "Canny aukštasis slenkstis",
        ["ui.Parameter.changePercent"] = "Mažiausia briaunų kaita (%)",
        ["ui.Parameter.differencePercent"] = "Mažiausias suvokiamasis skirtumas (%)"
    };

    public static IReadOnlyDictionary<string, string> All { get; } = new Dictionary<string, string>
    {
        ["en"] = Serialize(English),
        ["lt"] = Serialize(Lithuanian)
    };

    private static string Serialize(Dictionary<string, string> values) =>
        new SerializerBuilder().Build().Serialize(values);
}
