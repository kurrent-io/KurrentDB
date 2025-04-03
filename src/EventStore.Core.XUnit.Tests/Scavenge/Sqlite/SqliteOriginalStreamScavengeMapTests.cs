// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Data;
using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.TransactionLog.Scavenging.Sqlite;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge.Sqlite;

public class SqliteOriginalStreamScavengeMapTests : SqliteDbPerTest<SqliteOriginalStreamScavengeMapTests> {

	[Fact]
	public void can_set_original_stream_data() {
		var sut = new SqliteOriginalStreamScavengeMap<int>("OriginalStreamScavengeMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var data = new OriginalStreamData {
			DiscardPoint = DiscardPoint.DiscardIncluding(5),
			IsTombstoned = true,
			MaxAge = TimeSpan.FromDays(13),
			MaxCount = 33,
			MaybeDiscardPoint = DiscardPoint.DiscardBefore(long.MaxValue-1),
			TruncateBefore = 43,
			Status = CalculationStatus.Archived
		};

		sut[33] = data;

		Assert.True(sut.TryGetValue(33, out var v));
		Assert.Equal(data, v, OriginalStreamDataComparer.Default);
	}
	
	[Fact]
	public void can_overwrite_existing() {
		var sut = new SqliteOriginalStreamScavengeMap<int>("OverwriteOriginalStreamScavengeMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		sut[33] = new OriginalStreamData {
			DiscardPoint = DiscardPoint.DiscardIncluding(5),
			IsTombstoned = true,
			MaxAge = TimeSpan.FromDays(13),
			MaxCount = 33,
			MaybeDiscardPoint = DiscardPoint.DiscardBefore(long.MaxValue-1),
			TruncateBefore = 43,
			Status = CalculationStatus.Active
		};
		
		var data = new OriginalStreamData {
			DiscardPoint = DiscardPoint.DiscardIncluding(50),
			IsTombstoned = true,
			MaxAge = TimeSpan.FromDays(30),
			MaxCount = 303,
			MaybeDiscardPoint = DiscardPoint.DiscardBefore(long.MaxValue-100),
			TruncateBefore = 430,
			Status = CalculationStatus.Archived
		};

		sut[33] = data;
		
		Assert.True(sut.TryGetValue(33, out var v));
		Assert.Equal(data, v, OriginalStreamDataComparer.Default);
	}
	
	[Fact]
	public void can_set_tombstone_of_existing() {
		var sut = new SqliteOriginalStreamScavengeMap<int>("OriginalStreamScavengeMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var data = new OriginalStreamData {
			DiscardPoint = DiscardPoint.DiscardIncluding(5),
			MaxAge = TimeSpan.FromDays(13),
			MaxCount = 33,
			MaybeDiscardPoint = DiscardPoint.DiscardBefore(12),
			TruncateBefore = 43,
			Status = CalculationStatus.Archived
		};

		sut[33] = data;
		
		sut.SetTombstone(33);
		data.IsTombstoned = true;
		data.Status = CalculationStatus.Active;
		
		Assert.True(sut.TryGetValue(33, out var v));
		Assert.Equal(data, v, OriginalStreamDataComparer.Default);
	}
	
	[Fact]
	public void can_set_tombstone_of_non_existing() {
		var sut = new SqliteOriginalStreamScavengeMap<int>("OriginalStreamScavengeMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		sut.SetTombstone(33);
		
		Assert.True(sut.TryGetValue(33, out var v));
		Assert.Equal(new OriginalStreamData {
			IsTombstoned = true,
			Status = CalculationStatus.Active
		}, v, OriginalStreamDataComparer.Default);
	}
	
	[Fact]
	public void can_set_stream_metadata_of_existing() {
		var sut = new SqliteOriginalStreamScavengeMap<int>("OriginalStreamScavengeMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var data = new OriginalStreamData() {
			DiscardPoint = DiscardPoint.DiscardIncluding(5),
			MaybeDiscardPoint = DiscardPoint.DiscardBefore(12),
			Status = CalculationStatus.Archived
		};

		sut[33] = data;

		var metadata = new StreamMetadata(
			maxAge: TimeSpan.FromDays(13),
			maxCount: 33,
			truncateBefore: 43);
		
		sut.SetMetadata(33, metadata);
		data.MaxAge = metadata.MaxAge;
		data.MaxCount = metadata.MaxCount;
		data.TruncateBefore = metadata.TruncateBefore;
		data.Status = CalculationStatus.Active;
		
		Assert.True(sut.TryGetValue(33, out var v));
		Assert.Equal(data, v, OriginalStreamDataComparer.Default);
	}
	
	[Fact]
	public void can_set_stream_metadata_without_max_age() {
		var sut = new SqliteOriginalStreamScavengeMap<int>("OriginalStreamScavengeMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var metadata = new StreamMetadata(
			maxCount: 33,
			truncateBefore: 43);
		
		sut.SetMetadata(33, metadata);
		
		Assert.True(sut.TryGetValue(33, out var v));
		Assert.Equal(new OriginalStreamData() {
			MaxCount = metadata.MaxCount,
			TruncateBefore = metadata.TruncateBefore,
			Status = CalculationStatus.Active
		}, v, OriginalStreamDataComparer.Default);
	}
	
	[Fact]
	public void can_set_stream_metadata_without_max_count() {
		var sut = new SqliteOriginalStreamScavengeMap<int>("OriginalStreamScavengeMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var metadata = new StreamMetadata(
			maxAge: TimeSpan.FromDays(13),
			truncateBefore: 43);
		
		sut.SetMetadata(33, metadata);
		
		Assert.True(sut.TryGetValue(33, out var v));
		Assert.Equal(new OriginalStreamData() {
			MaxAge = metadata.MaxAge,
			TruncateBefore = metadata.TruncateBefore,
			Status = CalculationStatus.Active
		}, v, OriginalStreamDataComparer.Default);
	}
	
	[Fact]
	public void can_set_stream_metadata_without_truncating() {
		var sut = new SqliteOriginalStreamScavengeMap<int>("OriginalStreamScavengeMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var metadata = new StreamMetadata(
			maxAge: TimeSpan.FromDays(13),
			maxCount: 33);
		
		sut.SetMetadata(33, metadata);
		
		Assert.True(sut.TryGetValue(33, out var v));
		Assert.Equal(new OriginalStreamData() {
			MaxAge = metadata.MaxAge,
			MaxCount = metadata.MaxCount,
			Status = CalculationStatus.Active
		}, v, OriginalStreamDataComparer.Default);
	}
	
	[Fact]
	public void can_set_stream_metadata_of_non_existing() {
		var sut = new SqliteOriginalStreamScavengeMap<int>("OriginalStreamScavengeMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var metadata = new StreamMetadata(
			maxAge: TimeSpan.FromDays(13),
			maxCount: 33,
			truncateBefore: 43);
		
		sut.SetMetadata(33, metadata);
		
		Assert.True(sut.TryGetValue(33, out var v));
		Assert.Equal(new OriginalStreamData() {
			MaxAge = metadata.MaxAge,
			MaxCount = metadata.MaxCount,
			TruncateBefore = metadata.TruncateBefore,
			Status = CalculationStatus.Active
		}, v, OriginalStreamDataComparer.Default);
	}
	
	[Fact]
	public void can_set_discard_points_of_existing() {
		var sut = new SqliteOriginalStreamScavengeMap<int>("OriginalStreamScavengeMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var data = new OriginalStreamData() {
			MaxAge = TimeSpan.FromDays(13),
			MaxCount = 33,
			TruncateBefore = 43,
		};

		sut[33] = data;

		data.Status = CalculationStatus.Spent;
		data.DiscardPoint = DiscardPoint.DiscardIncluding(5);
		data.MaybeDiscardPoint = DiscardPoint.DiscardIncluding(12);
		sut.SetDiscardPoints(33, data.Status, data.DiscardPoint, data.MaybeDiscardPoint);
		
		Assert.True(sut.TryGetValue(33, out var v));
		Assert.Equal(data, v, OriginalStreamDataComparer.Default);
	}
	
	[Fact]
	public void can_set_discard_points_of_non_existing() {
		var sut = new SqliteOriginalStreamScavengeMap<int>("OriginalStreamScavengeMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var discardPoint = DiscardPoint.DiscardIncluding(5);
		var maybeDiscardPoint = DiscardPoint.DiscardIncluding(12);
		sut.SetDiscardPoints(33, CalculationStatus.Archived, discardPoint, maybeDiscardPoint);

		Assert.True(sut.TryGetValue(33, out var v));
		Assert.Equal(new OriginalStreamData() {
			DiscardPoint = discardPoint,
			MaybeDiscardPoint = maybeDiscardPoint,
			Status = CalculationStatus.Archived
		}, v, OriginalStreamDataComparer.Default);
	}
	
	[Fact]
	public void can_get_chunk_execution_info() {
		var sut = new SqliteOriginalStreamScavengeMap<int>("OriginalStreamScavengeMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var data = new OriginalStreamData() {
			DiscardPoint = DiscardPoint.DiscardIncluding(5),
			MaybeDiscardPoint = DiscardPoint.DiscardIncluding(43),
			MaxAge = TimeSpan.FromDays(13),
			IsTombstoned = true
		};

		sut[33] = data;

		Assert.True(sut.TryGetChunkExecutionInfo(33, out var v));
		Assert.Equal(new ChunkExecutionInfo(
			data.IsTombstoned,
			data.DiscardPoint,
			data.MaybeDiscardPoint,
			data.MaxAge), v);
	}
	
	[Fact]
	public void can_try_get_chunk_execution_info_when_only_tombstoned() {
		var sut = new SqliteOriginalStreamScavengeMap<int>("OriginalStreamScavengeMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));
		
		sut.SetTombstone(33);

		Assert.True(sut.TryGetChunkExecutionInfo(33, out var v));
		Assert.True(v.IsTombstoned);
		Assert.Null(v.MaxAge);
		Assert.Equal(DiscardPoint.KeepAll, v.DiscardPoint);
		Assert.Equal(DiscardPoint.KeepAll, v.MaybeDiscardPoint);
	}
	
	[Fact]
	public void can_try_get_chunk_execution_info_of_non_existing() {
		var sut = new SqliteOriginalStreamScavengeMap<int>("OriginalStreamScavengeMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		Assert.False(sut.TryGetChunkExecutionInfo(33, out var v));
		Assert.Equal(default, v);
	}

	[Fact]
	public void can_get_all_records() {
		var sut = new SqliteOriginalStreamScavengeMap<int>("OriginalStreamScavengeMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var osd = GetOriginalStreamTestData();
		
		sut[0] = osd[0];
		sut[1] = osd[1];
		sut[2] = osd[2];
		sut[3] = osd[3];
		sut[4] = osd[4];

		Assert.Collection(sut.AllRecords(),
			item => {
				Assert.Equal(0, item.Key);
				Assert.Equal(osd[0], item.Value, OriginalStreamDataComparer.Default);
			},
			item => {
				Assert.Equal(1, item.Key);
				Assert.Equal(osd[1], item.Value, OriginalStreamDataComparer.Default);
			},
			item => {
				Assert.Equal(2, item.Key);
				Assert.Equal(osd[2], item.Value, OriginalStreamDataComparer.Default);
			},
			item => {
				Assert.Equal(3, item.Key);
				Assert.Equal(osd[3], item.Value, OriginalStreamDataComparer.Default);
			},
			item => {
				Assert.Equal(4, item.Key);
				Assert.Equal(osd[4], item.Value, OriginalStreamDataComparer.Default);
			});
	}

	[Fact]
	public void can_get_active_records() {
		var sut = new SqliteOriginalStreamScavengeMap<int>("OriginalStreamScavengeMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var osd = GetOriginalStreamTestData();
		
		osd[0].Status = CalculationStatus.Archived;
		osd[1].Status = CalculationStatus.Spent;
		osd[2].Status = CalculationStatus.Active;
		osd[3].Status = CalculationStatus.Active;
		osd[4].Status = CalculationStatus.None;
		
		sut[0] = osd[0];
		sut[1] = osd[1];
		sut[2] = osd[2];
		sut[3] = osd[3];
		sut[4] = osd[4];

		Assert.Collection(sut.ActiveRecords(),
			item => {
				Assert.Equal(2, item.Key);
				Assert.Equal(osd[2], item.Value, OriginalStreamDataComparer.Default);
			},
			item => {
				Assert.Equal(3, item.Key);
				Assert.Equal(osd[3], item.Value, OriginalStreamDataComparer.Default);
			});
	}
	
	[Fact]
	public void can_get_all_active_records_from_checkpoint() {
		var sut = new SqliteOriginalStreamScavengeMap<int>("OriginalStreamScavengeMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var osd = GetOriginalStreamTestData();

		osd[0].Status = CalculationStatus.Active;
		osd[1].Status = CalculationStatus.Active;
		osd[2].Status = CalculationStatus.Active;
		osd[3].Status = CalculationStatus.Active;
		osd[4].Status = CalculationStatus.Archived;
		
		sut[0] = osd[0];
		sut[1] = osd[1];
		sut[2] = osd[2];
		sut[3] = osd[3];
		sut[4] = osd[4];
		
		Assert.Collection(sut.ActiveRecordsFromCheckpoint(1),
			item => {
				Assert.Equal(2, item.Key);
				Assert.Equal(osd[2], item.Value, OriginalStreamDataComparer.Default);
			},
			item => {
				Assert.Equal(3, item.Key);
				Assert.Equal(osd[3], item.Value, OriginalStreamDataComparer.Default);
			});
	}
	
	[Fact]
	public void can_remove_value_from_map() {
		var sut = new SqliteOriginalStreamScavengeMap<int>("OriginalStreamScavengeMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var osd = GetOriginalStreamTestData();
		
		sut[33] = osd[0];
		sut[1] = osd[1];

		Assert.True(sut.TryGetValue(33, out _));
		Assert.True(sut.TryRemove(33, out var removedValue));
		Assert.Equal(osd[0], removedValue, OriginalStreamDataComparer.Default);
		Assert.False(sut.TryGetValue(33, out _));
		
		Assert.True(sut.TryGetValue(1, out var v));
		Assert.Equal(osd[1], v, OriginalStreamDataComparer.Default);
	}
	
	[Fact]
	public void can_try_remove_value_from_map() {
		var sut = new SqliteOriginalStreamScavengeMap<int>("OriginalStreamScavengeMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		Assert.False(sut.TryRemove(33, out _));
	}

	[Fact]
	public void can_remove_multiple_records_at_once() {
		var sut = new SqliteOriginalStreamScavengeMap<int>("OriginalStreamScavengeMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var osd = GetOriginalStreamTestData();

		osd[0].Status = CalculationStatus.Archived;
		osd[1].Status = CalculationStatus.Spent;
		osd[2].Status = CalculationStatus.Spent;
		osd[3].Status = CalculationStatus.Active;
		osd[4].Status = CalculationStatus.Archived;
		
		sut[0] = osd[0];
		sut[1] = osd[1];
		sut[2] = osd[2];
		sut[3] = osd[3];
		sut[4] = osd[4];
		
		sut.DeleteMany(deleteArchived: false);
		
		Assert.Collection(sut.AllRecords(),
			item => { Assert.Equal(CalculationStatus.Archived, item.Value.Status); },
			item => { Assert.Equal(CalculationStatus.Active, item.Value.Status); },
			item => { Assert.Equal(CalculationStatus.Archived, item.Value.Status); });
	}
	
	[Fact]
	public void can_remove_multiple_records_at_once_including_archived() {
		var sut = new SqliteOriginalStreamScavengeMap<int>("OriginalStreamScavengeMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var osd = GetOriginalStreamTestData();

		osd[0].Status = CalculationStatus.Archived;
		osd[1].Status = CalculationStatus.Spent;
		osd[2].Status = CalculationStatus.Spent;
		osd[3].Status = CalculationStatus.Active;
		osd[4].Status = CalculationStatus.Archived;
		
		sut[0] = osd[0];
		sut[1] = osd[1];
		sut[2] = osd[2];
		sut[3] = osd[3];
		sut[4] = osd[4];
		
		sut.DeleteMany(deleteArchived: true);
		
		Assert.Collection(sut.AllRecords(),
			item => { Assert.Equal(CalculationStatus.Active, item.Value.Status); });
	}
	
	private OriginalStreamData[] GetOriginalStreamTestData() {
		return new[] {
			new OriginalStreamData() {
				IsTombstoned = false,
				MaxAge = TimeSpan.FromDays(3),
				MaxCount = 10,
				TruncateBefore = 1
			},
			new OriginalStreamData() {
				DiscardPoint = DiscardPoint.DiscardBefore(10),
				IsTombstoned = true,
				MaxAge = TimeSpan.FromDays(3),
				MaxCount = 100,
				TruncateBefore = 13,
				MaybeDiscardPoint = DiscardPoint.DiscardBefore(21)
			},
			new OriginalStreamData() {
				DiscardPoint = DiscardPoint.DiscardBefore(20),
				MaxAge = TimeSpan.FromHours(12),
				MaybeDiscardPoint = DiscardPoint.DiscardBefore(21)
			},
			new OriginalStreamData() {
				DiscardPoint = DiscardPoint.DiscardBefore(10),
				IsTombstoned = true,
				MaxAge = TimeSpan.FromDays(33),
				TruncateBefore = 11,
			},
			new OriginalStreamData() {
				DiscardPoint = DiscardPoint.DiscardBefore(300),
				MaxAge = TimeSpan.FromDays(300),
				MaxCount = 1000,
				TruncateBefore = 1333,
				MaybeDiscardPoint = DiscardPoint.DiscardBefore(500)
			}
		};
	}
	
	public class OriginalStreamDataComparer : IEqualityComparer<OriginalStreamData> {
		
		public static readonly OriginalStreamDataComparer Default = new OriginalStreamDataComparer();
		
		public bool Equals(OriginalStreamData x, OriginalStreamData y)
		{
			if (x is null || y is null) {
				return false;
			}

			return x.MaxCount == y.MaxCount &&
			       x.MaxAge == y.MaxAge &&
			       x.TruncateBefore == y.TruncateBefore &&
			       x.IsTombstoned == y.IsTombstoned &&
			       x.DiscardPoint == y.DiscardPoint &&
			       x.MaybeDiscardPoint == y.MaybeDiscardPoint && 
			       x.Status == y.Status;
		}

		public int GetHashCode(OriginalStreamData obj) {
			throw new NotImplementedException();
		}
	}
}
