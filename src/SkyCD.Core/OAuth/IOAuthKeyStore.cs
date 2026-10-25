namespace SkyCD.Core.OAuth;

/// <summary>OS-backed storage of encryption keys. Returned buffers belong to the caller.</summary>
internal interface IOAuthKeyStore
{
    byte[]? Read(string keyId);
    void Create(string keyId, byte[] key);
}
