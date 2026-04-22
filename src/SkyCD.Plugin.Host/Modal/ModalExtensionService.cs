using System.Collections.Concurrent;
using SkyCD.Plugin.Abstractions.Capabilities.Modal;

namespace SkyCD.Plugin.Host.Modal;

/// <summary>
///     Host facade for plugin modal registration and guarded modal execution.
/// </summary>
public sealed class ModalExtensionService(PluginCatalog pluginCatalog)
{
    private readonly ConcurrentDictionary<string, byte> _activeModalIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _blockingModalGate = new(1, 1);

    public IReadOnlyList<ModalRegistration> GetModalRegistrations()
    {
        return pluginCatalog.Plugins
            .SelectMany(plugin =>
                plugin.Capabilities.OfType<IModalPluginCapability>()
                    .SelectMany(capability =>
                        capability.GetModals().Select(modal => new ModalRegistration(
                            plugin.Plugin.Descriptor.Id,
                            modal.ModalId,
                            modal.Title,
                            modal.Width,
                            modal.Height,
                            modal.RequiredPermissions ?? [],
                            modal.IsBlocking,
                            modal.AllowReentry,
                            modal.InputContract?.TypeId,
                            modal.InputContract?.IsRequired ?? false,
                            modal.OutputContract?.TypeId,
                            modal.OutputContract?.IsRequired ?? false))))
            .OrderBy(modal => modal.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<ModalOpenResult> OpenAsync(
        ModalOpenRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var resolved = ResolveCapability(request.ModalId);
        var modal = resolved.Modal;

        var permissionError = ValidatePermissions(modal, request.GrantedPermissions);
        if (permissionError is not null)
            return new ModalOpenResult
            {
                Success = false,
                Error = permissionError
            };

        var inputError = ValidatePayload("Input", modal.InputContract, request.Input);
        if (inputError is not null)
            return new ModalOpenResult
            {
                Success = false,
                Error = inputError
            };

        var enteredBlockingGate = false;
        var addedToActive = _activeModalIds.TryAdd(modal.ModalId, 0);
        if (!addedToActive && !modal.AllowReentry)
            return new ModalOpenResult
            {
                Success = false,
                Error = $"Modal '{modal.ModalId}' is already active."
            };

        try
        {
            if (modal.IsBlocking)
            {
                await _blockingModalGate.WaitAsync(cancellationToken);
                enteredBlockingGate = true;
            }

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            ModalOpenResult result;
            try
            {
                result = await resolved.Capability.OpenModalAsync(request, linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested ||
                                                     cancellationToken.IsCancellationRequested)
            {
                return new ModalOpenResult
                {
                    Success = false,
                    Canceled = true,
                    Error = timeoutCts.IsCancellationRequested
                        ? $"Modal '{modal.ModalId}' timed out."
                        : $"Modal '{modal.ModalId}' canceled."
                };
            }
            catch (Exception exception)
            {
                return new ModalOpenResult
                {
                    Success = false,
                    Error = exception.Message
                };
            }

            var outputError = ValidatePayload("Output", modal.OutputContract, result.Output);
            if (outputError is not null)
                return new ModalOpenResult
                {
                    Success = false,
                    Error = outputError
                };

            return result;
        }
        finally
        {
            if (addedToActive) _activeModalIds.TryRemove(modal.ModalId, out _);

            if (enteredBlockingGate) _blockingModalGate.Release();
        }
    }

    private (IModalPluginCapability Capability, ModalDescriptor Modal) ResolveCapability(string modalId)
    {
        foreach (var capability in pluginCatalog.GetCapabilities<IModalPluginCapability>())
        {
            var modal = capability.GetModals()
                .FirstOrDefault(candidate =>
                    candidate.ModalId.Equals(modalId, StringComparison.OrdinalIgnoreCase));

            if (modal is not null) return (capability, modal);
        }

        throw new InvalidOperationException($"No plugin capability found for modal '{modalId}'.");
    }

    private static string? ValidatePermissions(ModalDescriptor modal, IReadOnlyCollection<string> grantedPermissions)
    {
        var requiredPermissions = modal.RequiredPermissions ?? [];
        var grantedSet = grantedPermissions.Count == 0
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(grantedPermissions, StringComparer.OrdinalIgnoreCase);

        var missing = requiredPermissions
            .Where(permission => !grantedSet.Contains(permission))
            .ToList();

        return missing.Count == 0
            ? null
            : $"Missing required permissions for modal '{modal.ModalId}': {string.Join(", ", missing)}.";
    }

    private static string? ValidatePayload(string kind, ModalPayloadContract? contract, ModalPayload? payload)
    {
        if (contract is null) return null;

        if (payload is null) return contract.IsRequired ? $"{kind} payload is required." : null;

        return payload.TypeId.Equals(contract.TypeId, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"{kind} payload type mismatch. Expected '{contract.TypeId}' but got '{payload.TypeId}'.";
    }
}