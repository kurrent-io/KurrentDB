// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.
#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

// ReSharper disable MethodHasAsyncOverload

using System.Collections.Immutable;
using System.Security.Claims;
using EventStore.Plugins.Authorization;
using Grpc.Core;
using KurrentDB.Api.Errors;
using KurrentDB.Api.Infrastructure;
using KurrentDB.Api.Infrastructure.Grpc.Validation;
using KurrentDB.Api.Streams.Authorization;
using KurrentDB.Common.Configuration;
using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Metrics;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Protocol.V2.Streams;
using Microsoft.Extensions.Logging;
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
        NodeSystemInfoProvider node,
        TimeProvider time,
        ApiV2Trackers trackers,
        RequestValidation validation,
        ILogger<StreamsService> logger
    ) {
        Options     = options;
        Publisher   = publisher;
        Authz       = authz;
        Node        = node;
        Time        = time;
        Validation  = validation;
        Logger      = logger;

        //TODO SS: -_-' consider moving to interceptor and/or using proper asp.net grpc instrumentation
        AppendDuration = trackers[MetricsConfiguration.ApiV2Method.StreamAppendSession];
    }

    StreamsServiceOptions   Options        { get; }
    IPublisher              Publisher      { get; }
    IAuthorizationProvider  Authz          { get; }
    NodeSystemInfoProvider  Node           { get; }
    IDurationTracker        AppendDuration { get; }
    TimeProvider            Time           { get; }
    RequestValidation       Validation     { get; }
    ILogger<StreamsService> Logger         { get; }

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
       await Node.EnsureNodeIsLeader(context.CancellationToken);

       var user = context.GetHttpContext().User;

       var command = await requests
           .Do((req, ct) => Authz.AuthorizeStreamWrite(req.Stream, user, ct))
           .AggregateAsync(
               new WriteEventsCommand()
                   .WithUser(user)
                   .WithMaxRecordSize(Options.MaxRecordSize)
                   .WithMaxAppendSize(Options.MaxAppendSize)
                   .WithPublisher(Publisher)
                   .WithTime(Time),
               (command, req) => command.WithRequest(req),
               context.CancellationToken);

       try {
           return await command.Execute(user, context.CancellationToken);
       }
       catch (AggregateException ex) {
           Logger.LogError(ex, "Error processing append request");
           throw ex.InnerException ?? ex;
       }
    }

    class WriteEventsCommand {
        static readonly EqualityComparer<AppendRequest> AppendRequestComparer = EqualityComparer<AppendRequest>.Create(
            equals: (x, y) => string.Equals(x?.Stream, y?.Stream, StringComparison.OrdinalIgnoreCase),
            getHashCode: obj => StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Stream)
        );

        IndexedSet<AppendRequest>      Requests  { get; } = new(AppendRequestComparer);
        ImmutableArray<Event>.Builder  Events    { get; } = ImmutableArray.CreateBuilder<Event>();
        ImmutableArray<string>.Builder Streams   { get; } = ImmutableArray.CreateBuilder<string>();
		ImmutableArray<long>.Builder   Revisions { get; } = ImmutableArray.CreateBuilder<long>();
		ImmutableArray<int>.Builder    Indexes   { get; } = ImmutableArray.CreateBuilder<int>();

		int TotalAppendSize { get; set; }

        IPublisher             Publisher     { get; set; } = null!;
        TimeProvider           Time          { get; set; } = TimeProvider.System;
        int                    MaxAppendSize { get; set; } = TFConsts.ChunkSize;
        int                    MaxRecordSize { get; set; } = TFConsts.EffectiveMaxLogRecordSize;
        ClaimsPrincipal        User          { get; set; } = null!;

        public WriteEventsCommand WithPublisher(IPublisher publisher) {
            Publisher = publisher;
            return this;
        }

        public WriteEventsCommand WithTime(TimeProvider time) {
            Time = time;
            return this;
        }

        public WriteEventsCommand WithMaxAppendSize(int maxAppendSize) {
            MaxAppendSize = maxAppendSize;
            return this;
        }

        public WriteEventsCommand WithMaxRecordSize(int maxRecordSize) {
            MaxRecordSize = maxRecordSize;
            return this;
        }

        public WriteEventsCommand WithUser(ClaimsPrincipal user) {
            User  = user;
            return this;
        }

		public WriteEventsCommand WithRequest(AppendRequest request) {
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
				var recordSize = record.CalculateSizeOnDisk(MaxRecordSize);

				if (recordSize.ExceedsMax)
					throw ApiErrors.AppendRecordSizeExceeded(request.Stream, record.RecordId, recordSize.TotalSize, MaxRecordSize);

				if ((TotalAppendSize += recordSize.TotalSize) > MaxAppendSize)
					throw ApiErrors.AppendTransactionSizeExceeded(MaxAppendSize);

				Events.Add(record.MapToEvent());
                Indexes.Add(eventStreamIndex);
			}

			return this;
		}

        public Task<AppendSessionResponse> Execute(ClaimsPrincipal user, CancellationToken cancellationToken = default) {
            var callback = new AppendRecordsCallback(requests: Requests);

            // not yet because of all the validations
            // in the constructor that are helpful.
            // should actually transform it into tests.
            // var command = new WriteEvents(
            //     callback,
            //     Streams.ToImmutable(),
            //     Revisions.ToImmutable(),
            //     Events.ToImmutable(),
            //     Indexes.ToImmutable(),
            //     user,
            //     cancellationToken
            // );

            var cid = Guid.NewGuid();
            var command = new WriteEvents(
                internalCorrId: cid,
                correlationId: cid,
                envelope: callback,
                requireLeader: true,
                eventStreamIds: Streams.ToImmutable(),
                expectedVersions: Revisions.ToImmutable(),
                events: Events.ToImmutable(),
                eventStreamIndexes: Indexes.ToImmutable(),
                user: user,
                tokens: null,
                cancellationToken: cancellationToken
            );

            Publisher.Publish(message: command);

            return callback.WaitForReply;
        }
    }

    class AppendRecordsCallback(IndexedSet<AppendRequest> requests)
        : ApiCallback<IndexedSet<AppendRequest>, AppendSessionResponse, RpcException>(requests, "streams.append-session") {
        protected override bool SuccessPredicate(Message message, IndexedSet<AppendRequest> requests) =>
            message is WriteEventsCompleted { Result: OperationResult.Success };

        protected override AppendSessionResponse MapToApiResponse(Message message, IndexedSet<AppendRequest> requests) {
            var completed = (WriteEventsCompleted)message;
            var output    = new List<AppendResponse>();

            for (var i = 0; i < completed.LastEventNumbers.Length; i++)
                output.Add(new() {
                    Stream         = requests.ElementAt(i).Stream,
                    StreamRevision = completed.LastEventNumbers.Span[i]
                });

            return new() {
                Output   = { output },
                Position = completed.CommitPosition
            };
        }

        protected override RpcException MapToApiError(Message message, IndexedSet<AppendRequest> requests) {
            return message switch {
                WriteEventsCompleted completed => completed.Result switch {
                    // because we can append to soft deleted streams, StreamDeleted means "hard deleted" AKA tombstoned
                    OperationResult.StreamDeleted  => ApiErrors.StreamTombstoned(requests.ElementAt(completed.FailureStreamIndexes.Span[0]).Stream),
                    OperationResult.PrepareTimeout => ApiErrors.OperationTimeout(completed.Message),
                    OperationResult.CommitTimeout  => ApiErrors.OperationTimeout(completed.Message),

                    // TODO SS: consider returning all StreamRevisionConflict in one go, instead of pretending to "fail fast".
                    OperationResult.WrongExpectedVersion => ApiErrors.StreamRevisionConflict(
                        requests.ElementAt(completed.FailureStreamIndexes.Span[0]).Stream,
                        requests.ElementAt(completed.FailureStreamIndexes.Span[0]).ExpectedRevision,
                        completed.FailureCurrentVersions.Span[0]
                    ),

                    _ => ApiErrors.InternalServerError($"Append request completed in error with unexpected result: {completed.Result}")

                    // OperationResult.InvalidTransaction is not possible here as we validate the max append size while receiving the requests
                    // OperationResult.ForwardTimeout is not possible here as we set requireLeader=true on the request
                    // OperationResult.PrepareTimeout is not possible here as we don't have old explicit transactions
                    // OperationResult.AccessDenied is not possible here as we check authorization before starting the operation
                },
                _ => ApiErrors.InternalServerError($"Append request failed with unexpected callback message: {message.GetType().FullName}")
            };
        }
    }
}
