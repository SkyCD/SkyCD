using System.Collections.Generic;

namespace SkyCD.Presentation.ViewModels;

public sealed record AboutDiagnosticGroup(string Title, IReadOnlyList<AboutDiagnosticFact> Facts)
{
    public static AboutDiagnosticGroup Empty { get; } = new(string.Empty, []);
}
