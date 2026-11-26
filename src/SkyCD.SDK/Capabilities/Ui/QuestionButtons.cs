namespace SkyCD.SDK.Capabilities.Ui;

/// <summary>The actions offered by a question dialog.</summary>
public enum QuestionButtons
{
    /// <summary>Offers OK.</summary>
    Ok,
    /// <summary>Offers OK and Cancel.</summary>
    OkCancel,
    /// <summary>Offers Yes and No.</summary>
    YesNo,
    /// <summary>Offers Yes, No and Cancel.</summary>
    YesNoCancel,
    /// <summary>Offers Save, Discard and Cancel.</summary>
    SaveDiscardCancel,

    /// <summary>Requires explicit opt-in to save when doing so would lose data.</summary>
    SaveAnywayCancel
}
