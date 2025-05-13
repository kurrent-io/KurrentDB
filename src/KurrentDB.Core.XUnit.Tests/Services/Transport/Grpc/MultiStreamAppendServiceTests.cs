// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable
#pragma warning disable CA1861 // Avoid constant arrays as arguments
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
#pragma warning disable xUnit2023 // Do not use collection methods for single-item collections

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Metrics;
using KurrentDB.Core.Services.Transport.Grpc;
using KurrentDB.Core.Tests.Authorization;
using KurrentDB.Core.Tests.Fakes;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Protocol.V2;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Services.Transport.Grpc;

public class MultiStreamAppendServiceTests {
	readonly AdHocPublisher _mainQueue = new();
	readonly AdHocAuthorizationProvider _authorizationProvider = new() { CheckAccess = (_, _) => true };
	readonly MultiStreamAppendService _sut;
	readonly TestServerCallContext _context = TestServerCallContext.Create();

	public MultiStreamAppendServiceTests() {
		_context.UserState["__HttpContext"] = new DefaultHttpContext();
		_sut = new(
			publisher: _mainQueue,
			authorizationProvider: _authorizationProvider,
			appendTracker: new DurationTracker.NoOp(),
			maxAppendSize: int.MaxValue,
			maxAppendEventSize: int.MaxValue,
			chunkSize: TFConsts.ChunkSize);
	}

	[Fact]
	public async Task when_successfully_appending_multiple_events_to_multiple_streams() {
		// write event 2 to stream-a
		// write events 5 & 6 to stream-b
		// given
		var event2Id = Guid.NewGuid();
		var event5Id = Guid.NewGuid();
		var event6Id = Guid.NewGuid();

		var request = new MultiStreamAppendRequest() {
			Input = {
				new AppendStreamRequest {
					Stream = "stream-a",
					ExpectedRevision = 1,
					Records = {
						new AppendRecord {
							Data = ByteString.CopyFromUtf8("event-2"),
							RecordId = event2Id.ToString(),
							Properties = {
								{ Constants.Properties.EventType, new() { BytesValue = ByteString.CopyFromUtf8("event-2-type") } },
								{ Constants.Properties.DataFormat, new() { BytesValue = ByteString.CopyFromUtf8("json") } },
							},
						},
					},
				},
				new AppendStreamRequest {
					Stream = "stream-b",
					ExpectedRevision = -2,
					Records = {
						new AppendRecord {
							Data = ByteString.CopyFromUtf8("event-5"),
							RecordId = event5Id.ToString(),
							Properties = {
								{ Constants.Properties.EventType, new() { BytesValue = ByteString.CopyFromUtf8("event-5-type") } },
								{ Constants.Properties.DataFormat, new() { BytesValue = ByteString.CopyFromUtf8("json") } },
							},
						},
						new AppendRecord {
							Data = ByteString.CopyFromUtf8("event-6"),
							RecordId = event6Id.ToString(),
							Properties = {
								{ Constants.Properties.EventType, new() { BytesValue = ByteString.CopyFromUtf8("event-6-type") } },
								{ Constants.Properties.DataFormat, new() { BytesValue = ByteString.CopyFromUtf8("avro") } },
							},
						},
					},
				},
			},
		};

		_mainQueue.OnPublish = message => {
			// check that the request was created as expected
			var writeEvents = Assert.IsType<ClientMessage.WriteEvents>(message);
			Assert.Equal(["stream-a", "stream-b"], writeEvents.EventStreamIds.Span);
			Assert.Equal([1, -2], writeEvents.ExpectedVersions.Span);
			Assert.Equal([0, 1, 1], writeEvents.EventStreamIndexes.Span);
			Assert.Equal(3, writeEvents.Events.Length);

			var proposedEvent2 = writeEvents.Events.Span[0];
			Assert.Equal(event2Id, proposedEvent2.EventId);
			Assert.Equal("event-2-type", proposedEvent2.EventType);
			Assert.True(proposedEvent2.IsJson);
			Assert.Equal(Encoding.UTF8.GetBytes("event-2"), proposedEvent2.Data);

			var proposedEvent5 = writeEvents.Events.Span[1];
			Assert.Equal(event5Id, proposedEvent5.EventId);
			Assert.Equal("event-5-type", proposedEvent5.EventType);
			Assert.True(proposedEvent5.IsJson);
			Assert.Equal(Encoding.UTF8.GetBytes("event-5"), proposedEvent5.Data);

			var proposedEvent6 = writeEvents.Events.Span[2];
			Assert.Equal(event6Id, proposedEvent6.EventId);
			Assert.Equal("event-6-type", proposedEvent6.EventType);
			Assert.False(proposedEvent6.IsJson);
			Assert.Equal(Encoding.UTF8.GetBytes("event-6"), proposedEvent6.Data);

			// send the response so that we can check that it is processed correctly
			writeEvents.Envelope.ReplyWith(new ClientMessage.WriteEventsCompleted(
				correlationId: writeEvents.CorrelationId,
				firstEventNumbers: new[] { 2L, 5L },
				lastEventNumbers: new[] { 2L, 6L },
				preparePosition: 100,
				commitPosition: 100));
		};

		// when
		var result = await _sut.MultiStreamAppend(request, _context);

		// then
		Assert.Collection(
			result.Success.Output,
			x => {
				Assert.Equal("stream-a", x.Stream);
				Assert.Equal(2, x.StreamRevision);
				Assert.Equal(100u, x.Position);
			},
			x => {
				Assert.Equal("stream-b", x.Stream);
				Assert.Equal(6, x.StreamRevision);
				Assert.Equal(100u, x.Position);
			});
	}

	[Fact]
	public async Task checks_access() {
		// given
		_context.GetHttpContext().User = new(new ClaimsIdentity([
			new Claim(ClaimTypes.Name, "the-user"),
		]));

		_authorizationProvider.CheckAccess = (claimsPrincipal, operation) => {
			Assert.Equal("the-user", claimsPrincipal.Identity!.Name);
			Assert.Equal("write", operation.Action);
			Assert.Equal(1, operation.Parameters.Length);
			var parameter = operation.Parameters.Span[0];
			Assert.Equal("streamId", parameter.Name);
			return parameter.Value == "stream-allowed";
		};

		// when
		var result = await _sut.MultiStreamAppend(
			new MultiStreamAppendRequest() {
				Input = {
					new AppendStreamRequest {
						Stream = "stream-allowed",
					},
					new AppendStreamRequest {
						Stream = "stream-denied",
					},
				},
			},
			_context);

		// then
		Assert.Collection(
			result.Failure.Output,
			x => {
				Assert.Equal("stream-denied", x.Stream);
				Assert.Equal("", x.AccessDenied.Reason);
			});
	}

	[Fact]
	public async Task when_converting_WriteEventsCompleted_fails() {
		// given
		var request = new MultiStreamAppendRequest() {
			Input = {
				new AppendStreamRequest {
					Stream = "my-stream",
					Records = {
						new AppendRecord {
							Properties = {
								{ Constants.Properties.EventType, new() { BytesValue = ByteString.CopyFromUtf8("my-event-type") } },
								{ Constants.Properties.DataFormat, new() { BytesValue = ByteString.CopyFromUtf8("json") } },
							}
						}
					}
				},
			},
		};

		_mainQueue.OnPublish = message => {
			var writeEvents = Assert.IsType<ClientMessage.WriteEvents>(message);
			writeEvents.Envelope.ReplyWith(new ClientMessage.WriteEventsCompleted(
				correlationId: Guid.NewGuid(),
				result: OperationResult.PrepareTimeout,
				message: "the details"));
		};

		// when
		var ex = await Assert.ThrowsAnyAsync<RpcException>(async () => {
			await _sut.MultiStreamAppend(request, _context);
		});

		// then
		Assert.Equal("Operation timed out: the details", ex.Status.Detail);
		Assert.Equal(StatusCode.Aborted, ex.Status.StatusCode);
	}

	[Fact]
	public async Task can_call_MultiStreamAppendSession() {
		// logic mostly shared with MultiStreamAppend non-streaming version.
		// given
		static async IAsyncEnumerable<AppendStreamRequest> GetRequests() {
			yield return new AppendStreamRequest {
				Stream = "stream-a",
				Records = {
					new AppendRecord {
						Data = ByteString.CopyFromUtf8("data"),
						RecordId = Guid.NewGuid().ToString(),
						Properties = {
							{ Constants.Properties.EventType, new() { BytesValue = ByteString.CopyFromUtf8("the-type") } },
							{ Constants.Properties.DataFormat, new() { BytesValue = ByteString.CopyFromUtf8("json") } },
						},
					},
				},
			};

			yield return new AppendStreamRequest {
				Stream = "stream-b",
				Records = {
					new AppendRecord {
						Data = ByteString.CopyFromUtf8("data"),
						RecordId = Guid.NewGuid().ToString(),
						Properties = {
							{ Constants.Properties.EventType, new() { BytesValue = ByteString.CopyFromUtf8("the-type") } },
							{ Constants.Properties.DataFormat, new() { BytesValue = ByteString.CopyFromUtf8("json") } },
						},
					},
				},
			};
		}

		_mainQueue.OnPublish = message => {
			var writeEvents = Assert.IsType<ClientMessage.WriteEvents>(message);
			// both requests appear in the ClientMessage.WriteEvents message that the sut produces
			Assert.Equal(["stream-a", "stream-b"], writeEvents.EventStreamIds.Span);

			writeEvents.Envelope.ReplyWith(new ClientMessage.WriteEventsCompleted(
				correlationId: writeEvents.CorrelationId,
				firstEventNumbers: new[] { 1L, 10L },
				lastEventNumbers: new[] { 3L, 12L },
				preparePosition: 100,
				commitPosition: 100));
		};

		// when
		var result = await _sut.MultiStreamAppendSession(
			FakeAsyncStreamReader.Create(GetRequests()),
			_context);

		// then
		// both requests appear in the response the sut sends
		Assert.Collection(
			result.Success.Output,
			x => Assert.Equal("stream-a", x.Stream),
			x => Assert.Equal("stream-b", x.Stream));
	}
}
