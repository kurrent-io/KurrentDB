// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace KurrentDB.Core.Tests;

[TestFixture]
public class ExclusiveDbLockTests {
	[Test]
	public async Task can_release_when_running_in_task_pool() {
		using var sut = new ExclusiveDbLock(GetDbPath());
		Assert.True(sut.Acquire());
		Assert.True(sut.IsAcquired);
		await Task.Delay(1);
		sut.Release();
	}

	[Test]
	public void acquiring_twice_throws() {
		using var sut = new ExclusiveDbLock(GetDbPath());
		sut.Acquire();
		Assert.Throws<InvalidOperationException>(() => sut.Acquire());
	}

	[Test]
	public void releasing_before_acquiring_throws() {
		using var sut = new ExclusiveDbLock(GetDbPath());
		Assert.Throws<InvalidOperationException>(() => sut.Release());
	}

	[Test]
	public void a_second_lock_on_the_same_directory_is_refused() {
		var dbPath = GetDbPath();

		using var first = new ExclusiveDbLock(dbPath);
		Assert.True(first.Acquire());

		using var second = new ExclusiveDbLock(dbPath);
		Assert.False(second.Acquire());
		Assert.False(second.IsAcquired);
	}

	[Test]
	public void can_be_taken_again_once_released() {
		var dbPath = GetDbPath();

		using (var first = new ExclusiveDbLock(dbPath)) {
			Assert.True(first.Acquire());
			first.Release();
		}

		using var second = new ExclusiveDbLock(dbPath);
		Assert.True(second.Acquire());
	}

	[Test]
	public void puts_the_lock_file_in_the_database_directory() {
		using var sut = new ExclusiveDbLock(GetDbPath());
		Assert.True(sut.Acquire());

		Assert.True(File.Exists(sut.LockFilePath));
	}

	[Test]
	public void is_refused_when_the_directory_is_not_there() {
		var dbPath = Path.Combine(Path.GetTempPath(), "kurrentdb-lock-tests", Guid.NewGuid().ToString());
		Assert.False(Directory.Exists(dbPath));

		using var sut = new ExclusiveDbLock(dbPath);
		Assert.False(sut.Acquire());
	}

	// the node creates the database directory, and falls back to another one if it cannot, before it
	// takes the lock on whichever directory it settled on
	private static string GetDbPath() {
		var dbPath = Path.Combine(Path.GetTempPath(), "kurrentdb-lock-tests", Guid.NewGuid().ToString());
		Directory.CreateDirectory(dbPath);
		return dbPath;
	}
}
