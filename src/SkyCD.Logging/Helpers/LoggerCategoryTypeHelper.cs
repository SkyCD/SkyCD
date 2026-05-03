using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;

namespace SkyCD.Logging.Helpers;

internal static class LoggerCategoryTypeHelper
{
    private static readonly ConcurrentDictionary<string, Type> CategoryTypes = new(StringComparer.Ordinal);

    public static Type ResolveCategoryType(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return typeof(object);
        }

        return CategoryTypes.GetOrAdd(categoryName, static name =>
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type? type;
                try
                {
                    type = assembly.GetType(name, throwOnError: false, ignoreCase: false);
                }
                catch (ReflectionTypeLoadException)
                {
                    continue;
                }

                if (type is not null)
                {
                    return type;
                }
            }

            return typeof(object);
        });
    }

    public static ILogger CreateGenericLogger<TLoggerTemplate>(string categoryName, params object?[] constructorArgs)
    {
        var categoryType = ResolveCategoryType(categoryName);
        var templateType = typeof(TLoggerTemplate);
        var openGenericLoggerType = templateType.IsGenericTypeDefinition
            ? templateType
            : templateType.GetGenericTypeDefinition();

        var loggerType = openGenericLoggerType.MakeGenericType(categoryType);
        return (ILogger)Activator.CreateInstance(loggerType, constructorArgs)!;
    }
}
