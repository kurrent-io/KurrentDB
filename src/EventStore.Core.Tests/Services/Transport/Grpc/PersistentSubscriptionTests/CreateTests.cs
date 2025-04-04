// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.Client;
using EventStore.Client.PersistentSubscriptions;
using EventStore.Core.Tests.Services.Transport.Grpc.StreamsTests;
using Google.Protobuf;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Transport.Grpc.PersistentSubscriptionTests;

[TestFixture(typeof(LogFormat.V2), typeof(string), false)]
[TestFixture(typeof(LogFormat.V3), typeof(uint), false)]
[TestFixture(typeof(LogFormat.V2), typeof(string), true)]
[TestFixture(typeof(LogFormat.V3), typeof(uint), true)]
public class CreateTests<TLogFormat, TStreamId> : GrpcSpecification<TLogFormat, TStreamId> {
	private readonly bool _legacy;

	public CreateTests(bool legacy) {
		_legacy = legacy;
	}
	protected override Task Given() => Task.CompletedTask;

	protected override Task When() => Task.CompletedTask;

	[Test]
	public async Task can_create_persistent_subscription() {
		var client = new PersistentSubscriptions.PersistentSubscriptionsClient(Channel);

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
			ReadBatchSize = 50
		};

		if (_legacy) {
#pragma warning disable 612
			settings.NamedConsumerStrategy = CreateReq.Types.ConsumerStrategy.Pinned;
#pragma warning restore 612
		} else {
			settings.ConsumerStrategy = "Pinned";
		}

		await client.CreateAsync(
			new CreateReq {
				Options = new CreateReq.Types.Options {
					GroupName = "group",
					Stream = new CreateReq.Types.StreamOptions {
						Start = new Empty(),
						StreamIdentifier = new StreamIdentifier {
							StreamName = ByteString.CopyFromUtf8("stream")
						}
					},
					Settings = settings
				}
			},
			GetCallOptions(AdminCredentials));
	}
}
