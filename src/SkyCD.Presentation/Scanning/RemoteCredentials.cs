namespace SkyCD.Presentation.Scanning;

public sealed class RemoteCredentials(string? username, string? password, bool anonymous)
{
    public string? Username { get; } = username;

    public string? Password { get; } = password;

    public bool Anonymous { get; } = anonymous;
}
