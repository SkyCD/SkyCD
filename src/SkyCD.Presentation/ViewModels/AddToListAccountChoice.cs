using System.Collections.Generic;
using SkyCD.Presentation.Accounts;
using SkyCD.SDK.Capabilities.Accounts;

namespace SkyCD.Presentation.ViewModels;

/// <summary>A connected account offered as an Add To List source, with its previewed products.</summary>
public sealed class AddToListAccountChoice
{
    public AddToListAccountChoice(IAccountSession session, AccountSessionStatus status, AccountFolderPlan plan)
    {
        Session = session;
        Status = status;
        Plan = plan;
    }

    public IAccountSession Session { get; }

    public AccountSessionStatus Status { get; }

    public AccountFolderPlan Plan { get; }

    /// <summary>The account folder name, so the picker previews what the add creates.</summary>
    public string Name => Plan.FolderName;

    public IReadOnlyList<AccountEntryPlan> Products => Plan.Entries;
}
