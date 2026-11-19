using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.App.Converters;

public sealed class AcpAgentSelectionConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, System.Type targetType, object? parameter, CultureInfo culture)
        => values.Count == 2 && values[0] is AcpAgentChoice { IsInstalled: true } agent && Equals(agent, values[1]);
}
