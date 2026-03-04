// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.
#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

// ReSharper disable MethodHasAsyncOverload
// ReSharper disable ConvertIfStatementToReturnStatement

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
using KurrentDB.Protocol.V2.Streams.Errors;
using static EventStore.Plugins.Authorization.Operations.Streams.Parameters;
using static KurrentDB.Core.Messages.ClientMessage;
using static KurrentDB.Protocol.V2.Streams.StreamsService;

using Contracts = KurrentDB.Protocol.V2.Streams;

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
                eventStreamIndexes: Indexes.ToImmutable(),
                user: context.GetHttpContext().User,
                cancellationToken: context.CancellationToken
            );
        }

        protected override bool SuccessPredicate(Message message) =>
            message is WriteEventsCompleted { Result: OperationResult.Success };

        protected override AppendSessionResponse MapToResult(Message message) {
            var completed = (WriteEventsCompleted)message;
            var output    = new List<AppendResponse>();

            var lastNumbers = completed.LastEventNumbers.Span;
            for (var i = 0; i < completed.LastEventNumbers.Length; i++)
                output.Add(new() {
                    Stream         = Requests.ElementAt(i).Stream,
                    StreamRevision = lastNumbers[i]
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

                    OperationResult.StreamDeleted => ApiErrors.StreamTombstoned(Requests.ElementAt(completed.ConsistencyCheckFailures.Span[0].StreamIndex).Stream),

                    OperationResult.WrongExpectedVersion => ApiErrors.StreamRevisionConflict(
                        Requests.ElementAt(completed.ConsistencyCheckFailures.Span[0].StreamIndex).Stream,
                        Requests.ElementAt(completed.ConsistencyCheckFailures.Span[0].StreamIndex).ExpectedRevision,
                        completed.ConsistencyCheckFailures.Span[0].ActualVersion
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

    public override async Task<AppendRecordsResponse> AppendRecords(AppendRecordsRequest request, ServerCallContext context) {
        var command = Publisher
            .NewCommand<AppendRecordsCommand>()
            .WithMaxRecordSize(Options.Application.MaxAppendEventSize)
            .WithMaxAppendSize(Options.Application.MaxAppendSize)
            .WithRequest(request);

        foreach (var stream in command.WriteStreams)
            await Authz.AuthorizeOperation(Operations.Streams.Write, StreamId(stream), context);

        foreach (var stream in command.ReadOnlyStreams)
            await Authz.AuthorizeOperation(Operations.Streams.Read, StreamId(stream), context);

        return await command.Execute(context);
    }

    class AppendRecordsCommand : ApiCommand<AppendRecordsCommand, AppendRecordsResponse> {
        record struct StreamState(string Stream, long ExpectedState);

        ImmutableArray<Event>.Builder Events               { get; } = ImmutableArray.CreateBuilder<Event>();
        ImmutableArray<int>.Builder   Indexes              { get; } = ImmutableArray.CreateBuilder<int>();
        Dictionary<string, int>       StreamIndex          { get; } = new(StringComparer.OrdinalIgnoreCase);
        List<StreamState>             ExpectedStreamStates { get; } = [];

        int MaxAppendSize   { get; set; }
        int MaxRecordSize   { get; set; }
        int TotalAppendSize { get; set; }

        public IEnumerable<string> WriteStreams =>
            Indexes.Distinct().Select(i => ExpectedStreamStates[i].Stream);

        public IEnumerable<string> ReadOnlyStreams =>
            ExpectedStreamStates.Select(s => s.Stream).Except(WriteStreams, StringComparer.OrdinalIgnoreCase);

        public AppendRecordsCommand WithMaxAppendSize(int maxAppendSize) {
            MaxAppendSize = maxAppendSize;
            return this;
        }

        public AppendRecordsCommand WithMaxRecordSize(int maxRecordSize) {
            MaxRecordSize = maxRecordSize;
            return this;
        }

        public AppendRecordsCommand WithRequest(AppendRecordsRequest request) {
            RegisterChecks(request.Checks);
            PrepareRecords(request.Records);

            return this;

            void RegisterChecks(IReadOnlyList<ConsistencyCheck> checks) {
                StreamIndex.EnsureCapacity(checks.Count + request.Records.Count);

                foreach (var check in checks.Where(x => x.TypeCase == ConsistencyCheck.TypeOneofCase.StreamState)) {
                    StreamIndex.TryAdd(check.StreamState.Stream, ExpectedStreamStates.Count);
                    ExpectedStreamStates.Add(new StreamState(check.StreamState.Stream, check.StreamState.ExpectedState));
                }
            }

            void PrepareRecords(IReadOnlyList<AppendRecord> records) {
                Events.Capacity  = records.Count;
                Indexes.Capacity = records.Count;

                foreach (var record in records.Select(static rec => rec.PreProcessRecord())) {
                    var streamIndex = ResolveStreamIndex(record.Stream);

                    var recordSize = record.CalculateSizeOnDisk(MaxRecordSize);
                    if (recordSize.ExceedsMax)
                        throw ApiErrors.AppendRecordSizeExceeded(record.Stream, record.RecordId, recordSize.TotalSize, MaxRecordSize);

                    if ((TotalAppendSize += recordSize.TotalSize) > MaxAppendSize)
                        throw ApiErrors.AppendTransactionSizeExceeded(Events.Count + 1, TotalAppendSize, MaxAppendSize);

                    Events.Add(record.MapToEvent());
                    Indexes.Add(streamIndex);
                }

                return;

                int ResolveStreamIndex(string stream) {
                    if (!StreamIndex.TryGetValue(stream, out var index)) {
                        index = ExpectedStreamStates.Count;
                        StreamIndex[stream] = index;
                        ExpectedStreamStates.Add(new StreamState(stream, ExpectedStreamCondition.Any));
                    }
                    return index;
                }
            }
        }

        protected override Message BuildMessage(IEnvelope callback, ServerCallContext context) {
            var correlationId = Guid.NewGuid();

            var streamIds = ImmutableArray.CreateBuilder<string>(ExpectedStreamStates.Count);
            var revisions = ImmutableArray.CreateBuilder<long>(ExpectedStreamStates.Count);

            foreach (var streamEntry in ExpectedStreamStates) {
                streamIds.Add(streamEntry.Stream);
                revisions.Add(MapExpectedState(streamEntry.ExpectedState));
            }

            return new WriteEvents(
                internalCorrId: correlationId,
                correlationId: correlationId,
                envelope: callback,
                requireLeader: true,
                eventStreamIds: streamIds.ToImmutable(),
                expectedVersions: revisions.ToImmutable(),
                events: Events.ToImmutable(),
                eventStreamIndexes: Indexes.ToImmutable(),
                user: context.GetHttpContext().User,
                cancellationToken: context.CancellationToken
            );

            static long MapExpectedState(long expectedState) => expectedState switch {
                ExpectedStreamCondition.Tombstoned => long.MaxValue,
                _                                  => expectedState
            };
        }

        protected override bool SuccessPredicate(Message message) =>
            message is WriteEventsCompleted { Result: OperationResult.Success, ConsistencyCheckFailures.Length: 0 };

        protected override AppendRecordsResponse MapToResult(Message message) {
            var completed = (WriteEventsCompleted)message;
            var response  = new AppendRecordsResponse { Position = completed.CommitPosition };

            foreach (var i in Indexes.Distinct()) {
                response.Revisions.Add(new Contracts.StreamRevision {
                    Stream   = ExpectedStreamStates[i].Stream,
                    Revision = completed.LastEventNumbers.Span[i]
                });
            }

            return response;
        }

        protected override RpcException? MapToError(Message message) {
            return message switch {
                WriteEventsCompleted completed => completed.Result switch {
	                OperationResult.CommitTimeout => ApiErrors.OperationTimeout($"{FriendlyName} timed out while waiting for commit"),

	                OperationResult.WrongExpectedVersion or OperationResult.StreamDeleted => MapViolations(completed.ConsistencyCheckFailures.Span),

	                _ => ApiErrors.InternalServerError($"{FriendlyName} completed in error with unexpected result: {completed.Result}")
                },
                _ => null
            };

            RpcException MapViolations(ReadOnlySpan<ConsistencyCheckFailure> failures) {
                var violations = new List<ConsistencyViolation>(failures.Length);

                foreach (ref readonly var failure in failures) {
                    violations.Add(new ConsistencyViolation {
                        CheckIndex = failure.StreamIndex,
                        StreamState = new() {
                            Stream        = ExpectedStreamStates[failure.StreamIndex].Stream,
                            ExpectedState = ExpectedStreamStates[failure.StreamIndex].ExpectedState,
                            ActualState   = failure switch {
                                { ActualVersion: long.MaxValue } => ActualStreamCondition.Tombstoned,
                                { IsSoftDeleted: true }          => ActualStreamCondition.Deleted,
                                _                                => failure.ActualVersion
                            }
                        }
                    });
                }

                return ApiErrors.AppendConsistencyViolation(violations);
            }
        }
    }
}
