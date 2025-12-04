// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Tests.Modules.CustomIndexes;

public static class StreamsClientExtensions {
	public static ValueTask<AppendResponse> AppendEvent(
		this StreamsService.StreamsServiceClient self,
		string stream, string eventType, string jsonData, CancellationToken ct) =>

		self.AppendAsync(
			new() {
				ExpectedRevision = (long)ExpectedRevisionConstants.Any,
				Stream = stream,
				Records = {
					new AppendRecord() {
						RecordId = Guid.NewGuid().ToString(),
						Schema = new() {
							Name = eventType,
							Format = SchemaFormat.Json,
						},
						Data = ByteString.CopyFromUtf8(jsonData),
					}
				},
			},
			cancellationToken: ct);
}
