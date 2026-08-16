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
/// <c>transcript_parse_errors</c> snapshot) back through the same data sources the importer reads from. The
/// scheduler halves drive ticks deterministically with a FakeTimeProvider.
/// </summary>
[Category("Integration")]
public class AgentSessionImporterTests {
	[Test]
	public async ValueTask imports_session_messages() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		var sample     = NewSample();
		var sourceRoot = SeedSession(dir.Path, sample, [Fixture.UserLine, Fixture.AssistantTextLine, Fixture.AssistantToolUseLine]);

		var importer = NewImporter(dataSources, sourceRoot);
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

		// Assert
		await Assert.That(await importer.CountAsync()).IsEqualTo(3L);
		await Assert.That(await ReadToolUseRowAsync(dataSources, sample.Uuid3)).IsEqualTo(expectedRow);
	}

	[Test]
	public async ValueTask reimport_is_idempotent() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		var sample     = NewSample();
		var sourceRoot = SeedSession(dir.Path, sample, [Fixture.UserLine, Fixture.AssistantTextLine, Fixture.AssistantToolUseLine]);

		var importer = NewImporter(dataSources, sourceRoot);
		await importer.CreateAsync();

		// Act
		await importer.ImportAsync();
		await importer.ImportAsync();

		// Assert
		await Assert.That(await importer.CountAsync()).IsEqualTo(3L);
	}

	[Test]
	public async ValueTask imports_only_new_messages_on_reimport() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		var sample     = NewSample();
		var sourceRoot = SeedSession(dir.Path, sample, [Fixture.UserLine, Fixture.AssistantTextLine, Fixture.AssistantToolUseLine]);

		var importer = NewImporter(dataSources, sourceRoot);
		await importer.CreateAsync();

		await importer.ImportAsync();
		await Assert.That(await importer.CountAsync()).IsEqualTo(3L);

		// Act
		File.AppendAllLines(SessionFilePath(sourceRoot, sample), [Personalize(Fixture.ExtraAssistantLine, sample)]);
		await importer.ImportAsync();

		// Assert
		await Assert.That(await importer.CountAsync()).IsEqualTo(4L);
		await Assert.That(await ContainsUuidAsync(dataSources, sample.Uuid4)).IsTrue();
	}

	[Test]
	public async ValueTask keeps_unparseable_lines_in_parse_errors_table() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		var sample     = NewSample();
		var sourceRoot = SeedSession(
			dir.Path, sample, [
				Fixture.UserLine,
				Fixture.GarbageLine,
				Fixture.AssistantTextLine,
				Fixture.AttachmentLine,
				Fixture.AssistantToolUseLine,
			]);

		var importer = NewImporter(dataSources, sourceRoot);
		await importer.CreateAsync();

		// Act
		await importer.ImportAsync();

		// Assert
		await Assert.That(await importer.CountAsync()).IsEqualTo(3L);
		await Assert.That(await importer.CountParseErrorsAsync()).IsEqualTo(2L);
		await Assert.That(await CountParseErrorsLinkedToMessagesAsync(dataSources)).IsEqualTo(2L);
	}

	[Test]
	public async ValueTask parse_errors_is_a_snapshot_not_a_log() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		var sample     = NewSample();
		var sourceRoot = SeedSession(
			dir.Path, sample, [
				Fixture.UserLine,
				Fixture.AssistantTextLine,
				Fixture.AssistantToolUseLine,
				Fixture.GarbageLine,
			]);

		var importer = NewImporter(dataSources, sourceRoot);
		await importer.CreateAsync();

		await importer.ImportAsync();
		await Assert.That(await importer.CountAsync()).IsEqualTo(3L);
		await Assert.That(await importer.CountParseErrorsAsync()).IsEqualTo(1L);

		// Act
		File.WriteAllLines(
			SessionFilePath(sourceRoot, sample),
			new[] { Fixture.UserLine, Fixture.AssistantTextLine, Fixture.AssistantToolUseLine }.Select(line => Personalize(line, sample)));
		await importer.ImportAsync();

		// Assert
		await Assert.That(await importer.CountAsync()).IsEqualTo(3L);
		await Assert.That(await importer.CountParseErrorsAsync()).IsEqualTo(0L);
	}

	[Test]
	public async ValueTask count_is_zero_before_first_import() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		var importer = NewImporter(dataSources, Path.Combine(dir.Path, "claude-home"));

		// Act + Assert
		await Assert.That(await importer.CountAsync()).IsEqualTo(0L);
		await Assert.That(await importer.CountParseErrorsAsync()).IsEqualTo(0L);
	}

	[Test]
	public async ValueTask create_is_idempotent() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		var sample     = NewSample();
		var sourceRoot = SeedSession(dir.Path, sample, [Fixture.UserLine, Fixture.AssistantTextLine, Fixture.AssistantToolUseLine]);

		var importer = NewImporter(dataSources, sourceRoot);

		// Act
		await importer.CreateAsync();
		await importer.CreateAsync();
		await importer.ImportAsync();

		// Assert
		await Assert.That(await importer.ExistsAsync()).IsTrue();
		await Assert.That(await importer.CountAsync()).IsEqualTo(3L);
		await Assert.That(await importer.CountParseErrorsAsync()).IsEqualTo(0L);
	}

	[Test]
	public async ValueTask tick_runs_import() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		var sample     = NewSample();
		var sourceRoot = SeedSession(dir.Path, sample, [Fixture.UserLine, Fixture.AssistantTextLine, Fixture.AssistantToolUseLine]);

		var clock = new FakeTimeProvider();

		var options = new AgentSessionImportOptions {
			SourcePath   = sourceRoot,
			TickInterval = TimeSpan.FromMinutes(5),
		};

		var importer = new AgentSessionImporter(dataSources, options);
		await importer.CreateAsync();

		using var scheduler = new AgentSessionImportScheduler(importer, options, clock);

		// Act — the tick body through the timer's own gate. A timer-fired tick is fire-and-forget
		// and cannot be awaited, and advancing the clock first would race it for the gate.
		await scheduler.TickNowAsync();

		// Assert
		await Assert.That(await importer.CountAsync()).IsEqualTo(3L);
	}

	[Test]
	public async ValueTask tick_skips_quietly_before_bootstrap() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		var sample     = NewSample();
		var sourceRoot = SeedSession(dir.Path, sample, [Fixture.UserLine, Fixture.AssistantTextLine, Fixture.AssistantToolUseLine]);

		var clock = new FakeTimeProvider();

		var options = new AgentSessionImportOptions {
			SourcePath   = sourceRoot,
			TickInterval = TimeSpan.FromMinutes(5),
		};

		var importer = new AgentSessionImporter(dataSources, options);

		using var scheduler = new AgentSessionImportScheduler(importer, options, clock);

		// Act
		clock.Advance(TimeSpan.FromMinutes(6));
		await scheduler.TickNowAsync();

		// Assert
		await Assert.That(await importer.ExistsAsync()).IsFalse();
		await Assert.That(await importer.CountAsync()).IsEqualTo(0L);
		await Assert.That(await importer.CountParseErrorsAsync()).IsEqualTo(0L);
	}

	[Test]
	public async ValueTask tick_failure_does_not_throw() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		var clock = new FakeTimeProvider();

		var options = new AgentSessionImportOptions {
			SourcePath   = Path.Combine(dir.Path, "does-not-exist"),
			TickInterval = TimeSpan.FromMinutes(5),
		};

		var importer = new AgentSessionImporter(dataSources, options);
		await importer.CreateAsync();

		using var scheduler = new AgentSessionImportScheduler(importer, options, clock);

		// Act
		clock.Advance(TimeSpan.FromMinutes(6));

		// the scheduler stays usable after the failing background tick: a direct tick is still a safe no-throw.
		await scheduler.TickNowAsync();

		// Assert
		await Assert.That(await importer.ExistsAsync()).IsTrue();
		await Assert.That(await importer.CountAsync()).IsEqualTo(0L);
		await Assert.That(await importer.CountParseErrorsAsync()).IsEqualTo(0L);
	}

	#region ->> Test Infrastructure <<-

	static KontextDataSource NewDataSources(string dir) => MemorySeeding.NewDataSources(dir);

	static AgentSessionImporter NewImporter(KontextDataSource dataSource, string sourcePath) =>
		new(dataSource, new() { SourcePath = sourcePath });

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

	/// <summary>Reads the asserted columns of one imported message back through the data sources' read surface.</summary>
	static ValueTask<(string Source, string Role, string Model, string ToolName, DateTimeOffset Timestamp)> ReadToolUseRowAsync(
		KontextDataSource dataSource, string uuid
	) =>
		dataSource.ExecuteAsync(
			connection => {
				using var command = connection.CreateCommand();
				command.CommandText =
					"""
					SELECT source, message_role, model, tool_name, "timestamp"
					FROM memory.main.transcripts
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

	static ValueTask<bool> ContainsUuidAsync(KontextDataSource dataSource, string uuid) =>
		dataSource.ExecuteAsync(
			connection => {
				using var command = connection.CreateCommand();
				command.CommandText = "SELECT count(*) FROM memory.main.transcripts WHERE uuid = $uuid";
				command.Parameters.Add(new("uuid", uuid));

				return (long)command.ExecuteScalar()! > 0;
			});

	/// <summary>
	/// Counts parse-error rows whose <c>session_id</c> matches an imported transcript — the logical
	/// link the two tables share (a SEMI JOIN, so a shared session never fans the count out per row).
	/// </summary>
	static ValueTask<long> CountParseErrorsLinkedToMessagesAsync(KontextDataSource dataSource) =>
		dataSource.ExecuteAsync(
			connection => {
				using var command = connection.CreateCommand();
				command.CommandText =
					"""
					SELECT count(*)
					FROM memory.main.transcript_parse_errors AS pe
					SEMI JOIN memory.main.transcripts AS m ON pe.session_id = m.session_id
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


	#endregion // Test Infrastructure
}
