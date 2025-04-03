// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Client;
using EventStore.Client.PersistentSubscriptions;
using EventStore.Client.Streams;
using EventStore.Core.Tests.Services.Transport.Grpc.StreamsTests;
using Google.Protobuf;
using Grpc.Core;
using NUnit.Framework;
using ReadReq = EventStore.Client.PersistentSubscriptions.ReadReq;
using ReadResp = EventStore.Client.PersistentSubscriptions.ReadResp;
using StreamsReadReq = EventStore.Client.Streams.ReadReq;
using StreamsReadResp = EventStore.Client.Streams.ReadResp;

namespace EventStore.Core.Tests.Services.Transport.Grpc.PersistentSubscriptionTests;

public class GetInfoTests {
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class
		when_getting_info_for_persistent_subscription_group_on_stream<TLogFormat, TStreamId>
			: GrpcSpecification<TLogFormat, TStreamId> {
		private string _groupName = "test-group";
		private string _streamName = "test-stream";
		private SubscriptionInfo _actualSubscriptionInfo;
		private SubscriptionInfo _expectedSubscriptionInfo;
		private PersistentSubscriptions.PersistentSubscriptionsClient _persistentSubscriptionsClient;

		protected override async Task Given() {
			_persistentSubscriptionsClient = new PersistentSubscriptions.PersistentSubscriptionsClient(Channel);

			var settings = WithoutExtraStatistics(TestPersistentSubscriptionSettings);
			
			await _persistentSubscriptionsClient.CreateAsync(new CreateReq {
				Options = new CreateReq.Types.Options {
					GroupName = _groupName,
					Stream = new CreateReq.Types.StreamOptions {
						Start = new Empty(),
						StreamIdentifier = new StreamIdentifier {
							StreamName = ByteString.CopyFromUtf8(_streamName)
						}
					},
					Settings = settings
				}
			}, GetCallOptions(AdminCredentials));
			
			// create a connection to the persistent subscription
			var call = _persistentSubscriptionsClient.Read(GetCallOptions(AdminCredentials));
			await call.RequestStream.WriteAsync(new ReadReq {
				Options = new ReadReq.Types.Options {
					GroupName = _groupName,
					StreamIdentifier = new StreamIdentifier {
						StreamName = ByteString.CopyFromUtf8(_streamName)
					},
					UuidOption = new ReadReq.Types.Options.Types.UUIDOption {Structured = new Empty()},
					BufferSize = 10
				}
			});

			await call.ResponseStream.MoveNext();
			
			Assert.IsTrue(call.ResponseStream.Current.ContentCase == ReadResp.ContentOneofCase.SubscriptionConfirmation);

			var expectedConnection = new SubscriptionInfo.Types.ConnectionInfo() {
				Username = "admin",
				AvailableSlots = 10,
				ConnectionName = "\u003cunknown\u003e"
			};
			_expectedSubscriptionInfo = GetSubscriptionInfoFromSettings(settings,
				_groupName, _streamName, "0", string.Empty, new [] { expectedConnection });
		}

		private CreateReq.Types.Settings WithoutExtraStatistics(CreateReq.Types.Settings settings) {
			settings.ExtraStatistics = false;
			return settings;
		}

		protected override async Task When() {
			await WaitForSubscriptionsToBeLive(_persistentSubscriptionsClient, GetCallOptions(AdminCredentials));
			
			var resp = await _persistentSubscriptionsClient.GetInfoAsync(new GetInfoReq {
				Options = new GetInfoReq.Types.Options {
					GroupName = _groupName,
					StreamIdentifier = new StreamIdentifier {
						StreamName = ByteString.CopyFromUtf8(_streamName)
					}
				}
			}, GetCallOptions(AdminCredentials));
			_actualSubscriptionInfo = resp.SubscriptionInfo;
		}

		[Test]
		public void should_receive_the_correct_stats() {
			Assert.AreEqual(_expectedSubscriptionInfo, _actualSubscriptionInfo);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class
		when_listing_persistent_subscriptions_on_stream<TLogFormat, TStreamId>
			: GrpcSpecification<TLogFormat, TStreamId> {
		private string _streamName = "test-stream";
		private List<SubscriptionInfo> _actualSubscriptionInfo;
		private List<SubscriptionInfo> _expectedSubscriptionInfo = new();
		private PersistentSubscriptions.PersistentSubscriptionsClient _persistentSubscriptionsClient;

		protected override async Task Given() {
			_persistentSubscriptionsClient = new PersistentSubscriptions.PersistentSubscriptionsClient(Channel);

			var groupNames = new[] { "groupA", "groupB", "groupC" };
			foreach (var group in groupNames) {
				await _persistentSubscriptionsClient.CreateAsync(new CreateReq {
					Options = new CreateReq.Types.Options {
						GroupName = group,
						Stream = new CreateReq.Types.StreamOptions {
							Start = new Empty(),
							StreamIdentifier = new StreamIdentifier {
								StreamName = ByteString.CopyFromUtf8(_streamName)
							}
						},
						Settings = TestPersistentSubscriptionSettings
					}
				}, GetCallOptions(AdminCredentials));
				_expectedSubscriptionInfo.Add(GetSubscriptionInfoFromSettings(
					TestPersistentSubscriptionSettings, group, _streamName, "0", string.Empty));
			}
		}

		protected override async Task When() {
			await WaitForSubscriptionsToBeLive(_persistentSubscriptionsClient, GetCallOptions(AdminCredentials));
			
			ListResp resp = await _persistentSubscriptionsClient.ListAsync(new ListReq {
				Options = new ListReq.Types.Options {
					ListForStream = new ListReq.Types.StreamOption{
						Stream = new StreamIdentifier {
							StreamName = ByteString.CopyFromUtf8(_streamName)
						}
					}
				}
			}, GetCallOptions(AdminCredentials));
			_actualSubscriptionInfo = resp.Subscriptions.ToList();
		}

		[Test]
		public void should_receive_the_correct_stats() {
			CollectionAssert.AreEquivalent(_expectedSubscriptionInfo, _actualSubscriptionInfo);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class
		when_listing_persistent_subscriptions_on_all_stream<TLogFormat, TStreamId>
			: GrpcSpecification<TLogFormat, TStreamId> {
		private List<SubscriptionInfo> _actualSubscriptionInfo;
		private List<SubscriptionInfo> _expectedSubscriptionInfo = new();
		private PersistentSubscriptions.PersistentSubscriptionsClient _persistentSubscriptionsClient;

		protected override async Task Given() {
			_persistentSubscriptionsClient = new PersistentSubscriptions.PersistentSubscriptionsClient(Channel);

			var groupNames = new[] {"groupA", "groupB", "groupC"};
			foreach (var group in groupNames) {
				await _persistentSubscriptionsClient.CreateAsync(new CreateReq {
					Options = new CreateReq.Types.Options {
						GroupName = group,
						All = new CreateReq.Types.AllOptions {
							Start = new Empty()
						},
						Settings = TestPersistentSubscriptionSettings
					}
				}, GetCallOptions(AdminCredentials));
			}

			var events = await GetAllEvents(StreamsClient, GetCallOptions(AdminCredentials));

			var lastPosition = (long)events.Last().Event.CommitPosition;
			_expectedSubscriptionInfo = groupNames.Select(x =>
				GetSubscriptionInfoFromSettings(TestPersistentSubscriptionSettings, x, "$all", "C:0/P:0",
					$"C:{lastPosition}/P:{lastPosition}")).ToList();
		}

		protected override async Task When() {
			await WaitForSubscriptionsToBeLive(_persistentSubscriptionsClient, GetCallOptions(AdminCredentials));
			
			// Get the subscription info
			ListResp resp = await _persistentSubscriptionsClient.ListAsync(new ListReq {
				Options = new ListReq.Types.Options {
					ListForStream = new ListReq.Types.StreamOption {
						All = new Empty()
					}
				}
			}, GetCallOptions(AdminCredentials));
			_actualSubscriptionInfo = resp.Subscriptions.ToList();
		}

		[Test]
		public void should_receive_the_correct_stats() {
			// Set the buffer counts to 0 to make comparison easier
			_actualSubscriptionInfo.ForEach(x => x.LiveBufferCount = 0);
			_actualSubscriptionInfo.ForEach(x => x.ReadBufferCount = 0);
			CollectionAssert.AreEquivalent(_expectedSubscriptionInfo, _actualSubscriptionInfo);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class
		when_listing_all_persistent_subscriptions<TLogFormat, TStreamId>
			: GrpcSpecification<TLogFormat, TStreamId> {
		private List<SubscriptionInfo> _actualSubscriptionInfo;
		private List<SubscriptionInfo> _expectedSubscriptionInfo = new();
		private PersistentSubscriptions.PersistentSubscriptionsClient _persistentSubscriptionsClient;

		protected override async Task Given() {
			_persistentSubscriptionsClient = new PersistentSubscriptions.PersistentSubscriptionsClient(Channel);

			var subscriptionNames = new Dictionary<string, string[]> {
				{"streamA", new[] {"groupA"}},
				{"streamB", new[] {"groupB-1", "groupB-2"}},
				{"streamC", new[] {"groupC"}},
			};
			foreach (var (stream, groupNames) in subscriptionNames) {
				foreach (var group in groupNames) {
					await _persistentSubscriptionsClient.CreateAsync(new CreateReq {
						Options = new CreateReq.Types.Options {
							GroupName = group,
							Stream = new CreateReq.Types.StreamOptions {
								Start = new Empty(),
								StreamIdentifier = new StreamIdentifier {
									StreamName = ByteString.CopyFromUtf8(stream)
								}
							},
							Settings = TestPersistentSubscriptionSettings
						}
					}, GetCallOptions(AdminCredentials));
					_expectedSubscriptionInfo.Add(GetSubscriptionInfoFromSettings(
						TestPersistentSubscriptionSettings, group, stream, "0",	string.Empty));
				}
			}
			await _persistentSubscriptionsClient.CreateAsync(new CreateReq {
				Options = new CreateReq.Types.Options {
					GroupName = "groupD",
					All = new CreateReq.Types.AllOptions {
						Start = new Empty()
					},
					Settings = TestPersistentSubscriptionSettings
				}
			}, GetCallOptions(AdminCredentials));

			var events = await GetAllEvents(StreamsClient, GetCallOptions(AdminCredentials));

			var lastPosition = (long)events.Last().Event.CommitPosition;
			_expectedSubscriptionInfo.Add(GetSubscriptionInfoFromSettings(
				TestPersistentSubscriptionSettings, "groupD", "$all", "C:0/P:0",
				$"C:{lastPosition}/P:{lastPosition}"));
		}

		protected override async Task When() {
			await WaitForSubscriptionsToBeLive(_persistentSubscriptionsClient, GetCallOptions(AdminCredentials));
			
			var resp = await _persistentSubscriptionsClient.ListAsync(new ListReq {
				Options = new ListReq.Types.Options {
					ListAllSubscriptions = new Empty()
				}
			}, GetCallOptions(AdminCredentials));

			_actualSubscriptionInfo = resp.Subscriptions.ToList();
		}

		[Test]
		public void should_receive_the_correct_stats() {
			// Set the buffer counts to 0 to make comparison easier
			_actualSubscriptionInfo.ForEach(x => x.LiveBufferCount = 0);
			_actualSubscriptionInfo.ForEach(x => x.ReadBufferCount = 0);
			CollectionAssert.AreEquivalent(_expectedSubscriptionInfo, _actualSubscriptionInfo);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class
		when_getting_info_for_persistent_subscription_group_on_all<TLogFormat, TStreamId>
			: GrpcSpecification<TLogFormat, TStreamId> {
		private string _groupName = "test-group";
		private SubscriptionInfo _actualSubscriptionInfo;
		private SubscriptionInfo _expectedSubscriptionInfo;
		private PersistentSubscriptions.PersistentSubscriptionsClient _persistentSubscriptionsClient;

		protected override async Task Given() {
			_persistentSubscriptionsClient = new PersistentSubscriptions.PersistentSubscriptionsClient(Channel);

			await _persistentSubscriptionsClient.CreateAsync(new CreateReq {
				Options = new CreateReq.Types.Options {
					GroupName = _groupName,
					All = new CreateReq.Types.AllOptions {
						Start = new Empty()
					},
					Settings = TestPersistentSubscriptionSettings
				}
			}, GetCallOptions(AdminCredentials));

			var events = await GetAllEvents(StreamsClient, GetCallOptions(AdminCredentials));

			var lastPosition = (long)events.Last().Event.CommitPosition;
			_expectedSubscriptionInfo =
				GetSubscriptionInfoFromSettings(TestPersistentSubscriptionSettings, _groupName,
					"$all", "C:0/P:0", $"C:{lastPosition}/P:{lastPosition}");
		}

		protected override async Task When() {
			await WaitForSubscriptionsToBeLive(_persistentSubscriptionsClient, GetCallOptions(AdminCredentials));
			
			var resp = await _persistentSubscriptionsClient.GetInfoAsync(new GetInfoReq {
				Options = new GetInfoReq.Types.Options {
					GroupName = _groupName,
					All = new Empty()
				}
			}, GetCallOptions(AdminCredentials));
			_actualSubscriptionInfo = resp.SubscriptionInfo;
		}

		[Test]
		public void should_receive_the_correct_stats() {
			// Set the buffer counts to 0 to make comparison easier
			_actualSubscriptionInfo.LiveBufferCount = 0;
			_actualSubscriptionInfo.ReadBufferCount = 0;
			Assert.AreEqual(_expectedSubscriptionInfo, _actualSubscriptionInfo);
		}
	}

	private static async Task<List<StreamsReadResp>> GetAllEvents(Streams.StreamsClient client, CallOptions callOptions) {
		var call = client.Read(new() {
			Options = new() {
				ReadDirection = StreamsReadReq.Types.Options.Types.ReadDirection.Forwards,
				Count = 100,
				NoFilter = new(),
				UuidOption = new() { Structured = new() },
				All = new() {
					Start = new()
				}
			}
		}, callOptions);

		var events = new List<StreamsReadResp>();
		await foreach (var evnt in call.ResponseStream.ReadAllAsync()) {
			if (evnt.ContentCase is StreamsReadResp.ContentOneofCase.Event) {
				events.Add(evnt);

			}
		}
		return events;
	}

	private static CreateReq.Types.Settings TestPersistentSubscriptionSettings => new CreateReq.Types.Settings {
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

	private static SubscriptionInfo GetSubscriptionInfoFromSettings(
		CreateReq.Types.Settings settings, string groupName, string streamName, string startFrom,
		string lastKnownEvent, SubscriptionInfo.Types.ConnectionInfo[] connections=null ) {
		var info = new SubscriptionInfo() {
			EventSource = streamName,
			GroupName = groupName,
			Status = "Live",
			AveragePerSecond = 0,
			TotalItems = 0,
			CountSinceLastMeasurement = 0,
			LastCheckpointedEventPosition = string.Empty,
			LastKnownEventPosition = lastKnownEvent,
			ResolveLinkTos = settings.ResolveLinks,
			StartFrom = startFrom,
			MessageTimeoutMilliseconds = settings.MessageTimeoutMs,
			ExtraStatistics = settings.ExtraStatistics,
			MaxRetryCount = settings.MaxRetryCount,
			LiveBufferSize = settings.LiveBufferSize,
			BufferSize = settings.HistoryBufferSize,
			ReadBatchSize = settings.ReadBatchSize,
			CheckPointAfterMilliseconds = settings.CheckpointAfterMs,
			MinCheckPointCount = settings.MinCheckpointCount,
			MaxCheckPointCount = settings.MaxCheckpointCount,
			ReadBufferCount = 0,
			LiveBufferCount = 0,
			RetryBufferCount = 0,
			TotalInFlightMessages = 0,
			OutstandingMessagesCount = 0,
			NamedConsumerStrategy = "Pinned",
			MaxSubscriberCount = settings.MaxSubscriberCount,
			ParkedMessageCount = 0
		};

		if (connections != null) {
			info.Connections.AddRange(connections);
		}

		return info;
	}

	private static async Task WaitForSubscriptionsToBeLive(PersistentSubscriptions.PersistentSubscriptionsClient client, CallOptions callOptions) {
		var resp = await GetSubscriptions();

		for (int i = 0; resp.Subscriptions.Any(s => s.Status != "Live"); i++) {
			Assert.AreNotEqual(5, i, "Reached too many retries to get all subscriptions live!");
			
			await Task.Delay(500);
			resp = await GetSubscriptions();
		}
		
		async Task<ListResp> GetSubscriptions() {
			return await client.ListAsync(new ListReq {
				Options = new ListReq.Types.Options {
					ListAllSubscriptions = new Empty()
				}
			}, callOptions);
		}
	}
}
