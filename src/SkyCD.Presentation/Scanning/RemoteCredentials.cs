namespace SkyCD.Presentation.Scanning;

public sealed class RemoteCredentials(
    string? username,
    string? password,
    bool anonymous,
    RemoteAuthMethod authMethod = RemoteAuthMethod.Password,
    string? keyFilePath = null,
    string? keyFilePassphrase = null)
{
    public string? Username { get; } = username;

    public string? Password { get; } = password;

    public bool Anonymous { get; } = anonymous;

    public RemoteAuthMethod AuthMethod { get; } = authMethod;

    public string? KeyFilePath { get; } = keyFilePath;

    public string? KeyFilePassphrase { get; } = keyFilePassphrase;

    public override string ToString() =>
        $"RemoteCredentials user={Username} anonymous={Anonymous} auth={AuthMethod}";
}
