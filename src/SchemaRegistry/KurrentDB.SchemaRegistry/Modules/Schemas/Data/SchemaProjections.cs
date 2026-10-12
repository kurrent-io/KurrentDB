// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable VirtualMemberCallInConstructor

using System.Runtime.InteropServices;
using System.Text.Json;
using Kurrent.Quack;
using Kurrent.Surge.DuckDB.Projectors;
using KurrentDB.SchemaRegistry.Infrastructure;
using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;

namespace KurrentDB.SchemaRegistry.Data;

public class SchemaProjections : DuckDBProjection {
	public SchemaProjections() {
		Project<SchemaCreated>((msg, db, ctx) => {
			using var scope = db.GetScopedConnection(out var connection);
			using var tx = connection.BeginTransaction();

			var checkpoint = ctx.Record.LogPosition.CommitPosition ?? 0L;

			connection.ExecuteNonQuery<InsertSchemaVersionArgs, InsertSchemaVersionStmt>(new(
				msg.SchemaVersionId,
				msg.SchemaName,
				msg.VersionNumber,
				msg.SchemaDefinition.Memory,
				(sbyte)msg.DataFormat,
				msg.CreatedAt.ToDateTime(),
				checkpoint
			));

			connection.ExecuteNonQuery<InsertSchemaArgs, InsertSchemaStmt>(new(
				msg.SchemaName,
				msg.Description,
				(sbyte)msg.DataFormat,
				msg.VersionNumber,
				msg.SchemaVersionId,
				(sbyte)msg.Compatibility,
				JsonSerializer.Serialize(msg.Tags, SchemaRegistryJsonContext.Default.IDictionaryStringString),
				msg.CreatedAt.ToDateTime(),
				checkpoint
			));

			tx.CommitOnDispose();
			return ValueTask.CompletedTask;
		});

		Project<SchemaVersionRegistered>((msg, db, ctx) => {
			using var scope = db.GetScopedConnection(out var connection);
			using var tx = connection.BeginTransaction();

			var checkpoint = ctx.Record.LogPosition.CommitPosition ?? 0L;

			connection.ExecuteNonQuery<InsertSchemaVersionArgs, InsertSchemaVersionStmt>(new(
				msg.SchemaVersionId,
				msg.SchemaName,
				msg.VersionNumber,
				msg.SchemaDefinition.Memory,
				(sbyte)msg.DataFormat,
				msg.RegisteredAt.ToDateTime(),
				checkpoint
			));

			connection.ExecuteNonQuery<UpdateSchemaLatestVersionArgs, UpdateSchemaLatestVersionStmt>(new(
				msg.VersionNumber,
				msg.SchemaVersionId,
				checkpoint,
				msg.SchemaName
			));

			tx.CommitOnDispose();
			return ValueTask.CompletedTask;
		});

		Project<SchemaCompatibilityModeChanged>((msg, db, _) => {
			using var scope = db.GetScopedConnection(out var connection);

			connection.ExecuteNonQuery<UpdateSchemaCompatibilityArgs, UpdateSchemaCompatibilityStmt>(new(
				(sbyte)msg.Compatibility,
				msg.ChangedAt.ToDateTime(),
				msg.SchemaName
			));

			return ValueTask.CompletedTask;
		});

		Project<SchemaDescriptionUpdated>((msg, db, _) => {
			using var scope = db.GetScopedConnection(out var connection);

			connection.ExecuteNonQuery<UpdateSchemaDescriptionArgs, UpdateSchemaDescriptionStmt>(new(
				msg.Description,
				msg.UpdatedAt.ToDateTime(),
				msg.SchemaName
			));

			return ValueTask.CompletedTask;
		});

		Project<SchemaTagsUpdated>((msg, db, _) => {
			using var scope = db.GetScopedConnection(out var connection);

			connection.ExecuteNonQuery<UpdateSchemaTagsArgs, UpdateSchemaTagsStmt>(new(
				JsonSerializer.Serialize(msg.Tags, SchemaRegistryJsonContext.Default.IDictionaryStringString),
				msg.UpdatedAt.ToDateTime(),
				msg.SchemaName
			));

			return ValueTask.CompletedTask;
		});

		Project<SchemaVersionsDeleted>((msg, db, ctx) => {
			using var scope = db.GetScopedConnection(out var connection);
			using var tx = connection.BeginTransaction();

			var checkpoint = ctx.Record.LogPosition.CommitPosition ?? 0L;
			var versionIds = msg.Versions.ToArray();

			connection.ExecuteNonQuery<DeleteSelectedSchemaVersionsArgs, DeleteSelectedSchemaVersionsStmt>(new(
				msg.SchemaName,
				versionIds
			));

			connection.ExecuteNonQuery<UpdateSchemaLatestVersionAfterDeleteArgs, UpdateSchemaLatestVersionAfterDeleteStmt>(new(
				msg.LatestSchemaVersionNumber,
				msg.LatestSchemaVersionId,
				checkpoint,
				msg.DeletedAt.ToDateTime(),
				msg.SchemaName
			));

			tx.CommitOnDispose();
			return ValueTask.CompletedTask;
		});

		Project<SchemaDeleted>((msg, db, _) => {
			using var scope = db.GetScopedConnection(out var connection);
			using var tx = connection.BeginTransaction();

			connection.ExecuteNonQuery<SchemaNameArgs, DeleteSchemaVersionsStmt>(new(msg.SchemaName));
			connection.ExecuteNonQuery<SchemaNameArgs, DeleteSchemaStmt>(new(msg.SchemaName));

			tx.CommitOnDispose();
			return ValueTask.CompletedTask;
		});
	}
}

file readonly record struct InsertSchemaVersionArgs(
	string VersionId,
	string SchemaName,
	int VersionNumber,
	ReadOnlyMemory<byte> SchemaDefinition,
	sbyte DataFormat,
	DateTime RegisteredAt,
	ulong Checkpoint);

[StructLayout(LayoutKind.Auto)]
file readonly struct InsertSchemaVersionStmt : IPreparedStatement<InsertSchemaVersionArgs> {
	public static ReadOnlySpan<byte> CommandText =>
		"""
		INSERT INTO schema_versions
		VALUES ($1, $2, $3, $4, $5, $6, $7);
		"""u8;

	public static StatementBindingResult Bind(in InsertSchemaVersionArgs args, PreparedStatement source) => new(source) {
		args.VersionId,
		args.SchemaName,
		args.VersionNumber,
		{ args.SchemaDefinition.Span, BlobType.Raw },
		args.DataFormat,
		args.RegisteredAt,
		args.Checkpoint,
	};
}

file readonly record struct InsertSchemaArgs(
	string SchemaName,
	string Description,
	sbyte DataFormat,
	int VersionNumber,
	string VersionId,
	sbyte Compatibility,
	string Tags,
	DateTime CreatedAt,
	ulong Checkpoint);

[StructLayout(LayoutKind.Auto)]
file readonly struct InsertSchemaStmt : IPreparedStatement<InsertSchemaArgs> {
	public static ReadOnlySpan<byte> CommandText =>
		"""
		INSERT INTO schemas
		VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $8, $9);
		"""u8;

	public static StatementBindingResult Bind(in InsertSchemaArgs args, PreparedStatement source) => new(source) {
		args.SchemaName,
		args.Description,
		args.DataFormat,
		args.VersionNumber,
		args.VersionId,
		args.Compatibility,
		args.Tags,
		args.CreatedAt,
		args.Checkpoint,
	};
}

file readonly record struct UpdateSchemaLatestVersionArgs(int VersionNumber, string VersionId, ulong Checkpoint, string SchemaName);

[StructLayout(LayoutKind.Auto)]
file readonly struct UpdateSchemaLatestVersionStmt : IPreparedStatement<UpdateSchemaLatestVersionArgs> {
	public static ReadOnlySpan<byte> CommandText =>
		"""
		UPDATE schemas
		SET latest_version_number = $1
		  , latest_version_id = $2
		  , checkpoint = $3
		WHERE schema_name = $4;
		"""u8;

	public static StatementBindingResult Bind(in UpdateSchemaLatestVersionArgs args, PreparedStatement source) => new(source) {
		args.VersionNumber,
		args.VersionId,
		args.Checkpoint,
		args.SchemaName,
	};
}

file readonly record struct UpdateSchemaCompatibilityArgs(sbyte Compatibility, DateTime UpdatedAt, string SchemaName);

[StructLayout(LayoutKind.Auto)]
file readonly struct UpdateSchemaCompatibilityStmt : IPreparedStatement<UpdateSchemaCompatibilityArgs> {
	public static ReadOnlySpan<byte> CommandText =>
		"""
		UPDATE schemas
		SET compatibility = $1
		  , updated_at = $2
		WHERE schema_name = $3;
		"""u8;

	public static StatementBindingResult Bind(in UpdateSchemaCompatibilityArgs args, PreparedStatement source) => new(source) {
		args.Compatibility,
		args.UpdatedAt,
		args.SchemaName,
	};
}

file readonly record struct UpdateSchemaDescriptionArgs(string Description, DateTime UpdatedAt, string SchemaName);

[StructLayout(LayoutKind.Auto)]
file readonly struct UpdateSchemaDescriptionStmt : IPreparedStatement<UpdateSchemaDescriptionArgs> {
	public static ReadOnlySpan<byte> CommandText =>
		"""
		UPDATE schemas
		SET description = $1
		  , updated_at = $2
		WHERE schema_name = $3;
		"""u8;

	public static StatementBindingResult Bind(in UpdateSchemaDescriptionArgs args, PreparedStatement source) => new(source) {
		args.Description,
		args.UpdatedAt,
		args.SchemaName,
	};
}

file readonly record struct UpdateSchemaTagsArgs(string Tags, DateTime UpdatedAt, string SchemaName);

[StructLayout(LayoutKind.Auto)]
file readonly struct UpdateSchemaTagsStmt : IPreparedStatement<UpdateSchemaTagsArgs> {
	public static ReadOnlySpan<byte> CommandText =>
		"""
		UPDATE schemas
		SET tags = $1
		  , updated_at = $2
		WHERE schema_name = $3;
		"""u8;

	public static StatementBindingResult Bind(in UpdateSchemaTagsArgs args, PreparedStatement source) => new(source) {
		args.Tags,
		args.UpdatedAt,
		args.SchemaName,
	};
}

file readonly record struct DeleteSelectedSchemaVersionsArgs(string SchemaName, string[] VersionIds);

[StructLayout(LayoutKind.Auto)]
file readonly struct DeleteSelectedSchemaVersionsStmt : IPreparedStatement<DeleteSelectedSchemaVersionsArgs> {
	public static ReadOnlySpan<byte> CommandText =>
		"""
		DELETE FROM schema_versions
		WHERE schema_name = $1 AND version_id = ANY($2);
		"""u8;

	public static StatementBindingResult Bind(in DeleteSelectedSchemaVersionsArgs args, PreparedStatement source) => new(source) {
		args.SchemaName,
		{ args.VersionIds, CollectionType.Array },
	};
}

file readonly record struct UpdateSchemaLatestVersionAfterDeleteArgs(
	int LatestVersionNumber,
	string LatestVersionId,
	ulong Checkpoint,
	DateTime UpdatedAt,
	string SchemaName);

[StructLayout(LayoutKind.Auto)]
file readonly struct UpdateSchemaLatestVersionAfterDeleteStmt : IPreparedStatement<UpdateSchemaLatestVersionAfterDeleteArgs> {
	public static ReadOnlySpan<byte> CommandText =>
		"""
		UPDATE schemas
		SET latest_version_number = $1
		  , latest_version_id = $2
		  , checkpoint = $3
		  , updated_at = $4
		WHERE schema_name = $5;
		"""u8;

	public static StatementBindingResult Bind(in UpdateSchemaLatestVersionAfterDeleteArgs args, PreparedStatement source) => new(source) {
		args.LatestVersionNumber,
		args.LatestVersionId,
		args.Checkpoint,
		args.UpdatedAt,
		args.SchemaName,
	};
}

file readonly record struct SchemaNameArgs(string SchemaName);

[StructLayout(LayoutKind.Auto)]
file readonly struct DeleteSchemaVersionsStmt : IPreparedStatement<SchemaNameArgs> {
	public static ReadOnlySpan<byte> CommandText =>
		"""
		DELETE FROM schema_versions
		WHERE schema_name = $1;
		"""u8;

	public static StatementBindingResult Bind(in SchemaNameArgs args, PreparedStatement source) => new(source) {
		args.SchemaName,
	};
}

[StructLayout(LayoutKind.Auto)]
file readonly struct DeleteSchemaStmt : IPreparedStatement<SchemaNameArgs> {
	public static ReadOnlySpan<byte> CommandText =>
		"""
		DELETE FROM schemas
		WHERE schema_name = $1;
		"""u8;

	public static StatementBindingResult Bind(in SchemaNameArgs args, PreparedStatement source) => new(source) {
		args.SchemaName,
	};
}
