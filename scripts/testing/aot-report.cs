#!/usr/bin/env dotnet
#:package CliWrap@3.8.2
#:package Spectre.Console@0.55.2
#:package System.CommandLine@2.0.8

// aot-report.cs — Generate an AOT/trim warnings report for the Kurrent SDK.
// C# file-based app using CliWrap, Spectre.Console, and System.CommandLine.
//
// Builds every project marked <IsAotCompatible>true> with --no-incremental — the only
// way to force the trim/AOT analyzers to re-run (an up-to-date build skips compilation,
// so the analyzers stay silent and the report comes back empty). It then parses the
// IL#### warnings (IL2xxx = trimming, IL3xxx = AOT/single-file — the whole scheme; nothing
// AOT-related lives outside the IL prefix) and writes a grouped markdown report.
//
// Scope is self-limiting: the analyzers only run on projects that opt in via
// IsAotCompatible/IsTrimmable, so non-opted projects (tests, generators, playground)
// emit zero IL warnings even if they were built.

using System.CommandLine;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CliWrap;
using CliWrap.EventStream;
using Spectre.Console;

var repoRoot = FindRepoRoot(Environment.CurrentDirectory);

// =============================================================================
// CLI
// =============================================================================

var outputOpt = new Option<string?>("--output") { Description = "Markdown output path (default: .artifacts/aot-report.md)" };
var stdoutOpt = new Option<bool>("--stdout") { Description = "Print the report to stdout instead of writing a file" };

var rootCmd = new RootCommand("Generate an AOT/trim warnings report for the Kurrent SDK") { outputOpt, stdoutOpt };

rootCmd.SetAction(async (result, _) => {
    Environment.ExitCode = await GenerateReport(
        result.GetValue(outputOpt),
        result.GetValue(stdoutOpt));
});

return await rootCmd.Parse(args).InvokeAsync();

// =============================================================================
// Report
// =============================================================================

async Task<int> GenerateReport(string? outputPath, bool toStdout) {
    var projects = DiscoverAotProjects(repoRoot);

    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[bold blue]AOT warnings report[/]").LeftJustified());
    AnsiConsole.WriteLine();

    if (projects.Count == 0) {
        AnsiConsole.MarkupLine("[yellow]No projects with <IsAotCompatible>true> found under src/.[/]");
        return 0;
    }

    // --- Build each AOT project with --no-incremental, collecting all output ---
    var rawOutput = new StringBuilder();
    var buildFailed = false;

    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .SpinnerStyle(new Style(Color.Blue))
        .StartAsync("[blue]Building...[/]", async ctx => {
            foreach (var project in projects) {
                var name = Path.GetFileNameWithoutExtension(project);
                ctx.Status($"[blue]Building [bold]{Markup.Escape(name)}[/] (--no-incremental)...[/]");

                var cmd = Cli.Wrap("dotnet")
                    .WithArguments(["build", project, "-c", "Release", "--no-incremental"])
                    .WithWorkingDirectory(repoRoot)
                    .WithValidation(CommandResultValidation.None);

                await foreach (var evt in cmd.ListenAsync()) {
                    switch (evt) {
                        case StandardOutputCommandEvent { Text: var line }:
                            rawOutput.AppendLine(line);
                            break;
                        case StandardErrorCommandEvent { Text: var err }:
                            rawOutput.AppendLine(err);
                            break;
                        case ExitedCommandEvent exited:
                            // Warnings keep exit code 0; a non-zero code means real build errors.
                            if (exited.ExitCode != 0) buildFailed = true;
                            break;
                    }
                }
            }
        });

    // --- Parse + dedup IL warnings ---
    var warnings = ParseWarnings(rawOutput.ToString(), repoRoot);
    var versions = LoadPackageVersions(projects);
    var ownCount = warnings.Count(w => w.File is not null);
    var refCount = warnings.Count - ownCount;

    if (buildFailed)
        AnsiConsole.MarkupLine("[red]⚠ one or more projects reported build errors — the warning list may be incomplete[/]\n");

    // --- Console summary ---
    var table = new Table()
        .Border(TableBorder.Rounded)
        .BorderColor(Color.Grey)
        .AddColumn(new TableColumn("[dim]Code[/]"))
        .AddColumn(new TableColumn("[dim]Count[/]").RightAligned())
        .AddColumn(new TableColumn("[dim]Meaning[/]"));

    foreach (var g in warnings.GroupBy(w => w.Code).OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
        table.AddRow(Markup.Escape(g.Key), g.Count().ToString(), Markup.Escape(CodeMeaning(g.Key)));

    AnsiConsole.Write(table);
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine($"[bold]{warnings.Count}[/] AOT warnings [dim]·[/] [yellow]{ownCount}[/] own-code [dim]·[/] [dim]{refCount} reference (IL3058)[/]");
    AnsiConsole.WriteLine();

    // --- Write or print the markdown report ---
    // Resolve the output location first so file links can be made relative to it.
    if (toStdout) {
        // No file location for stdout — link files relative to the repo root instead.
        Console.WriteLine(GenerateMarkdown(warnings, projects, versions, buildFailed, repoRoot, repoRoot));
        return 0;
    }

    var outPath = outputPath is not null
        ? Path.GetFullPath(outputPath)
        : Path.Combine(repoRoot, ".artifacts", "aot-report.md");
    var outDir = Path.GetDirectoryName(outPath)!;

    Directory.CreateDirectory(outDir);
    await File.WriteAllTextAsync(outPath, GenerateMarkdown(warnings, projects, versions, buildFailed, outDir, repoRoot));
    AnsiConsole.MarkupLine($"[green]✓[/] report written to [bold]{Markup.Escape(RelPath(outPath, repoRoot))}[/]");

    return 0;
}

// =============================================================================
// Markdown
// =============================================================================

static string GenerateMarkdown(IReadOnlyList<AotWarning> warnings, IReadOnlyList<string> projects, IReadOnlyDictionary<string, string> versions, bool buildFailed, string linkBaseDir, string repoRoot) {
    var sb = new StringBuilder();
    var names = projects.Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToList();

    sb.AppendLine($"# {SolutionTitle(repoRoot)} — AOT Compatibility Report");
    sb.AppendLine();
    sb.AppendLine($"*generated {DateTime.Now:yyyy-MM-dd HH:mm}*");
    sb.AppendLine();
    if (buildFailed) {
        sb.AppendLine("> ⚠️ One or more projects reported build **errors** — the warning list below may be incomplete.");
        sb.AppendLine();
    }

    // === TL;DR — totals + per-project table up front; the code legend lives at the end ===
    sb.AppendLine("## Summary");
    sb.AppendLine();
    var ownTotal = warnings.Count(w => w.File is not null);
    var refTotal = warnings.Count - ownTotal;
    sb.AppendLine(warnings.Count == 0
        ? $"✅ **No AOT warnings** across {names.Count} projects."
        : $"⚠️ **{warnings.Count} warnings** across {names.Count} projects (**{ownTotal}** own code / **{refTotal}** packages)");
    sb.AppendLine();
    sb.AppendLine("| Project | Own code | Packages | Total |");
    sb.AppendLine("|---------|---------:|---------:|------:|");
    foreach (var name in names) {
        var own = warnings.Count(w => w.Project == name && w.File is not null);
        var refs = warnings.Count(w => w.Project == name && w.File is null);
        var status = (own + refs) == 0 ? "✅" : "⚠️";
        sb.AppendLine($"| {status} {name} | {own} | {refs} | {own + refs} |");
    }
    sb.AppendLine();

    // === Per-project detail ===
    foreach (var name in names) {
        var projWarnings = warnings.Where(w => w.Project == name).ToList();
        sb.AppendLine($"## {(projWarnings.Count == 0 ? "✅" : "⚠️")} {name}");
        sb.AppendLine();

        if (projWarnings.Count == 0) {
            sb.AppendLine("**AOT-clean** — no warnings.");
            sb.AppendLine();
            continue;
        }

        var ownCode = projWarnings.Where(w => w.File is not null).ToList();
        var references = projWarnings.Where(w => w.File is null).ToList();

        // Own-code — actionable in our source.
        sb.AppendLine($"### Own code ({ownCode.Count})");
        sb.AppendLine();
        if (ownCode.Count == 0) {
            sb.AppendLine("✅ None — nothing to fix in our code.");
            sb.AppendLine();
        } else {
            foreach (var fileGroup in ownCode.GroupBy(w => w.File).OrderBy(g => g.Key, StringComparer.Ordinal)) {
                var link = RelLink(fileGroup.Key!, linkBaseDir, repoRoot);
                sb.AppendLine($"#### [{Path.GetFileName(fileGroup.Key!)}]({link})");
                sb.AppendLine();
                foreach (var w in fileGroup.OrderBy(w => w.Line).ThenBy(w => w.Col)) {
                    sb.AppendLine($"##### **{w.Code}** ({w.Line}:{w.Col})");
                    sb.AppendLine();
                    sb.AppendLine(w.Message);
                    sb.AppendLine();
                }
            }
        }

        // References — IL3058, upstream packages not marked AOT-compatible.
        sb.AppendLine($"### 📦 Packages ({references.Count})");
        sb.AppendLine();
        if (references.Count == 0) {
            sb.AppendLine("✅ None.");
            sb.AppendLine();
        } else {
            sb.AppendLine("Not marked AOT-compatible (IL3058): the package authors haven't declared them trim/AOT-safe, so they *may* break under Native AOT and need verifying. Fix by upgrading to an AOT-ready version, swapping the package, or testing the paths you use and suppressing once proven safe.");
            sb.AppendLine();
            foreach (var asm in references.Select(w => ReferencedAssembly(w.Message)).Distinct().OrderBy(a => a, StringComparer.Ordinal)) {
                var version = versions.GetValueOrDefault(asm);
                sb.AppendLine(version is null ? $"- `{asm}`" : $"- `{asm}` `{version}`");
            }
            sb.AppendLine();
        }
    }

    // === Code legend — reference material, kept at the end (not the headline) ===
    if (warnings.Count > 0) {
        sb.AppendLine("## Warning codes");
        sb.AppendLine();
        sb.AppendLine("| Code | Count | Meaning |");
        sb.AppendLine("|------|------:|---------|");
        foreach (var g in warnings.GroupBy(w => w.Code).OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
            sb.AppendLine($"| {g.Key} | {g.Count()} | {CodeMeaning(g.Key)} |");
        sb.AppendLine();
    }

    return sb.ToString();
}

// =============================================================================
// Parsing
// =============================================================================

static List<AotWarning> ParseWarnings(string output, string repoRoot) {
    var seen = new HashSet<string>();
    var warnings = new List<AotWarning>();

    // Two shapes emitted by `dotnet build`:
    //   own code:   /abs/path/File.cs(50,28): warning IL2026: <msg> [/abs/Project.csproj::TargetFramework=net10.0]
    //   reference:  CSC : warning IL3058: Referenced assembly 'NJsonSchema' is not built ... [/abs/Project.csproj::TargetFramework=net10.0]
    // The owning project is the .csproj inside the trailing [...]; the location prefix is
    // only present for own-code warnings (reference warnings come from CSC with no file).
    foreach (var rawLine in output.Split('\n')) {
        var line = rawLine.TrimEnd('\r');

        var m = Regex.Match(line, @"warning\s+(?<code>IL\d{4}):\s+(?<msg>.+?)\s*\[(?<proj>[^\]]+?\.csproj)(?:::[^\]]*)?\]");
        if (!m.Success) continue;

        var code = m.Groups["code"].Value;
        var msg = m.Groups["msg"].Value.Trim();
        var project = Path.GetFileNameWithoutExtension(m.Groups["proj"].Value);

        var loc = Regex.Match(line, @"^(?<file>[^(]+)\((?<line>\d+),(?<col>\d+)\)\s*:");
        string? file = null;
        var lineNo = 0;
        var col = 0;
        if (loc.Success) {
            file = RelPath(loc.Groups["file"].Value.Trim(), repoRoot);
            lineNo = int.Parse(loc.Groups["line"].Value);
            col = int.Parse(loc.Groups["col"].Value);
        }

        // Known false positives never reach the report — no point flagging a dependency
        // that isn't present at runtime (see IsKnownFalsePositive).
        if (IsKnownFalsePositive(code, msg)) continue;

        // The same warning re-emits once per build invocation, because each project's
        // --no-incremental build recompiles its dependency projects too. Collapse them.
        var key = $"{code}|{project}|{file}|{lineNo}|{col}|{msg}";
        if (seen.Add(key))
            warnings.Add(new AotWarning(code, project, file, lineNo, col, msg));
    }

    return warnings;
}

static string ReferencedAssembly(string message) {
    // "Referenced assembly 'NJsonSchema' is not built with ..." -> "NJsonSchema"
    var m = Regex.Match(message, @"Referenced assembly '(?<asm>[^']+)'");
    return m.Success ? m.Groups["asm"].Value : message;
}

static bool IsKnownFalsePositive(string code, string message) =>
    // JetBrains.Annotations is a compile-time-only dependency (PrivateAssets="All"); it is
    // never present at runtime, so its IL3058 "not AOT-compatible" flag is pure noise.
    code == "IL3058" && ReferencedAssembly(message) is "JetBrains.Annotations";

// =============================================================================
// Helpers
// =============================================================================

static List<string> DiscoverAotProjects(string repoRoot) {
    var srcDir = Path.Combine(repoRoot, "src");
    var searchRoot = Directory.Exists(srcDir) ? srcDir : repoRoot;

    var projects = new List<string>();
    foreach (var csproj in Directory.EnumerateFiles(searchRoot, "*.csproj", SearchOption.AllDirectories))
        if (File.ReadAllText(csproj).Contains("<IsAotCompatible>true", StringComparison.OrdinalIgnoreCase))
            projects.Add(csproj);

    projects.Sort(StringComparer.Ordinal);
    return projects;
}

static string FindRepoRoot(string startDir) {
    var dir = startDir;
    while (dir is not null) {
        if (Directory.Exists(Path.Combine(dir, ".git"))) return dir;
        dir = Path.GetDirectoryName(dir);
    }
    return startDir;
}

static string RelPath(string fullPath, string repoRoot) {
    var rooted = repoRoot.EndsWith(Path.DirectorySeparatorChar) ? repoRoot : repoRoot + Path.DirectorySeparatorChar;
    return fullPath.StartsWith(rooted, StringComparison.OrdinalIgnoreCase) ? fullPath[rooted.Length..] : fullPath;
}

static string RelLink(string repoRelativeFile, string linkBaseDir, string repoRoot) {
    // Path to the source file relative to the report's own location (.artifacts/aot-report.md
    // -> ../src/...), so links resolve wherever the report is written.
    var absolute = Path.GetFullPath(Path.Combine(repoRoot, repoRelativeFile));
    return Path.GetRelativePath(linkBaseDir, absolute).Replace('\\', '/');
}

static string SolutionTitle(string repoRoot) {
    // Derive the report title from the solution file name (Kurrent.Sdk.slnx -> "Kurrent SDK").
    // Falls back to the repo directory name when no solution is present.
    var solution = Directory.EnumerateFiles(repoRoot, "*.slnx")
        .Concat(Directory.EnumerateFiles(repoRoot, "*.sln"))
        .OrderBy(p => p, StringComparer.Ordinal)
        .FirstOrDefault();
    var name = solution is not null
        ? Path.GetFileNameWithoutExtension(solution)
        : Path.GetFileName(repoRoot.TrimEnd(Path.DirectorySeparatorChar));
    return Titleize(name);
}

static string Titleize(string raw) {
    // Split on . _ - and spaces, capitalize each word, and upper-case well-known acronyms.
    var acronyms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "sdk", "api", "db", "cli", "aot", "json", "xml", "html", "http", "grpc", "io", "ui", "ml"
    };
    var words = raw.Split(['.', '_', '-', ' '], StringSplitOptions.RemoveEmptyEntries);
    return string.Join(" ", words.Select(w =>
        acronyms.Contains(w) ? w.ToUpperInvariant() : char.ToUpperInvariant(w[0]) + w[1..]));
}

static Dictionary<string, string> LoadPackageVersions(IReadOnlyList<string> projects) {
    // Resolve package versions from each project's restored obj/project.assets.json. The
    // "libraries" map is keyed "Name/Version"; keep only type == "package" entries.
    // Central Package Management unifies versions, so a flat name -> version map is enough.
    var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var project in projects) {
        var assets = Path.Combine(Path.GetDirectoryName(project)!, "obj", "project.assets.json");
        if (!File.Exists(assets)) continue;
        try {
            using var doc = JsonDocument.Parse(File.ReadAllText(assets));
            if (!doc.RootElement.TryGetProperty("libraries", out var libraries)) continue;
            foreach (var lib in libraries.EnumerateObject()) {
                if (lib.Value.TryGetProperty("type", out var type) && type.GetString() != "package") continue;
                var slash = lib.Name.IndexOf('/');
                if (slash <= 0) continue;
                versions[lib.Name[..slash]] = lib.Name[(slash + 1)..];
            }
        } catch (JsonException) {
            // Best-effort: an unreadable/partial assets file just means no versions for that project.
        }
    }
    return versions;
}

static string CodeMeaning(string code) => code switch {
    "IL2026" => "Calls a [RequiresUnreferencedCode] member",
    "IL2055" => "MakeGenericType with non-statically-known arguments",
    "IL2057" => "Type.GetType with a non-statically-known type name",
    "IL2060" => "MakeGenericMethod with non-statically-known arguments",
    "IL2070" => "Reflection on an unannotated 'this' Type parameter",
    "IL2072" => "Unannotated value flows to a [DAM] parameter",
    "IL2075" => "Reflection on an unannotated Type",
    "IL2080" => "Field/property value doesn't satisfy [DAM]",
    "IL2087" => "Unannotated type argument flows to a [DAM] parameter",
    "IL2091" => "Unannotated generic argument flows to a [DAM] type parameter",
    "IL3000" => "Assembly.Location is empty in single-file apps",
    "IL3001" => "Assembly.GetSatelliteAssembly unsupported in single-file apps",
    "IL3050" => "Calls a [RequiresDynamicCode] member",
    "IL3058" => "Referenced assembly not marked AOT-compatible",
    _ => "AOT/trim analyzer warning",
};

// =============================================================================
// Model
// =============================================================================

record AotWarning(string Code, string Project, string? File, int Line, int Col, string Message);
