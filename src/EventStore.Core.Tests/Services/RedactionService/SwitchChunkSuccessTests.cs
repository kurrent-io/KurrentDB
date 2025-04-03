// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data.Redaction;
using EventStore.Core.TransactionLog.Chunks;
using NUnit.Framework;
using MD5 = EventStore.Core.Hashing.MD5;

// successful chunk switching tests have individual classes as they modify the database and thus the test fixture cannot be reused

namespace EventStore.Core.Tests.Services.RedactionService;

public class SwitchChunkSuccess<TLogFormat, TStreamId> : SwitchChunkTests<TLogFormat, TStreamId> {
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class CanSwitchWithExactCopy : SwitchChunkSuccess<TLogFormat, TStreamId> {
		[Test]
		public async Task can_switch_with_exact_copy() {
			var newChunk = Path.Combine(PathName, $"{nameof(can_switch_with_exact_copy)}.tmp");

			File.Copy(GetChunk(1, 0, true), newChunk);
			var msg = await SwitchChunk(GetChunk(1, 0), Path.GetFileName(newChunk));
			Assert.AreEqual(SwitchChunkResult.Success, msg.Result);
			Assert.True(!File.Exists(newChunk));
			Assert.True(!File.Exists(GetChunk(1, 0, true)));
			Assert.True(File.Exists(GetChunk(1, 1, true)));
			Assert.AreEqual(1, Db.Manager.FileSystem.LocalNamingStrategy.GetVersionFor(Path.GetFileName((await Db.Manager.GetInitializedChunk(1, CancellationToken.None)).LocalFileName)));
			Assert.True(File.Exists(GetChunk(0, 0, true)));

			// can switch again
			File.Copy(GetChunk(1, 1, true), newChunk);
			msg = await SwitchChunk(GetChunk(1, 1), Path.GetFileName(newChunk));
			Assert.AreEqual(SwitchChunkResult.Success, msg.Result);
			Assert.True(!File.Exists(newChunk));
			Assert.True(!File.Exists(GetChunk(1, 0, true)));
			Assert.True(!File.Exists(GetChunk(1, 1, true)));
			Assert.True(File.Exists(GetChunk(1, 2, true)));
			Assert.AreEqual(2, Db.Manager.FileSystem.LocalNamingStrategy.GetVersionFor(Path.GetFileName((await Db.Manager.GetInitializedChunk(1, CancellationToken.None)).LocalFileName)));
			Assert.True(File.Exists(GetChunk(0, 0, true)));
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class CanSwitchWithModifiedCopy : SwitchChunkSuccess<TLogFormat, TStreamId> {
		[Test]
		public async Task can_switch_with_modified_copy() {
			var newChunk = Path.Combine(PathName, $"{nameof(can_switch_with_modified_copy)}.tmp");

			File.Copy(GetChunk(1, 0, true), newChunk);

			// edit the chunk file
			File.SetAttributes(newChunk, FileAttributes.Normal);
			await using (var fs = new FileStream(newChunk, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
				// jump in the data and make some modifications
				fs.Seek(ChunkHeader.Size + 123, SeekOrigin.Begin);
				fs.WriteByte(0xAB);
				fs.WriteByte(0xCD);

				// truncate the hash
				fs.SetLength(fs.Length - ChunkFooter.ChecksumSize);

				// recompute the hash
				byte[] newHash;
				fs.Seek(0, SeekOrigin.Begin);
				using (var md5 = MD5.Create())
					newHash = await md5.ComputeHashAsync(fs);

				// write the new hash
				fs.Seek(0, SeekOrigin.End);
				fs.Write(newHash);
			}

			var msg = await SwitchChunk(GetChunk(1, 0), Path.GetFileName(newChunk));
			Assert.AreEqual(SwitchChunkResult.Success, msg.Result);
			Assert.True(!File.Exists(newChunk));
			Assert.True(!File.Exists(GetChunk(1, 0, true)));
			Assert.True(File.Exists(GetChunk(1, 1, true)));
			Assert.AreEqual(1, Db.Manager.FileSystem.LocalNamingStrategy.GetVersionFor(Path.GetFileName((await Db.Manager.GetInitializedChunk(1, CancellationToken.None)).LocalFileName)));
			Assert.True(File.Exists(GetChunk(0, 0, true)));
		}
	}
}
