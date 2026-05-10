using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace SkyCD.Cli.Extensions;

internal static class TextWriterJsonExtensions
{
    internal static Task WriteJsonAsync(this TextWriter writer, object? value, JsonSerializerOptions? options = null)
    {
        return writer.WriteLineAsync(JsonSerializer.Serialize(value, options));
    }
}