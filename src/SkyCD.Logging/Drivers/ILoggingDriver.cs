using Microsoft.Extensions.Logging;

namespace SkyCD.Logging.Drivers;

internal interface ILoggingDriver
{
    bool CanHandleCurrentPlatform { get; }

    void Configure(ILoggingBuilder builder, string subsystem);
}
