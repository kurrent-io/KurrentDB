// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Tests.Streams.AppendRecords;

static class AppendRecordsFixture {
	public static AppendRecord CreateRecord(string stream) =>
		new() {
			Stream   = stream,
			RecordId = Guid.NewGuid().ToString(),
			Schema = new SchemaInfo {
				Name   = "TestEvent.V1",
				Format = SchemaFormat.Json
			},
			Data = ByteString.CopyFromUtf8("{\"test\": true}")
		};

	public static AppendRecordsRequest SeedRequest(string stream, int count = 1) =>
		new() {
			Records = { Enumerable.Range(0, count).Select(_ => CreateRecord(stream)) }
		};

	public static AppendRecordsRequest WriteRequest(string stream) =>
		new() {
			Records = { CreateRecord(stream) }
		};

	public static AppendRecordsRequest WriteOnlyRequest(string stream, long expectedState) =>
		new() {
			Records = { CreateRecord(stream) },
			Checks = {
				new ConsistencyCheck {
					StreamState = new ConsistencyCheck.Types.StreamStateCheck {
						Stream        = stream,
						ExpectedState = expectedState
					}
				}
			}
		};

	public static AppendRecordsRequest CheckOnlyRequest(string writeStream, string checkStream, long expectedState) =>
		new() {
			Records = { CreateRecord(writeStream) },
			Checks = {
				new ConsistencyCheck {
					StreamState = new ConsistencyCheck.Types.StreamStateCheck {
						Stream        = checkStream,
						ExpectedState = expectedState
					}
				}
			}
		};
}
