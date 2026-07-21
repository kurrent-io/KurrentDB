// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Modules.Sessions;
using Microsoft.Extensions.Time.Testing;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// Behavioural tests for <see cref="AgentSessionImporter"/> and <see cref="AgentSessionImportScheduler"/>
/// against a REAL DuckDB engine and the REAL agent_data community extension: each test writes a
/// validated Claude Code session under <c>projects/-tmp-proj/&lt;session&gt;.jsonl</c>, bootstraps the
/// tables with CreateAsync, imports, and reads <c>transcripts</c> (and the
/// <c>transcript_parse_errors</c> snapshot) back through the same pool the importer rents from. The
/// scheduler halves drive ticks deterministically with a FakeTimeProvider.
/// </summary>
[Category("Integration")]
public class AgentSessionImporterTests {
	[Test]
	public async ValueTask imports_session_messages() {
		// Arrange — one validated three-line session: a user turn, an assistant text turn, and an
		// assistant tool_use turn.
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var sample     = NewSample();
		var sourceRoot = SeedSession(dir.Path, sample, [Fixture.UserLine, Fixture.AssistantTextLine, Fixture.AssistantToolUseLine]);

		var importer = NewImporter(pool, sourceRoot);
		await importer.CreateAsync();

		// The tool_use row is the one carrying every asserted field — a real model, a tool name, and a
		// timestamp that must land as an actual TIMESTAMPTZ instant, not the raw ISO-8601 string.
		var expectedRow = (
			Source:    "claude",
			Role:      "assistant",
			Model:     "claude-fable-5",
			ToolName:  "Bash",
			Timestamp: new DateTimeOffset(2026, 7, 21, 10, 0, 10, TimeSpan.Zero));

		// Act
		await importer.ImportAsync();

		// Assert — all three conversation rows landed, and the tool_use row round-trips whole.
		await Assert.That(await importer.CountAsync()).IsEqualTo(3L);
		await Assert.That(await ReadToolUseRowAsync(pool, sample.Uuid3)).IsEqualTo(expectedRow);
	}

	[Test]
	public async ValueTask reimport_is_idempotent() {
		// Arrange
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var sample     = NewSample();
		var sourceRoot = SeedSession(dir.Path, sample, [Fixture.UserLine, Fixture.AssistantTextLine, Fixture.AssistantToolUseLine]);

		var importer = NewImporter(pool, sourceRoot);
		await importer.CreateAsync();

		// Act — import the same unchanged session twice.
		await importer.ImportAsync();
		await importer.ImportAsync();

		// Assert — the anti-join dropped everything already stored: still exactly three rows.
		await Assert.That(await importer.CountAsync()).IsEqualTo(3L);
	}

	[Test]
	public async ValueTask imports_only_new_messages_on_reimport() {
		// Arrange
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var sample     = NewSample();
		var sourceRoot = SeedSession(dir.Path, sample, [Fixture.UserLine, Fixture.AssistantTextLine, Fixture.AssistantToolUseLine]);

		var importer = NewImporter(pool, sourceRoot);
		await importer.CreateAsync();

		await importer.ImportAsync();
		await Assert.That(await importer.CountAsync()).IsEqualTo(3L);

		// Act — a fourth valid turn is appended to the same session file, then re-imported.
		File.AppendAllLines(SessionFilePath(sourceRoot, sample), [Personalize(Fixture.ExtraAssistantLine, sample)]);
		await importer.ImportAsync();

		// Assert — only the new tail landed: four rows total, and the appended uuid is present.
		await Assert.That(await importer.CountAsync()).IsEqualTo(4L);
		await Assert.That(await ContainsUuidAsync(pool, sample.Uuid4)).IsTrue();
	}

	[Test]
	public async ValueTask keeps_unparseable_lines_in_parse_errors_table() {
		// Arrange — the three conversation lines plus raw garbage and a known-but-unmodeled metadata
		// type; both extras surface as _parse_error rows that are KEPT in transcript_parse_errors, not
		// dropped, and linked back to the imported transcripts by session_id.
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var sample     = NewSample();
		var sourceRoot = SeedSession(
			dir.Path, sample, [
				Fixture.UserLine,
				Fixture.GarbageLine,
				Fixture.AssistantTextLine,
				Fixture.AttachmentLine,
				Fixture.AssistantToolUseLine,
			]);

		var importer = NewImporter(pool, sourceRoot);
		await importer.CreateAsync();

		// Act
		await importer.ImportAsync();

		// Assert — the three real conversation messages landed; the two unparseable lines were kept in
		// the parse-error snapshot, both carrying the session_id that ties them to those messages.
		await Assert.That(await importer.CountAsync()).IsEqualTo(3L);
		await Assert.That(await importer.CountParseErrorsAsync()).IsEqualTo(2L);
		await Assert.That(await CountParseErrorsLinkedToMessagesAsync(pool)).IsEqualTo(2L);
	}

	[Test]
	public async ValueTask parse_errors_is_a_snapshot_not_a_log() {
		// Arrange — a session with one unparseable line alongside the three valid turns.
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var sample     = NewSample();
		var sourceRoot = SeedSession(
			dir.Path, sample, [
				Fixture.UserLine,
				Fixture.AssistantTextLine,
				Fixture.AssistantToolUseLine,
				Fixture.GarbageLine,
			]);

		var importer = NewImporter(pool, sourceRoot);
		await importer.CreateAsync();

		await importer.ImportAsync();
		await Assert.That(await importer.CountAsync()).IsEqualTo(3L);
		await Assert.That(await importer.CountParseErrorsAsync()).IsEqualTo(1L);

		// Act — rewrite the file WITHOUT the bad line (the graduation/self-heal case in miniature),
		// then re-import.
		File.WriteAllLines(
			SessionFilePath(sourceRoot, sample),
			new[] { Fixture.UserLine, Fixture.AssistantTextLine, Fixture.AssistantToolUseLine }.Select(line => Personalize(line, sample)));
		await importer.ImportAsync();

		// Assert — the snapshot rebuilt from the current scan drops the vanished error; messages are
		// untouched (already-stored uuids are anti-joined out).
		await Assert.That(await importer.CountAsync()).IsEqualTo(3L);
		await Assert.That(await importer.CountParseErrorsAsync()).IsEqualTo(0L);
	}

	[Test]
	public async ValueTask count_is_zero_before_first_import() {
		// Arrange — a fresh pool: neither transcripts nor transcript_parse_errors has been created.
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var importer = NewImporter(pool, Path.Combine(dir.Path, "claude-home"));

		// Act + Assert — both probes tolerate the missing table and return 0 rather than throwing.
		await Assert.That(await importer.CountAsync()).IsEqualTo(0L);
		await Assert.That(await importer.CountParseErrorsAsync()).IsEqualTo(0L);
	}

	[Test]
	public async ValueTask create_is_idempotent() {
		// Arrange — bootstrap runs on every host start, so calling it twice must be a harmless no-op
		// (both tables use IF NOT EXISTS).
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var sample     = NewSample();
		var sourceRoot = SeedSession(dir.Path, sample, [Fixture.UserLine, Fixture.AssistantTextLine, Fixture.AssistantToolUseLine]);

		var importer = NewImporter(pool, sourceRoot);

		// Act — create, create again, then import.
		await importer.CreateAsync();
		await importer.CreateAsync();
		await importer.ImportAsync();

		// Assert — the double bootstrap left the tables intact and the import still lands three rows.
		await Assert.That(await importer.ExistsAsync()).IsTrue();
		await Assert.That(await importer.CountAsync()).IsEqualTo(3L);
		await Assert.That(await importer.CountParseErrorsAsync()).IsEqualTo(0L);
	}

	[Test]
	public async ValueTask tick_runs_import() {
		// Arrange — one shared options instance, exactly as host DI would wire it: the importer bakes
		// in its source path, the scheduler drives the cadence off the same object.
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var sample     = NewSample();
		var sourceRoot = SeedSession(dir.Path, sample, [Fixture.UserLine, Fixture.AssistantTextLine, Fixture.AssistantToolUseLine]);

		var clock = new FakeTimeProvider();

		var options = new AgentSessionImportOptions {
			SourcePath   = sourceRoot,
			TickInterval = TimeSpan.FromMinutes(5),
		};

		var importer = new AgentSessionImporter(pool, options);
		await importer.CreateAsync();

		using var scheduler = new AgentSessionImportScheduler(importer, options, clock);

		// Act — advance past one tick interval so the background timer fires the import once.
		clock.Advance(TimeSpan.FromMinutes(6));

		// Assert — the tick imported the three conversation rows.
		await Assert.That(await importer.CountAsync()).IsEqualTo(3L);
	}

	[Test]
	public async ValueTask tick_skips_quietly_before_bootstrap() {
		// Arrange — CreateAsync is deliberately never called: the transcript tables do not exist, so
		// the tick must quiet-skip (the DML-only import would otherwise fail every tick).
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var sample     = NewSample();
		var sourceRoot = SeedSession(dir.Path, sample, [Fixture.UserLine, Fixture.AssistantTextLine, Fixture.AssistantToolUseLine]);

		var clock = new FakeTimeProvider();

		var options = new AgentSessionImportOptions {
			SourcePath   = sourceRoot,
			TickInterval = TimeSpan.FromMinutes(5),
		};

		var importer = new AgentSessionImporter(pool, options);

		using var scheduler = new AgentSessionImportScheduler(importer, options, clock);

		// Act — a background tick and a direct tick both fire before bootstrap; neither may throw.
		clock.Advance(TimeSpan.FromMinutes(6));
		await scheduler.TickNowAsync();

		// Assert — the tick created nothing and imported nothing: the tables still do not exist.
		await Assert.That(await importer.ExistsAsync()).IsFalse();
		await Assert.That(await importer.CountAsync()).IsEqualTo(0L);
		await Assert.That(await importer.CountParseErrorsAsync()).IsEqualTo(0L);
	}

	[Test]
	public async ValueTask tick_failure_does_not_throw() {
		// Arrange — bootstrap the tables so the tick actually reaches the import, then point the source
		// path at a directory that never exists so the scan fails; the scheduler must swallow it rather
		// than surface it onto the timer thread.
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var clock = new FakeTimeProvider();

		var options = new AgentSessionImportOptions {
			SourcePath   = Path.Combine(dir.Path, "does-not-exist"),
			TickInterval = TimeSpan.FromMinutes(5),
		};

		var importer = new AgentSessionImporter(pool, options);
		await importer.CreateAsync();

		using var scheduler = new AgentSessionImportScheduler(importer, options, clock);

		// Act — a failing background tick must not throw…
		clock.Advance(TimeSpan.FromMinutes(6));

		// …and the scheduler stays usable: a direct tick is still a safe no-throw.
		await scheduler.TickNowAsync();

		// Assert — the tables exist but the failed scan imported nothing, and no exception escaped.
		await Assert.That(await importer.ExistsAsync()).IsTrue();
		await Assert.That(await importer.CountAsync()).IsEqualTo(0L);
		await Assert.That(await importer.CountParseErrorsAsync()).IsEqualTo(0L);
	}

	#region ->> Test Infrastructure <<-

	static KontextConnectionPool NewPool(string dir) =>
		new($"Data Source={Path.Combine(dir, "engine.db")};access_mode=READ_WRITE", dir);

	static AgentSessionImporter NewImporter(KontextConnectionPool pool, string sourcePath) =>
		new(pool, new() { SourcePath = sourcePath });

	/// <summary>Fresh, arbitrary identity per test: distinct engines never collide, but the convention is per-test ids.</summary>
	static Sample NewSample() =>
		new(
			Guid.NewGuid().ToString(),
			Guid.NewGuid().ToString(),
			Guid.NewGuid().ToString(),
			Guid.NewGuid().ToString(),
			Guid.NewGuid().ToString());

	/// <summary>
	/// Writes <paramref name="lines"/> as a session file at <c>&lt;root&gt;/projects/-tmp-proj/&lt;session&gt;.jsonl</c> —
	/// the exact shape the spike proved read_conversations scans — and returns the source root the importer points at.
	/// </summary>
	static string SeedSession(string tempRoot, Sample sample, IEnumerable<string> lines) {
		var sourceRoot = Path.Combine(tempRoot, "claude-home");
		var projectDir = Path.Combine(sourceRoot, "projects", "-tmp-proj");
		Directory.CreateDirectory(projectDir);

		File.WriteAllLines(
			Path.Combine(projectDir, sample.SessionId + ".jsonl"),
			lines.Select(line => Personalize(line, sample)));

		return sourceRoot;
	}

	static string SessionFilePath(string sourceRoot, Sample sample) =>
		Path.Combine(sourceRoot, "projects", "-tmp-proj", sample.SessionId + ".jsonl");

	// Swaps the fixture's fixed ids for this test's ids; parent links stay consistent because the
	// same uuid is substituted everywhere it appears.
	static string Personalize(string line, Sample sample) =>
		line
			.Replace("11111111-1111-1111-1111-111111111111", sample.SessionId, StringComparison.Ordinal)
			.Replace("aaaaaaaa-0000-0000-0000-000000000001", sample.Uuid1, StringComparison.Ordinal)
			.Replace("aaaaaaaa-0000-0000-0000-000000000002", sample.Uuid2, StringComparison.Ordinal)
			.Replace("aaaaaaaa-0000-0000-0000-000000000003", sample.Uuid3, StringComparison.Ordinal)
			.Replace("aaaaaaaa-0000-0000-0000-000000000004", sample.Uuid4, StringComparison.Ordinal);

	/// <summary>Reads the asserted columns of one imported message back through the pool's read surface.</summary>
	static Task<(string Source, string Role, string Model, string ToolName, DateTimeOffset Timestamp)> ReadToolUseRowAsync(
		KontextConnectionPool pool, string uuid
	) =>
		pool.ExecuteAsync(
			connection => {
				using var command = connection.CreateCommand();
				command.CommandText =
					"""
					SELECT source, message_role, model, tool_name, "timestamp"
					FROM transcripts
					WHERE uuid = $uuid
					""";
				command.Parameters.Add(new("uuid", uuid));

				using var reader = command.ExecuteReader();
				reader.Read();

				// TIMESTAMPTZ arrives as a DateTimeOffset, or on some driver paths a bare DateTime whose
				// clock reading is UTC — the same wire shapes KontextDataStore reads.
				var timestamp = reader.GetValue(4) switch {
					DateTimeOffset instant => instant,
					DateTime clockReading  => new DateTimeOffset(DateTime.SpecifyKind(clockReading, DateTimeKind.Unspecified), TimeSpan.Zero),
					var other              => throw new NotSupportedException($"Unsupported timestamp value of type '{other.GetType()}'."),
				};

				return (
					Source:    reader.GetString(0),
					Role:      reader.GetString(1),
					Model:     reader.GetString(2),
					ToolName:  reader.GetString(3),
					Timestamp: timestamp);
			});

	static Task<bool> ContainsUuidAsync(KontextConnectionPool pool, string uuid) =>
		pool.ExecuteAsync(
			connection => {
				using var command = connection.CreateCommand();
				command.CommandText = "SELECT count(*) FROM transcripts WHERE uuid = $uuid";
				command.Parameters.Add(new("uuid", uuid));

				return (long)command.ExecuteScalar()! > 0;
			});

	/// <summary>
	/// Counts parse-error rows whose <c>session_id</c> matches an imported transcript — the logical
	/// link the two tables share (a SEMI JOIN, so a shared session never fans the count out per row).
	/// </summary>
	static Task<long> CountParseErrorsLinkedToMessagesAsync(KontextConnectionPool pool) =>
		pool.ExecuteAsync(
			connection => {
				using var command = connection.CreateCommand();
				command.CommandText =
					"""
					SELECT count(*)
					FROM transcript_parse_errors AS pe
					SEMI JOIN transcripts AS m ON pe.session_id = m.session_id
					""";

				return (long)command.ExecuteScalar()!;
			});

	/// <summary>One session's arbitrary ids; four message uuids so an incremental append has a fresh one.</summary>
	sealed record Sample(string SessionId, string Uuid1, string Uuid2, string Uuid3, string Uuid4);

	/// <summary>
	/// The validated JSONL line shapes from design/spike-fixture-session.jsonl — verbatim, so what the
	/// extension parses here is what it parses in production — plus a fourth valid turn and the two
	/// unparseable shapes the importer must drop.
	/// </summary>
	static class Fixture {
		public const string UserLine =
			"""{"type":"user","uuid":"aaaaaaaa-0000-0000-0000-000000000001","parentUuid":null,"sessionId":"11111111-1111-1111-1111-111111111111","timestamp":"2026-07-21T10:00:00.000Z","cwd":"/tmp/proj","gitBranch":"main","version":"2.0.0","message":{"role":"user","content":"hello"}}""";

		public const string AssistantTextLine =
			"""{"type":"assistant","uuid":"aaaaaaaa-0000-0000-0000-000000000002","parentUuid":"aaaaaaaa-0000-0000-0000-000000000001","sessionId":"11111111-1111-1111-1111-111111111111","timestamp":"2026-07-21T10:00:05.000Z","cwd":"/tmp/proj","gitBranch":"main","version":"2.0.0","message":{"role":"assistant","model":"claude-fable-5","content":[{"type":"text","text":"hi there"}],"usage":{"input_tokens":10,"output_tokens":5,"cache_creation_input_tokens":0,"cache_read_input_tokens":0},"stop_reason":"end_turn"}}""";

		public const string AssistantToolUseLine =
			"""{"type":"assistant","uuid":"aaaaaaaa-0000-0000-0000-000000000003","parentUuid":"aaaaaaaa-0000-0000-0000-000000000002","sessionId":"11111111-1111-1111-1111-111111111111","timestamp":"2026-07-21T10:00:10.000Z","cwd":"/tmp/proj","gitBranch":"main","version":"2.0.0","message":{"role":"assistant","model":"claude-fable-5","content":[{"type":"tool_use","id":"toolu_01","name":"Bash","input":{"command":"ls"}}],"usage":{"input_tokens":12,"output_tokens":6}}}""";

		// A fourth valid assistant turn, chained onto the tool_use turn, for the incremental append test.
		public const string ExtraAssistantLine =
			"""{"type":"assistant","uuid":"aaaaaaaa-0000-0000-0000-000000000004","parentUuid":"aaaaaaaa-0000-0000-0000-000000000003","sessionId":"11111111-1111-1111-1111-111111111111","timestamp":"2026-07-21T10:00:15.000Z","cwd":"/tmp/proj","gitBranch":"main","version":"2.0.0","message":{"role":"assistant","model":"claude-fable-5","content":[{"type":"text","text":"done"}],"usage":{"input_tokens":8,"output_tokens":3},"stop_reason":"end_turn"}}""";

		// Raw non-JSON, and a valid but unmodeled metadata line type — both land as _parse_error.
		public const string GarbageLine = "not json";

		public const string AttachmentLine =
			"""{"type":"attachment","uuid":"aaaaaaaa-0000-0000-0000-000000000009","sessionId":"11111111-1111-1111-1111-111111111111","timestamp":"2026-07-21T10:00:03.000Z","path":"/tmp/proj/file.txt"}""";
	}

	/// <summary>A unique temp directory owned by one test; deleted on dispose.</summary>
	sealed class TempDir : IDisposable {
		public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kontext-agent-session-importer-tests", Guid.NewGuid().ToString("N"));

		public TempDir() => Directory.CreateDirectory(Path);

		public void Dispose() {
			try {
				if (Directory.Exists(Path))
					Directory.Delete(Path, recursive: true);
			} catch (IOException) {
				// Best-effort cleanup; a lingering native handle must not fail the test.
			} catch (UnauthorizedAccessException) {
				// Best-effort cleanup.
			}
		}
	}

	#endregion // Test Infrastructure
}
