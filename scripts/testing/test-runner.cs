#!/usr/bin/env dotnet
#:package CliWrap@3.8.2
#:package Spectre.Console@0.55.2
#:package System.CommandLine@2.0.8

// test-runner.cs — Canonical test runner for the Kurrent SDK solution.
// C# file-based app using CliWrap, Spectre.Console, and System.CommandLine.

using System.CommandLine;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CliWrap;
using CliWrap.Buffered;
using CliWrap.EventStream;
using Spectre.Console;
using SysCommand = System.CommandLine.Command;

var repoRoot = FindRepoRoot(Environment.CurrentDirectory);

// =============================================================================
// CLI
// =============================================================================

var categoryArg = new Argument<string>("category") { Description = "Test category: unit, integration, or all" };
var filterOpt = new Option<string?>("--treenode-filter") { Description = "TUnit treenode filter pattern" };
var htmlReportOpt = new Option<bool>("--html-report") { Description = "Generate TUnit HTML reports (disabled by default)" };
var rebuildOpt = new Option<bool>("--rebuild") { Description = "Force a full from-scratch build (--no-incremental) so trim/AOT analyzers re-emit" };
var runIdOpt = new Option<string?>("--run-id") { Description = "Use this results folder name instead of an auto-generated one, so the caller knows the path upfront (.artifacts/test-results/<run-id>)" };
var extraArgs = new Argument<string[]>("extra") { Arity = ArgumentArity.ZeroOrMore, Description = "Extra TUnit arguments" };

var runCmd = new SysCommand("run", "Execute tests and produce results") { categoryArg, filterOpt, htmlReportOpt, rebuildOpt, runIdOpt, extraArgs };
var listCmd = new SysCommand("list", "Discover and list matching tests without running them") { categoryArg, filterOpt };
var rootCmd = new RootCommand("Canonical test runner for the Kurrent SDK solution") { runCmd, listCmd };

// The actions must RETURN their exit code: the top-level `return ... InvokeAsync()` below
// propagates the action's return value and overrides Environment.ExitCode — assigning it
// inside the action makes build/test failures exit 0.
runCmd.SetAction((result, _) => RunTests(
    result.GetValue(categoryArg)!,
    result.GetValue(filterOpt),
    result.GetValue(htmlReportOpt),
    result.GetValue(rebuildOpt),
    result.GetValue(runIdOpt),
    result.GetValue(extraArgs) ?? []));

listCmd.SetAction((result, _) => ListTests(
    result.GetValue(categoryArg)!,
    result.GetValue(filterOpt)));

return await rootCmd.Parse(args).InvokeAsync();

// =============================================================================
// List
// =============================================================================

async Task<int> ListTests(string category, string? userFilter) {
    var filter = BuildFilter(userFilter, CategoryProp(category));

    AnsiConsole.Write(new Rule($"[blue]list {category} tests[/]").LeftJustified());
    AnsiConsole.MarkupLine($"  Filter: [dim]{Markup.Escape(filter)}[/]");
    AnsiConsole.WriteLine();

    // -c Release so discovery hits the same binaries `run` builds and tests.
    // DisableCiTestRsp turns off the ci/ci.rsp injection from src/Directory.Build.props:
    // the rsp carries --report-trx, which TUnit rejects when combined with --list-tests,
    // killing discovery entirely. Note: TUnit (observed on 1.0.39) ignores --treenode-filter
    // during discovery, so the listing shows ALL discovered tests, not just the filter's scope.
    var result = await Cli.Wrap("dotnet")
        .WithArguments(["test", "-c", "Release", "--list-tests", "--no-progress", "--ignore-exit-code", "8",
            "--", "--disable-logo", "--treenode-filter", filter])
        .WithWorkingDirectory(repoRoot)
        .WithEnvironmentVariables(env => env.Set("DisableCiTestRsp", "true"))
        .WithValidation(CommandResultValidation.None)
        .ExecuteBufferedAsync();

    // Non-TUnit assemblies (xUnit/NUnit on MTP) reject the TUnit-only flags and dump a full
    // usage/help screen per assembly (~350 lines each):
    //   Standard output: xUnit.net v3 Microsoft.Testing.Platform Runner v3.1.0 ...
    //   Unknown option '--treenode-filter'
    //   Usage KurrentDB.Auth.StreamPolicyPlugin.Tests [option providers] ...
    //       --config-file
    //           Specifies a testconfig.json file.
    //   ...
    // Suppress from the first "Unknown option"/"Usage " line until the next assembly block
    // ("Standard output:"), plus the per-assembly "Zero tests ran"/"Exit code:" noise.
    var suppressing = false;
    foreach (var line in result.StandardOutput.Split('\n')) {
        var trimmed = line.TrimStart();
        // Usage-dump bodies are fully indented; a new assembly block ("Standard output:") or any
        // column-0 line — "Discovered N tests in assembly - ..." headers and the final summary —
        // marks the end of a dump.
        if (trimmed.StartsWith("Standard output:") || (line.Length > 0 && !char.IsWhiteSpace(line[0])))
            suppressing = false;
        if (trimmed.StartsWith("Unknown option") || trimmed.StartsWith("Usage ") ||
            trimmed.StartsWith("Invalid configuration for provider")) suppressing = true;

        if (!suppressing && !string.IsNullOrEmpty(line) && !line.StartsWith("Discovering")
            && !line.Contains("Zero tests ran") && !trimmed.StartsWith("Exit code:"))
            Console.WriteLine(line);
    }

    // Same raw-exit pollution as in RunTests: non-TUnit assemblies exit 5 rejecting the
    // TUnit-only flag, and 8 means zero tests matched — both benign for a listing.
    return result.ExitCode is 0 or 5 or 8 ? 0 : result.ExitCode;
}

// =============================================================================
// Run
// =============================================================================

async Task<int> RunTests(string category, string? userFilter, bool htmlReport, bool rebuild, string? runIdOverride, string[] passthrough) {
    // A caller-supplied --run-id makes the results path deterministic (.artifacts/test-results/<run-id>),
    // so the caller can read reports directly without scraping console output for the generated folder.
    var runId = string.IsNullOrWhiteSpace(runIdOverride)
        ? $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[^4..]}"
        : runIdOverride;
    var filter = BuildFilter(userFilter, CategoryProp(category));
    var resultsDir = Path.Combine(repoRoot, ".artifacts", "test-results", runId);
    Directory.CreateDirectory(resultsDir);

    // --- Header ---
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule($"[bold blue]{category} tests[/]").LeftJustified());
    AnsiConsole.WriteLine();

    var headerTable = new Table()
        .Border(TableBorder.None)
        .HideHeaders()
        .AddColumn(new TableColumn("k").Width(10))
        .AddColumn("v");
    headerTable.AddRow("[dim]Run ID[/]", $"[bold]{Markup.Escape(runId)}[/]");
    headerTable.AddRow("[dim]Filter[/]", $"[dim]{Markup.Escape(filter)}[/]");
    headerTable.AddRow("[dim]Results[/]", $"[dim].artifacts/test-results/{Markup.Escape(runId)}[/]");
    AnsiConsole.Write(headerTable);
    AnsiConsole.WriteLine();

    // --- Build first, then run tests with --no-build (no second compile) ---
    // Default build is incremental. --rebuild forces --no-incremental so the trim/AOT
    // analyzers recompile from scratch and re-emit their warnings into output.log.
    // Note: we never use `-t:Rebuild` — its Clean/Build steps are unordered across the
    // solution graph and race-delete dependency ref assemblies (CS0006).
    var logBuilder = new StringBuilder();
    var buildExit = 0;

    var buildArgs = new List<string> { "build", "-c", "Release" };
    if (rebuild) buildArgs.Add("--no-incremental");

    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .SpinnerStyle(new Style(Color.Blue))
        .StartAsync(rebuild ? "[blue]Building (--no-incremental)...[/]" : "[blue]Building...[/]", async _ => {
            var buildCmd = Cli.Wrap("dotnet")
                .WithArguments(buildArgs)
                .WithWorkingDirectory(repoRoot)
                .WithValidation(CommandResultValidation.None);

            await foreach (var evt in buildCmd.ListenAsync()) {
                switch (evt) {
                    case StandardOutputCommandEvent { Text: var line }:
                        logBuilder.AppendLine(line);
                        break;
                    case StandardErrorCommandEvent { Text: var err }:
                        logBuilder.AppendLine(err);
                        break;
                    case ExitedCommandEvent exited:
                        buildExit = exited.ExitCode;
                        break;
                }
            }
        });

    if (buildExit != 0) {
        // Build failed — persist the log and stop before running any tests.
        await File.WriteAllTextAsync(Path.Combine(resultsDir, "output.log"), logBuilder.ToString());
        AnsiConsole.MarkupLine($"[red]✗ build failed (exit {buildExit})[/] [dim]— see .artifacts/test-results/{Markup.Escape(runId)}/output.log[/]");
        return buildExit;
    }

    // --- Build dotnet test args (--no-build: tests run against the build above) ---
    var dotnetArgs = new List<string> {
        "test", "-c", "Release", "--no-build",
        // 8 = zero tests matched, fine per assembly. Non-TUnit assemblies additionally exit 5
        // rejecting the TUnit-only --treenode-filter; no ignore flag suppresses that (they fail
        // during argument parsing), so the final exit code is computed from the parsed summary
        // after the run instead of trusting dotnet test's aggregate.
        "--no-progress", "--ignore-exit-code", "8",
        "--results-directory", resultsDir,
        "--",
        "--disable-logo", "--treenode-filter", filter,
        "--report-trx", "--log-level", "Trace", "--detailed-stacktrace"
    };
    dotnetArgs.AddRange(passthrough);

    // --- Run dotnet test silently with live status ---
    var exitCode = 0;
    var passed = 0;
    var failed = 0;
    var skipped = 0;
    var currentAssembly = "";
    var assemblyResults = new Dictionary<string, (string Status, string Duration)>();

    var testCmd = Cli.Wrap("dotnet")
        .WithArguments(dotnetArgs)
        .WithWorkingDirectory(repoRoot)
        .WithEnvironmentVariables(env => {
            if (!htmlReport)
                env.Set("TUNIT_DISABLE_HTML_REPORTER", "true");
            // Opt out of the CI-owned ci/ci.rsp injection (src/Directory.Build.props): coverage
            // instrumentation and hangdump are CI concerns that only add overhead and .coverage
            // litter locally, and the runner passes --report-trx itself. CI's own dotnet test
            // invocations don't set this variable and are unaffected.
            env.Set("DisableCiTestRsp", "true");
        })
        .WithValidation(CommandResultValidation.None);

    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .SpinnerStyle(new Style(Color.Blue))
        .StartAsync("[blue]Running tests...[/]", async ctx => {
            await foreach (var evt in testCmd.ListenAsync()) {
                switch (evt) {
                    case StandardOutputCommandEvent { Text: var line }:
                        logBuilder.AppendLine(line);

                        // Track assembly under test
                        var runMatch = Regex.Match(line, @"Running tests from .+[/\\]([^/\\]+)\.dll");
                        if (runMatch.Success) {
                            currentAssembly = runMatch.Groups[1].Value;
                            ctx.Status($"[blue]Running [bold]{Markup.Escape(currentAssembly)}[/]...[/]");
                        }

                        // Track assembly completion
                        // Matches both "passed (1s 022ms)" and "failed with 3 error(s) (45s 060ms)"
                        var doneMatch = Regex.Match(line, @"([^\s/\\]+)\.dll \([^)]+\) (passed|failed)[^(]*\(([^)]+)\)$");
                        if (doneMatch.Success) {
                            var asmName = doneMatch.Groups[1].Value;
                            var status = doneMatch.Groups[2].Value;
                            var duration = doneMatch.Groups[3].Value;
                            assemblyResults[asmName] = (status, duration);

                            var icon = status == "passed" ? "[green]✓[/]" : "[red]✗[/]";
                            ctx.Status($"{icon} {Markup.Escape(asmName)} [dim]({Markup.Escape(duration)})[/]");
                        }

                        // Track test counts from the summary
                        var totalMatch = Regex.Match(line, @"^\s+total: (\d+)");
                        var failedMatch = Regex.Match(line, @"^\s+failed: (\d+)");
                        var succeededMatch = Regex.Match(line, @"^\s+succeeded: (\d+)");
                        var skippedMatch = Regex.Match(line, @"^\s+skipped: (\d+)");
                        if (failedMatch.Success) failed = int.Parse(failedMatch.Groups[1].Value);
                        if (succeededMatch.Success) passed = int.Parse(succeededMatch.Groups[1].Value);
                        if (skippedMatch.Success) skipped = int.Parse(skippedMatch.Groups[1].Value);
                        break;

                    case StandardErrorCommandEvent { Text: var err }:
                        logBuilder.AppendLine(err);
                        break;

                    case ExitedCommandEvent exited:
                        exitCode = exited.ExitCode;
                        break;
                }
            }
        });

    await File.WriteAllTextAsync(Path.Combine(resultsDir, "output.log"), logBuilder.ToString());

    // --- Assembly results table ---
    var asmTable = new Table()
        .Border(TableBorder.Rounded)
        .BorderColor(Color.Grey)
        .AddColumn(new TableColumn("[dim]Assembly[/]"))
        .AddColumn(new TableColumn("[dim]Status[/]").Centered())
        .AddColumn(new TableColumn("[dim]Duration[/]").RightAligned());

    foreach (var (name, (status, duration)) in assemblyResults) {
        var statusMarkup = status switch {
            "passed" => "[green]✓ passed[/]",
            _ => $"[red]✗ failed[/]"
        };
        asmTable.AddRow(new Markup(Markup.Escape(name)), new Markup(statusMarkup), new Markup($"[dim]{Markup.Escape(duration)}[/]"));
    }

    AnsiConsole.Write(asmTable);
    AnsiConsole.WriteLine();

    // --- Summary bar ---
    var total = passed + failed + skipped;
    var passRate = total > 0 ? (int)Math.Round(passed * 100.0 / total) : 100;
    var statusColor = failed > 0 ? "red" : "green";
    var statusText = failed > 0 ? "FAILED" : "PASSED";
    var statusIcon = failed > 0 ? "✗" : "✓";

    var summaryParts = new List<string> {
        $"[{statusColor} bold]{statusIcon} {statusText}[/]",
        $"[bold]{passRate}%[/] pass rate",
        $"[green]✓ {passed}[/] passed"
    };
    if (failed > 0) summaryParts.Add($"[red]✗ {failed}[/] failed");
    if (skipped > 0) summaryParts.Add($"[yellow]△ {skipped}[/] skipped");
    summaryParts.Add($"[dim]{total} total[/]");

    AnsiConsole.MarkupLine(string.Join(" [dim]·[/] ", summaryParts));
    AnsiConsole.WriteLine();

    // dotnet test's raw exit is polluted in this mixed-framework solution: non-TUnit assemblies
    // (xUnit/NUnit on MTP) reject the TUnit-only --treenode-filter with exit 5 during argument
    // parsing even when every matched test passed, and neither --ignore-exit-code nor
    // TESTINGPLATFORM_EXITCODE_IGNORE suppresses that (verified 2026-07-21). The parsed summary
    // is authoritative instead: any failed test/assembly -> 2; nothing matched anywhere -> 8
    // (loud, so a typo'd filter can't pass); benign raw exits (0/5/8) -> 0; anything else
    // (crash, infra failure) surfaces unchanged.
    var anyAssemblyFailed = assemblyResults.Values.Any(r => r.Status != "passed");
    exitCode = failed > 0 || anyAssemblyFailed ? 2
        : total == 0 ? 8
        : exitCode is 0 or 5 or 8 ? 0
        : exitCode;
    if (exitCode == 8)
        AnsiConsole.MarkupLine("[red]✗ the filter matched zero tests across all assemblies[/]");

    // --- Post-processing ---
    PostProcess(resultsDir, htmlReport);

    // --- Generate markdown reports ---
    var trxFiles = Directory.GetFiles(resultsDir, "*.trx").OrderBy(f => f).ToArray();
    var trx2md = Path.Combine(repoRoot, "scripts", "testing", "trx2md.py");
    var subtitle = $"{category} tests";

    if (trxFiles.Length > 0) {
        // Generate persistent .md reports (default preset, silent).
        // ExecuteBufferedAsync ensures the process fully completes before continuing —
        // ExecuteAsync can return before file writes are flushed.
        var trx2mdResult = await Cli.Wrap(trx2md)
            .WithArguments(b => { b.Add("--subtitle").Add(subtitle); foreach (var f in trxFiles) b.Add(f); })
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        if (trx2mdResult.ExitCode != 0) {
            AnsiConsole.MarkupLine($"[red]trx2md failed (exit {trx2mdResult.ExitCode})[/]");
            if (!string.IsNullOrEmpty(trx2mdResult.StandardError))
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(trx2mdResult.StandardError)}[/]");
            if (!string.IsNullOrEmpty(trx2mdResult.StandardOutput))
                AnsiConsole.MarkupLine($"[dim]{Markup.Escape(trx2mdResult.StandardOutput)}[/]");
        }

        // Get the ai summary for terminal display
        var mdReport = await Cli.Wrap(trx2md)
            .WithArguments(b => {
                b.Add("--stdout").Add("--preset").Add("ai").Add("--subtitle").Add(subtitle);
                foreach (var f in trxFiles) b.Add(f);
            })
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        var reportText = mdReport.StandardOutput.Trim();
        if (!string.IsNullOrEmpty(reportText)) {
            AnsiConsole.Write(new Rule("[blue]report[/]").LeftJustified());
            AnsiConsole.WriteLine();
            Console.WriteLine(reportText);
        }
    }

    // --- Command reproduction block ---
    var relResults = $".artifacts/test-results/{runId}";
    var extra = passthrough.Length > 0 ? $" \\\n    {string.Join(' ', passthrough)}" : "";
    var rebuildFlag = rebuild ? " --no-incremental" : "";
    var cmdText = $"""
        dotnet build -c Release{rebuildFlag} && \
        dotnet test \
          -c Release --no-build --no-progress --ignore-exit-code 8 \
          --results-directory {relResults} \
          -- \
          --disable-logo --log-level Trace --detailed-stacktrace \
          --treenode-filter "{filter}" \
          --report-trx{extra}
        """;

    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[dim]command[/]").LeftJustified());
    AnsiConsole.Write(new Panel(Markup.Escape(cmdText.Trim()))
        .Border(BoxBorder.Rounded)
        .BorderColor(Color.Grey)
        .Padding(1, 0));

    return exitCode;
}

// =============================================================================
// Post-Processing
// =============================================================================

void PostProcess(string resultsDir, bool htmlReport) {
    foreach (var dir in Directory.GetDirectories(resultsDir))
        Directory.Delete(dir, recursive: true);

    if (htmlReport) {
        foreach (var html in Directory.GetFiles(resultsDir, "*-report.html")) {
            var lower = Path.Combine(Path.GetDirectoryName(html)!, Path.GetFileName(html).ToLowerInvariant());
            if (html != lower) File.Move(html, lower);
        }
    }

    var osName = GetShortOsName();
    XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    foreach (var trx in Directory.GetFiles(resultsDir, "*.trx").OrderBy(f => f)) {
        try {
            var doc = XDocument.Load(trx);
            var unitTest = doc.Descendants(ns + "UnitTest").FirstOrDefault();
            if (unitTest is null) { File.Delete(trx); continue; }

            var storage = unitTest.Attribute("storage")?.Value ?? "";
            if (string.IsNullOrEmpty(storage)) continue;

            var assembly = Path.GetFileNameWithoutExtension(storage);
            var framework = Path.GetFileName(Path.GetDirectoryName(storage));
            var newName = Path.Combine(resultsDir, $"{assembly}-{osName}-{framework}-report.trx");

            if (trx != newName && !File.Exists(newName))
                File.Move(trx, newName);
        } catch {
            // Skip unparseable files
        }
    }
}

// =============================================================================
// Helpers
// =============================================================================

static string FindRepoRoot(string startDir) {
    var dir = startDir;
    while (dir is not null) {
        // In a linked worktree, `.git` is a FILE (a gitdir pointer), not a directory. Match either,
        // so the runner anchors on the worktree it is invoked from instead of walking up to the main
        // checkout — otherwise it would build and test the wrong tree.
        var git = Path.Combine(dir, ".git");
        if (Directory.Exists(git) || File.Exists(git)) return dir;
        dir = Path.GetDirectoryName(dir);
    }
    return startDir;
}

static string CategoryProp(string category) => category switch {
    "unit" => "Category!=Integration",
    "integration" => "Category=Integration",
    "all" => "",
    _ => throw new ArgumentException($"Unknown category: {category}")
};

static string BuildFilter(string? userFilter, string categoryProp) {
    if (string.IsNullOrEmpty(categoryProp))
        return userFilter ?? "/*/*/*/*";

    if (!string.IsNullOrEmpty(userFilter)) {
        if (userFilter.EndsWith(']')) {
            var bracketIdx = userFilter.IndexOf('[');
            var path = userFilter[..bracketIdx];
            var props = userFilter[(bracketIdx + 1)..^1];
            return $"{path}[({props})&({categoryProp})]";
        }
        return $"{userFilter}[{categoryProp}]";
    }

    return $"/*/*/*/*[{categoryProp}]";
}

static string GetShortOsName() {
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "windows";
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "linux";
    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "macos";
    return "unknown";
}
