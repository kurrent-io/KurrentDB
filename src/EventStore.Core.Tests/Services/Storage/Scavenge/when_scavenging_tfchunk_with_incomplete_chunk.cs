// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.LogCommon;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.Scavenge;

[TestFixture(typeof(LogFormat.V2), typeof(string), LogRecordVersion.LogRecordV0)]
[TestFixture(typeof(LogFormat.V2), typeof(string), LogRecordVersion.LogRecordV1)]
[TestFixture(typeof(LogFormat.V3), typeof(uint), LogRecordVersion.LogRecordV1)]
public class when_scavenging_tfchunk_with_incomplete_chunk<TLogFormat, TStreamId> : ScavengeTestScenario<TLogFormat, TStreamId> {
	private readonly byte _version = LogRecordVersion.LogRecordV0;

	public when_scavenging_tfchunk_with_incomplete_chunk(byte version) {
		_version = version;
	}

	protected override ValueTask<DbResult> CreateDb(TFChunkDbCreationHelper<TLogFormat, TStreamId> dbCreator, CancellationToken token) {
		return dbCreator
			.Chunk(
				Rec.Prepare(0, "ES1", version: _version),
				Rec.Commit(0, "ES1", version: _version),
				Rec.Prepare(1, "ES1", version: _version),
				Rec.Commit(1, "ES1", version: _version))
			.Chunk(
				Rec.Prepare(2, "ES2", version: _version),
				Rec.Commit(2, "ES2", version: _version),
				Rec.Prepare(3, "ES2", version: _version),
				Rec.Commit(3, "ES2", version: _version))
			.CompleteLastChunk()
			.CreateDb(token: token);
	}

	protected override ILogRecord[][] KeptRecords(DbResult dbResult) {
		if (LogFormatHelper<TLogFormat, TStreamId>.IsV2) {
			return new[] {
				new[] {
					dbResult.Recs[0][0],
					dbResult.Recs[0][1],
					dbResult.Recs[0][2],
					dbResult.Recs[0][3],
				},
				new[] {
					dbResult.Recs[1][0],
					dbResult.Recs[1][1],
					dbResult.Recs[1][2],
					dbResult.Recs[1][3],
				}
			};
		}

		return new[] {
			new[] {
				dbResult.Recs[0][0], // "ES1" created
				dbResult.Recs[0][1],
				dbResult.Recs[0][2],
				dbResult.Recs[0][3],
				dbResult.Recs[0][4],
			},
			new[] {
				dbResult.Recs[1][0], // "ES2" created
				dbResult.Recs[1][1],
				dbResult.Recs[1][2],
				dbResult.Recs[1][3],
				dbResult.Recs[1][4],
			}
		};
	}

	[Test]
	public async Task should_not_have_changed_any_records() {
		await CheckRecords();
	}
}
