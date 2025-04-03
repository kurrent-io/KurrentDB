// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Client.Streams;
using EventStore.ClientAPI;
using EventStore.Core.Services;
using EventStore.Core.Services.Transport.Common;
using EventStore.Core.Services.Transport.Grpc;
using Google.Protobuf;
using Grpc.Core;
using NUnit.Framework;
using ExpectedVersion = EventStore.Core.Data.ExpectedVersion;
using Metadata=EventStore.Core.Services.Transport.Grpc.Constants.Metadata;
namespace EventStore.Core.Tests.Services.Transport.Grpc.StreamsTests;

[TestFixture]
public class DeleteTests {
	private const string StreamName = nameof(DeleteTests);

	public abstract class DeleteExistingStreamSpecification<TLogFormat, TStreamId>
		: GrpcSpecification<TLogFormat, TStreamId> {
		private readonly DeleteReq.Types.Options _options;
		private Exception _caughtException;

		private DeleteExistingStreamSpecification(DeleteReq.Types.Options options) {
			_options = new(options) {
				StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8(StreamName) }
			};
		}

		protected DeleteExistingStreamSpecification(StreamRevision expectedStreamRevision) : this(
			new DeleteReq.Types.Options {
				Revision = expectedStreamRevision.ToUInt64(),
			}) {
		}

		protected DeleteExistingStreamSpecification(long expectedStreamState) : this(expectedStreamState switch {
			ExpectedVersion.Any => new DeleteReq.Types.Options { Any = new() },
			ExpectedVersion.NoStream => new DeleteReq.Types.Options { NoStream = new() },
			ExpectedVersion.StreamExists => new DeleteReq.Types.Options { StreamExists = new() },
			_ => throw new InvalidOperationException()
		}) {
		}

		[Test]
		public void no_exception_is_thrown() {
			Assert.Null(_caughtException);
		}

		[Test]
		public async Task the_stream_is_deleted() {
			using var call = StreamsClient.Read(new() {
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

			var responses = await call.ResponseStream.ReadAllAsync().ToArrayAsync();

			Assert.AreEqual(1, responses.Length);
			Assert.AreEqual(new ReadResp {
				StreamNotFound = new() {
					StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8(StreamName) }
				}
			}, responses[0]);
		}

		protected override async Task Given() => await AppendToStreamBatch(new BatchAppendReq {
			Options = new() {
				NoStream = new(),
				StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8(StreamName) }
			},
			CorrelationId = Uuid.NewUuid().ToDto(),
			IsFinal = true,
			ProposedMessages = { CreateEvents(1) }
		});

		protected override async Task When() {
			using var call = StreamsClient.DeleteAsync(new() {
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
	public class DeleteExistingStreamExpectedRevision<TLogFormat, TStreamId>
		: DeleteExistingStreamSpecification<TLogFormat, TStreamId> {
		public DeleteExistingStreamExpectedRevision() : base(StreamRevision.Start) { }
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class DeleteExistingStreamAny<TLogFormat, TStreamId>
		: DeleteExistingStreamSpecification<TLogFormat, TStreamId> {
		public DeleteExistingStreamAny() : base(ExpectedVersion.Any) { }
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class DeleteExistingStreamExists<TLogFormat, TStreamId>
		: DeleteExistingStreamSpecification<TLogFormat, TStreamId> {
		public DeleteExistingStreamExists() : base(ExpectedVersion.StreamExists) { }
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class DeleteNoStream<TLogFormat, TStreamId> : GrpcSpecification<TLogFormat, TStreamId> {
		private Exception _caughtException;
		protected override Task Given() => Task.CompletedTask;

		protected override async Task When() {
			using var call = StreamsClient.DeleteAsync(new() {
				Options = new() {
					NoStream = new(),
					StreamIdentifier = new() {
						StreamName = ByteString.CopyFromUtf8(Guid.NewGuid().ToString())
					}
				}
			}, GetCallOptions());

			try {
				await call.ResponseAsync;
			} catch (Exception ex) {
				_caughtException = ex;
			}
		}

		[Test]
		public void an_exception_is_thrown() {
			Assert.IsInstanceOf<RpcException>(_caughtException);
			var ex = _caughtException as RpcException;
			Assert.AreEqual(StatusCode.FailedPrecondition, ex.StatusCode);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class DeleteWithExistingMetadata<TLogFormat, TStreamId> : GrpcSpecification<TLogFormat, TStreamId> {
		private Exception _caughtException;
		private readonly string _streamName;
		private readonly string _metadataStreamName;

		public DeleteWithExistingMetadata() {
			_streamName = Guid.NewGuid().ToString();
			_metadataStreamName = SystemStreams.MetastreamOf(_streamName);
		}

		protected override async Task Given() {
			using var call = StreamsClient.Append(GetCallOptions());
			await call.RequestStream.WriteAsync(new() {
				Options = new() { NoStream = new(), StreamIdentifier = _metadataStreamName }
			});
			await call.RequestStream.WriteAsync(new() {
				ProposedMessage = new() {
					Id = Uuid.NewUuid().ToDto(),
					Metadata = {
						{ Metadata.Type, SystemEventTypes.StreamMetadata },
						{ Metadata.ContentType, Metadata.ContentTypes.ApplicationJson }
					},
					Data = ByteString.CopyFromUtf8(StreamMetadata.Build().Build().AsJsonString())
				}
			});

			await call.RequestStream.CompleteAsync();

			await call.ResponseAsync;
		}

		protected override async Task When() {
			using var call = StreamsClient.DeleteAsync(new() {
				Options = new() { NoStream = new(), StreamIdentifier = _streamName }
			}, GetCallOptions());

			try {
				await call.ResponseAsync;
			} catch (Exception ex) {
				_caughtException = ex;
			}
		}

		[Test]
		public void no_exception_is_thrown() {
			Assert.Null(_caughtException);
		}
	}
}
