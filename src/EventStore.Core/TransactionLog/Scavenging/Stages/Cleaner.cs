// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Threading;
using Serilog;

namespace EventStore.Core.TransactionLog.Scavenging {
	public class Cleaner : ICleaner {
		private readonly ILogger _logger;
		private readonly bool _unsafeIgnoreHardDeletes;

		public Cleaner(
			ILogger logger,
			bool unsafeIgnoreHardDeletes) {
			_logger = logger;
			_unsafeIgnoreHardDeletes = unsafeIgnoreHardDeletes;
		}

		public void Clean(
			ScavengePoint scavengePoint,
			IScavengeStateForCleaner state,
			CancellationToken cancellationToken) {

			_logger.Debug("SCAVENGING: Started new scavenge clean up phase for {scavengePoint}",
				scavengePoint.GetName());

			var checkpoint = new ScavengeCheckpoint.Cleaning(scavengePoint);
			state.SetCheckpoint(checkpoint);
			Clean(checkpoint, state, cancellationToken);
		}

		public void Clean(
			ScavengeCheckpoint.Cleaning checkpoint,
			IScavengeStateForCleaner state,
			CancellationToken cancellationToken) {

			_logger.Debug("SCAVENGING: Cleaning checkpoint: {checkpoint}", checkpoint);

			cancellationToken.ThrowIfCancellationRequested();

			// we clean up in a transaction, not so that we can checkpoint, but just to save lots of
			// implicit transactions from being created
			var transaction = state.BeginTransaction();
			try {
				CleanImpl(state, cancellationToken);
				transaction.Commit(checkpoint);
			} catch {
				transaction.Rollback();
				throw;
			}
		}

		private void CleanImpl(
			IScavengeStateForCleaner state,
			CancellationToken cancellationToken) {

			// constant time operation
			if (state.AllChunksExecuted()) {
				// Now we know we have successfully executed every chunk with weight.

				_logger.Debug("SCAVENGING: Deleting metastream data");
				state.DeleteMetastreamData();

				cancellationToken.ThrowIfCancellationRequested();

				_logger.Debug("SCAVENGING: Deleting originalstream data. Deleting archived: {deleteArchived}",
					_unsafeIgnoreHardDeletes);
				state.DeleteOriginalStreamData(deleteArchived: _unsafeIgnoreHardDeletes);

			} else {
				// one or more chunks was not executed, due to error or not meeting the threshold
				// either way, we cannot clean up the stream datas
				if (_unsafeIgnoreHardDeletes) {
					// the chunk executor should have stopped the scavenge if it couldn't execute any
					// chunk when this flag is set.
					// we could have removed the tombstone without removing all the other records.
					throw new Exception(
						"UnsafeIgnoreHardDeletes is true but not all chunks have been executed");
				} else {
					_logger.Debug("SCAVENGING: Skipping cleanup because some chunks have not been executed");
				}
			}
		}
	}
}
