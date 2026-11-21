namespace SkyCD.Plugin.Librer.Models;

/// <summary>
/// Decoded Librer 10-bit entry flags.
/// </summary>
public readonly struct LibrerFlags
{
    public bool IsDir { get; }
    public bool IsFile { get; }
    public bool IsSymlink { get; }
    public bool IsBind { get; }
    public bool HasCd { get; }
    public bool HasFiles { get; }
    public bool CdOk { get; }
    public bool CdAborted { get; }
    public bool CdEmpty { get; }
    public bool Aux2 { get; }

    public LibrerFlags(int code)
    {
        IsDir = (code & 512) != 0;
        IsFile = (code & 256) != 0;
        IsSymlink = (code & 128) != 0;
        IsBind = (code & 64) != 0;
        HasCd = (code & 32) != 0;
        HasFiles = (code & 16) != 0;
        CdOk = (code & 8) != 0;
        CdAborted = (code & 4) != 0;
        CdEmpty = (code & 2) != 0;
        Aux2 = (code & 1) != 0;
    }
}
