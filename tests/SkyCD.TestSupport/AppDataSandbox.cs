using System;
using System.IO;
using System.Security;

namespace SkyCD.TestSupport;

/// <summary>
/// Redirects the process-wide application-data locations (<c>APPDATA</c>, <c>XDG_CONFIG_HOME</c> and
/// <c>SKYCD_DATABASE_DIRECTORY</c>) into a throwaway <see cref="TestWorkspace"/> directory and restores
/// the previous values on <see cref="Dispose"/>.
/// </summary>
/// <remarks>
/// The redirection is process-global: two live sandboxes overwrite each other, and any test that reads
/// those variables while a sandbox is active sees the redirected values. Tests must therefore not run in
/// parallel while a sandbox is active; SkyCD.App.Tests and SkyCD.UI.Tests disable test parallelization
/// for this reason. Keep the sandbox alive for exactly as long as the redirected state is needed.
/// </remarks>
public sealed class AppDataSandbox : IDisposable
{
    private const string AppDataVariable = "APPDATA";
    private const string XdgConfigVariable = "XDG_CONFIG_HOME";
    private const string DatabaseVariable = "SKYCD_DATABASE_DIRECTORY";

    private readonly TestWorkspace workspace;
    private readonly string? previousAppData;
    private readonly string? previousXdgConfig;
    private readonly string? previousDatabaseDirectory;
    private bool disposed;

    /// <summary>
    /// Captures the current variable values, redirects them into a fresh workspace and creates the root.
    /// If any step fails, the variables already changed are restored and the workspace is deleted before
    /// the exception is rethrown, so a failed constructor leaves no redirected state behind.
    /// </summary>
    public AppDataSandbox()
    {
        previousAppData = Environment.GetEnvironmentVariable(AppDataVariable);
        previousXdgConfig = Environment.GetEnvironmentVariable(XdgConfigVariable);
        previousDatabaseDirectory = Environment.GetEnvironmentVariable(DatabaseVariable);

        workspace = new TestWorkspace();
        string root;
        string databaseDirectory;
        try
        {
            root = workspace.Root;
            databaseDirectory = Path.Combine(root, "SkyCD");
            Environment.SetEnvironmentVariable(AppDataVariable, root);
            Environment.SetEnvironmentVariable(XdgConfigVariable, root);
            Environment.SetEnvironmentVariable(DatabaseVariable, databaseDirectory);
        }
        catch
        {
            RestoreEnvironment();
            workspace.Dispose();
            throw;
        }

        Root = root;
        DatabaseDirectory = databaseDirectory;
    }

    /// <summary>Absolute path of the app-data root; the directory exists for as long as the sandbox is alive.</summary>
    public string Root { get; }

    /// <summary>Absolute path of the catalog database directory used by the sandbox.</summary>
    public string DatabaseDirectory { get; }

    /// <summary>Restores the three environment variables and deletes the sandbox tree; safe to call more than once.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        RestoreEnvironment();
        workspace.Dispose();
    }

    private void RestoreEnvironment()
    {
        TryRestore(AppDataVariable, previousAppData);
        TryRestore(XdgConfigVariable, previousXdgConfig);
        TryRestore(DatabaseVariable, previousDatabaseDirectory);
    }

    private static void TryRestore(string name, string? value)
    {
        try
        {
            Environment.SetEnvironmentVariable(name, value);
        }
        catch (Exception exception) when (exception is ArgumentException or SecurityException)
        {
            // Restoring one variable must not stop the remaining ones from being restored.
        }
    }
}
