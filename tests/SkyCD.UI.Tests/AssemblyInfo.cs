using Xunit;

// Headless sessions share Avalonia's render loop, which belongs to one dispatcher thread.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
