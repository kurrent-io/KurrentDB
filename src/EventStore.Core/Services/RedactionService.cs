using System;
using System.IO;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Data.Redaction;
using EventStore.Core.Exceptions;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Synchronization;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using Serilog;

namespace EventStore.Core.Services {
	public abstract class RedactionService {
		protected static readonly ILogger Log = Serilog.Log.ForContext<RedactionService>();
	}

	public class RedactionService<TStreamId> :
		RedactionService,
		IHandle<RedactionMessage.GetEventPosition>,
		IHandle<RedactionMessage.AcquireChunksLock>,
		IHandle<RedactionMessage.SwitchChunk>,
		IHandle<RedactionMessage.ReleaseChunksLock>,
		IHandle<SystemMessage.BecomeShuttingDown> {

		private readonly IQueuedHandler _queuedHandler;
		private readonly TFChunkDb _db;
		private readonly IReadIndex<TStreamId> _readIndex;
		private readonly SemaphoreSlimLock _switchChunksLock;

		private const string NewChunkFileExtension = ".tmp";

		public RedactionService(
			IQueuedHandler queuedHandler,
			TFChunkDb db,
			IReadIndex<TStreamId> readIndex,
			SemaphoreSlimLock switchChunksLock) {
			Ensure.NotNull(queuedHandler, nameof(queuedHandler));
			Ensure.NotNull(db, nameof(db));
			Ensure.NotNull(readIndex, nameof(readIndex));
			Ensure.NotNull(switchChunksLock, nameof(switchChunksLock));

			_queuedHandler = queuedHandler;
			_db = db;
			_readIndex = readIndex;
			_switchChunksLock = switchChunksLock;
		}

		public void Handle(RedactionMessage.GetEventPosition message) {
			try {
				GetEventPosition(message.EventStreamId, message.EventNumber, message.Envelope);
			} catch (Exception ex) {
				Log.Error(ex, "REDACTION: An error has occurred when getting position for stream: {stream}, event number: {eventNumber}.",
					message.EventStreamId, message.EventNumber);
				message.Envelope.ReplyWith(
					new RedactionMessage.GetEventPositionCompleted(GetEventPositionResult.UnexpectedError, Array.Empty<EventPosition>()));
			}
		}

		private void GetEventPosition(string streamName, long eventNumber, IEnvelope envelope) {
			var streamId = _readIndex.GetStreamId(streamName);
			var result = _readIndex.ReadEventInfo_KeepDuplicates(streamId, eventNumber);

			var eventPositions = new EventPosition[result.EventInfos.Length];

			for (int i = 0; i < result.EventInfos.Length; i++) {
				var eventInfo = result.EventInfos[i];
				var logPos = eventInfo.LogPosition;
				var chunk = _db.Manager.GetChunkFor(logPos);
				var localPosition = chunk.ChunkHeader.GetLocalLogPosition(logPos);
				var chunkEventOffset = chunk.GetActualRawPosition(localPosition);

				// all the events returned by ReadEventInfo_KeepDuplicates() must exist in the log
				// since the log record was read from the chunk to check for hash collisions.
				if (chunkEventOffset < 0)
					throw new Exception($"Failed to fetch actual raw position for event at log position: {logPos}");

				if (chunkEventOffset > uint.MaxValue)
					throw new Exception($"Actual raw position for event at log position: {logPos} is larger than uint.MaxValue: {chunkEventOffset}");

				eventPositions[i] = new EventPosition(
					logPosition: logPos,
					chunkFile: Path.GetFileName(chunk.FileName),
					chunkVersion: chunk.ChunkHeader.Version,
					chunkComplete: chunk.ChunkFooter is { IsCompleted: true },
					chunkEventOffset: (uint) chunkEventOffset);
			}

			envelope.ReplyWith(
				new RedactionMessage.GetEventPositionCompleted(GetEventPositionResult.Success, eventPositions));
		}

		public void Handle(RedactionMessage.AcquireChunksLock message) {
			if (_switchChunksLock.TryAcquire(out var acquisitionId)) {
				Log.Information("REDACTION: Acquired the chunks lock");
				message.Envelope.ReplyWith(
					new RedactionMessage.AcquireChunksLockCompleted(AcquireChunksLockResult.Success, acquisitionId));
			} else {
				Log.Information("REDACTION: Failed to acquire the chunks lock");
				message.Envelope.ReplyWith(
					new RedactionMessage.AcquireChunksLockCompleted(AcquireChunksLockResult.Failed, Guid.Empty));
			}
		}

		public void Handle(RedactionMessage.ReleaseChunksLock message) {
			if (_switchChunksLock.TryRelease(message.AcquisitionId)) {
				Log.Information("REDACTION: Released the chunks lock");
				message.Envelope.ReplyWith(
					new RedactionMessage.ReleaseChunksLockCompleted(ReleaseChunksLockResult.Success));
			} else {
				Log.Information("REDACTION: Failed to release the chunks lock");
				message.Envelope.ReplyWith(
					new RedactionMessage.ReleaseChunksLockCompleted(ReleaseChunksLockResult.Failed));
			}
		}

		public void Handle(RedactionMessage.SwitchChunk message) {
			var currentAcquisitionId = _switchChunksLock.CurrentAcquisitionId;
			if (currentAcquisitionId != message.AcquisitionId) {
				Log.Error("REDACTION: Skipping switching of chunk: {targetChunk} with chunk: {newChunk} " +
				          "as the lock is not currently held by the requester. " +
				          "(Requester\'s lock ID: {requestLockId:B}. Current lock ID: {currentLockId:B})",
					message.TargetChunkFile, message.NewChunkFile, message.AcquisitionId, currentAcquisitionId);
				message.Envelope.ReplyWith(
					new RedactionMessage.SwitchChunkCompleted(SwitchChunkResult.UnexpectedError));
				return;
			}

			try {
				Log.Information("REDACTION: Replacing chunk {targetChunk} with {newChunk}", message.TargetChunkFile, message.NewChunkFile);
				SwitchChunk(message.TargetChunkFile, message.NewChunkFile, message.Envelope);
			} catch (Exception ex) {
				Log.Error(ex, "REDACTION: An error has occurred when trying to switch chunk: {targetChunk} with chunk: {newChunk}.",
					message.TargetChunkFile, message.NewChunkFile);
				message.Envelope.ReplyWith(
					new RedactionMessage.SwitchChunkCompleted(SwitchChunkResult.UnexpectedError));
			}
		}

		private void SwitchChunk(string targetChunkFile, string newChunkFile, IEnvelope envelope) {
			if (!IsValidSwitchChunkRequest(targetChunkFile, newChunkFile, out var newChunk, out var failReason)) {
				envelope.ReplyWith(new RedactionMessage.SwitchChunkCompleted(failReason));
				return;
			}

			_db.Manager.SwitchChunk(
				chunk: newChunk,
				verifyHash: false,
				removeChunksWithGreaterNumbers: false);

			envelope.ReplyWith(new RedactionMessage.SwitchChunkCompleted(SwitchChunkResult.Success));
		}

		private static bool IsUnsafeFileName(string fileName) {
			// protect against directory traversal attacks
			return fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains("..");
		}

		private bool IsValidSwitchChunkRequest(string targetChunkFile, string newChunkFile, out TFChunk newChunk, out SwitchChunkResult failReason) {
			newChunk = null;

			if (IsUnsafeFileName(targetChunkFile)) {
				failReason = SwitchChunkResult.TargetChunkFileNameInvalid;
				return false;
			}

			if (IsUnsafeFileName(newChunkFile)) {
				failReason = SwitchChunkResult.NewChunkFileNameInvalid;
				return false;
			}

			int targetChunkNumber;
			try {
				targetChunkNumber = _db.Config.FileNamingStrategy.GetIndexFor(targetChunkFile);
			} catch {
				failReason = SwitchChunkResult.TargetChunkFileNameInvalid;
				return false;
			}

			if (Path.GetExtension(newChunkFile) != NewChunkFileExtension) {
				failReason = SwitchChunkResult.NewChunkFileNameInvalid;
				return false;
			}

			if (!File.Exists(Path.Combine(_db.Config.Path, targetChunkFile))) {
				failReason = SwitchChunkResult.TargetChunkFileNotFound;
				return false;
			}

			var newChunkPath = Path.Combine(_db.Config.Path, newChunkFile);
			if (!File.Exists(newChunkPath)) {
				failReason = SwitchChunkResult.NewChunkFileNotFound;
				return false;
			}

			TFChunk targetChunk;
			try {
				targetChunk = _db.Manager.GetChunk(targetChunkNumber);
			} catch(ArgumentOutOfRangeException) {
				failReason = SwitchChunkResult.TargetChunkExcessive;
				return false;
			}

			if (Path.GetFileName(targetChunk.FileName) != targetChunkFile) {
				failReason = SwitchChunkResult.TargetChunkInactive;
				return false;
			}

			if (targetChunk.ChunkFooter is not { IsCompleted: true }) {
				failReason = SwitchChunkResult.TargetChunkNotCompleted;
				return false;
			}

			ChunkHeader newChunkHeader;
			ChunkFooter newChunkFooter;
			try {
				using var fs = new FileStream(newChunkPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				try {
					newChunkHeader = ChunkHeader.FromStream(fs);
					fs.Seek(-ChunkFooter.Size, SeekOrigin.End);
					newChunkFooter = ChunkFooter.FromStream(fs);
				} catch {
					failReason = SwitchChunkResult.NewChunkHeaderOrFooterInvalid;
					return false;
				}
			} catch {
				failReason = SwitchChunkResult.NewChunkOpenFailed;
				return false;
			}

			if (newChunkHeader.ChunkStartNumber != targetChunk.ChunkHeader.ChunkStartNumber ||
			    newChunkHeader.ChunkEndNumber != targetChunk.ChunkHeader.ChunkEndNumber) {
				failReason = SwitchChunkResult.ChunkRangeDoesNotMatch;
				return false;
			}

			if (!newChunkFooter.IsCompleted) {
				failReason = SwitchChunkResult.NewChunkNotCompleted;
				return false;
			}

			try {
				// temporarily open the chunk to verify its integrity
				newChunk = TFChunk.FromCompletedFile(
					_db.Manager.ChunkCacheManager,
					filename: newChunkPath,
					verifyHash: true,
					unbufferedRead: _db.Config.Unbuffered,
					initialReaderCount: 1,
					maxReaderCount: 1,
					optimizeReadSideCache: false,
					reduceFileCachePressure: true,
					tracker: new TFChunkTracker.NoOp());
			} catch (HashValidationException) {
				failReason = SwitchChunkResult.NewChunkHashInvalid;
				return false;
			} catch {
				failReason = SwitchChunkResult.NewChunkOpenFailed;
				return false;
			}

			failReason = SwitchChunkResult.None;
			return true;
		}

		public void Handle(SystemMessage.BecomeShuttingDown message) {
			// _switchChunksLock is not disposed here since it's shared between multiple services
			_queuedHandler?.RequestStop();
		}
	}
}
