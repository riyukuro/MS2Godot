using NUnit.Framework;

// Fixtures run side by side (the tests inside one fixture stay in order). A fixture that changes process-wide state
// (environment variables, the PackSet counters) is marked [NonParallelizable] and runs alone.
[assembly: Parallelizable(ParallelScope.Fixtures)]
[assembly: LevelOfParallelism(8)]
