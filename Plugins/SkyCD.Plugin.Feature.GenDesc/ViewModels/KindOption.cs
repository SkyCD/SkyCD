namespace SkyCD.Plugin.Feature.GenDesc.ViewModels;

/// <summary>One entry of the template-kind picker; the value is the persisted kind, the label is localized.</summary>
public sealed record KindOption(string Value, string Label);
