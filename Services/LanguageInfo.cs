namespace SkyCD.Services
{
    public class LanguageInfo
    {
        // Language code, e.g. "en"
        public string Code { get; set; } = string.Empty;

        // Display name, e.g. "English"
        public string Name { get; set; } = string.Empty;

        // Optional flag emoji or path
        public string? FlagPath { get; set; }

        public override string ToString()
        {
            if (string.IsNullOrEmpty(Name))
                return Code;

            if (string.IsNullOrEmpty(FlagPath))
                return Name;

            return FlagPath + " " + Name;
        }
    }
}
