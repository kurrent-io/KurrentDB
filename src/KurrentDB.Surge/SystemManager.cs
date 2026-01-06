// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge;
using KurrentDB.Core;
using KurrentDB.Core.Services.Transport.Enumerators;
using ResolvedEvent = KurrentDB.Core.Data.ResolvedEvent;
using StreamMetadata = Kurrent.Surge.StreamMetadata;
using StreamRevision = Kurrent.Surge.StreamRevision;

namespace KurrentDB.Surge;

public class SystemManager : IManager {
    public SystemManager(ISystemClient client) => Client = client;

    ISystemClient Client { get; }

    public async ValueTask<bool> StreamExists(StreamId stream, CancellationToken cancellationToken) {
        try {
            var read = Client.Reading.ReadStreamBackwards(stream: stream,
                    startRevision: Core.Services.Transport.Common.StreamRevision.End,
                    maxCount: 1,
                    cancellationToken: cancellationToken)
                .FirstOrDefaultAsync(cancellationToken: cancellationToken);

            return await read.Then(x => x != ResolvedEvent.EmptyEvent);
        } catch (Exception) {
            return false;
        }
    }

    public async ValueTask<DeleteStreamResult> DeleteStream(StreamId stream, CancellationToken cancellationToken) {
        try {
            var result = await Client.Management.SoftDeleteStream(stream, expectedRevision: (int)StreamState.Exists, cancellationToken);
            return LogPosition.From(result.Position.CommitPosition);
        } catch (ReadResponseException.WrongExpectedRevision) {
            return new StreamNotFoundError(stream);
        } catch (ReadResponseException.StreamNotFound) {
            return new StreamNotFoundError(stream);
        } catch (ReadResponseException.StreamDeleted) {
            return new StreamNotFoundError(stream);
        }
    }

    public async ValueTask<DeleteStreamResult> DeleteStream(StreamId stream, StreamRevision expectedStreamRevision, CancellationToken cancellationToken) {
        try {
            var result = await Client
                .Management
                .DeleteStream(stream, expectedStreamRevision, cancellationToken: cancellationToken)
                ;

            return LogPosition.From(result.Position.CommitPosition);
        } catch (ReadResponseException.WrongExpectedRevision ex) {
            return new ExpectedStreamRevisionError(stream, expectedStreamRevision, StreamRevision.From(ex.ActualStreamRevision.ToInt64()));
        } catch (ReadResponseException.StreamNotFound) {
            return new StreamNotFoundError(stream);
        } catch (ReadResponseException.StreamDeleted) {
            return new StreamNotFoundError(stream);
        }
    }

    public async ValueTask<DeleteStreamResult> DeleteStream(StreamId stream, LogPosition expectedLogPosition, CancellationToken cancellationToken) {
        var result = await GetStreamInfo(expectedLogPosition, cancellationToken);

        return await result.Match(
            info => DeleteStream(stream, info.Revision, cancellationToken),
            _ => new(new StreamNotFoundError(stream))
        );
    }

    async ValueTask<StreamMetadata> IManager.ConfigureStream(
        StreamId stream, Func<StreamMetadata, StreamMetadata> configure, CancellationToken cancellationToken
    ) {
        var (metadata, revision) = await GetStreamMetadataInternal(stream, cancellationToken);

        var newMetadata = configure(metadata);

        if (newMetadata != metadata)
            _ = await SetStreamMetadata(stream, newMetadata, revision, cancellationToken);

        return newMetadata;
    }

    async ValueTask<(StreamMetadata Metadata, StreamRevision Revision)> SetStreamMetadata(
        StreamId stream, StreamMetadata metadata, StreamRevision expectedRevision, CancellationToken cancellationToken = default
    ) {
        var meta = new KurrentDB.Core.Data.StreamMetadata(
            maxCount: metadata.MaxCount is not null ? (int)metadata.MaxCount.Value : null, // so in the DB its a long but in the client its an int?!?
            maxAge: metadata.MaxAge,
            truncateBefore: metadata.TruncateBefore, // this should be a log position apparently
            cacheControl: metadata.CacheControl,
            acl: metadata.Acl is not null
                ? new KurrentDB.Core.Data.StreamAcl(readRoles: metadata.Acl.ReadRoles,
                    writeRoles: metadata.Acl.WriteRoles,
                    deleteRoles: metadata.Acl.DeleteRoles,
                    metaReadRoles: metadata.Acl.MetaReadRoles,
                    metaWriteRoles: metadata.Acl.MetaWriteRoles)
                : null);

        if (expectedRevision == StreamRevision.Unset) {
            var result = await Client.Management.SetStreamMetadata(stream,
                metadata: meta,
                expectedRevision: -1,
                cancellationToken: cancellationToken);

            return (metadata, StreamRevision.From(result.Revision));
        } else {
            var metaRevision = KurrentDB.Core.Services.Transport.Common.StreamRevision.FromInt64(expectedRevision);
            var result       = await Client.Management.SetStreamMetadata(stream, meta, metaRevision.ToInt64(), cancellationToken: cancellationToken);
            return (metadata, StreamRevision.From(result.Revision));
        }
    }

    async ValueTask<(StreamMetadata Metadata, StreamRevision MetadataRevision)> GetStreamMetadataInternal(
        StreamId stream, CancellationToken cancellationToken = default
    ) {
        var result = await Client.Management.GetStreamMetadata(stream, cancellationToken: cancellationToken);

        var meta = new StreamMetadata {
            MaxCount       = result.Metadata.MaxCount,
            MaxAge         = result.Metadata.MaxAge,
            TruncateBefore = result.Metadata.TruncateBefore, // this should be a log position apparently // review
            CacheControl   = result.Metadata.CacheControl,
            Acl = result.Metadata.Acl is not null
                ? new StreamMetadata.StreamAcl {
                    ReadRoles      = result.Metadata.Acl.ReadRoles,
                    WriteRoles     = result.Metadata.Acl.WriteRoles,
                    DeleteRoles    = result.Metadata.Acl.DeleteRoles,
                    MetaReadRoles  = result.Metadata.Acl.MetaReadRoles,
                    MetaWriteRoles = result.Metadata.Acl.MetaWriteRoles
                }
                : null
        };

        if (result.Revision is -2) // stream not found
            return (StreamMetadata.Empty, StreamRevision.Unset);

        var metaRevision = result.Revision is -1 // unset
            ? StreamRevision.Unset
            : StreamRevision.From(result.Revision);

        return (meta, metaRevision);
    }

    public ValueTask<StreamMetadata> GetStreamMetadata(StreamId stream, CancellationToken cancellationToken) =>
        GetStreamMetadataInternal(stream, cancellationToken).Then(x => x.Metadata);

    public async ValueTask<GetStreamInfoResult> GetStreamInfo(LogPosition position, CancellationToken cancellationToken = default) {
        var kdbPosition = position == LogPosition.Earliest
            ? KurrentDB.Core.Services.Transport.Common.Position.Start
            : new KurrentDB.Core.Services.Transport.Common.Position(position.CommitPosition.GetValueOrDefault(),
                position.PreparePosition.GetValueOrDefault());

        ResolvedEvent? re = await Client
            .Reading
            .ReadForwards(kdbPosition, maxCount: 1, cancellationToken: cancellationToken)
            .FirstOrDefaultAsync(cancellationToken)
            ;

        return (
            StreamId.From(re.Value.OriginalEvent.EventStreamId),
            StreamRevision.From(re.Value.OriginalEvent.EventNumber)
        );
    }
}
