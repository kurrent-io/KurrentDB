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
using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Protocol.V2.Streams;

using static EventStore.Plugins.Authorization.Operations.Streams.Parameters;
using static KurrentDB.Core.Messages.ClientMessage;
using static KurrentDB.Protocol.V2.Streams.StreamsService;

namespace KurrentDB.Api.Streams;

public class StreamsService : StreamsServiceBase {
    public StreamsService(
        ClusterVNodeOptions options,
        IPublisher publisher,
        IAuthorizationProvider authz
    ) {
        Options   = options;
        Publisher = publisher;
        Authz     = authz;
    }

    ClusterVNodeOptions    Options   { get; }
    IPublisher             Publisher { get; }
    IAuthorizationProvider Authz     { get; }

    public override async Task<AppendSessionResponse> AppendSession(IAsyncStreamReader<AppendRequest> requests, ServerCallContext context) {
        var command = await requests
            .ReadAllAsync()
            .Do((req, _) => Authz.AuthorizeOperation(Operations.Streams.Write, StreamId(req.Stream), context))
            .AggregateAsync(
                Publisher
                    .NewCommand<AppendSessionCommand>()
                    .WithMaxRecordSize(Options.Application.MaxAppendEventSize)
                    .WithMaxAppendSize(Options.Application.MaxAppendSize),
                (cmd, req) => cmd.WithRequest(req),
                context.CancellationToken
            );

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

            // Prepare and validate records
            // We do this here to ensure that if any record is invalid, we fail the entire session.
			foreach (var record in request.Records.Select(static rec => rec.PreProcessRecord())) {
				var recordSize = record.CalculateSizeOnDisk(MaxRecordSize);
				if (recordSize.ExceedsMax)
					throw ApiErrors.AppendRecordSizeExceeded(request.Stream, record.RecordId, recordSize.TotalSize, MaxRecordSize);

				if ((TotalAppendSize += recordSize.TotalSize) > MaxAppendSize)
					throw ApiErrors.AppendTransactionSizeExceeded(Events.Count + 1, TotalAppendSize, MaxAppendSize);

				Events.Add(record.MapToEvent());
                Indexes.Add(eventStreamIndex);
			}

			return this;
		}

        protected override Message BuildMessage(IEnvelope callback, ServerCallContext context) {
            if (Requests.Count == 0)
                throw ApiErrors.AppendTransactionNoRequests();

            var cid = Guid.NewGuid();
            var streamIds = Streams.ToImmutable();
            return new WriteEvents(
                internalCorrId: cid,
                correlationId: cid,
                envelope: callback,
                requireLeader: true,
                eventStreamIds: streamIds,
                expectedVersions: Revisions.ToImmutable(),
                events: Events.ToImmutable(),
                eventStreamIndexes: streamIds.Length == 1 ? [] : Indexes.ToImmutable(),
                user: context.GetHttpContext().User,
                cancellationToken: context.CancellationToken
            );
        }

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

        protected override RpcException? MapToError(Message message) =>
            message switch {
                WriteEventsCompleted completed => completed.Result switch {
                    OperationResult.CommitTimeout => ApiErrors.OperationTimeout($"{FriendlyName} timed out while waiting for commit"),

                    OperationResult.StreamDeleted => ApiErrors.StreamTombstoned(Requests.ElementAt(completed.FailureStreamIndexes.Span[0]).Stream),

                    OperationResult.WrongExpectedVersion => ApiErrors.StreamRevisionConflict(
                        Requests.ElementAt(completed.FailureStreamIndexes.Span[0]).Stream,
                        Requests.ElementAt(completed.FailureStreamIndexes.Span[0]).ExpectedRevision,
                        completed.FailureCurrentVersions.Span[0]
                    ),

                    _ => ApiErrors.InternalServerError($"{FriendlyName} completed in error with unexpected result: {completed.Result}")
                },
                _ => null
            };

        static readonly EqualityComparer<AppendRequest> AppendRequestComparer = EqualityComparer<AppendRequest>.Create(
            (x, y) => string.Equals(x?.Stream, y?.Stream, StringComparison.OrdinalIgnoreCase),
            obj => StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Stream)
        );
    }
}
