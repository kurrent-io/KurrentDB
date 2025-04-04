// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Threading.Tasks;
using EventStore.Core.Tests;
using Xunit;

namespace EventStore.Core.XUnit.Tests;

public class DirectoryFixture<T> : IAsyncLifetime {
	public string Directory;

	public DirectoryFixture() {
		var typeName = typeof(T).Name.Length > 30 ? typeof(T).Name.Substring(0, 30) : typeof(T).Name;
		Directory = Path.Combine(Path.GetTempPath(), string.Format("ESX-{0}-{1}", Guid.NewGuid(), typeName));
		System.IO.Directory.CreateDirectory(Directory);
	}

	~DirectoryFixture() {
		DirectoryDeleter.TryForceDeleteDirectoryAsync(Directory).Wait();
	}

	public string GetTempFilePath() {
		return Path.Combine(Directory, string.Format("{0}-{1}", Guid.NewGuid(), typeof(T).FullName));
	}

	public string GetFilePathFor(string fileName) {
		return Path.Combine(Directory, fileName);
	}

	public Task InitializeAsync() {
		return Task.CompletedTask;
	}

	public async Task DisposeAsync() {
		await DirectoryDeleter.TryForceDeleteDirectoryAsync(Directory);
#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
		GC.SuppressFinalize(this);
#pragma warning restore CA1816 // Dispose methods should call SuppressFinalize
	}
}
