// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable VirtualMemberCallInConstructor

using System.Text.Json;
using Dapper;
using Kurrent.Surge.DuckDB.Projectors;
using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;

namespace KurrentDB.SchemaRegistry.Data;

public class SchemaProjections : DuckDBProjection {
	public SchemaProjections() {
		Project<SchemaCreated>((msg, db, ctx) => {
			using var scope = db.GetScopedConnection(out var connection);
			using var tx = connection.BeginTransaction();

			const string insertSchemaVersionSql =
				"""
				INSERT INTO schema_versions
				VALUES (
					  $version_id
					, $schema_name
					, $version_number
					, $schema_definition
					, $data_format
					, $created_at
					, $checkpoint
				);
				""";

			const string insertSchemaSql =
				"""
				INSERT INTO schemas
				VALUES (
					$schema_name
				  , $description
				  , $data_format
				  , $version_number
				  , $version_id
				  , $compatibility
				  , $tags
				  , $created_at
				  , $created_at
				  , $checkpoint
				);
				""";

			connection.Execute(insertSchemaVersionSql,
				new {
					version_id = msg.SchemaVersionId,
					schema_name = msg.SchemaName,
					version_number = msg.VersionNumber,
					schema_definition = msg.SchemaDefinition.ToByteArray(),
					data_format = msg.DataFormat,
					created_at = msg.CreatedAt.ToDateTime(),
					checkpoint = ctx.Record.LogPosition.CommitPosition
				}
			);
			connection.Execute(insertSchemaSql,
				new {
					schema_name = msg.SchemaName,
					description = msg.Description,
					data_format = msg.DataFormat,
					version_number = msg.VersionNumber,
					version_id = msg.SchemaVersionId,
					compatibility = msg.Compatibility,
					tags = JsonSerializer.Serialize(msg.Tags),
					created_at = msg.CreatedAt.ToDateTime(),
					checkpoint = ctx.Record.LogPosition.CommitPosition
				}
			);
			tx.CommitOnDispose();
			return ValueTask.CompletedTask;
		});

		Project<SchemaVersionRegistered>((msg, db, ctx) => {
			using var scope = db.GetScopedConnection(out var connection);
			using var tx = connection.BeginTransaction();

			const string insertSchemaVersionSql =
				"""
				INSERT INTO schema_versions VALUES (
				      $version_id
				    , $schema_name
				    , $version_number
				    , $schema_definition
				    , $data_format
				    , $registered_at
				    , $checkpoint
				);
				""";

			const string updateSchemaLatestVersionSql =
				"""
				UPDATE schemas
				SET latest_version_number = $version_number
				  , latest_version_id = $version_id
				  , checkpoint = $checkpoint
				WHERE schema_name = $schema_name;
				""";

			connection.Execute(
				insertSchemaVersionSql,
				new {
					version_id = msg.SchemaVersionId,
					version_number = msg.VersionNumber,
					schema_name = msg.SchemaName,
					schema_definition = msg.SchemaDefinition.ToByteArray(),
					data_format = msg.DataFormat,
					registered_at = msg.RegisteredAt.ToDateTime(),
					checkpoint = ctx.Record.LogPosition.CommitPosition
				}
			);

			connection.Execute(
				updateSchemaLatestVersionSql,
				new {
					version_number = msg.VersionNumber,
					version_id = msg.SchemaVersionId,
					schema_name = msg.SchemaName,
					checkpoint = ctx.Record.LogPosition.CommitPosition
				}
			);

			tx.CommitOnDispose();

			return ValueTask.CompletedTask;
		});

		Project<SchemaCompatibilityModeChanged>((msg, db, _) => {
			using var scope = db.GetScopedConnection(out var connection);
			const string updateSchemaCompatibilitySql =
				"""
				UPDATE schemas
				SET compatibility = $compatibility
				  , updated_at = $updated_at
				WHERE schema_name = $schema_name
				""";

			connection.Execute(
				updateSchemaCompatibilitySql,
				new {
					schema_name = msg.SchemaName,
					compatibility = msg.Compatibility,
					updated_at = msg.ChangedAt.ToDateTime()
				}
			);
			return ValueTask.CompletedTask;
		});

		Project<SchemaDescriptionUpdated>((msg, db, _) => {
			using var scope = db.GetScopedConnection(out var connection);
			const string updateSchemaDescriptionSql =
				"""
				UPDATE schemas
				SET description = $description
				  , updated_at = $updated_at
				WHERE schema_name = $schema_name
				""";

			connection.Execute(
				updateSchemaDescriptionSql,
				new {
					schema_name = msg.SchemaName,
					description = msg.Description,
					updated_at = msg.UpdatedAt.ToDateTime()
				}
			);
			return ValueTask.CompletedTask;
		});

		Project<SchemaTagsUpdated>((msg, db, _) => {
			using var scope = db.GetScopedConnection(out var connection);
			const string updateSchemaTagsSql =
				"""
				UPDATE schemas
				SET tags = $tags
				  , updated_at = $updated_at
				WHERE schema_name = $schema_name
				""";

			connection.Execute(
				updateSchemaTagsSql,
				new {
					schema_name = msg.SchemaName,
					tags = JsonSerializer.Serialize(msg.Tags),
					updated_at = msg.UpdatedAt.ToDateTime()
				}
			);
			return ValueTask.CompletedTask;
		});

		Project<SchemaVersionsDeleted>((msg, db, ctx) => {
			using var scope = db.GetScopedConnection(out var connection);
			using var tx = connection.BeginTransaction();

			// TODO: Must figure out a better way to do this. Right now, we have to do string interpolation,
			// but ideally, we would want to simply pass the list of versions
			const string deleteSelectedSchemaVersionsSql =
				"""
				DELETE FROM schema_versions
				WHERE schema_name = $schema_name AND version_id IN $versions;
				""";

			const string updateSchemaLatestVersionSql =
				"""
				UPDATE schemas
				SET latest_version_number = $latest_version_number
				  , latest_version_id = $latest_version_id
				  , checkpoint = $checkpoint
				  , updated_at = $deleted_at
				WHERE schema_name = $schema_name;
				""";

			connection.Execute(deleteSelectedSchemaVersionsSql, new {
				schema_name = msg.SchemaName,
				versions = msg.Versions.ToList()
			});

			connection.Execute(updateSchemaLatestVersionSql, new {
				schema_name = msg.SchemaName,
				latest_version_id = msg.LatestSchemaVersionId,
				latest_version_number = msg.LatestSchemaVersionNumber,
				deleted_at = msg.DeletedAt.ToDateTime(),
				checkpoint = ctx.Record.LogPosition.CommitPosition
			});

			tx.CommitOnDispose();

			return ValueTask.CompletedTask;
		});

		Project<SchemaDeleted>((msg, db, _) => {
			using var scope = db.GetScopedConnection(out var connection);
			using var tx = connection.BeginTransaction();

			const string deleteSchemaVersionsSql =
				"""
				DELETE FROM schema_versions
				WHERE schema_name = $schema_name;
				""";

			const string deleteSchemasSql =
				"""
				DELETE FROM schemas
				WHERE schema_name = $schema_name;
				""";

			connection.Execute(deleteSchemaVersionsSql, new { schema_name = msg.SchemaName });
			connection.Execute(deleteSchemasSql, new { schema_name = msg.SchemaName });
			tx.CommitOnDispose();

			return ValueTask.CompletedTask;
		});
	}
}
