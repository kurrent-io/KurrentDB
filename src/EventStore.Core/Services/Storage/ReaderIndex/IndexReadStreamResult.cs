using System;
using EventStore.Common.Utils;
using EventStore.Core.Data;

namespace EventStore.Core.Services.Storage.ReaderIndex {
	public struct IndexReadStreamResult {
		internal static readonly EventRecord[] EmptyRecords = new EventRecord[0];

		public readonly long FromEventNumber;
		public readonly int MaxCount;

		public readonly ReadStreamResult Result;
		public readonly long NextEventNumber;
		public readonly long LastEventNumber;
		public readonly bool IsEndOfStream;

		public readonly EventRecord[] Records;
		public readonly StreamMetadata Metadata;

		/// <summary>
		/// Failure Constructor
		/// </summary>
		public IndexReadStreamResult(long fromEventNumber, int maxCount, ReadStreamResult result,
			StreamMetadata metadata, long lastEventNumber) {
			if (result == ReadStreamResult.Success)
				throw new ArgumentException(
					String.Format("Wrong ReadStreamResult provided for failure constructor: {0}.", result), "result");

			FromEventNumber = fromEventNumber;
			MaxCount = maxCount;

			Result = result;
			NextEventNumber = -1;
			LastEventNumber = lastEventNumber;
			IsEndOfStream = true;
			Records = EmptyRecords;
			Metadata = metadata;
		}

		/// <summary>
		/// Success Constructor
		/// </summary>
		/// <param name="fromEventNumber">
		///     EventNumber that the read starts from. Usually as specified in the request.
		///     Sometimes resolved (e.g. from EndOfStream to the actual lastEventNumber)
		///     This is not returned to the client when reading forwards, only backwards.
		/// </param>
		/// <param name="maxCount">The maximum number of events requested</param>
		/// <param name="records"></param>
		/// <param name="metadata"></param>
		/// <param name="nextEventNumber">The event number to start the next _request_</param>
		/// <param name="lastEventNumber">The last event number of the _stream_</param>
		/// <param name="isEndOfStream"></param>
		public IndexReadStreamResult(
			long fromEventNumber,
			int maxCount,
			EventRecord[] records,
			StreamMetadata metadata,
			long nextEventNumber,
			long lastEventNumber,
			bool isEndOfStream) {

			Ensure.NotNull(records, "records");

			FromEventNumber = fromEventNumber;
			MaxCount = maxCount;

			Result = ReadStreamResult.Success;
			Records = records;
			Metadata = metadata;
			NextEventNumber = nextEventNumber;
			LastEventNumber = lastEventNumber;
			IsEndOfStream = isEndOfStream;
		}

		public override string ToString() {
			return String.Format("FromEventNumber: {0}, Maxcount: {1}, Result: {2}, Record count: {3}, Metadata: {4}, "
								 + "NextEventNumber: {5}, LastEventNumber: {6}, IsEndOfStream: {7}",
				FromEventNumber,
				MaxCount,
				Result,
				Records.Length,
				Metadata,
				NextEventNumber,
				LastEventNumber,
				IsEndOfStream);
		}
	}
}
