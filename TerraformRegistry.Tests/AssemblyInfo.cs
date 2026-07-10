using Xunit;

// Integration tests create disposable Docker resources and in-process hosts.
// Serializing the assembly prevents startup and port races from masking the
// behavior under test in CI.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
