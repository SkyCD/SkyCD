using System;
using System.IO;
using System.Threading;
using OfficeIMO.IWork;
using SkyCD.Plugin.IWork.Exceptions;
using SkyCD.Plugin.IWork.Models;

namespace SkyCD.Plugin.IWork.Parsing;

/// <summary>
/// Opens the bounded OfficeIMO source reader over bytes the plugin already read through the host contract.
/// </summary>
/// <remarks>
/// A protected package is deliberately not parsed: protection encrypts the component archive, so the reader
/// would only find ciphertext, and no credential is requested, guessed or stored. The caller keeps the plain
/// metadata path for that case.
/// </remarks>
internal static class IWorkDocumentLoader
{
    public static IWorkSourceDocument? TryOpen(
        IWorkPackage package,
        byte[] data,
        IWorkPluginOptions options,
        CancellationToken cancellationToken)
    {
        if (package.IsPasswordProtected)
        {
            return null;
        }

        try
        {
            var loadOptions = new IWorkLoadOptions
            {
                MaxPackageBytes = options.MaxSourceBytes
            };

            return IWorkSourceDocument.Load(data, loadOptions, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or NotSupportedException or ArgumentException
                or InvalidOperationException)
        {
            throw new IWorkDecodeException("the package structure could not be parsed", exception);
        }
    }
}
