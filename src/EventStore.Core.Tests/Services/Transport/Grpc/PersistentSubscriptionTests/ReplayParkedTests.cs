// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Client;
using EventStore.Client.PersistentSubscriptions;
using EventStore.Client.Streams;
using EventStore.Core.Services.Transport.Grpc;
using EventStore.Core.Tests.Services.Transport.Grpc.StreamsTests;
using Google.Protobuf;
using Grpc.Core;
using NUnit.Framework;
using ReadReq = EventStore.Client.PersistentSubscriptions.ReadReq;
using ReadResp = EventStore.Client.PersistentSubscriptions.ReadResp;
using StreamsReadReq = EventStore.Client.Streams.ReadReq;
using StreamsReadResp = EventStore.Client.Streams.ReadResp;

namespace EventStore.Core.Tests.Services.Transport.Grpc.PersistentSubscriptionTests;

public class ReplayParkedTests {
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class
		when_replaying_parked_messages_with_no_limit_for_existing_subscription<TLogFormat, TStreamId>
			: GrpcSpecification<TLogFormat, TStreamId> {
		private string _groupName = "test-group";
		private string _streamName = "test-stream";
		private int _eventCount = 10;
		private PersistentSubscriptions.PersistentSubscriptionsClient _persistentSubscriptionsClient;
		private AsyncDuplexStreamingCall<ReadReq, ReadResp> _subscriptionStream;
		private List<ulong> _expectedEventVersions = new List<ulong>();
		private List<ulong> _actualEventVersions = new List<ulong>();

		protected override async Task Given() {
			_persistentSubscriptionsClient = new PersistentSubscriptions.PersistentSubscriptionsClient(Channel);

			await CreateTestPersistentSubscription(_persistentSubscriptionsClient, _streamName, _groupName, GetCallOptions(AdminCredentials));

			_subscriptionStream = await SubscribeToPersistentSubscription(
				_persistentSubscriptionsClient, _groupName, _streamName, GetCallOptions(AdminCredentials));

			// Write events to the subscription
			var writeRes = await AppendToStreamBatch(new BatchAppendReq {
				Options = new() {
					Any = new(),
					StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8(_streamName) }
				},
				IsFinal = true,
				ProposedMessages = { CreateEvents(_eventCount) },
				CorrelationId = Uuid.NewUuid().ToDto(),
			});
			Assert.NotNull(writeRes.Success);

			// Park all of the messages
			for(var i = 0; i < _eventCount; i++) {
				Assert.True(await _subscriptionStream.ResponseStream.MoveNext());
				Assert.AreEqual(ReadResp.ContentOneofCase.Event, _subscriptionStream.ResponseStream.Current.ContentCase);
				var evnt = _subscriptionStream.ResponseStream.Current.Event;
				await _subscriptionStream.RequestStream.WriteAsync(new ReadReq {
					Nack = new ReadReq.Types.Nack {
						Action = ReadReq.Types.Nack.Types.Action.Park,
						Ids = {
						evnt.Event.Id
					},
						Reason = "testing"
					}
				});
			}

			// Check that the events were parked
			await WaitForEventsInStream(
				StreamsClient, $"$persistentsubscription-{_streamName}::{_groupName}-parked", _eventCount, GetCallOptions(AdminCredentials));
		}

		protected override async Task When() {
			await _persistentSubscriptionsClient.ReplayParkedAsync(new ReplayParkedReq {
				Options = new ReplayParkedReq.Types.Options {
					GroupName = _groupName,
					StreamIdentifier = new StreamIdentifier {
						StreamName = ByteString.CopyFromUtf8(_streamName)
					},
					NoLimit = new()
				}
			}, GetCallOptions(AdminCredentials));
			for (var i = 0; i < _eventCount; i++) {
				Assert.True(await _subscriptionStream.ResponseStream.MoveNext());
				var curr = _subscriptionStream.ResponseStream.Current;
				Assert.AreEqual(ReadResp.ContentOneofCase.Event, curr.ContentCase);
				_actualEventVersions.Add(curr.Event.Event.StreamRevision);

				// Ack the event otherwise the test can throw an error
				await _subscriptionStream.RequestStream.WriteAsync(new ReadReq {
						Ack = new ReadReq.Types.Ack {
							Ids = {
							curr.Event.Event.Id
						},
					}
				});
			}
		}

		[Test]
		public void should_receive_replayed_messages() {
			_expectedEventVersions = Enumerable.Range(0, _eventCount).Select(x => (ulong)x).ToList();
			CollectionAssert.AreEqual(_expectedEventVersions, _actualEventVersions);
		}

		[TearDown]
		public void Teardown() {
			_subscriptionStream.Dispose();
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class
		when_replaying_parked_messages_with_a_replay_limit_for_existing_subscription<TLogFormat, TStreamId>
			: GrpcSpecification<TLogFormat, TStreamId> {
		private string _groupName = "test-group";
		private string _streamName = "test-stream";
		private int _eventCount = 10;
		private PersistentSubscriptions.PersistentSubscriptionsClient _persistentSubscriptionsClient;
		private AsyncDuplexStreamingCall<ReadReq, ReadResp> _subscriptionStream;
		private List<ulong> _expectedEventVersions = new List<ulong>();
		private List<ulong> _actualEventVersions = new List<ulong>();

		protected override async Task Given() {
			_persistentSubscriptionsClient = new PersistentSubscriptions.PersistentSubscriptionsClient(Channel);

			await CreateTestPersistentSubscription(_persistentSubscriptionsClient, _streamName, _groupName, GetCallOptions(AdminCredentials));

			_subscriptionStream = await SubscribeToPersistentSubscription(
				_persistentSubscriptionsClient, _groupName, _streamName, GetCallOptions(AdminCredentials));

			// Write events to the subscription
			var writeRes = await AppendToStreamBatch(new BatchAppendReq {
				Options = new() {
					Any = new(),
					StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8(_streamName) }
				},
				IsFinal = true,
				ProposedMessages = { CreateEvents(_eventCount) },
				CorrelationId = Uuid.NewUuid().ToDto(),
			});
			Assert.NotNull(writeRes.Success);

			// Park all of the messages
			for (var i = 0; i < _eventCount; i++) {
				Assert.True(await _subscriptionStream.ResponseStream.MoveNext());
				Assert.AreEqual(ReadResp.ContentOneofCase.Event, _subscriptionStream.ResponseStream.Current.ContentCase);
				var evnt = _subscriptionStream.ResponseStream.Current.Event;
				//_allEventIds.Add(Uuid.FromDto(evnt.Event.Id));
				await _subscriptionStream.RequestStream.WriteAsync(new ReadReq {
					Nack = new ReadReq.Types.Nack {
						Action = ReadReq.Types.Nack.Types.Action.Park,
						Ids = {
						evnt.Event.Id
					},
						Reason = "testing"
					}
				});
			}

			// Check that the events were parked
			await WaitForEventsInStream(
				StreamsClient, $"$persistentsubscription-{_streamName}::{_groupName}-parked", _eventCount, GetCallOptions(AdminCredentials));
		}

		protected override async Task When() {
			await _persistentSubscriptionsClient.ReplayParkedAsync(new ReplayParkedReq {
				Options = new ReplayParkedReq.Types.Options {
					GroupName = _groupName,
					StreamIdentifier = new StreamIdentifier {
						StreamName = ByteString.CopyFromUtf8(_streamName)
					},
					StopAt = 1
				}
			}, GetCallOptions(AdminCredentials));

			Assert.True(await _subscriptionStream.ResponseStream.MoveNext());
			var evnt = _subscriptionStream.ResponseStream.Current.Event.Event;
			_actualEventVersions.Add(evnt.StreamRevision);
			// Ack the event otherwise the test can throw an error
			await _subscriptionStream.RequestStream.WriteAsync(new ReadReq {
				Ack = new ReadReq.Types.Ack {
					Ids = {
						evnt.Id
					},
				}
			});

			// Write one more event so we can be sure the other parked messages aren't being replayed
			var writeRes = await AppendToStreamBatch(new BatchAppendReq {
				Options = new() {
					Any = new(),
					StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8(_streamName) }
				},
				IsFinal = true,
				ProposedMessages = { CreateEvents(1) },
				CorrelationId = Uuid.NewUuid().ToDto(),
			});
			Assert.NotNull(writeRes.Success);

			Assert.True(await _subscriptionStream.ResponseStream.MoveNext());
			evnt = _subscriptionStream.ResponseStream.Current.Event.Event;
			_actualEventVersions.Add(evnt.StreamRevision);

			// Ack the event otherwise the test can throw an error
			await _subscriptionStream.RequestStream.WriteAsync(new ReadReq {
				Ack = new ReadReq.Types.Ack {
					Ids = {
						evnt.Id
					},
				}
			});
		}

		[Test]
		public void should_receive_one_replayed_message_and_new_message() {
			_expectedEventVersions = new List<ulong> { 0, 10 };
			CollectionAssert.AreEqual(_expectedEventVersions, _actualEventVersions);
		}

		[TearDown]
		public void Teardown() {
			_subscriptionStream.Dispose();
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class
		when_replaying_parked_messages_for_non_existent_subscription<TLogFormat, TStreamId>
			: GrpcSpecification<TLogFormat, TStreamId> {
		private string _groupName = "test-group";
		private string _streamName = "test-stream";
		private PersistentSubscriptions.PersistentSubscriptionsClient _persistentSubscriptionsClient;
		private Exception _exception;

		protected override Task Given() {
			_persistentSubscriptionsClient = new PersistentSubscriptions.PersistentSubscriptionsClient(Channel);
			return Task.CompletedTask;
		}

		protected override async Task When() {
			try {
				await _persistentSubscriptionsClient.ReplayParkedAsync(new ReplayParkedReq {
					Options = new ReplayParkedReq.Types.Options {
						GroupName = _groupName,
						StreamIdentifier = new StreamIdentifier {
							StreamName = ByteString.CopyFromUtf8(_streamName)
						},
						NoLimit = new()
					}
				}, GetCallOptions(AdminCredentials));
			} catch(Exception e) {
				_exception = e;
			}
		}

		[Test]
		public void should_receive_not_found_error() {
			Assert.IsInstanceOf<RpcException>(_exception);
			Assert.AreEqual(StatusCode.NotFound, ((RpcException)_exception).Status.StatusCode);
		}
	}

	private static async Task<AsyncDuplexStreamingCall<ReadReq, ReadResp>> SubscribeToPersistentSubscription(
		PersistentSubscriptions.PersistentSubscriptionsClient client, string groupName, string streamName, CallOptions callOptions) {
		var call = client.Read(callOptions);

		await call.RequestStream.WriteAsync(new ReadReq {
			Options = new ReadReq.Types.Options {
				GroupName = groupName,
				StreamIdentifier = new StreamIdentifier {
					StreamName = ByteString.CopyFromUtf8(streamName)
				},
				BufferSize = 10,
				UuidOption = new ReadReq.Types.Options.Types.UUIDOption {
					Structured = new Empty()
				}
			}
		});
		if (!await call.ResponseStream.MoveNext() ||
			call.ResponseStream.Current.ContentCase != ReadResp.ContentOneofCase.SubscriptionConfirmation) {
			throw new InvalidOperationException();
		}

		return call;
	}

	private static async Task<StreamsReadResp[]> WaitForEventsInStream(
		Streams.StreamsClient streamsClient, string streamName, int count, CallOptions callOptions) {
		using var call = streamsClient.Read(new() {
			Options = new() {
				Subscription = new(),
				NoFilter = new(),
				ReadDirection = StreamsReadReq.Types.Options.Types.ReadDirection.Forwards,
				UuidOption = new() { Structured = new() },
				Stream = new() {
					StreamIdentifier = new() {
						StreamName = ByteString.CopyFromUtf8(streamName)
					},
					Start = new()
				}
			}
		}, callOptions);

		var events = new List<StreamsReadResp>();
		await foreach (var evnt in call.ResponseStream.ReadAllAsync()) {
			if (evnt.ContentCase is StreamsReadResp.ContentOneofCase.Event) {
				events.Add(evnt);
				if (events.Count >= count)
					break;
			}
		}
		return events.ToArray();
	}

	private static async Task CreateTestPersistentSubscription(
		PersistentSubscriptions.PersistentSubscriptionsClient client, string streamName, string groupName, CallOptions callOptions) {
		var settings = new CreateReq.Types.Settings {
			CheckpointAfterMs = 10000,
			ExtraStatistics = true,
			MaxCheckpointCount = 20,
			MinCheckpointCount = 10,
			MaxRetryCount = 30,
			MaxSubscriberCount = 40,
			MessageTimeoutMs = 20000,
			HistoryBufferSize = 60,
			LiveBufferSize = 10,
			ConsumerStrategy = "Pinned",
			ReadBatchSize = 50
		};

		await client.CreateAsync(new CreateReq {
			Options = new CreateReq.Types.Options {
				GroupName = groupName,
				Stream = new CreateReq.Types.StreamOptions {
					Start = new Empty(),
					StreamIdentifier = new StreamIdentifier {
						StreamName = ByteString.CopyFromUtf8(streamName)
					}
				},
				Settings = settings
			}
		}, callOptions);
	}
}
