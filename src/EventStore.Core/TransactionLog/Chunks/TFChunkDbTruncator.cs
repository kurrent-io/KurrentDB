using System;
using System.Collections.Generic;
using System.IO;
using EventStore.Common.Utils;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.TransactionLog.Chunks {
	public class TFChunkDbTruncator {
		private static readonly ILogger Log = Serilog.Log.ForContext<TFChunkDbTruncator>();

		private readonly TFChunkDbConfig _config;

		public TFChunkDbTruncator(TFChunkDbConfig config) {
			Ensure.NotNull(config, "config");
			_config = config;
		}

		public void TruncateDb(long truncateChk) {
			var writerChk = _config.WriterCheckpoint.Read();
			var requestedTruncation = writerChk - truncateChk;
			if (_config.MaxTruncation >= 0 && requestedTruncation > _config.MaxTruncation) {
				Log.Error(
					"MaxTruncation is set and truncate checkpoint is out of bounds. MaxTruncation {maxTruncation} vs requested truncation {requestedTruncation} [{writerChk} => {truncateChk}].  To proceed, set MaxTruncation to -1 (no max) or greater than {requestedTruncationHint}.",
					_config.MaxTruncation, requestedTruncation, writerChk, truncateChk, requestedTruncation);
				throw new Exception(
					string.Format("MaxTruncation is set ({0}) and truncate checkpoint is out of bounds (requested truncation is {1} [{2} => {3}]).", _config.MaxTruncation, requestedTruncation, writerChk, truncateChk));
			}

			var oldLastChunkNum = (int)(writerChk / _config.ChunkSize);
			var newLastChunkNum = (int)(truncateChk / _config.ChunkSize);
			var chunkEnumerator = new TFChunkEnumerator(_config.FileNamingStrategy);

			var excessiveChunks = _config.FileNamingStrategy.GetAllVersionsFor(oldLastChunkNum + 1);
			if (excessiveChunks.Length > 0)
				throw new Exception(string.Format("During truncation of DB excessive TFChunks were found:\n{0}.",
					string.Join("\n", excessiveChunks)));

			ChunkHeader newLastChunkHeader = null;
			string newLastChunkFilename = null;

			// find the chunk to truncate to
			foreach (var chunkInfo in chunkEnumerator.EnumerateChunks(oldLastChunkNum)) {
				switch (chunkInfo) {
					case LatestVersion(var fileName, var _, var end):
						if (newLastChunkFilename != null || end < newLastChunkNum)
							break;
						newLastChunkHeader = ReadChunkHeader(fileName);
						newLastChunkFilename = fileName;
						break;
					case MissingVersion(var fileName, var chunkNum):
						if (chunkNum < newLastChunkNum)
							throw new Exception($"Could not find any chunk #{fileName}.");
						break;
				}
			}

			// it's not bad if there is no file, it could have been deleted on previous run
			if (newLastChunkHeader != null) {
				var chunksToDelete = new List<string>();
				var chunkNumToDeleteFrom = newLastChunkNum + 1;
				if (newLastChunkHeader.IsScavenged) {
					Log.Information(
						"Deleting ALL chunks from #{chunkStartNumber} inclusively "
						+ "as truncation position is in the middle of scavenged chunk.",
						newLastChunkHeader.ChunkStartNumber);
					chunkNumToDeleteFrom = newLastChunkHeader.ChunkStartNumber;
				}

				foreach (var chunkInfo in chunkEnumerator.EnumerateChunks(oldLastChunkNum)) {
					switch (chunkInfo) {
						case LatestVersion(var fileName, var start, _):
							if (start >= chunkNumToDeleteFrom)
								chunksToDelete.Add(fileName);
							break;
						case OldVersion(var fileName, var start):
							if (start >= chunkNumToDeleteFrom)
								chunksToDelete.Add(fileName);
							break;
					}
				}

				// we need to remove excessive chunks from largest number to lowest one, so in case of crash
				// mid-process, we don't end up with broken non-sequential chunks sequence.
				chunksToDelete.Reverse();
				foreach (var chunkFile in chunksToDelete) {
					Log.Information("File {chunk} will be deleted during TruncateDb procedure.", chunkFile);
					File.SetAttributes(chunkFile, FileAttributes.Normal);
					File.Delete(chunkFile);
				}

				if (!newLastChunkHeader.IsScavenged) {
					TruncateChunkAndFillWithZeros(newLastChunkHeader, newLastChunkFilename, truncateChk);
				} else {
					truncateChk = newLastChunkHeader.ChunkStartPosition;
					Log.Information(
						"Setting TruncateCheckpoint to {truncateCheckpoint} "
						+ "as truncation position is in the middle of scavenged chunk.",
						truncateChk);
				}
			}

			// Note: Prior to going offline for truncation, we attempt to set the epoch checkpoint to an epoch record
			// located before the truncate position (if such a record is present in the epoch cache).
			// This saves us from resetting it to -1 here and also saves us from scanning the transaction log for
			// a proper epoch record during startup.
			if (_config.EpochCheckpoint.Read() >= truncateChk) {
				var epochChk = _config.EpochCheckpoint.Read();
				Log.Information("Truncating epoch from {epochFrom} (0x{epochFrom:X}) to {epochTo} (0x{epochTo:X}).", epochChk,
					epochChk, -1, -1);
				_config.EpochCheckpoint.Write(-1);
				_config.EpochCheckpoint.Flush();
			}

			if (_config.ChaserCheckpoint.Read() > truncateChk) {
				var chaserChk = _config.ChaserCheckpoint.Read();
				Log.Information(
					"Truncating chaser from {chaserCheckpoint} (0x{chaserCheckpoint:X}) to {truncateCheckpoint} (0x{truncateCheckpoint:X}).",
					chaserChk, chaserChk, truncateChk, truncateChk);
				_config.ChaserCheckpoint.Write(truncateChk);
				_config.ChaserCheckpoint.Flush();
			}

			if (_config.WriterCheckpoint.Read() > truncateChk) {
				var writerCheckpoint = _config.WriterCheckpoint.Read();
				Log.Information(
					"Truncating writer from {writerCheckpoint} (0x{writerCheckpoint:X}) to {truncateCheckpoint} (0x{truncateCheckpoint:X}).",
					writerCheckpoint, writerCheckpoint, truncateChk, truncateChk);
				_config.WriterCheckpoint.Write(truncateChk);
				_config.WriterCheckpoint.Flush();
			}

			Log.Information("Resetting TruncateCheckpoint to {truncateCheckpoint} (0x{truncateCheckpoint:X}).", -1, -1);
			_config.TruncateCheckpoint.Write(-1);
			_config.TruncateCheckpoint.Flush();
		}

		private void TruncateChunkAndFillWithZeros(ChunkHeader chunkHeader, string chunkFilename, long truncateChk) {
			if (chunkHeader.IsScavenged
				|| chunkHeader.ChunkStartNumber != chunkHeader.ChunkEndNumber
				|| truncateChk < chunkHeader.ChunkStartPosition
				|| truncateChk >= chunkHeader.ChunkEndPosition) {
				throw new Exception(
					string.Format(
						"Chunk #{0}-{1} ({2}) is not correct unscavenged chunk. TruncatePosition: {3}, ChunkHeader: {4}.",
						chunkHeader.ChunkStartNumber, chunkHeader.ChunkEndNumber, chunkFilename, truncateChk,
						chunkHeader));
			}

			File.SetAttributes(chunkFilename, FileAttributes.Normal);
			using (var fs = new FileStream(chunkFilename, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) {
				fs.SetLength(ChunkHeader.Size + chunkHeader.ChunkSize + ChunkFooter.Size);
				fs.Position = ChunkHeader.Size + chunkHeader.GetLocalLogPosition(truncateChk);
				var zeros = new byte[65536];
				var leftToWrite = fs.Length - fs.Position;
				while (leftToWrite > 0) {
					var toWrite = (int)Math.Min(leftToWrite, zeros.Length);
					fs.Write(zeros, 0, toWrite);
					leftToWrite -= toWrite;
				}

				fs.FlushToDisk();
			}
		}

		private static ChunkHeader ReadChunkHeader(string chunkFileName) {
			ChunkHeader chunkHeader;
			using (var fs = new FileStream(chunkFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
				chunkHeader = ChunkHeader.FromStream(fs);
			}

			return chunkHeader;
		}
	}
}
