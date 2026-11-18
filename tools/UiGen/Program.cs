using Ms2.UiGen;

// UiGen: builds the UI code assembly from a client's Data folder (UiGenerator.Build), as the game does on first launch.
// The output is made from the player's own data and is never committed.
//   UiGen --dll FILE [--out DIR] [--data DIR]    (MS2_DATA = the client's Data folder; --out also writes the C#)
string data = Environment.GetEnvironmentVariable("MS2_DATA"), outDir = null, dll = null;
for (int i = 0; i < args.Length; i++)
    switch (args[i])
    {
        case "--data": data = args[++i]; break;
        case "--out": outDir = args[++i]; break;
        case "--dll": dll = args[++i]; break;
    }
if (string.IsNullOrEmpty(data) || dll == null) { Console.Error.WriteLine("usage: UiGen --dll FILE [--out DIR] [--data DIR] (MS2_DATA)"); return 2; }
var r = UiGenerator.Build(data, dll, outDir);
Console.WriteLine($"movies {r.Movies}, with code {r.MoviesWithCode}; ABC blocks {r.Blocks}; method bodies {r.Bodies} translated, {r.Skipped} skipped; {r.Distinct} distinct");
if (r.Unsupported.Count > 0) Console.WriteLine("unsupported: " + string.Join(", ", r.Unsupported.Take(30)));
foreach (var e in r.Errors) Console.WriteLine("  " + e);
Console.WriteLine($"read {r.ReadSeconds:F1} s, translate {r.TranslateSeconds:F1} s, source {r.SourceBytes / 1048576.0:F1} MB in {r.BodyFiles + 1} files");
Console.WriteLine($"compile {r.CompileSeconds:F1} s: body files {r.BodyFiles - r.BadBodyFiles}/{r.BodyFiles} compile as-is; " +
                  $"movies with code whose bodies all compile {r.MoviesAllCompiled}/{r.MoviesWithCode}; dll {r.DllBytes / 1048576.0:F2} MB");
if (r.FailedMovies.Count > 0) Console.WriteLine("movies with bodies left to the interpreter: " + string.Join(", ", r.FailedMovies));
return r.Ok ? 0 : 1;
