using Microsoft.Extensions.Logging;
using SkyCD.Logging.Helpers;

namespace SkyCD.Logging.Tests;

public sealed class LoggerCategoryTypeHelperTests
{
    [Fact]
    public void ResolveCategoryType_ReturnsObject_WhenCategoryCannotBeResolved()
    {
        var type = LoggerCategoryTypeHelper.ResolveCategoryType("SkyCD.Unknown.MissingType");

        Assert.Equal(typeof(object), type);
    }

    [Fact]
    public void ResolveCategoryType_ReturnsObject_WhenCategoryIsEmpty()
    {
        var type = LoggerCategoryTypeHelper.ResolveCategoryType(string.Empty);

        Assert.Equal(typeof(object), type);
    }

    [Fact]
    public void ResolveCategoryType_ReturnsRuntimeType_WhenCategoryExists()
    {
        var categoryName = typeof(LoggerCategoryTypeHelperTests).FullName!;

        var type = LoggerCategoryTypeHelper.ResolveCategoryType(categoryName);

        Assert.Equal(typeof(LoggerCategoryTypeHelperTests), type);
    }

    [Fact]
    public void CreateGenericLogger_CreatesClosedGenericLoggerUsingResolvedCategory()
    {
        var logger = LoggerCategoryTypeHelper.CreateGenericLogger<TestLogger<object>>(
            typeof(LoggerCategoryTypeHelperTests).FullName!,
            "payload");

        var categoryType = logger.GetType().GetGenericArguments().Single();
        Assert.Equal(typeof(LoggerCategoryTypeHelperTests), categoryType);
    }

    [Fact]
    public void CreateGenericLogger_UsesObjectFallback_WhenCategoryCannotBeResolved()
    {
        var logger = LoggerCategoryTypeHelper.CreateGenericLogger<TestLogger<object>>(
            "SkyCD.Unknown.MissingType",
            "payload");

        var categoryType = logger.GetType().GetGenericArguments().Single();
        Assert.Equal(typeof(object), categoryType);
    }

    private sealed class TestLogger<TCategoryName>(string payload) : ILogger<TCategoryName>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _ = payload;
        }
    }
}
