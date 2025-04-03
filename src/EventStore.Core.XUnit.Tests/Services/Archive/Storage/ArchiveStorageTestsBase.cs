// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Services.Archive;
using EventStore.Core.Services.Archive.Naming;
using EventStore.Core.Services.Archive.Storage;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.FileNamingStrategy;
using EventStore.Plugins.Transforms;

namespace EventStore.Core.XUnit.Tests.Services.Archive.Storage;

public abstract class ArchiveStorageTestsBase<T> : DirectoryPerTest<T> {
	protected const string AwsRegion = "eu-west-1";
	protected const string AwsBucket = "archiver-unit-tests";

	protected const string ChunkPrefix = "chunk-";
	protected string ArchivePath => Path.Combine(Fixture.Directory, "archive");
	protected string DbPath => Path.Combine(Fixture.Directory, "db");

	public ArchiveStorageTestsBase() {
		Directory.CreateDirectory(ArchivePath);
		Directory.CreateDirectory(DbPath);
	}

	protected IArchiveStorage CreateSut(StorageType storageType) {
		var namingStrategy = new VersionedPatternFileNamingStrategy(ArchivePath, ChunkPrefix);
		var archiveNamingStrategy  = new ArchiveNamingStrategy(namingStrategy);
		var archiveStorage = ArchiveStorageFactory.Create(
				new() {
					StorageType = storageType,
					FileSystem = new() {
						Path = ArchivePath
					},
					S3 = new() {
						Bucket = AwsBucket,
						Region = AwsRegion,
					}
				},
				archiveNamingStrategy);
		return archiveStorage;
	}

	protected static async ValueTask<FakeChunkBlob> CreateChunk(string path, int chunkStartNumber, byte chunkVersion) {
		var namingStrategy = new VersionedPatternFileNamingStrategy(path, ChunkPrefix);

		var chunk = Path.Combine(path, namingStrategy.GetFilenameFor(chunkStartNumber, chunkVersion));
		var content = new byte[1000];
		RandomNumberGenerator.Fill(content);
		var result = new FakeChunkBlob(chunk, chunkStartNumber, chunkVersion);
		await result.WriteAsync(content);
		await result.FlushAsync();
		result.Seek(0, SeekOrigin.Begin);
		return result;
	}

	protected ValueTask<FakeChunkBlob> CreateArchiveChunk(int chunkStartNumber, byte chunkVersion) => CreateChunk(ArchivePath, chunkStartNumber, chunkVersion);
	protected ValueTask<FakeChunkBlob> CreateLocalChunk(int chunkStartNumber, byte chunkVersion) => CreateChunk(DbPath, chunkStartNumber, chunkVersion);

	protected sealed class FakeChunkBlob(string chunkPath, int chunkStartNumber, byte chunkVersion, FileMode mode = FileMode.Create) : FileStream(chunkPath, mode, FileAccess.ReadWrite, FileShare.None, 1024, FileOptions.Asynchronous), IChunkBlob {
		private ChunkHeader _header;
		private ChunkFooter _footer;

		public string ChunkLocator => Name;

		ValueTask IChunkBlob.EnsureInitialized(CancellationToken token)
			=> token.IsCancellationRequested ? ValueTask.FromCanceled(token) : ValueTask.CompletedTask;

		public ValueTask<IChunkRawReader> AcquireRawReader(CancellationToken token)
			=> token.IsCancellationRequested
				? ValueTask.FromCanceled<IChunkRawReader>(token)
				: ValueTask.FromResult<IChunkRawReader>(new StreamReader(this));

		public IAsyncEnumerable<IChunkBlob> UnmergeAsync(CancellationToken token) {
			throw new NotImplementedException();
		}

		public void MarkForDeletion() {
			throw new NotImplementedException();
		}

		public async ValueTask<byte[]> ReadAllBytes() {
			var buffer = new byte[Length];
			Position = 0L;
			await ReadExactlyAsync(buffer);
			return buffer;
		}

		public ChunkHeader ChunkHeader {
			get {
				return _header ??= new(chunkVersion, chunkVersion, (int)Length, chunkStartNumber,
					chunkStartNumber, false, Guid.NewGuid(), TransformType.Identity);
			}
		}

		public ChunkFooter ChunkFooter {
			get {
				return _footer ??= new(isCompleted: true, isMap12Bytes: false, (int)Length, Length, mapSize: 0);
			}
		}
	}

	private sealed class StreamReader(Stream stream) : IChunkRawReader {
		void IDisposable.Dispose() {
		}

		Stream IChunkRawReader.Stream => stream;
	}
}
