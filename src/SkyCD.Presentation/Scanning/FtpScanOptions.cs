using SkyCD.Presentation.Operations;

namespace SkyCD.Presentation.Scanning;

public sealed class FtpScanOptions
{
    public FtpScanOptions(
        string address,
        string? username,
        string? password,
        bool anonymous,
        string? mediaName,
        string? targetParentId,
        bool includeExtendedInfo)
    {
        Address = address;
        Username = username;
        Password = password;
        Anonymous = anonymous;
        MediaName = mediaName;
        TargetParentId = targetParentId;
        IncludeExtendedInfo = includeExtendedInfo;
    }

    public string Address { get; }

    public string? Username { get; }

    public string? Password { get; }

    public bool Anonymous { get; }

    public string? MediaName { get; }

    public string? TargetParentId { get; }

    public bool IncludeExtendedInfo { get; }

    public override string ToString()
    {
        string location;
        try
        {
            location = FtpLocation.Parse(Address).PublicUrl;
        }
        catch (System.Exception)
        {
            location = SecretSanitizer.Sanitize(Address);
        }

        return $"FTP {location} anonymous={Anonymous} extended={IncludeExtendedInfo}";
    }
}
