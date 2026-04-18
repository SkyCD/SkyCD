using SkyCD.Migration.Cli;

const string usage = """
SkyCD Migration CLI

Usage:
  dotnet run --project tools/SkyCD.Migration.Cli -- --legacy-db <path> --target-db <path> [--dry-run]
""";

var argsMap = ParseArgs(args);

if (!argsMap.TryGetValue("--legacy-db", out var legacyPath) ||
    !argsMap.TryGetValue("--target-db", out var targetPath))
{
    Console.WriteLine(usage);
    return 1;
}

var dryRun = argsMap.ContainsKey("--dry-run");

if (!File.Exists(legacyPath))
{
    Console.Error.WriteLine($"Legacy DB not found: {legacyPath}");
    return 2;
}

var importer = new LegacyDbImporter();
var result = await importer.ImportAsync(legacyPath, targetPath, dryRun);

if (result.Errors.Count > 0)
{
    Console.Error.WriteLine("Migration completed with validation/import errors:");
    foreach (var error in result.Errors)
    {
        Console.Error.WriteLine($"  - {error}");
    }
}

Console.WriteLine(dryRun
    ? $"Dry-run complete. Prepared {result.ImportedCatalogs} catalogs and {result.ImportedNodes} nodes."
    : $"Import complete. Imported {result.ImportedCatalogs} catalogs and {result.ImportedNodes} nodes.");

return 0;

static Dictionary<string, string> ParseArgs(string[] args)
{
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        var key = args[i];
        if (!key.StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            map[key] = args[i + 1];
            i++;
        }
        else
        {
            map[key] = "true";
        }
    }

    return map;
}
