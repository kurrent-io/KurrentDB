// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Client;
using EventStore.Client.PersistentSubscriptions;
using EventStore.Client.Streams;
using Google.Protobuf;
using Grpc.Core;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Transport.Grpc;
using KurrentDB.Core.Tests.Services.Transport.Grpc.StreamsTests;
using NUnit.Framework;
using MetadataConstants = KurrentDB.Core.Services.Transport.Grpc.Constants.Metadata;
using ReadReq = EventStore.Client.Streams.ReadReq;
using ReadResp = EventStore.Client.Streams.ReadResp;

namespace KurrentDB.Core.Tests.Services.Transport.Grpc;

[TestFixture]
public class PropertiesTests {
	private static readonly string StreamName = "stream";
	private static StreamIdentifier StreamIdentifier => new() { StreamName = ByteString.CopyFromUtf8(StreamName) };

	private static Dictionary<string, string> Properties => new() {
		{ "property-key-1", "value-1" },
		{ "property-key-2", "value-2" },
		{ "property-key-3", "value-3" },
	};

	private static AppendReq.Types.ProposedMessage CreateAppendReqEvent(Dictionary<string, string> properties) {
		var data = ByteString.CopyFromUtf8("test-data");
		var metadata = ByteString.CopyFromUtf8("test-metadata");
		properties ??= [];
		properties[MetadataConstants.ContentType] = MetadataConstants.ContentTypes.ApplicationOctetStream;
		properties[MetadataConstants.Type] = "test-type";

		return new AppendReq.Types.ProposedMessage {
			Data = data,
			Id = Uuid.NewUuid().ToDto(),
			CustomMetadata = metadata,
			Metadata = { properties }
		};
	}

	[TestFixture]
	public class single_append_with_properties : GrpcSpecification<LogFormat.V2, string> {
		private AppendReq.Types.ProposedMessage _proposedMessage;
		private AppendResp _appendResponse;
		private ReadResp _readResponse;

		protected override async Task Given() {
			_proposedMessage = CreateAppendReqEvent(Properties);

			using var call = StreamsClient.Append(GetCallOptions(AdminCredentials));
			await call.RequestStream.WriteAsync(new() {
				Options = new() {
					StreamIdentifier = StreamIdentifier,
					Any = new()
				}
			});

			await call.RequestStream.WriteAsync(new() { ProposedMessage = _proposedMessage });
			await call.RequestStream.CompleteAsync();
			_appendResponse = await call.ResponseAsync;
		}

		protected override async Task When() {
			using var call = StreamsClient.Read(new() {
				Options = new() {
					Count = 1,
					Stream = new() {
						StreamIdentifier = StreamIdentifier,
						Start = new()
					},
					UuidOption = new() { Structured = new() },
					ReadDirection = ReadReq.Types.Options.Types.ReadDirection.Forwards,
					NoFilter = new(),
					ControlOption = new() {
						Compatibility = 21
					}
				}
			});
			var readResponses = await call.ResponseStream.ReadAllAsync().ToArrayAsync();
			_readResponse = readResponses.Single(x => x.Event is not null);
		}

		[Test]
		public void append_is_successful() {
			Assert.AreEqual(_appendResponse.ResultCase, AppendResp.ResultOneofCase.Success);
		}

		[Test]
		public void read_contains_the_correct_metadata() {
			var readMetadata = _readResponse.Event.Event.Metadata;
			Assert.AreEqual(_proposedMessage.Metadata[MetadataConstants.ContentType], readMetadata[MetadataConstants.ContentType]);
			foreach (var (key, _) in Properties) {
				Assert.AreEqual(_proposedMessage.Metadata[key], readMetadata[key]);
			}
		}
	}

	[TestFixture]
	public class batch_append_with_properties : GrpcSpecification<LogFormat.V2, string> {
		private BatchAppendReq.Types.ProposedMessage _proposedMessage;
		private BatchAppendResp _appendResponse;
		private ReadResp _readResponse;

		protected override async Task Given() {
			_proposedMessage = CreateEvent("test-event", 10, 5, Properties);
			_appendResponse = await AppendToStreamBatch(new BatchAppendReq {
				Options = new() {
					Any = new(),
					StreamIdentifier = StreamIdentifier
				},
				IsFinal = true,
				ProposedMessages = { _proposedMessage },
				CorrelationId = Uuid.NewUuid().ToDto()
			});
		}

		protected override async Task When() {
			using var call = StreamsClient.Read(new() {
				Options = new() {
					Count = 1,
					Stream = new() {
						StreamIdentifier = StreamIdentifier,
						Start = new()
					},
					UuidOption = new() { Structured = new() },
					ReadDirection = ReadReq.Types.Options.Types.ReadDirection.Forwards,
					NoFilter = new(),
					ControlOption = new() {
						Compatibility = 21
					}
				}
			});
			var readResponses = await call.ResponseStream.ReadAllAsync().ToArrayAsync();
			_readResponse = readResponses.Single(x => x.Event is not null);
		}

		[Test]
		public void append_is_successful() {
			Assert.AreEqual(_appendResponse.ResultCase, BatchAppendResp.ResultOneofCase.Success);
		}

		[Test]
		public void read_contains_the_correct_metadata() {
			var readMetadata = _readResponse.Event.Event.Metadata;
			Assert.AreEqual(_proposedMessage.Metadata[MetadataConstants.ContentType], readMetadata[MetadataConstants.ContentType]);
			foreach (var (key, _) in Properties) {
				Assert.AreEqual(_proposedMessage.Metadata[key], readMetadata[key]);
			}
		}
	}

	[TestFixture]
	public class when_receiving_an_event_with_properties_over_persistent_subscription : GrpcSpecification<LogFormat.V2, string> {
		private readonly string _groupName = "test-group";
		private AppendReq.Types.ProposedMessage _proposedMessage;
		private AppendResp _appendResponse;
		private EventStore.Client.PersistentSubscriptions.ReadResp _readResponse;

		protected override async Task Given() {
			_proposedMessage = CreateAppendReqEvent(Properties);

			using var call = StreamsClient.Append(GetCallOptions(AdminCredentials));
			await call.RequestStream.WriteAsync(new() {
				Options = new() {
					StreamIdentifier = StreamIdentifier,
					Any = new()
				}
			});

			await call.RequestStream.WriteAsync(new() { ProposedMessage = _proposedMessage });
			await call.RequestStream.CompleteAsync();
			_appendResponse = await call.ResponseAsync;
		}

		protected override async Task When() {
			await PersistentSubscriptionsClient.CreateAsync(new CreateReq {
				Options = new() {
					GroupName = _groupName,
					Stream = new() {
						StreamIdentifier = StreamIdentifier,
						Start = new()
					},
					Settings = new() {
						ConsumerStrategy = SystemConsumerStrategies.RoundRobin,
						MaxRetryCount = 1,
						HistoryBufferSize = 10,
						LiveBufferSize = 10,
						ReadBatchSize = 5
					}
				}
			}, GetCallOptions(AdminCredentials));

			using var call = PersistentSubscriptionsClient.Read(GetCallOptions(AdminCredentials));
			await call.RequestStream.WriteAsync(new() {
				Options = new() {
					GroupName = _groupName,
					StreamIdentifier = StreamIdentifier,
					BufferSize = 10,
					UuidOption = new() {
						String = new()
					}
				}
			});
			await call.ResponseStream.MoveNext();
			Assert.True(call.ResponseStream.Current.ContentCase == EventStore.Client.PersistentSubscriptions.ReadResp.ContentOneofCase.SubscriptionConfirmation);

			await call.ResponseStream.MoveNext();
			_readResponse = call.ResponseStream.Current;
		}

		[Test]
		public void append_is_successful() {
			Assert.AreEqual(_appendResponse.ResultCase, AppendResp.ResultOneofCase.Success);
		}

		[Test]
		public void read_contains_the_correct_metadata() {
			var readMetadata = _readResponse.Event.Event.Metadata;
			Assert.AreEqual(_proposedMessage.Metadata[MetadataConstants.ContentType], readMetadata[MetadataConstants.ContentType]);
			foreach (var (key, _) in Properties) {
				Assert.AreEqual(_proposedMessage.Metadata[key], readMetadata[key]);
			}
		}
	}
}
