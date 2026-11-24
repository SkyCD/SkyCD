namespace SkyCD.Presentation.ViewModels;

/// <summary>A source resolved from an OS drag-and-drop payload, ready to prefill the Add To List dialog.</summary>
public sealed record DroppedSource(AddToListSourceMode Mode, string Value, string? MediaName);
