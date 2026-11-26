namespace SkyCD.SDK.Capabilities.Ui;

/// <summary>The action selected in a question dialog. Closing the window returns Cancel.</summary>
public enum QuestionResult
{
    /// <summary>The question was canceled or dismissed.</summary>
    Cancel,
    /// <summary>The user acknowledged the question.</summary>
    Ok,
    /// <summary>The user answered yes.</summary>
    Yes,
    /// <summary>The user answered no.</summary>
    No,
    /// <summary>The user chose to save.</summary>
    Save,
    /// <summary>The user chose to discard changes.</summary>
    Discard
}
