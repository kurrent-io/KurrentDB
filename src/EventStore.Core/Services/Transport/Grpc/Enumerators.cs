using System.Threading.Channels;
using EventStore.Client;
using EventStore.Client.Streams;
using EventStore.Core.Data;
using Google.Protobuf;

namespace EventStore.Core.Services.Transport.Grpc {
	public static partial class Enumerators {
		private const int MaxLiveEventBufferCount = 32;
		private const int ReadBatchSize = 32; // TODO  JPB make this configurable

		private static readonly BoundedChannelOptions BoundedChannelOptions =
			new(MaxLiveEventBufferCount) {
				FullMode = BoundedChannelFullMode.Wait,
				SingleReader = true,
				SingleWriter = true
			};

		private static ReadResp.Types.ReadEvent.Types.RecordedEvent ConvertToRecordedEvent(
			ReadReq.Types.Options.Types.UUIDOption uuidOption, EventRecord e, long? commitPosition,
			long? preparePosition) {
			if (e == null)
				return null;
			var position = Position.FromInt64(commitPosition ?? -1, preparePosition ?? -1);
			return new ReadResp.Types.ReadEvent.Types.RecordedEvent {
				Id = uuidOption.ContentCase switch {
					ReadReq.Types.Options.Types.UUIDOption.ContentOneofCase.String => new UUID {
						String = e.EventId.ToString()
					},
					_ => Uuid.FromGuid(e.EventId).ToDto()
				},
				StreamIdentifier = e.EventStreamId,
				StreamRevision = StreamRevision.FromInt64(e.EventNumber),
				CommitPosition = position.CommitPosition,
				PreparePosition = position.PreparePosition,
				Metadata = {
					[Constants.Metadata.Type] = e.EventType,
					[Constants.Metadata.Created] = e.TimeStamp.ToTicksSinceEpoch().ToString(),
					[Constants.Metadata.ContentType] = e.IsJson
						? Constants.Metadata.ContentTypes.ApplicationJson
						: Constants.Metadata.ContentTypes.ApplicationOctetStream
				},
				Data = ByteString.CopyFrom(e.Data.Span),
				CustomMetadata = ByteString.CopyFrom(e.Metadata.Span)
			};
		}

		private static ReadResp.Types.ReadEvent ConvertToReadEvent(ReadReq.Types.Options.Types.UUIDOption uuidOption,
			ResolvedEvent e) {
			var readEvent = new ReadResp.Types.ReadEvent {
				Link = ConvertToRecordedEvent(uuidOption, e.Link, e.LinkPosition?.CommitPosition,
					e.LinkPosition?.PreparePosition),
				Event = ConvertToRecordedEvent(uuidOption, e.Event, e.EventPosition?.CommitPosition,
					e.EventPosition?.PreparePosition),
			};
			if (e.OriginalPosition.HasValue) {
				var position = Position.FromInt64(
					e.OriginalPosition.Value.CommitPosition,
					e.OriginalPosition.Value.PreparePosition);
				readEvent.CommitPosition = position.CommitPosition;
			} else {
				readEvent.NoPosition = new Empty();
			}

			return readEvent;
		}
	}
}
