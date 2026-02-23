// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.
#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

// ReSharper disable MethodHasAsyncOverload

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
        record struct StreamInfo(string Name, long Revision, long CheckIndex = -1);

        ImmutableArray<Event>.Builder Events            { get; } = ImmutableArray.CreateBuilder<Event>();
        ImmutableArray<int>.Builder   Indexes           { get; } = ImmutableArray.CreateBuilder<int>();
        List<StreamInfo>              Streams           { get; } = [];
        Dictionary<string, int>       StreamIndexByName { get; } = new(StringComparer.OrdinalIgnoreCase);

        int MaxAppendSize    { get; set; }
        int MaxRecordSize    { get; set; }
        int TotalAppendSize  { get; set; }
        int WriteStreamCount { get; set; }

        public IEnumerable<string> WriteStreams {
            get {
                for (var i = 0; i < WriteStreamCount; i++)
                    yield return Streams[i].Name;
            }
        }

        public IEnumerable<string> ReadOnlyStreams {
            get {
                for (var i = WriteStreamCount; i < Streams.Count; i++)
                    yield return Streams[i].Name;
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
            ProcessRecords(request.Records);
            WriteStreamCount = Streams.Count;
            ApplyConsistencyChecks(request.ConsistencyChecks);

            return this;

            void ProcessRecords(IReadOnlyList<AppendRecord> records) {
                Events.Capacity = records.Count;
                Indexes.Capacity = records.Count;

                foreach (var record in records.Select(static rec => rec.PreProcessRecord())) {
                    var streamIndex = GetOrAddStreamIndex(record.Stream);

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
                for (var i = 0; i < checks.Count; i++) {
                    var check = checks[i];
                    if (check.KindCase != ConsistencyCheck.KindOneofCase.Revision)
                        continue;

                    var streamRevisionCheck = check.Revision;
                    var streamIndex = GetOrAddStreamIndex(streamRevisionCheck.Stream);
                    var info = Streams[streamIndex];
                    Streams[streamIndex] = info with {
	                    Revision = streamRevisionCheck.Revision,
	                    CheckIndex = i
                    };
                }
            }

            int GetOrAddStreamIndex(string stream) {
	            if (StreamIndexByName.TryGetValue(stream, out var index))
		            return index;

	            index = Streams.Count;
	            StreamIndexByName[stream] = index;
	            Streams.Add(new StreamInfo(stream, ExpectedVersion.Any));
	            return index;
            }
        }

        protected override Message BuildMessage(IEnvelope callback, ServerCallContext context) {
            var cid = Guid.NewGuid();
            var streamIds = ImmutableArray.CreateBuilder<string>(Streams.Count);
            var revisions = ImmutableArray.CreateBuilder<long>(Streams.Count);
            foreach (var info in Streams) {
                streamIds.Add(info.Name);
                revisions.Add(info.Revision);
            }

            return new WriteEvents(
                internalCorrId: cid,
                correlationId: cid,
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

        protected override bool SuccessPredicate(Message message) =>
            message is WriteEventsCompleted { Result: OperationResult.Success };

        protected override AppendRecordsResponse MapToResult(Message message) {
            var completed = (WriteEventsCompleted)message;
            var response  = new AppendRecordsResponse { Position = completed.CommitPosition };

            for (var i = 0; i < completed.LastEventNumbers.Length; i++) {
                response.Revisions.Add(new StreamRevision {
                    Stream   = Streams[i].Name,
                    Revision = completed.LastEventNumbers.Span[i]
                });
            }

            return response;
        }

        [SkipLocalsInit]
        protected override RpcException? MapToError(Message message) {
	        return message switch {
		        WriteEventsCompleted completed => completed.Result switch {
			        OperationResult.WrongExpectedVersion or OperationResult.StreamDeleted => MapToConsistencyCheckFailed(completed),

			        OperationResult.CommitTimeout => ApiErrors.OperationTimeout($"{FriendlyName} timed out while waiting for commit"),

			        _ => ApiErrors.InternalServerError($"{FriendlyName} completed in error with unexpected result: {completed.Result}")
		        },
		        _ => null
	        };

	        RpcException MapToConsistencyCheckFailed(WriteEventsCompleted completed) {
		        var details = new ConsistencyCheckFailedErrorDetails();

		        for (var i = 0; i < completed.ConsistencyCheckFailures.Length; i++) {
			        var failure = completed.ConsistencyCheckFailures.Span[i];
			        var info = Streams[failure.StreamIndex];

			        if (info.CheckIndex < 0)
				        continue;

			        details.Failures[info.CheckIndex] = new ConsistencyCheckErrorDetails {
				        Revision = new RevisionConsistencyCheckErrorDetails {
					        Stream           = info.Name,
					        ExpectedRevision = failure.ExpectedVersion,
					        ActualRevision   = MapActualRevision(failure)
				        }
			        };
		        }

		        return ApiErrors.ConsistencyCheckFailed(details);

		        [MethodImpl(MethodImplOptions.AggressiveInlining)]
		        static long MapActualRevision(ConsistencyCheckFailure failure) => failure switch {
			        { ActualVersion: long.MaxValue } => -100, // Stream is tombstoned
			        { IsSoftDeleted: true }          => -10, // Stream is soft-deleted
			        _                                => failure.ActualVersion // Stream not found or revision mismatch
		        };
	        }
        }
    }
}
