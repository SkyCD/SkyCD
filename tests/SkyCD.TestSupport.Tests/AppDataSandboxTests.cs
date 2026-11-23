using System;
using System.IO;
using Xunit;

namespace SkyCD.TestSupport.Tests;

public sealed class AppDataSandboxTests
{
    private const string AppDataVariable = "APPDATA";
    private const string XdgConfigVariable = "XDG_CONFIG_HOME";
    private const string DatabaseVariable = "SKYCD_DATABASE_DIRECTORY";

    [Fact]
    public void Constructor_RedirectsAllThreeVariablesToTheSandbox()
    {
        var previousAppData = Environment.GetEnvironmentVariable(AppDataVariable);
        var previousXdgConfig = Environment.GetEnvironmentVariable(XdgConfigVariable);
        var previousDatabaseDirectory = Environment.GetEnvironmentVariable(DatabaseVariable);
        var sandbox = new AppDataSandbox();
        try
        {
            Assert.True(Directory.Exists(sandbox.Root));
            Assert.Equal(sandbox.Root, Environment.GetEnvironmentVariable(AppDataVariable));
            Assert.Equal(sandbox.Root, Environment.GetEnvironmentVariable(XdgConfigVariable));
            Assert.Equal(sandbox.DatabaseDirectory, Environment.GetEnvironmentVariable(DatabaseVariable));
        }
        finally
        {
            sandbox.Dispose();
            Restore(AppDataVariable, previousAppData);
            Restore(XdgConfigVariable, previousXdgConfig);
            Restore(DatabaseVariable, previousDatabaseDirectory);
        }
    }

    [Fact]
    public void DatabaseDirectory_IsSkyCdFolderInsideTheRoot()
    {
        var previousDatabaseDirectory = Environment.GetEnvironmentVariable(DatabaseVariable);
        var sandbox = new AppDataSandbox();
        try
        {
            Assert.Equal(Path.Combine(sandbox.Root, "SkyCD"), sandbox.DatabaseDirectory);
        }
        finally
        {
            sandbox.Dispose();
            Restore(DatabaseVariable, previousDatabaseDirectory);
        }
    }

    [Fact]
    public void Dispose_RestoresThePreviousValues()
    {
        var previousAppData = Environment.GetEnvironmentVariable(AppDataVariable);
        var previousXdgConfig = Environment.GetEnvironmentVariable(XdgConfigVariable);
        var previousDatabaseDirectory = Environment.GetEnvironmentVariable(DatabaseVariable);
        var dummyAppData = Path.Combine(Path.GetTempPath(), "skycd-sandbox-previous-appdata");
        var dummyXdgConfig = Path.Combine(Path.GetTempPath(), "skycd-sandbox-previous-xdg");
        var dummyDatabaseDirectory = Path.Combine(Path.GetTempPath(), "skycd-sandbox-previous-database");
        try
        {
            Environment.SetEnvironmentVariable(AppDataVariable, dummyAppData);
            Environment.SetEnvironmentVariable(XdgConfigVariable, dummyXdgConfig);
            Environment.SetEnvironmentVariable(DatabaseVariable, dummyDatabaseDirectory);

            var sandbox = new AppDataSandbox();
            Assert.NotEqual(dummyAppData, Environment.GetEnvironmentVariable(AppDataVariable));
            sandbox.Dispose();

            Assert.Equal(dummyAppData, Environment.GetEnvironmentVariable(AppDataVariable));
            Assert.Equal(dummyXdgConfig, Environment.GetEnvironmentVariable(XdgConfigVariable));
            Assert.Equal(dummyDatabaseDirectory, Environment.GetEnvironmentVariable(DatabaseVariable));
        }
        finally
        {
            Restore(AppDataVariable, previousAppData);
            Restore(XdgConfigVariable, previousXdgConfig);
            Restore(DatabaseVariable, previousDatabaseDirectory);
        }
    }

    [Fact]
    public void Dispose_DeletesTheRoot()
    {
        var previousAppData = Environment.GetEnvironmentVariable(AppDataVariable);
        var previousXdgConfig = Environment.GetEnvironmentVariable(XdgConfigVariable);
        var previousDatabaseDirectory = Environment.GetEnvironmentVariable(DatabaseVariable);
        var sandbox = new AppDataSandbox();
        string root;
        try
        {
            root = sandbox.Root;
            Directory.CreateDirectory(sandbox.DatabaseDirectory);
            File.WriteAllText(Path.Combine(root, "sample.json"), "{}");
        }
        finally
        {
            sandbox.Dispose();
            Restore(AppDataVariable, previousAppData);
            Restore(XdgConfigVariable, previousXdgConfig);
            Restore(DatabaseVariable, previousDatabaseDirectory);
        }

        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Dispose_Twice_IsSafeAndKeepsTheVariablesRestored()
    {
        var previousAppData = Environment.GetEnvironmentVariable(AppDataVariable);
        var previousXdgConfig = Environment.GetEnvironmentVariable(XdgConfigVariable);
        var previousDatabaseDirectory = Environment.GetEnvironmentVariable(DatabaseVariable);
        var sandbox = new AppDataSandbox();
        var root = sandbox.Root;
        try
        {
            sandbox.Dispose();
            sandbox.Dispose();

            Assert.False(Directory.Exists(root));
            Assert.Equal(previousAppData, Environment.GetEnvironmentVariable(AppDataVariable));
            Assert.Equal(previousXdgConfig, Environment.GetEnvironmentVariable(XdgConfigVariable));
            Assert.Equal(previousDatabaseDirectory, Environment.GetEnvironmentVariable(DatabaseVariable));
        }
        finally
        {
            Restore(AppDataVariable, previousAppData);
            Restore(XdgConfigVariable, previousXdgConfig);
            Restore(DatabaseVariable, previousDatabaseDirectory);
        }
    }

    private static void Restore(string name, string? value) => Environment.SetEnvironmentVariable(name, value);
}
