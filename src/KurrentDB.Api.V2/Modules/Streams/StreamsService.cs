// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.
#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

// ReSharper disable MethodHasAsyncOverload

using System.Collections.Immutable;
using EventStore.Plugins.Authorization;
using Grpc.Core;
using KurrentDB.Api.Errors;
using KurrentDB.Api.Infrastructure;
using KurrentDB.Api.Infrastructure.Authorization;
using KurrentDB.Api.Streams.Authorization;
using KurrentDB.Common.Configuration;
using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Metrics;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Protocol.V2.Streams;
using Microsoft.AspNetCore.Http.Features;

using static KurrentDB.Core.Messages.ClientMessage;
using static KurrentDB.Protocol.V2.Streams.StreamsService;

namespace KurrentDB.Api.Streams;

public record StreamsServiceOptions {
	public int MaxAppendSize { get; init; } = TFConsts.ChunkSize;
	public int MaxRecordSize { get; init; } = TFConsts.EffectiveMaxLogRecordSize;
}

public class StreamsService : StreamsServiceBase {
    public StreamsService(
        StreamsServiceOptions options,
        IPublisher publisher,
        IAuthorizationProvider authz,
        Func<CancellationToken, ValueTask> ensureNodeIsLeader,
        ApiV2Trackers trackers
    ) {
        Options            = options;
        Publisher          = publisher;
        Authz              = authz;
        EnsureNodeIsLeader = ensureNodeIsLeader;

        //TODO SS: -_-' consider moving to interceptor and/or using proper asp.net grpc instrumentation
        AppendDuration = trackers[MetricsConfiguration.ApiV2Method.StreamAppendSession];
    }

    public StreamsService(
        StreamsServiceOptions options,
        IPublisher publisher,
        IAuthorizationProvider authz,
        NodeSystemInfoProvider node,
        ApiV2Trackers trackers
    ) : this(
        options,
        publisher,
        authz,
        node.EnsureNodeIsLeader,
        trackers
    ) { }

    StreamsServiceOptions              Options            { get; }
    IPublisher                         Publisher          { get; }
    IAuthorizationProvider             Authz              { get; }
    Func<CancellationToken, ValueTask> EnsureNodeIsLeader { get; }
    IDurationTracker                   AppendDuration     { get; }

    public override async Task<AppendResponse> Append(AppendRequest request, ServerCallContext context) {
        var sessionResponse = await AppendSession(new[] { request }.ToAsyncEnumerable(), context);
        var response = sessionResponse.Output[0].With(r => r.Position = sessionResponse.Position);
        return response;
    }

    public override Task<AppendSessionResponse> AppendSession(IAsyncStreamReader<AppendRequest> requests, ServerCallContext context) =>
        AppendDuration.Record(
            static state => state.Service.AppendSession(state.Requests.ReadAllAsync(), state.Context),
            (Service: this, Requests: requests, Context: context));

    async Task<AppendSessionResponse> AppendSession(IAsyncEnumerable<AppendRequest> requests, ServerCallContext context) {
       await EnsureNodeIsLeader(context.CancellationToken);

       var command = await requests
           .Do((req, _) => Authz.AuthorizeOperation(StreamPermission.Append.WithStream(req.Stream), context))
           .AggregateAsync(
               Publisher
                   .NewCommand<AppendSessionCommand>()
                   .WithMaxRecordSize(Options.MaxRecordSize)
                   .WithMaxAppendSize(Options.MaxAppendSize),
               (cmd, req) => cmd.WithRequest(req),
               context.CancellationToken);

       return await command.Execute(context);
    }

    class AppendSessionCommand : ApiCommand<AppendSessionCommand, AppendSessionResponse> {
        IndexedSet<AppendRequest>      Requests  { get; } = new(AppendRequestComparer);
        ImmutableArray<Event>.Builder  Events    { get; } = ImmutableArray.CreateBuilder<Event>();
        ImmutableArray<string>.Builder Streams   { get; } = ImmutableArray.CreateBuilder<string>();
		ImmutableArray<long>.Builder   Revisions { get; } = ImmutableArray.CreateBuilder<long>();
		ImmutableArray<int>.Builder    Indexes   { get; } = ImmutableArray.CreateBuilder<int>();

        int MaxAppendSize   { get; set; }
        int MaxRecordSize   { get; set; }
        int TotalAppendSize { get; set; }

        public AppendSessionCommand WithMaxAppendSize(int maxAppendSize) {
            MaxAppendSize = maxAppendSize;
            return this;
        }

        public AppendSessionCommand WithMaxRecordSize(int maxRecordSize) {
            MaxRecordSize = maxRecordSize;
            return this;
        }

		public AppendSessionCommand WithRequest(AppendRequest request) {
			// *** Temporary limitation ***
            // We do not allow appending to the same stream multiple times in a single append session.
            // This is to prevent complexity around expected revisions and ordering of events.
            // In the future, we can consider relaxing this limitation if there is a valid use case.
            // For now, we keep it simple and safe.
			if (!Requests.Add(request))
                throw ApiErrors.StreamAlreadyInAppendSession(request.Stream);

            Streams.Add(request.Stream);
			Revisions.Add(request.ExpectedRevisionOrAny());

            var eventStreamIndex = Streams.Count - 1;

			foreach (var record in request.Records.Select(rec => rec.PrepareRecord(Time).EnsureValid())) {
				var recordSize = record.CalculateSizeOnDisk(MaxRecordSize); // still considering using the validator...

				if (recordSize.ExceedsMax)
					throw ApiErrors.AppendRecordSizeExceeded(request.Stream, record.RecordId, recordSize.TotalSize, MaxRecordSize);

				if ((TotalAppendSize += recordSize.TotalSize) > MaxAppendSize)
					throw ApiErrors.AppendTransactionSizeExceeded(MaxAppendSize);

				Events.Add(record.MapToEvent());
                Indexes.Add(eventStreamIndex);
			}

			return this;
		}

        protected override Message BuildMessage(IEnvelope callback, ServerCallContext context) =>
            new WriteEvents(
                callback,
                Streams.ToImmutable(),
                Revisions.ToImmutable(),
                Events.ToImmutable(),
                Indexes.ToImmutable(),
                SystemAccounts.System, // its the only thing that makes sense
                context.CancellationToken
            );

        protected override bool SuccessPredicate(Message message) =>
            message is WriteEventsCompleted { Result: OperationResult.Success };

        protected override AppendSessionResponse MapToResult(Message message) {
            var completed = (WriteEventsCompleted)message;
            var output    = new List<AppendResponse>();

            for (var i = 0; i < completed.LastEventNumbers.Length; i++)
                output.Add(new() {
                    Stream         = Requests.ElementAt(i).Stream,
                    StreamRevision = completed.LastEventNumbers.Span[i]
                });

            return new AppendSessionResponse {
                Output   = { output },
                Position = completed.CommitPosition
            };
        }

        protected override RpcException MapToError(Message message) => message switch {
            WriteEventsCompleted completed => completed.Result switch {
                // because we can append to soft deleted streams, StreamDeleted means "hard deleted" AKA tombstoned
                OperationResult.StreamDeleted  => ApiErrors.StreamTombstoned(Requests.ElementAt(completed.FailureStreamIndexes.Span[0]).Stream),
                OperationResult.PrepareTimeout => ApiErrors.OperationTimeout(completed.Message),
                OperationResult.CommitTimeout  => ApiErrors.OperationTimeout(completed.Message),

                // TODO SS: consider returning all StreamRevisionConflict in one go, instead of pretending to "fail fast".
                OperationResult.WrongExpectedVersion => ApiErrors.StreamRevisionConflict(
                    Requests.ElementAt(completed.FailureStreamIndexes.Span[0]).Stream,
                    Requests.ElementAt(completed.FailureStreamIndexes.Span[0]).ExpectedRevision,
                    completed.FailureCurrentVersions.Span[0]
                ),

                _ => ApiErrors.InternalServerError($"{OperationName} completed in error with unexpected result: {completed.Result}")

                // OperationResult.InvalidTransaction is not possible here as we validate the max append size while receiving the requests
                // OperationResult.ForwardTimeout is not possible here as we set requireLeader=true on the request
                // OperationResult.PrepareTimeout is not possible here as we don't have old explicit transactions
                // OperationResult.AccessDenied is not possible here as we check authorization before starting the operation
            },
            _ => ApiErrors.InternalServerError($"{OperationName} failed with unexpected callback message: {message.GetType().FullName}")
        };

        protected override ValueTask OnSuccess(AppendSessionResponse result, ServerCallContext context) {
            if (context.GetHttpContext().Features.Get<IHttpMetricsTagsFeature>() is { } metrics) {
                metrics.Tags.Add(new KeyValuePair<string, object?>("streams", Requests.Count));
                metrics.Tags.Add(new KeyValuePair<string, object?>("records", Events.Count));
                metrics.Tags.Add(new KeyValuePair<string, object?>("bytes", TotalAppendSize));
            }

            return ValueTask.CompletedTask;
        }

        static readonly EqualityComparer<AppendRequest> AppendRequestComparer = EqualityComparer<AppendRequest>.Create(
            equals: (x, y) => string.Equals(x?.Stream, y?.Stream, StringComparison.OrdinalIgnoreCase),
            getHashCode: obj => StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Stream)
        );
    }
}
