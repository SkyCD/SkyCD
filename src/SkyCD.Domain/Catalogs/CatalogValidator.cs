namespace SkyCD.Domain.Catalogs;

public static class CatalogValidator
{
    public static IReadOnlyList<string> Validate(Catalog catalog)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(catalog.Name)) errors.Add("Catalog.Name is required.");

        if (catalog.SchemaVersion <= 0) errors.Add("Catalog.SchemaVersion must be greater than zero.");

        foreach (var node in catalog.Nodes) ValidateNode(node, errors);

        return errors;
    }

    private static void ValidateNode(CatalogNode node, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(node.Name)) errors.Add($"CatalogNode[{node.Id}].Name is required.");

        if (node.Kind == CatalogNodeKind.File && node.SizeBytes is < 0)
            errors.Add($"CatalogNode[{node.Id}].SizeBytes cannot be negative for file nodes.");

        if (node.Kind == CatalogNodeKind.Folder && node.SizeBytes is not null)
            errors.Add($"CatalogNode[{node.Id}].SizeBytes must be null for folder nodes.");
    }
}