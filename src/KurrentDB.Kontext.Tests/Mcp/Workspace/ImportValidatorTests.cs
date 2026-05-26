// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;

namespace KurrentDB.Kontext.Tests.Mcp.Workspace;

public class ImportValidatorTests {
	static readonly Func<string, ValueTask<bool>> AllowAll = _ => ValueTask.FromResult(true);

	static ImportEvent MakeEvent(string stream = "order-1", string eventType = "OrderPlaced", string json = """{"x":1}""") {
		return new ImportEvent {
			Stream = stream,
			EventType = eventType,
			Data = JsonDocument.Parse(json).RootElement
		};
	}

	[Test]
	public async Task Validate_Valid_Events_Preserves_Stream_Name() {
		var events = new[] { MakeEvent() };
		var (valid, errors) = await ImportValidator.Validate(events, [], AllowAll);

		await Assert.That(valid.Count).IsEqualTo(1);
		await Assert.That(valid[0].Stream).IsEqualTo("order-1");
		await Assert.That(valid[0].EventType).IsEqualTo("OrderPlaced");
		await Assert.That(errors).IsEmpty();
	}

	[Test]
	public async Task Validate_Rejects_System_Stream_Prefix() {
		var events = new[] { MakeEvent(stream: "$kontext-memory:foo") };
		var (valid, errors) = await ImportValidator.Validate(events, [], AllowAll);

		await Assert.That(valid).IsEmpty();
		await Assert.That(errors.Count).IsEqualTo(1);
		await Assert.That(errors[0].Message).Contains("system stream");
	}

	[Test]
	public async Task Validate_Missing_Stream_Returns_Error() {
		var events = new[] { MakeEvent(stream: "") };
		var (valid, errors) = await ImportValidator.Validate(events, [], AllowAll);

		await Assert.That(valid).IsEmpty();
		await Assert.That(errors[0].Message).Contains("missing stream name");
	}

	[Test]
	public async Task Validate_Missing_EventType_Returns_Error() {
		var events = new[] { MakeEvent(eventType: "") };
		var (valid, errors) = await ImportValidator.Validate(events, [], AllowAll);

		await Assert.That(valid).IsEmpty();
		await Assert.That(errors[0].Message).Contains("missing event type");
	}

	[Test]
	public async Task Validate_Missing_Data_Returns_Error() {
		var events = new[] { new ImportEvent { Stream = "s", EventType = "T" } };
		var (valid, errors) = await ImportValidator.Validate(events, [], AllowAll);

		await Assert.That(valid).IsEmpty();
		await Assert.That(errors[0].Message).Contains("missing data");
	}

	[Test]
	public async Task Validate_Mixed_Valid_And_Invalid() {
		var events = new[]
		{
			MakeEvent(stream: ""),
			MakeEvent(stream: "order-1"),
			MakeEvent(stream: "$system-stream"),
		};
		var (valid, errors) = await ImportValidator.Validate(events, [], AllowAll);

		await Assert.That(valid.Count).IsEqualTo(1);
		await Assert.That(valid[0].Stream).IsEqualTo("order-1");
		await Assert.That(errors.Count).IsEqualTo(2);
		await Assert.That(errors[0].Index).IsEqualTo(0);
		await Assert.That(errors[1].Index).IsEqualTo(2);
	}

	[Test]
	public async Task Validate_Empty_Array() {
		var (valid, errors) = await ImportValidator.Validate([], [], AllowAll);

		await Assert.That(valid).IsEmpty();
		await Assert.That(errors).IsEmpty();
	}

	[Test]
	public async Task Validate_Rejects_Stream_That_Doesnt_Match_Prefixes() {
		var events = new[]
		{
			MakeEvent(stream: "order-1"),
			MakeEvent(stream: "other-1"),
		};
		var (valid, errors) = await ImportValidator.Validate(events, ["order-", "customer-"], AllowAll);

		await Assert.That(valid.Count).IsEqualTo(1);
		await Assert.That(valid[0].Stream).IsEqualTo("order-1");
		await Assert.That(errors.Count).IsEqualTo(1);
		await Assert.That(errors[0].Message).Contains("other-1");
	}

	[Test]
	public async Task Validate_Empty_Prefix_List_Allows_Any_Stream() {
		var events = new[]
		{
			MakeEvent(stream: "anything-goes"),
			MakeEvent(stream: "another-1"),
		};
		var (valid, errors) = await ImportValidator.Validate(events, [], AllowAll);

		await Assert.That(valid.Count).IsEqualTo(2);
		await Assert.That(errors).IsEmpty();
	}

	[Test]
	public async Task Validate_Rejects_Events_For_Streams_Without_Write_Access() {
		var events = new[]
		{
			MakeEvent(stream: "order-1"),
			MakeEvent(stream: "order-2"),
		};
		// Deny writes to order-2 only.
		var (valid, errors) = await ImportValidator.Validate(
			events, [], stream => ValueTask.FromResult(stream != "order-2"));

		await Assert.That(valid.Count).IsEqualTo(1);
		await Assert.That(valid[0].Stream).IsEqualTo("order-1");
		await Assert.That(errors.Count).IsEqualTo(1);
		await Assert.That(errors[0].Index).IsEqualTo(1);
		await Assert.That(errors[0].Message).Contains("write access denied");
	}

	[Test]
	public async Task FormatResult_Includes_Stream_Counts() {
		var valid = new List<PendingEvent>
		{
			new("s1", "T", []),
			new("s1", "T", []),
			new("s2", "T", []),
		};
		var json = ImportValidator.FormatResult(valid, []);
		var doc = JsonDocument.Parse(json);

		await Assert.That(doc.RootElement.GetProperty("imported").GetInt32()).IsEqualTo(3);
		await Assert.That(doc.RootElement.GetProperty("streams").GetArrayLength()).IsEqualTo(2);
	}

	[Test]
	public async Task FormatResult_Omits_Errors_When_Empty() {
		var json = ImportValidator.FormatResult([], []);
		var doc = JsonDocument.Parse(json);

		await Assert.That(doc.RootElement.TryGetProperty("errors", out _)).IsFalse();
	}
}