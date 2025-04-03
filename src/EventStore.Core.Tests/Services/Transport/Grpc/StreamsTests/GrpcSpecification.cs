// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EventStore.Client.Streams;
using EventStore.Core.Services.Transport.Grpc;
using EventStore.Core.Tests.Helpers;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using NUnit.Framework;
using Convert = System.Convert;
using Streams = EventStore.Client.Streams.Streams;
using GrpcMetadata = EventStore.Core.Services.Transport.Grpc.Constants.Metadata;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.TransactionLog.Chunks;

namespace EventStore.Core.Tests.Services.Transport.Grpc.StreamsTests;

public abstract class GrpcSpecification<TLogFormat, TStreamId> {
	protected GrpcChannel Channel;
	private readonly MiniNode<TLogFormat, TStreamId> _node;
	internal Streams.StreamsClient StreamsClient { get; set; }
	private BatchAppender _batchAppender;

	protected GrpcSpecification(IExpiryStrategy expiryStrategy = null,
		int maxAppendEventSize = TFConsts.EffectiveMaxLogRecordSize) {
		_node = new MiniNode<TLogFormat, TStreamId>(GetType().FullName,
			inMemDb: true,
			expiryStrategy: expiryStrategy,
			maxAppendEventSize: maxAppendEventSize);
	}

	protected abstract Task Given();

	protected abstract Task When();

	[OneTimeSetUp]
	public async Task SetUp() {
		await _node.Start();
		await _node.AdminUserCreated;

		Channel = GrpcChannel.ForAddress(
			new UriBuilder { Scheme = Uri.UriSchemeHttps }.Uri,
			new() {
				HttpClient = _node.HttpClient,
				DisposeHttpClient = false,
			});
		StreamsClient = new(Channel);
		_batchAppender = new(StreamsClient);
		_batchAppender.Start();
		try {
			await Given().WithTimeout(TimeSpan.FromSeconds(30));
		} catch (Exception ex) {
			throw new Exception("Given Failed", ex);
		}

		try {
			await When().WithTimeout(TimeSpan.FromSeconds(30));
		} catch (Exception ex) {
			throw new Exception("When Failed", ex);
		}
	}

	[OneTimeTearDown]
	public async Task TearDown() {
		await _batchAppender.DisposeAsync();
		Channel?.Dispose();
		await _node.Shutdown();
	}

	private static CallCredentials CallCredentialsFromUser((string userName, string password) credentials) =>
		CallCredentials.FromInterceptor((_, metadata) => {
			metadata.Add(new Metadata.Entry("authorization",
				$"Basic {Convert.ToBase64String(Encoding.ASCII.GetBytes($"{credentials.userName}:{credentials.password}"))}"));

			return Task.CompletedTask;
		});

	protected static (string userName, string password) AdminCredentials => ("admin", "changeit");

	protected virtual (string userName, string password) DefaultCredentials => default;

	protected CallOptions GetCallOptions((string userName, string password) credentials = default) =>
		new(credentials: GetCredentials(credentials == default ? DefaultCredentials : credentials),
			deadline: Debugger.IsAttached
				? DateTime.UtcNow.AddDays(1)
				: new DateTime?());

	private static CallCredentials GetCredentials((string userName, string password) credentials) =>
		credentials == default ? null : CallCredentialsFromUser(credentials);

	internal ValueTask<BatchAppendResp> AppendToStreamBatch(params BatchAppendReq[] requests) =>
		_batchAppender.Call(requests);

	internal static IEnumerable<BatchAppendReq.Types.ProposedMessage> CreateEvents(int count) =>
		Enumerable.Range(0, count).Select(_ => CreateEvent());

	internal static BatchAppendReq.Types.ProposedMessage CreateEvent(string type = "-", int? dataSize = null, int? metadataSize = null) {
		var data = dataSize is null
			? ByteString.Empty
			: ByteString.CopyFrom(Encoding.UTF8.GetBytes(new string('*', dataSize.Value)));
		var metadata = metadataSize is null
			? ByteString.Empty
			: ByteString.CopyFrom(Encoding.UTF8.GetBytes(new string('*', metadataSize.Value)));
		return new BatchAppendReq.Types.ProposedMessage {
			Data = data,
			Id = Uuid.NewUuid().ToDto(),
			CustomMetadata = metadata,
			Metadata = {
				{ GrpcMetadata.ContentType, GrpcMetadata.ContentTypes.ApplicationOctetStream },
				{ GrpcMetadata.Type, type }
			}
		};
	}

	private class BatchAppender : IAsyncDisposable {
		private readonly Lazy<AsyncDuplexStreamingCall<BatchAppendReq, BatchAppendResp>> _batchAppendLazy;
		private AsyncDuplexStreamingCall<BatchAppendReq, BatchAppendResp> BatchAppend => _batchAppendLazy.Value;
		private readonly ConcurrentDictionary<Uuid, TaskCompletionSource<BatchAppendResp>> _responses;

		public BatchAppender(Streams.StreamsClient streamsClient) {
			_batchAppendLazy = new Lazy<AsyncDuplexStreamingCall<BatchAppendReq, BatchAppendResp>>(() =>
				streamsClient.BatchAppend(new CallOptions(credentials: GetCredentials(default),
					deadline: DateTime.UtcNow.AddDays(1))));
			_responses = new();
		}

		public void Start() =>
			Task.Run(async () => {
				while (await BatchAppend.ResponseStream.MoveNext()) {
					var response = BatchAppend.ResponseStream.Current;
					var correlationId = Uuid.FromDto(response.CorrelationId);

					if (_responses.TryRemove(correlationId, out var tcs)) {
						tcs.TrySetResult(response);
					}
				}
			});

		public ValueTask DisposeAsync() =>
			_batchAppendLazy.IsValueCreated
				? new(_batchAppendLazy.Value.RequestStream.CompleteAsync())
				: new(Task.CompletedTask);

		public async ValueTask<BatchAppendResp> Call(params BatchAppendReq[] requests) {
			if (requests.Length == 0) {
				throw new ArgumentException($"Must have at least one {nameof(BatchAppendReq)}.",
					nameof(requests));
			}

			if (requests.Select(r => r.CorrelationId).Distinct().Count() > 1) {
				throw new ArgumentException($"All {nameof(BatchAppendReq)} must have same CorrelationId.",
					nameof(requests));
			}
			var tcs = new TaskCompletionSource<BatchAppendResp>();
			var correlationId = Uuid.FromDto(requests[0].CorrelationId);

			if (!_responses.TryAdd(correlationId, tcs)) {
				throw new ArgumentException("CorrelationId is already reserved.", nameof(correlationId));
			}

			foreach (var request in requests) {
				await BatchAppend.RequestStream.WriteAsync(request);
			}

			return await tcs.Task;
		}
	}
}
