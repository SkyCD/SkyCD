using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using SkyCD.Plugin.Ai.Description.Views;
using SkyCD.SDK.Capabilities.Configuration;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Ai.Description;

public sealed partial class AiDescriptionPlugin
{
    /// <summary>Configuration page that selects and manages the ACP agent.</summary>
    public const string AgentsConfigurationId = "acp";

    /// <inheritdoc />
    public IReadOnlyList<PluginConfigurationDescriptor> Configurations =>
        [new(AgentsConfigurationId, LocalizationService.Current.Get(UiKeys.AcpAgents), Order: -20)];

    /// <inheritdoc />
    public Task<PluginConfigurationPage> CreatePageAsync(PluginConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!request.ConfigurationId.Equals(AgentsConfigurationId, StringComparison.OrdinalIgnoreCase))
        {
            var error = $"Unsupported configuration '{request.ConfigurationId}'.";
            return Task.FromResult(new PluginConfigurationPage(
                () => new TextBlock
                {
                    Text = error,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                _ => Task.FromResult(new PluginConfigurationResult { Success = false, Error = error })));
        }

        var viewModel = new AcpAgentsPageViewModel(this, request.CurrentSettings);
        return Task.FromResult(new PluginConfigurationPage(
            () => new AcpAgentsView(viewModel),
            token =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(viewModel.Accept());
            },
            () => viewModel.Dispose()));
    }
}
