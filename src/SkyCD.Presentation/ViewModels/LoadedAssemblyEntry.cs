namespace SkyCD.Presentation.ViewModels;

public sealed record LoadedAssemblyEntry(
    string Name,
    string Version,
    string Copyright,
    string RepositoryUrl)
{
    public bool HasCopyright => !string.IsNullOrWhiteSpace(Copyright);

    public bool HasRepositoryUrl => !string.IsNullOrWhiteSpace(RepositoryUrl);
}
