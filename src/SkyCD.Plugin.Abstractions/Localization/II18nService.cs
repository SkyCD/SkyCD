namespace SkyCD.Plugin.Abstractions.Localization;

public interface II18nService
{
    string Get(string key);

    string Format(string key, params object[] args);
}

