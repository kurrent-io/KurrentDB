// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text.Json;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using Xunit;
using Xunit.Abstractions;

namespace EventStore.Projections.Core.Javascript.Tests.Integration;

public class MetadataSerializationTests : ProjectionRuntimeScenario {
	[Fact]
	public async Task CanHandleNulls() {
		var notification = Notify("emitted-stream");


		await WriteEvents("source-stream", ExpectedVersion.NoStream,
			new Event(Guid.NewGuid(), "foo", true,
				JsonSerializer.SerializeToUtf8Bytes(new object()), Array.Empty<byte>()));

		var js = @"
fromStream('source-stream').
    when({
        $any: function (s, e) {
            emit('emitted-stream',
			'event-type',
			e,
			{test:null});
		}
	});
";
		await SendProjectionMessage<ProjectionManagementMessage.Updated>(envelope =>
			new ProjectionManagementMessage.Command.Post(envelope, ProjectionMode.Continuous,
				"can-handle-null-metadata", ProjectionManagementMessage.RunAs.System, "js", js,
				true, true, true,
				false,
				true));

		await notification.WaitAsync(TestTimeout);
		var events = await ReadStream("emitted-stream", 0);
		var e = Assert.Single(events);
		JsonDocument doc = JsonDocument.Parse(e.Event.Metadata);
		Assert.True(doc.RootElement.TryGetProperty("test", out var prop));
		Assert.Equal(JsonValueKind.Null, prop.ValueKind);
	}

	[Fact]
	public async Task CanHandleEscapedMetadata() {
		var notification = Notify("emitted-stream");

		await WriteEvents("source-stream", ExpectedVersion.NoStream,
			new Event(Guid.NewGuid(), "foo", true,
				JsonSerializer.SerializeToUtf8Bytes(new object()), Array.Empty<byte>()));

		var js = @"
fromStream('source-stream').
    when({
        $any: function (s, e) {
            emit('emitted-stream',
			'event-type',
			e,
			{test:""\""some-data\""""});
		}
	});
";
		await SendProjectionMessage<ProjectionManagementMessage.Updated>(envelope =>
			new ProjectionManagementMessage.Command.Post(envelope, ProjectionMode.Continuous,
				"can-handle-null-metadata", ProjectionManagementMessage.RunAs.System, "js", js,
				true, true, true,
				false,
				true));


		await notification.WaitAsync(TestTimeout);
		var events = await ReadStream("emitted-stream", 0);
		var e = Assert.Single(events);
		JsonDocument doc = JsonDocument.Parse(e.Event.Metadata);
		Assert.True(doc.RootElement.TryGetProperty("test", out var prop));
		Assert.Equal(JsonValueKind.String, prop.ValueKind);
		Assert.Equal("\"some-data\"", prop.GetString());
	}
}
