// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Client.Streams;
using EventStore.Core.Data;
using EventStore.Core.Services.Transport.Common;
using EventStore.Core.Services.Transport.Grpc;
using Google.Protobuf;
using Grpc.Core;
using NUnit.Framework;
using GrpcConstants = EventStore.Core.Services.Transport.Grpc.Constants;

namespace EventStore.Core.Tests.Services.Transport.Grpc.StreamsTests;

[TestFixture]
public class TombstoneTests {
	private const string StreamName = nameof(TombstoneTests);

	public abstract class TombstoneExistingStreamSpecification<TLogFormat, TStreamId>
		: GrpcSpecification<TLogFormat, TStreamId> {
		private readonly TombstoneReq.Types.Options _options;
		private Exception _caughtException;

		private TombstoneExistingStreamSpecification(TombstoneReq.Types.Options options) {
			_options = new(options) {
				StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8(StreamName) }
			};
		}

		protected TombstoneExistingStreamSpecification(StreamRevision expectedStreamRevision) : this(
			new TombstoneReq.Types.Options {
				Revision = expectedStreamRevision.ToUInt64(),
			}) { }

		protected TombstoneExistingStreamSpecification(long expectedStreamState) : this(expectedStreamState switch {
			ExpectedVersion.Any => new TombstoneReq.Types.Options { Any = new() },
			ExpectedVersion.NoStream => new TombstoneReq.Types.Options { NoStream = new() },
			ExpectedVersion.StreamExists => new TombstoneReq.Types.Options { StreamExists = new() },
			_ => throw new InvalidOperationException()
		}){ }

		[Test]
		public void no_exception_is_thrown() {
			Assert.Null(_caughtException);
		}
		
		[Test]
		public void the_stream_is_deleted() {
			var ex = Assert.ThrowsAsync<RpcException>(async () => {
				using var call = StreamsClient.Read(new () {
					Options = new() {
						UuidOption = new() { Structured = new() },
						NoFilter = new(),
						ResolveLinks = false,
						ReadDirection = ReadReq.Types.Options.Types.ReadDirection.Forwards,
						Count = 1,
						Stream = new() {
							StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8(StreamName) },
							Start = new()
						}
					}
				}, GetCallOptions());

				await call.ResponseStream.ReadAllAsync().ToArrayAsync();
			});

			Assert.NotNull(ex);
			Assert.AreEqual(StatusCode.FailedPrecondition, ex.Status.StatusCode);
			Assert.Contains((GrpcConstants.Exceptions.ExceptionKey, GrpcConstants.Exceptions.StreamDeleted),
				ex.Trailers.Select(x => (x.Key, x.Value)).ToArray());
		}

		protected override Task Given() => _options.ExpectedStreamRevisionCase switch {
			TombstoneReq.Types.Options.ExpectedStreamRevisionOneofCase.NoStream => Task.CompletedTask,
			_ => AppendToStreamBatch(new BatchAppendReq {
				Options = new() {
					NoStream = new(),
					StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8(StreamName) }
				},
				CorrelationId = Uuid.NewUuid().ToDto(),
				IsFinal = true,
				ProposedMessages = { CreateEvents(1) }
			}).AsTask()
		};

		protected override async Task When() {
			using var call = StreamsClient.TombstoneAsync(new() {
				Options = _options
			}, GetCallOptions());

			try {
				await call.ResponseAsync;
			} catch (Exception ex) {
				_caughtException = ex;
			}
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class TombstoneExistingStreamExpectedRevision<TLogFormat, TStreamId>
		: TombstoneExistingStreamSpecification<TLogFormat, TStreamId> {
		public TombstoneExistingStreamExpectedRevision() : base(StreamRevision.Start) { }
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class TombstoneExistingStreamAny<TLogFormat, TStreamId>
		: TombstoneExistingStreamSpecification<TLogFormat, TStreamId> {
		public TombstoneExistingStreamAny() : base(ExpectedVersion.Any) { }
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class TombstoneExistingStreamNoStream<TLogFormat, TStreamId>
		: TombstoneExistingStreamSpecification<TLogFormat, TStreamId> {
		public TombstoneExistingStreamNoStream() : base(ExpectedVersion.NoStream) { }
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class TombstoneExistingStreamExists<TLogFormat, TStreamId>
		: TombstoneExistingStreamSpecification<TLogFormat, TStreamId> {
		public TombstoneExistingStreamExists() : base(ExpectedVersion.StreamExists) { }
	}
}
