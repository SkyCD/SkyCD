using SkyCD.Documents;

namespace SkyCD.Presentation.ViewModels;

public sealed record CatalogFindResult(CatalogDocument Document, string FullPath, string Context);
