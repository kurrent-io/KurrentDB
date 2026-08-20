---
name: kurrentdb-repo-test-execution
description: How to run tests in the kurrentdb repo — test-runner script exists as of 2026-07-21; observed suite durations
metadata: 
  node_type: memory
  type: project
  originSessionId: 5efcc2a3-5207-42e4-8b2d-4438c5af3a19
  modified: 2026-08-20T12:40:14.181Z
---

Verified 2026-07-21: `scripts/testing/test-runner.cs` now EXISTS in the kurrentdb repo (Sérgio added
it; before 2026-07-21 it did not — earlier note said to use `dotnet test <project>` directly).
Use it per the Testing directives: `dotnet scripts/testing/test-runner.cs -- run unit|integration|all
[--treenode-filter ...] [--run-id <guid>]` from the repo root. It builds the whole solution
(`dotnet build -c Release`, no explicit target) then runs `dotnet test --no-build` — so a single
broken project anywhere in `KurrentDB.slnx` blocks every test run. Reports land in
`.artifacts/test-results/<run-id>/*.md` (plus `.trx`, `output.log`). `scripts/testing/README.md`
documents it; `aot-report.cs` and `trx2md.py` live alongside.

Observed durations (for Bash timeouts): first probe (`list unit`, near-cold solution build in
Release) ≈ 9 min; warm incremental build ≈ 1 min. `Kurrent.Kontext.Tests` full suite (70 tests,
2026-07-21, real DuckDB+Lance + ONNX) ≈ 31s test time, ≈ 2-3 min total including warm build.
`DuckLance.Tests` full suite (448 tests) ≈ 23s test time. Filtered unit run of one small Kontext
class (4 tests, 2026-08-01) ≈ 6 min wall — build-dominated; test time itself 390ms.
`Kurrent.Kontext.Tests` unit category (38 tests, 2026-08-10) ≈ 60s test time, ≈ 6 min wall
including the slnx build. Full `/*Kontext*/` scope (all 5 Kontext assemblies, 2026-08-13) ≈ 11s
test time for Kurrent.Kontext.Tests (139 tests) + ≈ 60s for KurrentDB.Plugins.Kontext.Tests
(41 tests, boots real nodes); ≈ 4-6 min wall including the warm slnx build.

Observed 2026-08-20, scope `/(Kurrent.Kontext.Tests)|(Kurrent.Kontext.Retrieval.Tests)/*/*/*`:
unit = 250 tests (52 Kontext + 198 Retrieval) ≈ 32s test time; `all` = 357 tests (159 + 198) ≈ 2.0m
test time. Both ≈ 4-5 min wall including the warm slnx build. Two OR'd assemblies in one
treenode-filter segment works: `/(A)|(B)/*/*/*`.

Exit codes (fixed 2026-07-21, verified): runner computes its own exit from the parsed summary —
0 all passed, 2 any failure, 8 filter matched nothing, else raw dotnet-test exit (crash/infra).
Raw `dotnet test` aggregate is useless in this repo: ~30 non-TUnit assemblies (xUnit/NUnit on MTP)
reject the TUnit-only `--treenode-filter` with exit 5 during arg parsing; neither
`--ignore-exit-code "8;5"` nor `TESTINGPLATFORM_EXITCODE_IGNORE` suppresses that (verified
empirically). Reports still land in `.artifacts/test-results/<run-id>/` (`.md` auto-generated —
trx2md.py exec bit fixed).

`list` gotchas (2026-07-21): `src/Directory.Build.props` injects `ci/ci.rsp` (`--report-trx`,
`--coverage`, hangdump) into every test project via TestingPlatformCommandLineArguments; TUnit
rejects `--report-trx` with `--list-tests`, so the runner sets `DisableCiTestRsp=true` (props
condition added) for listings. TUnit 1.0.39 IGNORES `--treenode-filter` during discovery — `list`
shows ALL tests in ALL TUnit assemblies regardless of filter. TRX per-test categories can be
verified by parsing `TestCategoryItem` elements if a category split needs proving.

**The runner CANNOT run xUnit/NUnit projects at all** (verified 2026-07-22). It always passes the
TUnit-only `--treenode-filter`, which those assemblies reject during arg parsing — they report
"Zero tests ran" and silently contribute nothing, no matter what filter you give. So a green runner
report is NOT evidence that an xUnit assembly passed; check whether the assembly produced a `.md`
report at all. `KurrentDB.Connectors.Tests` (`IsKurrentXUnit=true`) is the big one — 150 tests that
never execute under the runner.

To run those, invoke the assembly's MTP executable directly (the one documented exception to the
"always use the runner" rule, since the runner physically cannot):
`src/Connectors/KurrentDB.Connectors.Tests/bin/Release/net10.0/KurrentDB.Connectors.Tests`
with xunit filters `--filter-namespace` / `--filter-class` / `--filter-method` / `--list-tests`.
Full Connectors suite ≈52s / 150 tests; the `KurrentDB.Connectors.Tests.System` namespace (27 tests,
leadership + node lifetime) ≈21s.

`KurrentDB.Ammeter` runs an EMBEDDED SECURE node (its `appsettings.json` sets `Node:Insecure:false`),
so it needs the repo-root `certs/` tree. Generate it once per machine with `docker compose run --rm
cert-gen`; it is gitignored, and the csproj copies it into the test output beside the binaries.
Without it every ammeter test dies at node startup. `KurrentDB.Api.V2.Tests` has NO `appsettings.json`,
so `NodeShimOptions.Insecure` defaults to `true` — it needs no certs. FIXED 2026-08-17: the paths were
hardcoded Windows absolutes (`D:/Kurrent/cluster/certs/...`) and had never run outside Windows since
they landed in 9dcba92d8; they are now relative to the test output dir.

Kontext tests (2026-07-21 evening): `Kurrent.Kontext.Tests` = 57, ALL integration (V1 unit tests
+ TestVectorStore deleted with the Experiments purge; KontextMemory now has 9 integration tests
over the real engine; KontextDataStoreV2Tests renamed KontextDataStoreTests).
`Kurrent.Kontext.Embeddings.Tests` = 13 (1 unit class + 4 integration ONNX classes). Suite ≈7s.
