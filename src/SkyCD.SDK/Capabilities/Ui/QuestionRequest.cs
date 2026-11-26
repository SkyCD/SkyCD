using System.Collections.Generic;

namespace SkyCD.SDK.Capabilities.Ui;

/// <summary>A localized question for a host-owned modal dialog.</summary>
public sealed record QuestionRequest(
    string Title,
    string Question,
    QuestionButtons Buttons = QuestionButtons.YesNo,
    IReadOnlyList<string>? Details = null);
