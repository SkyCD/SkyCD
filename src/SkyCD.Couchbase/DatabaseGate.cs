using System;
using System.Runtime.CompilerServices;
using Couchbase.Lite;

namespace SkyCD.Couchbase;

/// <summary>
/// Serializes work that Couchbase Lite cannot run at the same time on one database: an open batch
/// transaction and a query.
/// </summary>
/// <remarks>
/// Running a query while another thread holds <see cref="Database.InBatch(Action)"/> open deadlocks the
/// process — both threads block inside LiteCore forever, which is what stalled the test host in CI. Single
/// document reads and saves are safe alongside queries, so only batches and queries take this gate; every
/// such call site on the same database must go through it so the two never overlap.
///
/// <para>Every database gets its own gate. Entries are re-entrant, so repository calls made inside a gated
/// batch or query do not deadlock against the section that encloses them.</para>
/// </remarks>
public static class DatabaseGate
{
    private static readonly ConditionalWeakTable<Database, object> Gates = new();

    /// <summary>Runs <paramref name="action"/> with exclusive access to <paramref name="database"/>.</summary>
    public static void Run(Database database, Action action)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(action);

        lock (Gates.GetOrCreateValue(database))
        {
            action();
        }
    }

    /// <summary>Runs <paramref name="read"/> with exclusive access to <paramref name="database"/>.</summary>
    public static T Run<T>(Database database, Func<T> read)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(read);

        lock (Gates.GetOrCreateValue(database))
        {
            return read();
        }
    }

    /// <summary>Opens a batch transaction inside the gate, so no query can run while it is open.</summary>
    public static void InBatch(Database database, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Run(database, () => database.InBatch(action));
    }
}
