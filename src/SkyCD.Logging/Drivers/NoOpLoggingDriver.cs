using Microsoft.Extensions.Logging;

namespace SkyCD.Logging.Drivers;

internal sealed class NoOpLoggingDriver : ILoggingDriver
{
    public bool CanHandleCurrentPlatform => true;

    public void Configure(ILoggingBuilder builder, string subsystem)
    {
    }
}
