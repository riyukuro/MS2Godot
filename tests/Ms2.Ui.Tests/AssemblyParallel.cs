using NUnit.Framework;

// Fixtures run side by side (the tests inside one fixture stay in order). A fixture that sets a process-wide hook or
// environment variable the product reads is marked [NonParallelizable] and runs alone.
[assembly: Parallelizable(ParallelScope.Fixtures)]
[assembly: LevelOfParallelism(6)]
