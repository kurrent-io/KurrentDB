// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Client;

namespace KurrentDB.Embedded.Tests;

/// <summary>
/// A running embedded database in a directory of its own, shared by the tests in a class.
/// </summary>
public sealed class EmbeddedDatabaseFixture : IAsyncInitializer, IAsyncDisposable {
	EmbeddedKurrentDB? _database;

	public EmbeddedKurrentDB Database =>
		_database ?? throw new InvalidOperationException("The fixture has not been initialized.");

	public string DataDirectory { get; } = TestPaths.NewDataDirectory();

	public string UnixSocketPath => Database.UnixSocketPath;

	public async Task InitializeAsync() {
		_database = new EmbeddedKurrentDB(TestPaths.Options(DataDirectory));
		await _database.StartAsync();
	}

	public KurrentDBClient CreateClient() => new(Database.CreateClientSettings());

	public async ValueTask DisposeAsync() {
		if (_database is not null)
			await _database.DisposeAsync();

		TestPaths.Delete(DataDirectory);
	}
}

/// <summary>
/// Temporary locations for a test database, and the options that go with them.
/// </summary>
public static class TestPaths {
	// the socket lives in the data directory and has around a hundred bytes to play with, so these go
	// under the temporary directory rather than next to a test's working directory
	static readonly string Root = Path.Combine(Path.GetTempPath(), "kdbe");

	static TestPaths() => Directory.CreateDirectory(Root);

	public static string NewDataDirectory() => Path.Combine(Root, $"d{Guid.NewGuid():N}"[..12]);

	public static EmbeddedKurrentDBOptions Options(string dataDirectory) =>
		new() {
			DataDirectory = dataDirectory,
			StartupTimeout = TimeSpan.FromMinutes(2),
		};

	public static void Delete(string directory) {
		try {
			if (Directory.Exists(directory))
				Directory.Delete(directory, recursive: true);
		} catch (IOException) {
			// a leftover temporary directory is not worth failing a test over
		}
	}
}
