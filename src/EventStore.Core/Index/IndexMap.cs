// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Exceptions;
using EventStore.Core.Settings;
using EventStore.Core.Util;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Index;

public class IndexMap {
	private static readonly ILogger Log = Serilog.Log.ForContext<IndexMap>();

	public const int IndexMapVersion = 2;

	public readonly int Version;

	public readonly long PrepareCheckpoint;
	public readonly long CommitCheckpoint;

	private readonly List<List<PTable>> _map;
	private readonly int _maxTablesPerLevel;
	private readonly int _maxTableLevelsForAutomaticMerge;
	private readonly int _pTableMaxReaderCount;

	private IndexMap(int version, List<List<PTable>> tables, long prepareCheckpoint, long commitCheckpoint,
		int maxTablesPerLevel, int maxTableLevelsForAutomaticMerge, int pTableMaxReaderCount) {
		Ensure.Nonnegative(version, "version");
		if (prepareCheckpoint < -1) throw new ArgumentOutOfRangeException("prepareCheckpoint");
		if (commitCheckpoint < -1) throw new ArgumentOutOfRangeException("commitCheckpoint");
		if (maxTablesPerLevel <= 1) throw new ArgumentOutOfRangeException("maxTablesPerLevel");

		Version = version;

		PrepareCheckpoint = prepareCheckpoint;
		CommitCheckpoint = commitCheckpoint;

		_map = CopyFrom(tables);
		_maxTablesPerLevel = maxTablesPerLevel;
		_maxTableLevelsForAutomaticMerge = maxTableLevelsForAutomaticMerge;
		_pTableMaxReaderCount = pTableMaxReaderCount;
		VerifyStructure();
	}

	private static List<List<PTable>> CopyFrom(List<List<PTable>> tables) {
		var tmp = new List<List<PTable>>();
		for (int i = 0; i < tables.Count; i++) {
			tmp.Add(new List<PTable>(tables[i]));
		}

		return tmp;
	}

	private void VerifyStructure() {
		if (_map.SelectMany(level => level).Any(item => item == null))
			throw new CorruptIndexException("Internal indexmap structure corruption.");
	}

	private static void AddTableToTables(List<List<PTable>> tables, int level, PTable table) {
		while (level >= tables.Count) {
			tables.Add(new List<PTable>());
		}

		var innerTables = tables[level] ?? (tables[level] = new List<PTable>());

		innerTables.Add(table);
	}

	private static void InsertTableToTables(List<List<PTable>> tables, int level, int position, PTable table) {
		while (level >= tables.Count)
			tables.Add(new List<PTable>());

		var innerTables = tables[level] ?? (tables[level] = new List<PTable>());

		while (position >= innerTables.Count)
			innerTables.Add(null);

		innerTables[position] = table;
	}

	public IEnumerable<PTable> InOrder() {
		var map = _map;
		// level 0 (newest tables) -> N (oldest tables)
		for (int i = 0; i < map.Count; ++i) {
			// last in the level's list (newest on level) -> to first (oldest on level)
			for (int j = map[i].Count - 1; j >= 0; --j) {
				yield return map[i][j];
			}
		}
	}

	public IEnumerable<PTable> InReverseOrder() {
		var map = _map;
		// N (oldest tables) -> level 0 (newest tables)
		for (int i = map.Count - 1; i >= 0; --i) {
			// from first (oldest on level) in the level's list -> last in the level's list (newest on level)
			for (int j = 0, n = map[i].Count; j < n; ++j) {
				yield return map[i][j];
			}
		}
	}

	public IEnumerable<string> GetAllFilenames() {
		return from level in _map
			from table in level
			select table.Filename;
	}

	public static IndexMap CreateEmpty(int maxTablesPerLevel, int maxTableLevelsForAutomaticMerge, int pTableMaxReaderCount) {
		return new IndexMap(IndexMapVersion, new List<List<PTable>>(), -1, -1, maxTablesPerLevel,
			maxTableLevelsForAutomaticMerge, pTableMaxReaderCount);
	}

	public static IndexMap FromFile(
		string filename,
		int maxTablesPerLevel,
		bool loadPTables,
		int cacheDepth,
		bool skipIndexVerify,
		bool useBloomFilter,
		int lruCacheSize,
		int threads,
		int maxAutoMergeLevel,
		int pTableMaxReaderCount) {
		if (!File.Exists(filename))
			return CreateEmpty(maxTablesPerLevel, maxAutoMergeLevel, pTableMaxReaderCount);

		using (var f = File.OpenRead(filename)) {
			// calculate real MD5 hash except first 32 bytes which are string representation of stored hash
			f.Position = 32;
			var realHash = MD5Hash.GetHashFor(f);
			f.Position = 0;

			using (var reader = new StreamReader(f)) {
				ReadAndCheckHash(reader, realHash);

				// at this point we can assume the format is ok, so actually no need to check errors.
				var version = ReadVersion(reader);

				var checkpoints = ReadCheckpoints(reader);
				var prepareCheckpoint = checkpoints.PreparePosition;
				var commitCheckpoint = checkpoints.CommitPosition;
				if (version > 1) {
					var tmpMaxAutoMergeLevel = ReadMaxAutoMergeLevel(reader);
					if (tmpMaxAutoMergeLevel < maxAutoMergeLevel)
						throw new CorruptIndexException(
							$"Index map has lower maximum auto merge level ({tmpMaxAutoMergeLevel}) than is currently configured ({maxAutoMergeLevel}) and the index will need to be rebuilt");

					maxAutoMergeLevel = Math.Min(maxAutoMergeLevel, tmpMaxAutoMergeLevel);
				}

				//we are doing a logical upgrade of the version to have the new data, so we will change the version to match so that new files are saved with the right version
				version = IndexMapVersion;
				var tables = loadPTables
					? LoadPTables(reader, filename, checkpoints, cacheDepth, skipIndexVerify, useBloomFilter, lruCacheSize, threads, pTableMaxReaderCount)
					: new List<List<PTable>>();

				if (!loadPTables && reader.ReadLine() != null)
					throw new CorruptIndexException(
						string.Format("Negative prepare/commit checkpoint in non-empty IndexMap: {0}.",
							checkpoints));

				return new IndexMap(version, tables, prepareCheckpoint, commitCheckpoint, maxTablesPerLevel,
					maxAutoMergeLevel, pTableMaxReaderCount);
			}
		}
	}

	private static void ReadAndCheckHash(TextReader reader, byte[] realHash) {
		// read stored MD5 hash and convert it from string to byte array
		string text;
		if ((text = reader.ReadLine()) == null)
			throw new CorruptIndexException("IndexMap file is empty.");
		if (text.Length != 32 || !text.All(x => char.IsDigit(x) || (x >= 'A' && x <= 'F')))
			throw new CorruptIndexException(string.Format("Corrupted IndexMap MD5 hash. Hash ({0}): {1}.",
				text.Length, text));

		// check expected and real hashes are the same
		var expectedHash = new byte[16];
		for (int i = 0; i < 16; ++i) {
			expectedHash[i] = Convert.ToByte(text.Substring(i * 2, 2), 16);
		}

		if (expectedHash.Length != realHash.Length) {
			throw new CorruptIndexException(
				string.Format("Hash validation error (different hash sizes).\n"
				              + "Expected hash ({0}): {1}, real hash ({2}): {3}.",
					expectedHash.Length, BitConverter.ToString(expectedHash),
					realHash.Length, BitConverter.ToString(realHash)));
		}

		for (int i = 0; i < realHash.Length; ++i) {
			if (expectedHash[i] != realHash[i]) {
				throw new CorruptIndexException(
					string.Format("Hash validation error (different hashes).\n"
					              + "Expected hash ({0}): {1}, real hash ({2}): {3}.",
						expectedHash.Length, BitConverter.ToString(expectedHash),
						realHash.Length, BitConverter.ToString(realHash)));
			}
		}
	}

	private static int ReadVersion(TextReader reader) {
		string text;
		if ((text = reader.ReadLine()) == null)
			throw new CorruptIndexException("Corrupted version.");
		return int.Parse(text);
	}

	private static TFPos ReadCheckpoints(TextReader reader) {
		// read and check prepare/commit checkpoint
		string text;
		if ((text = reader.ReadLine()) == null)
			throw new CorruptIndexException("Corrupted commit checkpoint.");
		try {
			long prepareCheckpoint;
			long commitCheckpoint;
			var checkpoints = text.Split('/');
			if (!long.TryParse(checkpoints[0], out prepareCheckpoint) || prepareCheckpoint < -1)
				throw new CorruptIndexException(string.Format("Invalid prepare checkpoint: {0}.", checkpoints[0]));
			if (!long.TryParse(checkpoints[1], out commitCheckpoint) || commitCheckpoint < -1)
				throw new CorruptIndexException(string.Format("Invalid commit checkpoint: {0}.", checkpoints[1]));
			return new TFPos(commitCheckpoint, prepareCheckpoint);
		} catch (Exception exc) {
			throw new CorruptIndexException("Corrupted prepare/commit checkpoints pair.", exc);
		}
	}

	private static int ReadMaxAutoMergeLevel(TextReader reader) {
		if (!(reader.ReadLine() is string text && int.TryParse(text, out var maxAutoMergeLevel)))
			throw new CorruptIndexException("Corrupted auto merge level.");
		return maxAutoMergeLevel;
	}

	private static IEnumerable<string> GetAllLines(StreamReader reader) {
		// all next lines are PTables sorted by levels
		string text;
		while ((text = reader.ReadLine()) != null) {
			yield return text;
		}
	}

	private static List<List<PTable>> LoadPTables(StreamReader reader, string indexmapFilename, TFPos checkpoints,
		int cacheDepth, bool skipIndexVerify,
		bool useBloomFilter, int lruCacheSize,
		int threads,
		int pTableMaxReaderCount) {
		var tables = new List<List<PTable>>();
		try {
			try {
				Parallel.ForEach(
					GetAllLines(reader)
						.Reverse(), // Reverse so we load the highest levels (biggest files) first - ensures we use concurrency in the most efficient way.
					new ParallelOptions {MaxDegreeOfParallelism = threads},
					indexMapEntry => {
						if (checkpoints.PreparePosition < 0 || checkpoints.CommitPosition < 0)
							throw new CorruptIndexException(
								string.Format("Negative prepare/commit checkpoint in non-empty IndexMap: {0}.",
									checkpoints));

						PTable ptable = null;
						var pieces = indexMapEntry.Split(',');
						try {
							var level = int.Parse(pieces[0]);
							var position = int.Parse(pieces[1]);
							var file = pieces[2];
							var path = Path.GetDirectoryName(indexmapFilename);
							var ptablePath = Path.Combine(path, file);

							ptable = PTable.FromFile(ptablePath, ESConsts.PTableInitialReaderCount, pTableMaxReaderCount, cacheDepth, skipIndexVerify, useBloomFilter, lruCacheSize);

							lock (tables) {
								InsertTableToTables(tables, level, position, ptable);
							}
						} catch (Exception) {
							// if PTable file path was correct, but data is corrupted, we still need to dispose opened streams
							if (ptable != null)
								ptable.Dispose();

							throw;
						}
					});

				// Verify map is correct
				for (int i = 0; i < tables.Count; ++i) {
					for (int j = 0; j < tables[i].Count; ++j) {
						if (tables[i][j] == null) {
							throw new CorruptIndexException(
								$"indexmap is missing contiguous level,position {i},{j}");
						}
					}
				}
			} catch (AggregateException aggEx) {
				// We only care that *something* has gone wrong, throw the first exception
				throw aggEx.InnerException;
			}
		} catch (Exception exc) {
			// also dispose all previously loaded correct PTables
			for (int i = 0; i < tables.Count; ++i) {
				for (int j = 0; j < tables[i].Count; ++j) {
					if (tables[i][j] != null)
						tables[i][j].Dispose();
				}
			}

			throw new CorruptIndexException("Error while loading IndexMap.", exc);
		}

		return tables;
	}

	public void SaveToFile(string filename) {
		var tmpIndexMap = string.Format("{0}.{1}.indexmap.tmp", filename, Guid.NewGuid());

		using (var memStream = new MemoryStream())
		using (var memWriter = new StreamWriter(memStream)) {
			memWriter.WriteLine(new string('0', 32)); // pre-allocate space for MD5 hash
			memWriter.WriteLine(Version);
			memWriter.WriteLine("{0}/{1}", PrepareCheckpoint, CommitCheckpoint);
			memWriter.WriteLine(_maxTableLevelsForAutomaticMerge);
			for (int i = 0; i < _map.Count; i++) {
				for (int j = 0; j < _map[i].Count; j++) {
					memWriter.WriteLine("{0},{1},{2}", i, j, new FileInfo(_map[i][j].Filename).Name);
				}
			}

			memWriter.Flush();

			memStream.Position = 32;
			var hash = MD5Hash.GetHashFor(memStream);

			memStream.Position = 0;
			foreach (var t in hash) {
				memWriter.Write(t.ToString("X2"));
			}

			memWriter.Flush();

			memStream.Position = 0;
			using (var f = File.OpenWrite(tmpIndexMap)) {
				f.Write(memStream.GetBuffer(), 0, (int)memStream.Length);
				f.FlushToDisk();
			}
		}

		int trial = 0;
		int maxTrials = 5;
		while (trial < maxTrials) {
			Action<Exception> errorHandler = ex => {
				Log.Error("Failed trial to replace indexmap {indexMap} with {tmpIndexMap}.", filename, tmpIndexMap);
				Log.Error("Exception: {e}", ex);
				trial += 1;
			};
			try {
				if (File.Exists(filename)) {
					File.SetAttributes(filename, FileAttributes.Normal);
					File.Delete(filename);
				}

				File.Move(tmpIndexMap, filename);
				break;
			} catch (IOException exc) {
				errorHandler(exc);
				if (trial >= maxTrials) {
					WindowsProcessUtil.PrintWhoIsLocking(tmpIndexMap, Log);
					WindowsProcessUtil.PrintWhoIsLocking(filename, Log);
				}
			} catch (UnauthorizedAccessException exc) {
				errorHandler(exc);
			}
		}
	}

	public AddResult AddPTable(PTable tableToAdd,
		long prepareCheckpoint,
		long commitCheckpoint) {
		Ensure.NotNull(tableToAdd, "tableToAdd");
		Ensure.Nonnegative(prepareCheckpoint, "prepareCheckpoint");
		Ensure.Nonnegative(commitCheckpoint, "commitCheckpoint");

		var tables = CopyFrom(_map);
		AddTableToTables(tables, 0, tableToAdd);

		var indexMap = new IndexMap(Version, tables, prepareCheckpoint, commitCheckpoint, _maxTablesPerLevel,
			_maxTableLevelsForAutomaticMerge, _pTableMaxReaderCount);

		var canMergeAny = false;
		var maxTableLevelsToMerge = Math.Min(tables.Count, _maxTableLevelsForAutomaticMerge);
		for (int level = 0; level < maxTableLevelsToMerge; level++) {
			if (tables[level].Count >= _maxTablesPerLevel) {
				canMergeAny = true;
				break;
			}
		}

		return new AddResult(indexMap, canMergeAny);
	}

	public MergeResult TryMergeOneLevel(
		IIndexFilenameProvider filenameProvider,
		byte version,
		int indexCacheDepth = 16,
		bool skipIndexVerify = false,
		bool useBloomFilter = true,
		int lruCacheSize = 1_000_000) {

		var tables = CopyFrom(_map);

		var hasMergedAny = false;
		var canMergeAny = false;
		var toDelete = new List<PTable>();
		var maxTableLevelsToMerge = Math.Min(tables.Count, _maxTableLevelsForAutomaticMerge);
		for (int level = 0; level < maxTableLevelsToMerge; level++) {
			if (tables[level].Count >= _maxTablesPerLevel) {
				if (hasMergedAny) {
					canMergeAny = true;
					break;
				}
				var filename = filenameProvider.GetFilenameNewTable();
				PTable mergedTable = PTable.MergeTo(
					tables[level],
					filename,
					version,
					ESConsts.PTableInitialReaderCount,
					_pTableMaxReaderCount,
					indexCacheDepth,
					skipIndexVerify,
					useBloomFilter,
					lruCacheSize);
				hasMergedAny = true;

				AddTableToTables(tables, level + 1, mergedTable);
				toDelete.AddRange(tables[level]);
				tables[level].Clear();
			}
		}

		var indexMap = new IndexMap(Version, tables, PrepareCheckpoint, CommitCheckpoint, _maxTablesPerLevel,
			_maxTableLevelsForAutomaticMerge, _pTableMaxReaderCount);
		return new MergeResult(indexMap, toDelete, hasMergedAny, canMergeAny);
	}

	public MergeResult TryManualMerge(
		IIndexFilenameProvider filenameProvider,
		byte version,
		int indexCacheDepth = 16,
		bool skipIndexVerify = false,
		bool useBloomFilter = true,
		int lruCacheSize = 1_000_000) {

		var tables = CopyFrom(_map);

		if (tables.Count <= _maxTableLevelsForAutomaticMerge) {
			return new MergeResult(this, new List<PTable>(), false, false);
		}

		var toDelete = new List<PTable>();
		var tablesToMerge = tables.Skip(_maxTableLevelsForAutomaticMerge).SelectMany(a => a).ToList();
		if (tablesToMerge.Count == 1)
			return new MergeResult(this, new List<PTable>(), false, false);

		var filename = filenameProvider.GetFilenameNewTable();
		PTable mergedTable = PTable.MergeTo(
			tablesToMerge,
			filename,
			version,
			ESConsts.PTableInitialReaderCount,
			_pTableMaxReaderCount,
			indexCacheDepth,
			skipIndexVerify,
			useBloomFilter,
			lruCacheSize);

		for (int i = tables.Count - 1; i > _maxTableLevelsForAutomaticMerge; i--) {
			tables.RemoveAt(i);
		}

		tables[_maxTableLevelsForAutomaticMerge].Clear();
		AddTableToTables(tables, _maxTableLevelsForAutomaticMerge + 1, mergedTable);
		toDelete.AddRange(tablesToMerge);

		var indexMap = new IndexMap(Version, tables, PrepareCheckpoint, CommitCheckpoint, _maxTablesPerLevel,
			_maxTableLevelsForAutomaticMerge, _pTableMaxReaderCount);
		return new MergeResult(indexMap, toDelete, true, false);
	}

	public async ValueTask<ScavengeResult> Scavenge(Guid toScavenge, CancellationToken ct,
		Func<IndexEntry, CancellationToken, ValueTask<bool>> shouldKeep,
		IIndexFilenameProvider filenameProvider,
		byte version,
		int indexCacheDepth = 16,
		bool skipIndexVerify = false,
		bool useBloomFilter = true,
		int lruCacheSize = 1_000_000) {

		var scavengedMap = CopyFrom(_map);
		for (int level = 0; level < scavengedMap.Count; level++) {
			for (int i = 0; i < scavengedMap[level].Count; i++) {
				if (scavengedMap[level][i].Id == toScavenge) {
					var filename = filenameProvider.GetFilenameNewTable();
					var oldTable = scavengedMap[level][i];

					var (scavenged, spaceSaved) = await PTable.Scavenged(
						oldTable,
						filename,
						version,
						shouldKeep,
						ESConsts.PTableInitialReaderCount,
						_pTableMaxReaderCount,
						indexCacheDepth,
						skipIndexVerify,
						useBloomFilter,
						lruCacheSize,
						ct);

					if (scavenged is null) {
						return ScavengeResult.Failed(oldTable, level, i);
					}

					scavengedMap[level][i] = scavenged;

					var indexMap = new IndexMap(Version, scavengedMap, PrepareCheckpoint, CommitCheckpoint,
						_maxTablesPerLevel, _maxTableLevelsForAutomaticMerge, _pTableMaxReaderCount);

					return ScavengeResult.Success(indexMap, oldTable, scavenged, spaceSaved, level, i);
				}
			}
		}

		throw new ArgumentException("Unable to find table in map.", nameof(toScavenge));
	}

	public void Dispose(TimeSpan timeout) {
		foreach (var ptable in InOrder()) {
			ptable.Dispose();
		}

		foreach (var ptable in InOrder()) {
			ptable.WaitForDisposal(timeout);
		}
	}
}
