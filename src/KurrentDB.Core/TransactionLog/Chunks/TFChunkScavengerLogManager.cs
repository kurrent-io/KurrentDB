// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Data;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Storage;
using KurrentDB.Core.Services.UserManagement;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Core.TransactionLog.Chunks;

public class TFChunkScavengerLogManager : ITFChunkScavengerLogManager {
	private readonly string _nodeEndpoint;
	private readonly TimeSpan _scavengeHistoryMaxAge;
	private readonly IODispatcher _ioDispatcher;
	private const int MaxRetryCount = 5;
	private static readonly ILogger Log = Serilog.Log.ForContext<StorageScavenger>();
	private int _isInitialised;

	public TFChunkScavengerLogManager(string nodeEndpoint, TimeSpan scavengeHistoryMaxAge,
		IODispatcher ioDispatcher) {
		_nodeEndpoint = nodeEndpoint;
		_scavengeHistoryMaxAge = scavengeHistoryMaxAge;
		_ioDispatcher = ioDispatcher;
	}

	public void Initialise() {
		// We only initialise on first election so we don't incorrectly mark running scavenges as interrupted.
		if (Interlocked.Exchange(ref _isInitialised, 1) != 0)
			return;

		SetScavengeStreamMetadata();

		Log.Debug("Searching for incomplete scavenges on node {nodeEndPoint}.", _nodeEndpoint);
		GatherIncompleteScavenges(-1, new HashSet<string>(), new List<string>(), new List<string>());
	}

	public ITFChunkScavengerLog CreateLog() {
		return CreateLogInternal(Guid.NewGuid().ToString());
	}

	private TFChunkScavengerLog CreateLogInternal(string scavengeId) {
		return new TFChunkScavengerLog(_ioDispatcher, scavengeId, _nodeEndpoint, MaxRetryCount,
			_scavengeHistoryMaxAge);
	}

	private void SetScavengeStreamMetadata() {
		var metaStreamId = SystemStreams.MetastreamOf(SystemStreams.ScavengesStream);

		_ioDispatcher.ReadBackward(metaStreamId, -1, 1, false, SystemAccounts.System, readResult => {
			if (readResult.Result == ReadStreamResult.Success || readResult.Result == ReadStreamResult.NoStream) {
				if (readResult.Events.Count is 1) {
					var currentMetadata = StreamMetadata.FromJsonBytes(readResult.Events[0].Event.Data);
					var hasProperACL = currentMetadata.Acl != null
									&& currentMetadata.Acl.ReadRoles != null
									&& currentMetadata.Acl.ReadRoles.Contains(x => x.Equals("$ops"));

					if (currentMetadata.MaxAge == _scavengeHistoryMaxAge && hasProperACL) {
						Log.Debug("Max age and $ops read permission already set for the {stream} stream.", SystemStreams.ScavengesStream);
						return;
					}
				}

				Log.Debug("Setting max age for the {stream} stream to {maxAge}.", SystemStreams.ScavengesStream,
					_scavengeHistoryMaxAge);

				Log.Debug("Setting $ops read permission for the {stream} stream", SystemStreams.ScavengesStream);

				var acl = new StreamAcl(
					new string[] { "$ops" },
					new string[] { },
					new string[] { },
					new string[] { },
					new string[] { }
				);

				var metadata = new StreamMetadata(maxAge: _scavengeHistoryMaxAge, acl: acl);
				var metaStreamEvent = new Event(Guid.NewGuid(), SystemEventTypes.StreamMetadata, isJson: true,
					data: metadata.ToJsonBytes(), metadata: null, SchemaInfo.None, SchemaInfo.None);
				_ioDispatcher.WriteEvent(metaStreamId, ExpectedVersion.Any, metaStreamEvent,
					SystemAccounts.System, m => {
						if (m.Result != OperationResult.Success) {
							Log.Error(
								"Failed to write the $maxAge of {days} days and set $ops permission for the {stream} stream. Reason: {reason}",
								_scavengeHistoryMaxAge.TotalDays, SystemStreams.ScavengesStream, m.Result);
						}
					});
			}
		});
	}

	private void GatherIncompleteScavenges(long from, ISet<string> completedScavenges,
		IList<string> incompleteScavenges, IList<string> recentScavenges) {
		_ioDispatcher.ReadBackward(SystemStreams.ScavengesStream, from, 20, true, SystemAccounts.System,
			readResult => {
				if (readResult.Result != ReadStreamResult.Success &&
					readResult.Result != ReadStreamResult.NoStream) {
					Log.Debug("Unable to read {stream} for scavenge log clean up. Result: {result}",
						SystemStreams.ScavengesStream, readResult.Result);
					return;
				}

				foreach (var ev in readResult.Events) {
					if (ev.ResolveResult == ReadEventResult.Success) {
						var dictionary = ev.Event.Data.ParseJson<Dictionary<string, object>>();

						object entryNode;
						if (!dictionary.TryGetValue("nodeEndpoint", out entryNode) ||
							entryNode.ToString() != _nodeEndpoint) {
							continue;
						}

						object scavengeIdEntry;
						if (!dictionary.TryGetValue("scavengeId", out scavengeIdEntry)) {
							Log.Warning("An entry in the scavenge log has no scavengeId");
							continue;
						}

						var scavengeId = scavengeIdEntry.ToString();
						if (recentScavenges.Count <= 1000) //bound size
							recentScavenges.Add(scavengeId);

						if (ev.Event.EventType == SystemEventTypes.ScavengeCompleted) {
							completedScavenges.Add(scavengeId);
						} else if (ev.Event.EventType == SystemEventTypes.ScavengeStarted) {
							if (!completedScavenges.Contains(scavengeId)) {
								incompleteScavenges.Add(scavengeId);
							}
						}
					}
				}

				if (readResult.IsEndOfStream || readResult.Events is []) {
					SetOpsPermissions(recentScavenges);
					CompleteInterruptedScavenges(incompleteScavenges);
				} else {
					GatherIncompleteScavenges(readResult.NextEventNumber, completedScavenges, incompleteScavenges, recentScavenges);
				}
			});
	}

	private void SetOpsPermissions(IList<string> recentScavengeIds) {
		//sets $ops permissions on last 30 $scavenges-<scavenge id> stream
		//added for backward compatibility to make UI scavenge history work properly with $ops users

		var last30ScavengeIds = new HashSet<string>();
		foreach (var scavengeId in recentScavengeIds) {
			if (last30ScavengeIds.Count >= 30)
				break;
			last30ScavengeIds.Add(scavengeId);
		}

		if (last30ScavengeIds.Count > 0)
			Log.Debug("Setting $ops read permission on last {count} $scavenges-<scavenge id> streams.", last30ScavengeIds.Count);

		foreach (var scavengeId in last30ScavengeIds) {
			var acl = new StreamAcl(
				new string[] { "$ops" },
				new string[] { },
				new string[] { },
				new string[] { },
				new string[] { }
			);

			var scavengeIdStream = SystemStreams.ScavengesStream + "-" + scavengeId;
			var metaStreamId = SystemStreams.MetastreamOf(scavengeIdStream);
			var metadata = new StreamMetadata(maxAge: _scavengeHistoryMaxAge, acl: acl);
			var metaStreamEvent = new Event(Guid.NewGuid(), SystemEventTypes.StreamMetadata, isJson: true,
				data: metadata.ToJsonBytes(), metadata: null, SchemaInfo.None, SchemaInfo.None);
			_ioDispatcher.WriteEvent(metaStreamId, ExpectedVersion.Any, metaStreamEvent,
				SystemAccounts.System, m => {
					if (m.Result != OperationResult.Success) {
						Log.Error(
							"Failed to write the $maxAge of {days} and set $ops read permission for the {stream} stream. Reason: {reason}",
							_scavengeHistoryMaxAge.TotalDays, scavengeIdStream, m.Result);
					}
				});
		}
	}

	private void CompleteInterruptedScavenges(IList<string> incompletedScavenges) {
		if (incompletedScavenges.Count == 0) {
			Log.Debug("No incomplete scavenges found on node {nodeEndPoint}.", _nodeEndpoint);
		} else {
			Log.Information(
				"Found {incomplete} incomplete scavenge{s} on node {nodeEndPoint}. Marking as failed:{newLine}{incompleteScavenges}",
				incompletedScavenges.Count, incompletedScavenges.Count == 1 ? "" : "s", _nodeEndpoint,
				Environment.NewLine, string.Join(Environment.NewLine, incompletedScavenges));
		}

		foreach (var incompletedScavenge in incompletedScavenges) {
			GatherIncompleteScavengeStats(-1, new IncompleteScavengeStats(incompletedScavenge));
		}
	}

	private void GatherIncompleteScavengeStats(long from, IncompleteScavengeStats incompleteScavengeStats) {
		_ioDispatcher.ReadBackward(incompleteScavengeStats.ScavengeStream, from, 50, true, SystemAccounts.System,
			readResult => {
				if (readResult.Result != ReadStreamResult.Success &&
					readResult.Result != ReadStreamResult.NoStream) {
					Log.Debug("Unable to read {stream} for scavenge log clean up. Result: {result}",
						incompleteScavengeStats.ScavengeStream, readResult.Result);
					return;
				}

				foreach (var ev in readResult.Events) {
					if (ev.ResolveResult == ReadEventResult.Success) {
						var dictionary = ev.Event.Data.ParseJson<Dictionary<string, object>>();

						if (ev.Event.EventType == SystemEventTypes.ScavengeChunksCompleted
						 || ev.Event.EventType == SystemEventTypes.ScavengeMergeCompleted
						 || ev.Event.EventType == SystemEventTypes.ScavengeIndexCompleted) {
							if (dictionary.TryGetValue("spaceSaved", out var spaceSavedEntry)) {
								incompleteScavengeStats.SpaceSaved += (long)spaceSavedEntry;
							}
							if (dictionary.TryGetValue("timeTaken", out var timeTakenEntry)) {
								incompleteScavengeStats.TimeTaken += TimeSpan.Parse(timeTakenEntry.ToString());
							}

							if (ev.Event.EventType == SystemEventTypes.ScavengeChunksCompleted) {
								if (dictionary.TryGetValue("chunkEndNumber", out var chunkEndNumberEntry)) {
									incompleteScavengeStats.MaxChunkScavenged = Math.Max(incompleteScavengeStats.MaxChunkScavenged, (int)(long)chunkEndNumberEntry);
								}
							}
						}
					}
				}

				if (readResult.IsEndOfStream || readResult.Events is []) {
					CompleteScavengeWithStats(incompleteScavengeStats);
				} else {
					GatherIncompleteScavengeStats(readResult.NextEventNumber, incompleteScavengeStats);
				}
			});
	}

	private void CompleteScavengeWithStats(IncompleteScavengeStats incompleteScavengeStats) {

		var log = CreateLogInternal(incompleteScavengeStats.ScavengeId);

		log.ScavengeCompleted(ScavengeResult.Interrupted,
			"The node was restarted.",
			incompleteScavengeStats.TimeTaken,
			incompleteScavengeStats.SpaceSaved,
			incompleteScavengeStats.MaxChunkScavenged);
	}

	class IncompleteScavengeStats {
		public string ScavengeId { get; private set; }
		public string ScavengeStream => string.Format("{0}-{1}", SystemStreams.ScavengesStream, ScavengeId);

		public IncompleteScavengeStats(string scavengeId) {
			ScavengeId = scavengeId;
		}


		public long SpaceSaved { get; set; }
		public int MaxChunkScavenged { get; set; }
		public TimeSpan TimeTaken { get; set; } = TimeSpan.Zero;

	}
}
