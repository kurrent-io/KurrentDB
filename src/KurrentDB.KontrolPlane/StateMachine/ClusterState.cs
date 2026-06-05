// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Google.Protobuf;

namespace KurrentDB.KontrolPlane.StateMachine;

partial class ClusterState : IReadOnlyDictionary<string, IDatabase>, IProtobufSerializable<ClusterState> {
	public ClusterState AddMainDatabase(out Database database) {
		var copy = new ClusterState(this) { Version = Version + 1UL };
		database = new Database { Version = copy.Version };
		copy.Databases.Add(IDatabase.MainDatabaseId, database);
		return copy;
	}

	public ClusterState AddDatabase(string databaseId, Database database) {
		var copy = new ClusterState(this) { Version = Version + 1UL };
		database.Version = copy.Version;
		copy.Databases.Add(databaseId, database);
		return copy;
	}

	public ClusterState RemoveDatabase(string databaseId) {
		var copy = new ClusterState(this) { Version = Version + 1UL };
		return copy.Databases.Remove(databaseId) ? copy : this;
	}

	public ClusterState AddDatabaseNode(string databaseId, DatabaseNode databaseNode) {
		var copy = new ClusterState(this) { Version = Version + 1UL };

		if (!copy.Databases.TryGetValue(databaseId, out var database))
			return this;

		copy.Databases[databaseId] = database.AddNode(databaseNode);
		return copy;
	}

	public ClusterState UpdateDatabaseNode(string databaseId, DatabaseNode databaseNode, int index) {
		var copy = new ClusterState(this) { Version = Version + 1UL };

		if (!copy.Databases.TryGetValue(databaseId, out var database))
			return this;

		copy.Databases[databaseId] = database.UpdateNode(databaseNode, index);
		return copy;
	}

	public ClusterState RemoveDatabaseNode(string databaseId, ByteString address) {
		var copy = new ClusterState(this) { Version = Version + 1UL };

		if (!copy.Databases.TryGetValue(databaseId, out var database) ||
		    database.Nodes.Find(address, out var index) is null)
			return this;

		database = new(database) { Version = copy.Version };
		database.Nodes.RemoveAt(index);
		copy.Databases[databaseId] = database;
		return copy;
	}

	public ClusterState AppointLeader(string databaseId, ulong expectedVersion, ByteString address) {
		if (!Databases.TryGetValue(databaseId, out var database)
		    || database.Version != expectedVersion
		    || database.Nodes.Find(address, out _) is null)
			return this;

		return new(this) {
			Version = Version + 1UL,
			Databases = { [databaseId] = database.AppointLeader(address) },
		};
	}

	IEnumerator<KeyValuePair<string, IDatabase>> IEnumerable<KeyValuePair<string, IDatabase>>.GetEnumerator() {
		foreach (var (key, value) in Databases) {
			yield return new(key, value);
		}
	}

	IEnumerator IEnumerable.GetEnumerator() => Databases.GetEnumerator();

	int IReadOnlyCollection<KeyValuePair<string, IDatabase>>.Count => Databases.Count;

	bool IReadOnlyDictionary<string, IDatabase>.ContainsKey(string key) => Databases.ContainsKey(key);

	bool IReadOnlyDictionary<string, IDatabase>.TryGetValue(string key, [MaybeNullWhen(false)] out IDatabase value) {
		if (Databases.TryGetValue(key, out var typedResult)) {
			value = typedResult;
			return true;
		}

		value = null;
		return false;
	}

	IDatabase IReadOnlyDictionary<string, IDatabase>.this[string key] => Databases[key];

	IEnumerable<string> IReadOnlyDictionary<string, IDatabase>.Keys => Databases.Keys;

	IEnumerable<IDatabase> IReadOnlyDictionary<string, IDatabase>.Values => Databases.Values;
}
