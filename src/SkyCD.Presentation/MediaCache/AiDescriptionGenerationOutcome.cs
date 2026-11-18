namespace SkyCD.Presentation.MediaCache;

/// <summary>Result of one explicit generation request; skipped reports an up-to-date description was reused.</summary>
public sealed record AiDescriptionGenerationOutcome(AiDescriptionDetails Details, bool Skipped);
