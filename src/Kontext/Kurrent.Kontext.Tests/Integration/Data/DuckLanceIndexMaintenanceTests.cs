// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Globalization;
using System.Text;
using Kurrent.Kontext.Infrastructure.Data.LanceDB;
using Kurrent.Kontext.Testing;
using Kurrent.Quack;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// Pins that every statement <see cref="DuckLanceIndexMaintenanceExtensions"/> renders is accepted by
/// the vendored lance build — one index type per case, created on a table large enough to train.
/// </summary>
[Category("Integration")]
[Timeout(300_000)]
public class DuckLanceIndexMaintenanceTests {
	const int Dimension = 32;
	const int Rows      = 300;

	[Test]
	[Arguments("IVF_FLAT")]
	[Arguments("IVF_PQ")]
	[Arguments("IVF_RQ")]
	[Arguments("IVF_SQ")]
	[Arguments("IVF_HNSW_FLAT")]
	[Arguments("IVF_HNSW_PQ")]
	[Arguments("IVF_HNSW_SQ")]
	public async ValueTask creates_every_vector_index_type(string indexType, CancellationToken cancellationToken) {
		var options = OptionsFor(indexType);

		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		using var connection  = dataSources.OpenLanceWriter();

		var table = SeedTable(connection, $"vec_{indexType.ToLowerInvariant()}");

		// Act
		connection.CreateVectorIndex(table, "embedding", options);

		// Assert
		var index = connection.GetTableInfo(table)!.FindIndex(LanceIndexNames.Vector("embedding"));

		await Assert.That(index).IsNotNull();
		await Assert.That(index!.IndexType).IsEqualTo(options.IndexType.Token);
		await Assert.That(connection.GetTableInfo(table)!.RowCount).IsEqualTo((long)Rows);
	}

	static ILanceVectorIndexOptions OptionsFor(string indexType) => indexType switch {
		"IVF_FLAT"      => new LanceIvfFlatIndexOptions { NumPartitions = 1 },
		"IVF_PQ"        => new LanceIvfPqIndexOptions { NumPartitions = 1, NumSubVectors = Dimension / 8, NumBits = 8 },
		"IVF_RQ"        => new LanceIvfRqIndexOptions { NumPartitions = 1 },
		"IVF_SQ"        => new LanceIvfSqIndexOptions { NumPartitions = 1 },
		"IVF_HNSW_FLAT" => new LanceIvfHnswFlatIndexOptions { NumPartitions = 1, HnswM = 16 },
		"IVF_HNSW_PQ"   => new LanceIvfHnswPqIndexOptions { NumPartitions = 1, NumSubVectors = Dimension / 8 },
		"IVF_HNSW_SQ"   => new LanceIvfHnswSqIndexOptions { NumPartitions = 1, SampleRate = 256 },
		_               => throw new ArgumentOutOfRangeException(nameof(indexType), indexType, "Unknown lance index type."),
	};

	[Test]
	public async ValueTask creates_inverted_and_scalar_indexes_and_optimizes_them(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		using var connection  = dataSources.OpenLanceWriter();

		var table = SeedTable(connection, "mixed");

		// Act
		connection.CreateInvertedIndex(table, "text", options => {
			options.Analyzer         = "code";
			options.BaseTokenizer    = "code";
			options.SplitIdentifiers = true;
			options.PreserveOriginal = true;
			options.Stem             = false;
			options.MaxTokenLength   = 1_048_576;
		});

		connection.CreateScalarIndex(table, "id", options => options.Type = LanceScalarIndexType.BTree);
		connection.CreateVectorIndex<LanceIvfPqIndexOptions>(table, "embedding", options => {
			options.NumPartitions = 1;
			options.NumSubVectors = Dimension / 8;
		});

		connection.OptimizeIndex(table, LanceIndexNames.Inverted("text"));
		connection.OptimizeIndex(table, LanceIndexNames.Vector("embedding"), options => {
			options.Mode              = LanceOptimizeMode.Merge;
			options.NumIndicesToMerge = 1;
		});
		connection.OptimizeIndex(table, LanceIndexNames.Vector("embedding"), options => options.Mode = LanceOptimizeMode.Retrain);
		connection.CompactTable(table);

		// Assert
		var names = connection.GetTableInfo(table)!.Indexes.Select(index => index.Name).ToArray();

		await Assert.That(names).Contains(LanceIndexNames.Inverted("text"));
		await Assert.That(names).Contains(LanceIndexNames.Scalar("id"));
		await Assert.That(names).Contains(LanceIndexNames.Vector("embedding"));
		await Assert.That(connection.GetTableInfo("ldb.main.mixed")).IsNotNull();
	}

	[Test]
	public async ValueTask drops_an_index(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		using var connection  = dataSources.OpenLanceWriter();

		var table = SeedTable(connection, "dropped");

		connection.CreateVectorIndex(table, "embedding", new LanceIvfPqIndexOptions { NumPartitions = 1, NumSubVectors = Dimension / 8 });

		// Act
		connection.DropIndex(table, LanceIndexNames.Vector("embedding"));

		// Assert
		await Assert.That(connection.GetTableInfo(table)!.FindIndex(LanceIndexNames.Vector("embedding"))).IsNull();
	}

	static string SeedTable(DuckDBAdvancedConnection connection, string name) {
		var table = $"ldb.main.{name}";

		connection.ExecuteAdHocNonQuery($"CREATE TABLE {table} (id BIGINT, text VARCHAR, embedding FLOAT[{Dimension}])");

		for (var row = 0; row < Rows; row++)
			connection.ExecuteAdHocNonQuery($"INSERT INTO {table} VALUES ({row}, 'toolName run step {row}', {Vector(row)})");

		return table;
	}

	// Deterministic and well spread: a unit-ish vector whose peak walks the dimensions, so the
	// quantizers have something to train on instead of 300 identical rows.
	static string Vector(int row) {
		var builder = new StringBuilder("[");

		for (var i = 0; i < Dimension; i++) {
			if (i > 0)
				builder.Append(',');

			var value = (float)Math.Sin((row + 1) * 0.37 + i * 0.11);
			builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
		}

		return builder.Append(']').ToString();
	}
}
