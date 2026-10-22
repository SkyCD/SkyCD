using Xunit;

// CLI commands capture process-wide console streams, so test classes must not overlap.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
