using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SkyCD.Plugin.Abstractions.OAuth;

namespace SkyCD.Core.OAuth;

/// <summary>Non-synchronizing generic-password keys in the current user's macOS Keychain.</summary>
internal sealed class MacOSOAuthKeyStore : IOAuthKeyStore
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private static readonly Lazy<IntPtr> SecurityLibrary = new(() => NativeLibrary.Load(Security));
    private static readonly Lazy<IntPtr> FoundationLibrary = new(() => NativeLibrary.Load(CoreFoundation));

    public byte[]? Read(string keyId)
    {
        using var query = Query(keyId);
        query.Set("kSecReturnData", Constant(FoundationLibrary.Value, "kCFBooleanTrue"));
        query.Set("kSecMatchLimit", Constant(SecurityLibrary.Value, "kSecMatchLimitOne"));
        var status = SecItemCopyMatching(query.Handle, out var data);
        try
        {
            if (status == -25300) // errSecItemNotFound is the only missing-key result.
                return null;
            Check(status);
            if (data == IntPtr.Zero || CFDataGetLength(data) != 32)
                throw new OAuthException(OAuthFailure.SecureStorageUnavailable);
            var result = new byte[32];
            Marshal.Copy(CFDataGetBytePtr(data), result, 0, result.Length);
            return result;
        }
        finally { if (data != IntPtr.Zero) CFRelease(data); }
    }

    public void Create(string keyId, byte[] key)
    {
        using var query = Query(keyId);
        var data = CFDataCreate(IntPtr.Zero, key, key.Length);
        try
        {
            query.Set("kSecValueData", data);
            var status = SecItemAdd(query.Handle, IntPtr.Zero);
            if (status != -25299) // A concurrent creator won; never replace a key.
                Check(status);
        }
        finally { if (data != IntPtr.Zero) CFRelease(data); }
    }

    // Used only by native integration tests to remove their unique keyring entries.
    internal void Delete(string keyId)
    {
        using var query = Query(keyId);
        var status = SecItemDelete(query.Handle);
        if (status != -25300)
            Check(status);
    }

    private static Dictionary Query(string keyId)
    {
        if (!OperatingSystem.IsMacOS())
            throw new OAuthException(OAuthFailure.SecureStorageUnavailable);
        var query = new Dictionary();
        query.Set("kSecClass", Constant(SecurityLibrary.Value, "kSecClassGenericPassword"));
        query.SetString("kSecAttrService", "org.skycd.oauth.keys.v1");
        query.SetString("kSecAttrAccount", keyId);
        query.Set("kSecAttrSynchronizable", Constant(FoundationLibrary.Value, "kCFBooleanFalse"));
        // Never display an unexpected unlock/access prompt during token refresh.
        query.Set("kSecUseAuthenticationUI", Constant(SecurityLibrary.Value, "kSecUseAuthenticationUIFail"));
        return query;
    }

    private static void Check(int status)
    {
        if (status != 0)
            throw new OAuthException(OAuthFailure.SecureStorageUnavailable);
    }

    private static IntPtr Constant(IntPtr library, string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(library, name));

    private sealed class Dictionary : IDisposable
    {
        // No retain callbacks: owned CFStrings remain alive until after the dictionary is released.
        public IntPtr Handle { get; } = CFDictionaryCreateMutable(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
        private readonly List<IntPtr> _owned = [];

        public void Set(string name, IntPtr value) => CFDictionarySetValue(Handle, Constant(SecurityLibrary.Value, name), value);

        public void SetString(string name, string value)
        {
            var text = CFStringCreateWithCString(IntPtr.Zero, value, 0x08000100);
            _owned.Add(text);
            Set(name, text);
        }

        public void Dispose()
        {
            CFRelease(Handle);
            foreach (var value in _owned)
                CFRelease(value);
        }
    }

    [DllImport(Security)] private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);
    [DllImport(Security)] private static extern int SecItemAdd(IntPtr attributes, IntPtr result);
    [DllImport(Security)] private static extern int SecItemDelete(IntPtr query);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDictionaryCreateMutable(IntPtr allocator, nint capacity, IntPtr keyCallbacks, IntPtr valueCallbacks);
    [DllImport(CoreFoundation)] private static extern void CFDictionarySetValue(IntPtr dictionary, IntPtr key, IntPtr value);
    [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, uint encoding);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);
    [DllImport(CoreFoundation)] private static extern nint CFDataGetLength(IntPtr data);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDataGetBytePtr(IntPtr data);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr value);
}
