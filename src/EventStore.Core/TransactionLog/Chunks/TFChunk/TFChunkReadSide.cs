// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Buffers;
using DotNext.IO;
using DotNext.Threading;
using EventStore.Common.Utils;
using EventStore.Core.Exceptions;
using EventStore.Core.Time;
using EventStore.Core.TransactionLog.LogRecords;
using Serilog;
using Range = EventStore.Core.Data.Range;

namespace EventStore.Core.TransactionLog.Chunks.TFChunk;

public partial class TFChunk {
	public interface IChunkReadSide {
		ValueTask<bool> ExistsAt(long logicalPosition, CancellationToken token);
		ValueTask<long> GetActualPosition(long logicalPosition, CancellationToken token);
		ValueTask<RecordReadResult> TryReadAt(long logicalPosition, bool couldBeScavenged, CancellationToken token);
		ValueTask<RecordReadResult> TryReadFirst(CancellationToken token);
		ValueTask<RecordReadResult> TryReadClosestForward(long logicalPosition, CancellationToken token);
		ValueTask<RawReadResult> TryReadClosestForwardRaw(long logicalPosition, Func<int, byte[]> getBuffer, CancellationToken token);
		ValueTask<RecordReadResult> TryReadLast(CancellationToken token);
		ValueTask<RecordReadResult> TryReadClosestBackward(long logicalPosition, CancellationToken token);
	}

	private class TFChunkReadSideUnscavenged : TFChunkReadSide, IChunkReadSide {
		public TFChunkReadSideUnscavenged(TFChunk chunk, ITransactionFileTracker tracker) : base(chunk, tracker) {
			if (chunk._chunkHeader.IsScavenged)
				throw new ArgumentException("Scavenged TFChunk passed into unscavenged chunk read side.");
		}

		public ValueTask<bool> ExistsAt(long logicalPosition, CancellationToken token)
			=> token.IsCancellationRequested
				? ValueTask.FromCanceled<bool>(token)
				: ValueTask.FromResult(logicalPosition >= 0 && logicalPosition < Chunk.LogicalDataSize);

		public ValueTask<long> GetActualPosition(long logicalPosition, CancellationToken token) {
			Ensure.Nonnegative(logicalPosition, nameof(logicalPosition));

			return token.IsCancellationRequested
			? ValueTask.FromCanceled<long>(token)
			: ValueTask.FromResult(logicalPosition >= Chunk.LogicalDataSize ? -1 : logicalPosition);
		}

		public async ValueTask<RecordReadResult> TryReadAt(long logicalPosition, bool couldBeScavenged, CancellationToken token) {
			var start = Instant.Now;
			var workItem = Chunk.GetReaderWorkItem();
			try {
				if (logicalPosition >= Chunk.LogicalDataSize) {
					_log.Warning(
						"Tried to read logical position {logicalPosition} which is greater than the chunk's logical size of {chunkLogicalSize}",
						logicalPosition, Chunk.LogicalDataSize);
					return RecordReadResult.Failure;
				}

				var (record, length) = await TryReadForwardInternal(start, workItem, logicalPosition, token);
				return new RecordReadResult(record is not null, -1, record, length);
			} finally {
				Chunk.ReturnReaderWorkItem(workItem);
			}
		}

		public ValueTask<RecordReadResult> TryReadFirst(CancellationToken token)
			=> TryReadClosestForward(0, token);

		public async ValueTask<RecordReadResult> TryReadClosestForward(long logicalPosition, CancellationToken token) {
			var start = Instant.Now;
			var workItem = Chunk.GetReaderWorkItem();
			try {
				if (logicalPosition >= Chunk.LogicalDataSize)
					return RecordReadResult.Failure;

				if (await TryReadForwardInternal(start, workItem, logicalPosition, token) is not ({ } record, var length))
					return RecordReadResult.Failure;

				long nextLogicalPos = record.GetNextLogPosition(logicalPosition, length);
				return new(true, nextLogicalPos, record, length);
			} finally {
				Chunk.ReturnReaderWorkItem(workItem);
			}
		}

		public async ValueTask<RawReadResult> TryReadClosestForwardRaw(long logicalPosition, Func<int, byte[]> getBuffer, CancellationToken token) {
			var workItem = Chunk.GetReaderWorkItem();
			try {
				if (logicalPosition >= Chunk.LogicalDataSize)
					return RawReadResult.Failure;

				if (await TryReadForwardRawInternal(workItem, logicalPosition, getBuffer, token) is not {Array: not null} record)
					return RawReadResult.Failure;

				var nextLogicalPos = logicalPosition + record.Count + 2 * sizeof(int);
				return new(true, nextLogicalPos, record.Array, record.Count);
			} finally {
				Chunk.ReturnReaderWorkItem(workItem);
			}
		}

		public ValueTask<RecordReadResult> TryReadLast(CancellationToken token)
			=> TryReadClosestBackward(Chunk.LogicalDataSize, token);

		public async ValueTask<RecordReadResult> TryReadClosestBackward(long logicalPosition, CancellationToken token) {
			var start = Instant.Now;
			var workItem = Chunk.GetReaderWorkItem();
			try {
				// here we allow actualPosition == _logicalDataSize as we can read backward the very last record that way
				if (logicalPosition > Chunk.LogicalDataSize)
					return RecordReadResult.Failure;

				if (await TryReadBackwardInternal(start, workItem, logicalPosition, token) is not ({ } record, var length))
					return RecordReadResult.Failure;

				long nextLogicalPos = record.GetPrevLogPosition(logicalPosition, length);
				return new RecordReadResult(true, nextLogicalPos, record, length);
			} finally {
				Chunk.ReturnReaderWorkItem(workItem);
			}
		}
	}

	private class TFChunkReadSideScavenged : TFChunkReadSide, IChunkReadSide {
		// must hold _lock to assign to _midpoints
		private readonly AsyncExclusiveLock _lock = new();
		private Midpoint[] _midpoints;

		public TFChunkReadSideScavenged(TFChunk chunk, ITransactionFileTracker tracker)
			: base(chunk, tracker) {
			if (!chunk._chunkHeader.IsScavenged)
				throw new ArgumentException($"Chunk provided is not scavenged: {chunk}");
		}

		private async ValueTask<Midpoint[]> GetOrCreateMidPoints(ReaderWorkItem workItem, CancellationToken token) {
			// double-checked lock pattern
			if (_midpoints is { } midpoints)
				return midpoints;

			await _lock.AcquireAsync(token);
			try {
				if (_midpoints is { } midpointsDouble)
					return midpointsDouble;

				_midpoints = await PopulateMidpoints(Chunk._midpointsDepth, workItem, token);
				return _midpoints;
			} finally {
				_lock.Release();
			}
		}

		private async ValueTask<Midpoint[]> PopulateMidpoints(int depth, ReaderWorkItem workItem,
			CancellationToken token) {
			if (depth > 31)
				throw new ArgumentOutOfRangeException(nameof(depth), "Depth too for midpoints.");

			var mapCount = Chunk.ChunkFooter.MapCount;
			if (mapCount is 0) // empty chunk
				return [];

			var posmapSize = Chunk.ChunkFooter.IsMap12Bytes ? PosMap.FullSize : PosMap.DeprecatedSize;
			using var posMapTable = UnmanagedMemory.Allocate<byte>(posmapSize * mapCount);

			// write the table once
			workItem.BaseStream.Position = ChunkHeader.Size + Chunk.ChunkFooter.PhysicalDataSize;
			await workItem.BaseStream.ReadExactlyAsync(posMapTable.Memory, token);

			return CreateMidpoints(posMapTable.Span, depth, mapCount, posmapSize);

			static Midpoint[] CreateMidpoints(ReadOnlySpan<byte> posMapTable, int depth, int mapCount, int posmapSize) {
				int midPointsCnt = 1 << depth;
				int segmentSize;
				Midpoint[] midpoints;
				if (mapCount < midPointsCnt) {
					segmentSize = 1; // we cache all items
					midpoints = GC.AllocateUninitializedArray<Midpoint>(mapCount);
				} else {
					segmentSize = mapCount / midPointsCnt;
					midpoints = GC.AllocateUninitializedArray<Midpoint>(1 + (mapCount + segmentSize - 1) /
						segmentSize);
				}

				var i = 0;
				for (int x = 0, xN = mapCount - 1; x < xN; x += segmentSize, i++) {
					midpoints[i] = new(x, ReadPosMap(posMapTable, x, posmapSize));
				}

				// add the very last item as the last midpoints (there can be 0, 1 or more of these)
				var lastMidpoint = new Midpoint(mapCount - 1, ReadPosMap(posMapTable, mapCount - 1, posmapSize));
				while (i < midpoints.Length) {
					midpoints[i++] = lastMidpoint;
				}

				return midpoints;
			}
		}

		private static unsafe PosMap ReadPosMap(ReadOnlySpan<byte> table, int index, int posmapSize) {
			delegate*<ReadOnlySpan<byte>, PosMap> factory;
			if (posmapSize is PosMap.FullSize) {
				factory = &PosMap.FromNewFormat;
			} else {
				factory = &PosMap.FromOldFormat;
			}

			return factory(table.Slice(index * posmapSize, posmapSize));
		}

		public async ValueTask<bool> ExistsAt(long logicalPosition, CancellationToken token) {
			var workItem = Chunk.GetReaderWorkItem();
			try {
				var actualPosition = await TranslateExactPosition(workItem, logicalPosition, token);
				return actualPosition >= 0 && actualPosition < Chunk.PhysicalDataSize;
			} finally {
				Chunk.ReturnReaderWorkItem(workItem);
			}
		}

		public async ValueTask<long> GetActualPosition(long logicalPosition, CancellationToken token) {
			Ensure.Nonnegative(logicalPosition, nameof(logicalPosition));

			var workItem = Chunk.GetReaderWorkItem();
			try {
				return await TranslateExactPosition(workItem, logicalPosition, token);
			} finally {
				Chunk.ReturnReaderWorkItem(workItem);
			}
		}

		public async ValueTask<RecordReadResult> TryReadAt(long logicalPosition, bool couldBeScavenged, CancellationToken token) {
			var start = Instant.Now;
			var workItem = Chunk.GetReaderWorkItem();
			try {
				var actualPosition = await TranslateExactPosition(workItem, logicalPosition, token);
				if (actualPosition is -1 || actualPosition >= Chunk.PhysicalDataSize) {
					if (!couldBeScavenged) {
						_log.Warning(
							"Tried to read actual position {actualPosition}, translated from logPosition {logicalPosition}, " +
							"which is greater than the chunk's physical size of {chunkPhysicalSize}",
							actualPosition, logicalPosition, Chunk.PhysicalDataSize);
					}
					return RecordReadResult.Failure;
				}

				var (record, length) = await TryReadForwardInternal(start, workItem, actualPosition, token);
				return new(record is not null, -1, record, length);
			} finally {
				Chunk.ReturnReaderWorkItem(workItem);
			}
		}

		private async ValueTask<int> TranslateExactPosition(ReaderWorkItem workItem, long pos, CancellationToken token) {
			return await GetOrCreateMidPoints(workItem, token) is { Length: > 0 } midpoints
				? await TranslateExactWithMidpoints(workItem, midpoints, pos, token)
				: await TranslateWithoutMidpoints(workItem, pos, 0, Chunk.ChunkFooter.MapCount - 1, exactMatch: true, token);
		}

		private ValueTask<int> TranslateExactWithMidpoints(ReaderWorkItem workItem, ReadOnlySpan<Midpoint> midpoints, long pos, CancellationToken token) {
			if (pos < midpoints[0].LogPos || pos > midpoints[^1].LogPos)
				return ValueTask.FromResult(-1);

			var recordRange = LocatePosRange(midpoints, pos);
			return TranslateWithoutMidpoints(workItem, pos, recordRange.Lower, recordRange.Upper, exactMatch: true, token);
		}

		public ValueTask<RecordReadResult> TryReadFirst(CancellationToken token)
			=> TryReadClosestForward(0, token);

		public async ValueTask<RecordReadResult> TryReadClosestForward(long logicalPosition, CancellationToken token) {
			if (Chunk.ChunkFooter.MapCount is 0)
				return RecordReadResult.Failure;

			var start = Instant.Now;
			var workItem = Chunk.GetReaderWorkItem();
			try {
				var actualPosition = await TranslateClosestForwardPosition(workItem, logicalPosition, token);
				if (actualPosition is -1 || actualPosition >= Chunk.PhysicalDataSize)
					return RecordReadResult.Failure;

				if (await TryReadForwardInternal(start, workItem, actualPosition, token) is not ({ } record, var length))
					return RecordReadResult.Failure;

				long nextLogicalPos =
					Chunk.ChunkHeader.GetLocalLogPosition(record.GetNextLogPosition(record.LogPosition, length));
				return new RecordReadResult(true, nextLogicalPos, record, length);
			} finally {
				Chunk.ReturnReaderWorkItem(workItem);
			}
		}

		public async ValueTask<RawReadResult> TryReadClosestForwardRaw(long logicalPosition, Func<int, byte[]> getBuffer, CancellationToken token) {
			if (Chunk.ChunkFooter.MapCount == 0)
				return RawReadResult.Failure;

			var workItem = Chunk.GetReaderWorkItem();
			try {
				var actualPosition = await TranslateClosestForwardPosition(workItem, logicalPosition, token);
				if (actualPosition is -1 || actualPosition >= Chunk.PhysicalDataSize)
					return RawReadResult.Failure;

				if (await TryReadForwardRawInternal(workItem, actualPosition, getBuffer, token) is not { Array: not null } record)
					return RawReadResult.Failure;

				// We need to read the record's log position from the buffer so that we can correctly compute
				// the next position. Simply adding the record's length, suffix & prefix to "logicalPosition" won't
				// work properly with scavenged chunks since the computed position may still be before the current
				// record's position, which would cause us to read it again.
				if (!BitConverter.IsLittleEndian)
					throw new NotSupportedException();

				const int logPositionOffset = 2;
				var recordLogPos = BitConverter.ToInt64(record.Array, logPositionOffset);
				long nextLogicalPos =
					Chunk.ChunkHeader.GetLocalLogPosition(recordLogPos + record.Count + 2 * sizeof(int));

				return new(true, nextLogicalPos, record.Array, record.Count);
			} finally {
				Chunk.ReturnReaderWorkItem(workItem);
			}
		}

		public ValueTask<RecordReadResult> TryReadLast(CancellationToken token)
			=> TryReadClosestBackward(Chunk.LogicalDataSize, token);

		public async ValueTask<RecordReadResult> TryReadClosestBackward(long logicalPosition, CancellationToken token) {
			if (Chunk.ChunkFooter.MapCount == 0)
				return RecordReadResult.Failure;

			var start = Instant.Now;
			var workItem = Chunk.GetReaderWorkItem();
			try {
				var actualPosition = await TranslateClosestForwardPosition(workItem, logicalPosition, token);
				// here we allow actualPosition == _physicalDataSize as we can read backward the very last record that way
				if (actualPosition is -1 || actualPosition > Chunk.PhysicalDataSize)
					return RecordReadResult.Failure;

				if (await TryReadBackwardInternal(start, workItem, actualPosition, token) is not ({ } record, var length))
					return RecordReadResult.Failure;

				long nextLogicalPos = Chunk.ChunkHeader.GetLocalLogPosition(record.LogPosition);
				return new(true, nextLogicalPos, record, length);
			} finally {
				Chunk.ReturnReaderWorkItem(workItem);
			}
		}

		private async ValueTask<int> TranslateClosestForwardPosition(ReaderWorkItem workItem, long logicalPosition, CancellationToken token) {
			return await GetOrCreateMidPoints(workItem, token) is { Length: > 0 } midpoints
				? await TranslateClosestForwardWithMidpoints(workItem, midpoints, logicalPosition, token)
				: await TranslateWithoutMidpoints(workItem, logicalPosition, 0,
					Chunk.ChunkFooter.MapCount - 1, exactMatch: false, token);
		}

		private ValueTask<int> TranslateClosestForwardWithMidpoints(ReaderWorkItem workItem, ReadOnlySpan<Midpoint> midpoints, long pos, CancellationToken token) {
			// to allow backward reading of the last record, forward read will decline anyway
			if (pos > midpoints[^1].LogPos)
				return ValueTask.FromResult(Chunk.PhysicalDataSize);

			var recordRange = LocatePosRange(midpoints, pos);
			return TranslateWithoutMidpoints(workItem, pos, recordRange.Lower, recordRange.Upper, exactMatch: false, token);
		}

		private async ValueTask<int> TranslateWithoutMidpoints(ReaderWorkItem workItem, long pos, long startIndex,
			long endIndex, [ConstantExpected] bool exactMatch, CancellationToken token) {
			var count = (int)(endIndex - startIndex + 1L);
			var posmapSize = Chunk.ChunkFooter.IsMap12Bytes
				? PosMap.FullSize
				: PosMap.DeprecatedSize;

			using var buffer = Memory.AllocateExactly<byte>(count * posmapSize);
			workItem.BaseStream.Position =
				ChunkHeader.Size + Chunk.ChunkFooter.PhysicalDataSize + startIndex * posmapSize;

			await workItem.BaseStream.ReadExactlyAsync(buffer.Memory, token);
			return exactMatch ? ExactMatch(buffer.Span) : ClosestForward(buffer.Span);

			int ExactMatch(ReadOnlySpan<byte> segment) {
				for (int low = 0, high = count - 1; low <= high;) {
					var mid = low + (high - low) / 2;
					var v = ReadPosMap(segment, mid, posmapSize);

					if (v.LogPos == pos)
						return v.ActualPos;
					if (v.LogPos < pos)
						low = mid + 1;
					else
						high = mid - 1;
				}

				return -1;
			}

			int ClosestForward(ReadOnlySpan<byte> segment) {
				var res = ReadPosMap(segment, count - 1, posmapSize);

				// to allow backward reading of the last record, forward read will decline anyway
				if (pos > res.LogPos)
					return Chunk.PhysicalDataSize;

				for (int low = 0, high = count - 1; low < high;) {
					var mid = low + (high - low) / 2;
					var v = ReadPosMap(segment, mid, posmapSize);

					if (v.LogPos < pos)
						low = mid + 1;
					else {
						high = mid;
						res = v;
					}
				}

				return res.ActualPos;
			}
		}

		private static Range LocatePosRange(ReadOnlySpan<Midpoint> midpoints, long pos) {
			int lowerMidpoint = LowerMidpointBound(midpoints, pos);
			int upperMidpoint = UpperMidpointBound(midpoints, pos);
			return new(midpoints[lowerMidpoint].ItemIndex, midpoints[upperMidpoint].ItemIndex);
		}

		/// <summary>
		/// Returns the index of lower midpoint for given logical position.
		/// Assumes it always exists.
		/// </summary>
		private static int LowerMidpointBound(ReadOnlySpan<Midpoint> midpoints, long pos) {
			int l = 0;
			int r = midpoints.Length - 1;
			while (l < r) {
				int m = l + (r - l + 1) / 2;
				if (midpoints[m].LogPos <= pos)
					l = m;
				else
					r = m - 1;
			}

			return l;
		}

		/// <summary>
		/// Returns the index of upper midpoint for given logical position.
		/// Assumes it always exists.
		/// </summary>
		private static int UpperMidpointBound(ReadOnlySpan<Midpoint> midpoints, long pos) {
			int l = 0;
			int r = midpoints.Length - 1;
			while (l < r) {
				int m = l + (r - l) / 2;
				if (midpoints[m].LogPos >= pos)
					r = m;
				else
					l = m + 1;
			}

			return l;
		}
	}

	private abstract class TFChunkReadSide {
		protected readonly TFChunk Chunk;
		protected readonly ILogger _log = Log.ForContext<TFChunkReader>();
		private readonly ITransactionFileTracker _tracker;

		protected TFChunkReadSide(TFChunk chunk, ITransactionFileTracker tracker) {
			Ensure.NotNull(chunk, "chunk");
			Chunk = chunk;
			_tracker = tracker;
		}

		private bool ValidateRecordPosition(long actualPosition) {
			// no space even for length prefix and suffix
			if (actualPosition + 2 * sizeof(int) > Chunk.PhysicalDataSize) {
				_log.Warning(
					"Tried to read actual position {actualPosition}, but there isn't enough space for a record left in the chunk. Chunk's data size: {chunkPhysicalDataSize}",
					actualPosition, Chunk.PhysicalDataSize);
				return false;
			}

			return true;
		}

		private void ValidateRecordLength(int length, long actualPosition) {
			if (length <= 0) {
				throw new InvalidReadException(
					$"Log record at actual pos {actualPosition} has non-positive length: {length}. " +
					$" in chunk {Chunk}.");
			}

			if (length > TFConsts.MaxLogRecordSize) {
				throw new InvalidReadException(
					$"Log record at actual pos {actualPosition} has too large length: {length} bytes, " +
					$"while limit is {TFConsts.MaxLogRecordSize} bytes. In chunk {Chunk}.");
			}

			if (actualPosition + length + 2 * sizeof(int) > Chunk.PhysicalDataSize) {
				throw new UnableToReadPastEndOfStreamException(
					$"There is not enough space to read full record (length prefix: {length}). " +
					$"Actual pre-position: {actualPosition}. Something is seriously wrong in chunk {Chunk}.");
			}
		}

		private void ValidatePrefixSuffixLength(int prefixLength, int suffixLength, long actualPosition, string actualPositionType) {
			// verify suffix length == prefix length
			if (suffixLength != prefixLength) {
				throw new InvalidReadException(
					$"Prefix/suffix length inconsistency: prefix length ({prefixLength}) != suffix length ({suffixLength}).\n" +
					$"Actual {actualPositionType}: {actualPosition}. Something is seriously wrong in chunk {Chunk}.");
			}
		}

		protected async ValueTask<(ILogRecord, int)> TryReadForwardInternal(Instant start, ReaderWorkItem workItem, long actualPosition, CancellationToken token) {
			workItem.BaseStream.Position = GetRawPosition(actualPosition);
			if (!ValidateRecordPosition(actualPosition))
				return (null, -1);

			int length;
			var buffer = Memory.AllocateAtLeast<byte>(sizeof(int));

			// Perf: use 'try-catch' instead of 'using' to avoid defensive copy of 'buffer' caused by the compiler
			try {
				length = await workItem.BaseStream.ReadLittleEndianAsync<int>(buffer.Memory, token);
				ValidateRecordLength(length, actualPosition);
			} finally {
				buffer.Dispose();
			}

			// log record payload + length suffix
			var lengthWithSuffix = length + sizeof(int);
			ILogRecord record;
			IBufferedReader bufferedReader = null;
			try {
				// Perf: if buffered stream contains necessary amount of buffered bytes, we can omit expensive buffer
				// rental and copy
				if ((bufferedReader = workItem.TryGetBufferedReader(lengthWithSuffix, out var input)) is null) {
					buffer = Memory.AllocateExactly<byte>(lengthWithSuffix);
					await workItem.BaseStream.ReadExactlyAsync(buffer.Memory, token);
					input = buffer.Memory;
				} else {
					Debug.Assert(buffer.IsEmpty);
				}

				var reader = new SequenceReader(new(input[..length]));
				record = LogRecord.ReadFrom(ref reader);

				_tracker.OnRead(start, record, workItem.Source);

				int suffixLength =
					BinaryPrimitives.ReadInt32LittleEndian(input.Span[^sizeof(int)..]);

				ValidatePrefixSuffixLength(length, suffixLength, actualPosition, "pre-position");
			} catch (Exception exc) {
				throw new InvalidReadException(
					$"Error while reading log record forwards at actual position {actualPosition}. {exc.Message}");
			} finally {
				bufferedReader?.Consume(lengthWithSuffix);
				buffer.Dispose();
			}

			return (record, length);
		}

		protected async ValueTask<ArraySegment<byte>> TryReadForwardRawInternal(ReaderWorkItem workItem, long actualPosition, Func<int, byte[]> getBuffer,
			CancellationToken token) {
			workItem.BaseStream.Position = GetRawPosition(actualPosition);
			if (!ValidateRecordPosition(actualPosition))
				return default;

			using var buffer = Memory.AllocateAtLeast<byte>(sizeof(int));
			var length = await workItem.BaseStream.ReadLittleEndianAsync<int>(buffer.Memory, token);
			ValidateRecordLength(length, actualPosition);

			var record = getBuffer(length);
			await workItem.BaseStream.ReadExactlyAsync(record.AsMemory(0, length), token);

			var suffixLength = await workItem.BaseStream.ReadLittleEndianAsync<int>(buffer.Memory, token);
			ValidatePrefixSuffixLength(length, suffixLength, actualPosition, "pre-position");

			return new(record, 0, length);
		}

		protected async ValueTask<(ILogRecord, int)> TryReadBackwardInternal(Instant start, ReaderWorkItem workItem, long actualPosition, CancellationToken token) {
			// no space even for length prefix and suffix
			if (actualPosition < 2 * sizeof(int)) {
				_log.Warning(
					"Tried to read actual position {actualPosition}, but the position isn't large enough to contain a record",
					actualPosition);
				return (null, -1);
			}

			var realPos = GetRawPosition(actualPosition);
			workItem.BaseStream.Position = realPos - sizeof(int);
			int length;
			MemoryOwner<byte> buffer;

			using (buffer = Memory.AllocateAtLeast<byte>(sizeof(int))) {
				length = await workItem.BaseStream.ReadLittleEndianAsync<int>(buffer.Memory, token);
				if (length <= 0) {
					throw new InvalidReadException(
						string.Format("Log record that ends at actual pos {0} has non-positive length: {1}. "
						              + "In chunk {2}.",
							actualPosition, length, Chunk));
				}

				if (length > TFConsts.MaxLogRecordSize) {
					throw new InvalidReadException(
						string.Format("Log record that ends at actual pos {0} has too large length: {1} bytes, "
						              + "while limit is {2} bytes. In chunk {3}.",
							actualPosition, length, TFConsts.MaxLogRecordSize, Chunk));
				}

				if (actualPosition < length + 2 * sizeof(int)) // no space for record + length prefix and suffix
				{
					throw new UnableToReadPastEndOfStreamException(
						string.Format("There is not enough space to read full record (length suffix: {0}). "
						              + "Actual post-position: {1}. Something is seriously wrong in chunk {2}.",
							length, actualPosition, Chunk));
				}

				workItem.BaseStream.Position = realPos - length - 2 * sizeof(int);

				// verify suffix length == prefix length
				int prefixLength = await workItem.BaseStream.ReadLittleEndianAsync<int>(buffer.Memory, token);
				ValidatePrefixSuffixLength(prefixLength, length, actualPosition, "post-position");
			}

			ILogRecord record;
			buffer = Memory.AllocateExactly<byte>(length);
			try {
				await workItem.BaseStream.ReadExactlyAsync(buffer.Memory, token);
				var reader = new SequenceReader(new(buffer.Memory));
				record = LogRecord.ReadFrom(ref reader);
			} catch (Exception exc) {
				throw new InvalidReadException(
					$"Error while reading log record backwards at actual position {actualPosition}. {exc.Message}");
			} finally {
				buffer.Dispose();
			}

			_tracker.OnRead(start, record, workItem.Source);

			return (record, length);
		}
	}
}

public class InvalidReadException : Exception {
	public InvalidReadException(string message) : base(message) {
	}
}
