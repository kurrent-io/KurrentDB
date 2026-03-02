// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.
#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

// ReSharper disable MethodHasAsyncOverload
// ReSharper disable ConvertIfStatementToReturnStatement

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
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
using static System.Runtime.CompilerServices.MethodImplOptions;
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
        record struct StreamInfo(string Name, long Revision, bool HasEvents);

        ImmutableArray<Event>.Builder Events        { get; } = ImmutableArray.CreateBuilder<Event>();
        ImmutableArray<int>.Builder   Indexes       { get; } = ImmutableArray.CreateBuilder<int>();
        List<StreamInfo>              StreamEntries { get; } = [];
        Dictionary<string, int>       StreamLookup  { get; } = new(StringComparer.OrdinalIgnoreCase);

        int MaxAppendSize   { get; set; }
        int MaxRecordSize   { get; set; }
        int TotalAppendSize { get; set; }

        public IEnumerable<string> WriteStreams {
            get {
                foreach (var entry in StreamEntries)
                    if (entry.HasEvents)
                        yield return entry.Name;
            }
        }

        public IEnumerable<string> ReadOnlyStreams {
            get {
                foreach (var entry in StreamEntries)
                    if (!entry.HasEvents)
                        yield return entry.Name;
            }
        }

        public AppendRecordsCommand WithMaxAppendSize(int maxAppendSize) {
            MaxAppendSize = maxAppendSize;
            return this;
        }

        public AppendRecordsCommand WithMaxRecordSize(int maxRecordSize) {
            MaxRecordSize = maxRecordSize;
            return this;
        }

        public AppendRecordsCommand WithRequest(AppendRecordsRequest request) {
            ApplyConsistencyChecks(request.Checks);
            ProcessRecords(request.Records);

            return this;

            void ProcessRecords(IReadOnlyList<AppendRecord> records) {
                Events.Capacity = records.Count;
                Indexes.Capacity = records.Count;

                foreach (var record in records.Select(static rec => rec.PreProcessRecord())) {
                    var streamIndex = GetOrAddStreamIndex(record.Stream, hasEvents: true);

                    var recordSize = record.CalculateSizeOnDisk(MaxRecordSize);
                    if (recordSize.ExceedsMax)
                        throw ApiErrors.AppendRecordSizeExceeded(record.Stream, record.RecordId, recordSize.TotalSize, MaxRecordSize);

                    if ((TotalAppendSize += recordSize.TotalSize) > MaxAppendSize)
                        throw ApiErrors.AppendTransactionSizeExceeded(Events.Count + 1, TotalAppendSize, MaxAppendSize);

                    Events.Add(record.MapToEvent());
                    Indexes.Add(streamIndex);
                }
            }

            void ApplyConsistencyChecks(IReadOnlyList<ConsistencyCheck> checks) {
	            foreach (var check in checks) {
		            if (check.TypeCase != ConsistencyCheck.TypeOneofCase.StreamState)
			            continue;

		            var streamStateCheck = check.StreamState;
		            var streamIndex = GetOrAddStreamIndex(streamStateCheck.Stream);
		            var streamEntry = StreamEntries[streamIndex];
		            StreamEntries[streamIndex] = streamEntry with {
			            Revision = streamStateCheck.ExpectedState
		            };
	            }
            }

            int GetOrAddStreamIndex(string stream, bool hasEvents = false) {
	            if (StreamLookup.TryGetValue(stream, out var index)) {
		            if (hasEvents && !StreamEntries[index].HasEvents)
			            StreamEntries[index] = StreamEntries[index] with { HasEvents = true };
		            return index;
	            }

	            index = StreamEntries.Count;
	            StreamLookup[stream] = index;
	            StreamEntries.Add(new StreamInfo(stream, ExpectedVersion.Any, hasEvents));
	            return index;
            }
        }

        protected override Message BuildMessage(IEnvelope callback, ServerCallContext context) {
            var correlationId = Guid.NewGuid();
            var streamIds = ImmutableArray.CreateBuilder<string>(StreamEntries.Count);
            var revisions = ImmutableArray.CreateBuilder<long>(StreamEntries.Count);
            foreach (var streamEntry in StreamEntries) {
                streamIds.Add(streamEntry.Name);
                revisions.Add(streamEntry.Revision);
            }

            return new WriteEvents(
                internalCorrId: correlationId,
                correlationId: correlationId,
                envelope: callback,
                requireLeader: true,
                eventStreamIds: streamIds.MoveToImmutable(),
                expectedVersions: revisions.MoveToImmutable(),
                events: Events.MoveToImmutable(),
                eventStreamIndexes: Indexes.MoveToImmutable(),
                user: context.GetHttpContext().User,
                cancellationToken: context.CancellationToken
            );
        }

        // Accepts non-success results to allow MapToResult to handle
        // consistency check failures and produce structured violations.
        protected override bool SuccessPredicate(Message message) =>
            message is WriteEventsCompleted { Result: OperationResult.Success or OperationResult.WrongExpectedVersion or OperationResult.StreamDeleted };

        [SkipLocalsInit]
        protected override AppendRecordsResponse MapToResult(Message message) {
            var completed = (WriteEventsCompleted)message;

            if (completed.ConsistencyCheckFailures.Length == 0 && StreamEntries.TrueForAll(static e => e.HasEvents))
                return BuildSuccess();

            var consistencyViolations = CollectViolations();

            if (consistencyViolations.Count > 0)
                throw ApiErrors.AppendConsistencyViolation(consistencyViolations);

            return BuildSuccess();

            AppendRecordsResponse BuildSuccess() {
                var response = new AppendRecordsResponse { Position = completed.CommitPosition };

                var lastNumbers = completed.LastEventNumbers.Span;
                for (var i = 0; i < StreamEntries.Count; i++) {
                    if (!StreamEntries[i].HasEvents)
                        continue;

                    response.Revisions.Add(new Contracts.StreamRevision {
                        Stream   = StreamEntries[i].Name,
                        Revision = lastNumbers[i]
                    });
                }

                return response;
            }

            List<ConsistencyViolation> CollectViolations() {
                var failures = completed.ConsistencyCheckFailures.Span;
                var violations = new List<ConsistencyViolation>(failures.Length + StreamEntries.Count);
                var failedStreamIndexes = new HashSet<int>(failures.Length);

                foreach (ref readonly var failure in failures) {
                    failedStreamIndexes.Add(failure.StreamIndex);
                    violations.Add(new ConsistencyViolation {
                        StreamState = new() {
                            Stream        = StreamEntries[failure.StreamIndex].Name,
                            ExpectedState = failure.ExpectedVersion,
                            ActualState   = MapActualRevision(failure)
                        }
                    });
                }

                // Check-only streams the core considered OK may still be tombstoned.
                // The core treats tombstoned streams as passing with ExpectedVersion.Any,
                // NoStream, or DeletedStream, so we must detect and report them here.
                var firstNumbers = completed.FirstEventNumbers.Span;
                var lastNumbers  = completed.LastEventNumbers.Span;
                for (var i = 0; i < StreamEntries.Count; i++) {
                    if (StreamEntries[i].HasEvents)
                        continue;

                    if (failedStreamIndexes.Contains(i))
                        continue;

                    if (IsTombstoned(firstNumbers, i, completed.Result, StreamEntries[i].Revision))
                        violations.Add(new ConsistencyViolation {
                            StreamState = new() {
                                Stream        = StreamEntries[i].Name,
                                ExpectedState = StreamEntries[i].Revision,
                                ActualState   = firstNumbers.Length > 0 ? MapLastEventNumber(lastNumbers[i]) : WireRevision.Tombstoned
                            }
                        });
                }

                return violations;
            }

            static long MapActualRevision(ConsistencyCheckFailure failure) => failure switch {
                { ActualVersion: long.MaxValue } => WireRevision.Tombstoned,
                { IsSoftDeleted: true }          => WireRevision.SoftDeleted,
                _                                => failure.ActualVersion
            };

            static long MapLastEventNumber(long lastEventNumber) => lastEventNumber switch {
                long.MaxValue => WireRevision.Tombstoned,
                _             => lastEventNumber
            };

            static bool IsTombstoned(ReadOnlySpan<long> firstNumbers, int streamIndex, OperationResult result, long expectedRevision) {
                // When all consistency checks pass (firstNumbers populated), core may
                // treat a tombstoned check-only stream as OK (e.g. with ExpectedVersion.Any or ExpectedVersion.NoStream).
                // We detect it here via the long.MinValue sentinel to report as a violation.
                if (firstNumbers.Length > 0)
                    return firstNumbers[streamIndex] is long.MinValue;

                // Fallback when firstNumbers is empty (another stream's check failed).
                // This stream isn't in the explicit failures, but may still be tombstoned.
                return result is OperationResult.StreamDeleted
                    && expectedRevision is ExpectedVersion.Any or ExpectedVersion.NoStream;
            }
        }

        protected override RpcException? MapToError(Message message) =>
            message switch {
                WriteEventsCompleted completed => completed.Result switch {
                    OperationResult.CommitTimeout => ApiErrors.OperationTimeout($"{FriendlyName} timed out while waiting for commit"),
                    _ => ApiErrors.InternalServerError($"{FriendlyName} completed in error with unexpected result: {completed.Result}")
                },
                _ => null
            };
    }

    static class WireRevision {
        public const long Tombstoned  = -100;
        public const long SoftDeleted = -10;
    }
}
