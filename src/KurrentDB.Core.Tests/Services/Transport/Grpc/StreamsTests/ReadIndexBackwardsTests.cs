// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Client.Streams;
using Grpc.Core;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Storage;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.Transport.Grpc.StreamsTests;

[TestFixture]
public class ReadIndexBackwardsTests {
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_reading_index_backwards<TLogFormat, TStreamId>
	  : GrpcSpecification<TLogFormat, TStreamId> {
		private const string IndexId = SystemStreams.DefaultSecondaryIndex;

		private readonly List<ReadResp> _responses = new();

		public when_reading_index_backwards() : base(new LotsOfExpiriesStrategy(), secondaryIndexReaders: new SecondaryIndexReaders([new FakeSecondaryIndexReader()])) {
		}

		protected override Task Given() {
			return Task.CompletedTask;
		}

		protected override async Task When() {
			using var call = StreamsClient.Read(new() {
				Options = new() {
					UuidOption = new() { Structured = new() },
					Count = 20,
					ReadDirection = ReadReq.Types.Options.Types.ReadDirection.Backwards,
					ResolveLinks = true,
					All = new() {
						End = new()
					},
					Filter = new() {
						Max = 32,
						CheckpointIntervalMultiplier = 4,
						StreamIdentifier = new() { Prefix = { IndexId } }
					}
				}
			}, GetCallOptions(AdminCredentials));
			_responses.AddRange(await call.ResponseStream.ReadAllAsync().ToArrayAsync());
		}

		[Test]
		public void should_read_a_number_of_events_equal_to_the_max_count() {
			Assert.AreEqual(20, _responses.Count(x => x.Event is not null));
		}
	}
}
