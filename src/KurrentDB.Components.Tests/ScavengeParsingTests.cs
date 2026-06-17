// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using KurrentDB.Components.Scavenges;
using Xunit;

namespace KurrentDB.Components.Tests;

// Pure unit tests of the scavenge event -> display-row mapping. No renderer or DI; wording is
// asserted against the legacy Angular scavenge view.
public class ScavengeParsingTests {
	static ScavengeDetailEvent Map(string eventType, string json) {
		using var doc = JsonDocument.Parse(json);
		return ScavengeParsing.MapDetailEvent(eventType, doc.RootElement);
	}

	[Fact]
	public void Started_maps_to_started_status() =>
		Assert.Equal("Started", Map("$scavengeStarted", "{}").Status);

	[Fact]
	public void Chunks_completed_uses_angular_wording_and_count() {
		var d = Map("$scavengeChunksCompleted",
			"""{"chunkStartNumber":0,"chunkEndNumber":2,"wasScavenged":true,"spaceSaved":1024,"timeTaken":"00:00:01.5"}""");
		Assert.Equal("Scavenging chunks 0 - 2 complete", d.Status);
		Assert.Equal("3 chunk(s) scavenged", d.Result);
		Assert.Equal(1024, d.SpaceSaved);
		Assert.Equal("0:00:01.500", d.TimeTaken);
	}

	[Fact]
	public void Chunks_not_scavenged_shows_none_text() =>
		Assert.Equal("No chunks scavenged",
			Map("$scavengeChunksCompleted", """{"chunkStartNumber":0,"chunkEndNumber":2,"wasScavenged":false}""").Result);

	[Fact]
	public void Error_message_takes_precedence_over_success() =>
		Assert.Equal("Error: disk full",
			Map("$scavengeChunksCompleted", """{"wasScavenged":true,"errorMessage":"disk full"}""").Result);

	[Fact]
	public void Empty_error_message_is_ignored() =>
		Assert.Equal("No chunks scavenged",
			Map("$scavengeChunksCompleted", """{"wasScavenged":false,"errorMessage":""}""").Result);

	[Fact]
	public void Merge_completed_uses_angular_wording() {
		var d = Map("$scavengeMergeCompleted", """{"chunkStartNumber":3,"chunkEndNumber":3,"wasMerged":true}""");
		Assert.Equal("Merging chunks 3 - 3 complete", d.Status);
		Assert.Equal("1 chunk(s) merged", d.Result);
	}

	[Fact]
	public void Index_completed_uses_angular_wording() {
		var d = Map("$scavengeIndexCompleted", """{"level":1,"index":2,"wasScavenged":true,"entriesDeleted":42}""");
		Assert.Equal("Scavenging index table (1,2) complete", d.Status);
		Assert.Equal("42 index entries scavenged", d.Result);
	}

	[Fact]
	public void Index_not_scavenged_shows_default() =>
		Assert.Equal("Index table not scavenged",
			Map("$scavengeIndexCompleted", """{"level":1,"index":2,"wasScavenged":false}""").Result);

	[Fact]
	public void Completed_success_shows_result() {
		var d = Map("$scavengeCompleted", """{"result":"Success"}""");
		Assert.Equal("Completed", d.Status);
		Assert.Equal("Success", d.Result);
	}

	[Fact]
	public void Completed_failed_appends_error() =>
		Assert.Equal("Failed: boom", Map("$scavengeCompleted", """{"result":"Failed","error":"boom"}""").Result);

	[Fact]
	public void Unknown_event_falls_back_to_event_type() =>
		Assert.Equal("$somethingElse", Map("$somethingElse", "{}").Status);

	static string Time(string json) {
		using var doc = JsonDocument.Parse(json);
		return ScavengeParsing.FormatTimeTaken(doc.RootElement);
	}

	[Fact]
	public void FormatTimeTaken_formats_valid_timespan() =>
		Assert.Equal("0:00:01.500", Time("""{"timeTaken":"00:00:01.5"}"""));

	[Fact]
	public void FormatTimeTaken_missing_returns_empty() =>
		Assert.Equal("", Time("{}"));

	[Fact]
	public void FormatTimeTaken_unparseable_passes_through() =>
		Assert.Equal("weird", Time("""{"timeTaken":"weird"}"""));
}
