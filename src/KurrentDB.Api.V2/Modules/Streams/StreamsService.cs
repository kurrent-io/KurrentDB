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
using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Protocol.V2.Streams;
using static KurrentDB.Core.Messages.ClientMessage;
using static KurrentDB.Protocol.V2.Streams.StreamsService;

namespace KurrentDB.Api.Streams;

public class StreamsServiceOptions {
    public int MaxAppendSize { get; set; } = TFConsts.ChunkSize;
    public int MaxRecordSize { get; set; } = TFConsts.EffectiveMaxLogRecordSize;
}

public class StreamsService : StreamsServiceBase {
    public StreamsService(
        StreamsServiceOptions options,
        IPublisher publisher,
        IAuthorizationProvider authz,
        INodeSystemInfoProvider node
    ) {
        Options            = options;
        Publisher          = publisher;
        Authz              = authz;
        EnsureNodeIsLeader = node.EnsureNodeIsLeader;
    }

    public StreamsService(
        ClusterVNodeOptions options,
        IPublisher publisher,
        IAuthorizationProvider authz,
        INodeSystemInfoProvider node
    ) : this(
        new StreamsServiceOptions {
            MaxAppendSize = options.Application.MaxAppendSize,
            MaxRecordSize = options.Application.MaxAppendEventSize
        }, publisher, authz, node
    ) { }

    StreamsServiceOptions              Options            { get; }
    IPublisher                         Publisher          { get; }
    IAuthorizationProvider             Authz              { get; }
    Func<CancellationToken, ValueTask> EnsureNodeIsLeader { get; }

    public override async Task<AppendResponse> Append(AppendRequest request, ServerCallContext context) {
        var sessionResponse = await AppendSession(new[] { request }.ToAsyncEnumerable(), context);
        var response = sessionResponse.Output[0].With(r => r.Position = sessionResponse.Position);
        return response;
    }

    public override Task<AppendSessionResponse> AppendSession(IAsyncStreamReader<AppendRequest> requests, ServerCallContext context) =>
        AppendSession(requests.ReadAllAsync(), context);

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

            // Prepare and validate records
            // We do this here to ensure that if any record is invalid, we fail the entire session.
			foreach (var record in request.Records.Select(rec => rec.PrepareRecord())) {
				var recordSize = record.CalculateSizeOnDisk(MaxRecordSize);
				if (recordSize.ExceedsMax)
					throw ApiErrors.AppendRecordSizeExceeded(request.Stream, record.RecordId, recordSize.TotalSize, MaxRecordSize);

				if ((TotalAppendSize += recordSize.TotalSize) > MaxAppendSize)
					throw ApiErrors.AppendTransactionSizeExceeded(TotalAppendSize, MaxAppendSize);

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
                context.GetHttpContext().User, // SystemAccounts.System,
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
                // StreamDeleted means "hard deleted" AKA tombstoned
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

        static readonly EqualityComparer<AppendRequest> AppendRequestComparer = EqualityComparer<AppendRequest>.Create(
            equals: (x, y) => string.Equals(x?.Stream, y?.Stream, StringComparison.OrdinalIgnoreCase),
            getHashCode: obj => StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Stream)
        );
    }
}
