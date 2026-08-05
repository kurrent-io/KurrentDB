using System.Diagnostics.CodeAnalysis;
using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace DuckLance.Tests.Storage;

/// <summary>
/// Pure unit tests for <see cref="DuckDBSqlComposer"/>: the CRUD SQL statements composed from a
/// <see cref="CollectionModel"/>. No DuckDB connection is used; every test asserts the exact SQL text.
/// </summary>
[Category("Storage")]
public class DuckDBSqlComposerTests {
    // The canonical 5-property model used by the golden oracle: key id, data category (string),
    // data tags (List<string>), data content (string), vector vec (dim 4).
    static CollectionModel CanonicalModel() =>
        BuildModel(
            new VectorStoreKeyProperty("id", typeof(string)),
            new VectorStoreDataProperty("category", typeof(string)),
            new VectorStoreDataProperty("tags", typeof(List<string>)),
            new VectorStoreDataProperty("content", typeof(string)),
            new VectorStoreVectorProperty("vec", typeof(ReadOnlyMemory<float>), 4));

    [Test]
    public async ValueTask build_merge_upsert_sql_produces_the_golden_oracle_statement() {
        var sql = DuckDBSqlComposer.BuildMergeUpsertSql("ns.main.vs_docs", CanonicalModel());

        await Assert
            .That(sql)
            .IsEqualTo(
                """
                MERGE INTO ns.main.vs_docs AS t
                USING (SELECT ? AS id, ? AS category, CAST(? AS VARCHAR[]) AS tags, ? AS content, CAST(? AS FLOAT[4]) AS vec) AS s
                ON t.id = s.id
                WHEN MATCHED THEN UPDATE SET category = s.category, tags = s.tags, content = s.content, vec = s.vec
                WHEN NOT MATCHED THEN INSERT (id, category, tags, content, vec) VALUES (s.id, s.category, s.tags, s.content, s.vec)
                """);
    }

    [Test]
    public async ValueTask build_merge_upsert_many_sql_two_rows_produces_the_golden_oracle_statement() {
        var sql = DuckDBSqlComposer.BuildMergeUpsertManySql("ns.main.vs_docs", CanonicalModel(), 2);

        // One VALUES row per record, each projecting one placeholder per property (vectors/list-typed data wrapped
        // in a per-row CAST); columns named once via the `AS s (...)` alias.
        await Assert
            .That(sql)
            .IsEqualTo(
                """
                MERGE INTO ns.main.vs_docs AS t
                USING (VALUES (?, ?, CAST(? AS VARCHAR[]), ?, CAST(? AS FLOAT[4])), (?, ?, CAST(? AS VARCHAR[]), ?, CAST(? AS FLOAT[4]))) AS s (id, category, tags, content, vec)
                ON t.id = s.id
                WHEN MATCHED THEN UPDATE SET category = s.category, tags = s.tags, content = s.content, vec = s.vec
                WHEN NOT MATCHED THEN INSERT (id, category, tags, content, vec) VALUES (s.id, s.category, s.tags, s.content, s.vec)
                """);
    }

    [Test]
    public async ValueTask build_merge_upsert_many_sql_single_row_emits_one_values_row() {
        var sql = DuckDBSqlComposer.BuildMergeUpsertManySql("ns.main.vs_docs", CanonicalModel(), 1);

        await Assert
            .That(sql)
            .IsEqualTo(
                """
                MERGE INTO ns.main.vs_docs AS t
                USING (VALUES (?, ?, CAST(? AS VARCHAR[]), ?, CAST(? AS FLOAT[4]))) AS s (id, category, tags, content, vec)
                ON t.id = s.id
                WHEN MATCHED THEN UPDATE SET category = s.category, tags = s.tags, content = s.content, vec = s.vec
                WHEN NOT MATCHED THEN INSERT (id, category, tags, content, vec) VALUES (s.id, s.category, s.tags, s.content, s.vec)
                """);
    }

    [Test]
    public async ValueTask build_merge_upsert_many_sql_uses_storage_name_overrides() {
        var model = BuildModel(
            new VectorStoreKeyProperty("Id", typeof(string)) { StorageName                       = "id_col" },
            new VectorStoreDataProperty("Category", typeof(string)) { StorageName                = "cat_col" },
            new VectorStoreVectorProperty("Vec", typeof(ReadOnlyMemory<float>), 2) { StorageName = "vec_col" });

        var sql = DuckDBSqlComposer.BuildMergeUpsertManySql("ns.main.vs_docs", model, 2);

        await Assert
            .That(sql)
            .IsEqualTo(
                """
                MERGE INTO ns.main.vs_docs AS t
                USING (VALUES (?, ?, CAST(? AS FLOAT[2])), (?, ?, CAST(? AS FLOAT[2]))) AS s (id_col, cat_col, vec_col)
                ON t.id_col = s.id_col
                WHEN MATCHED THEN UPDATE SET cat_col = s.cat_col, vec_col = s.vec_col
                WHEN NOT MATCHED THEN INSERT (id_col, cat_col, vec_col) VALUES (s.id_col, s.cat_col, s.vec_col)
                """);
    }

    [Test]
    public async ValueTask build_merge_upsert_many_sql_row_count_below_one_throws() =>
        await Assert
            .That(() => DuckDBSqlComposer.BuildMergeUpsertManySql("ns.main.vs_docs", CanonicalModel(), 0))
            .Throws<ArgumentOutOfRangeException>();

    [Test]
    public async ValueTask build_select_by_key_sql_include_vectors_emits_vector_column() {
        var sql = DuckDBSqlComposer.BuildSelectByKeySql("ns.main.vs_docs", CanonicalModel(), true);

        await Assert.That(sql).IsEqualTo("SELECT id, category, tags, content, vec FROM ns.main.vs_docs WHERE id = ?");
    }

    [Test]
    public async ValueTask build_select_by_key_sql_without_vectors_omits_vector_column() {
        var sql = DuckDBSqlComposer.BuildSelectByKeySql("ns.main.vs_docs", CanonicalModel(), false);

        await Assert.That(sql).IsEqualTo("SELECT id, category, tags, content FROM ns.main.vs_docs WHERE id = ?");
    }

    [Test]
    public async ValueTask build_select_by_keys_sql_include_vectors_emits_in_list() {
        var sql = DuckDBSqlComposer.BuildSelectByKeysSql(
            "ns.main.vs_docs", CanonicalModel(), true,
            3);

        await Assert.That(sql).IsEqualTo("SELECT id, category, tags, content, vec FROM ns.main.vs_docs WHERE id IN (?, ?, ?)");
    }

    [Test]
    public async ValueTask build_select_by_keys_sql_without_vectors_omits_vector_column() {
        var sql = DuckDBSqlComposer.BuildSelectByKeysSql(
            "ns.main.vs_docs", CanonicalModel(), false,
            3);

        await Assert.That(sql).IsEqualTo("SELECT id, category, tags, content FROM ns.main.vs_docs WHERE id IN (?, ?, ?)");
    }

    [Test]
    public async ValueTask build_delete_by_key_sql_filters_on_key() {
        var sql = DuckDBSqlComposer.BuildDeleteByKeySql("ns.main.vs_docs", CanonicalModel());

        await Assert.That(sql).IsEqualTo("DELETE FROM ns.main.vs_docs WHERE id = ?");
    }

    [Test]
    public async ValueTask build_delete_by_keys_sql_filters_on_key_in_list() {
        var sql = DuckDBSqlComposer.BuildDeleteByKeysSql("ns.main.vs_docs", CanonicalModel(), 3);

        await Assert.That(sql).IsEqualTo("DELETE FROM ns.main.vs_docs WHERE id IN (?, ?, ?)");
    }

    // A model whose data property carries a StorageName override must be referenced by storage name in SQL.
    [Test]
    public async ValueTask composer_uses_storage_name_overrides() {
        var model = BuildModel(
            new VectorStoreKeyProperty("Id", typeof(string)) { StorageName                       = "id_col" },
            new VectorStoreDataProperty("Category", typeof(string)) { StorageName                = "cat_col" },
            new VectorStoreVectorProperty("Vec", typeof(ReadOnlyMemory<float>), 2) { StorageName = "vec_col" });

        var merge = DuckDBSqlComposer.BuildMergeUpsertSql("ns.main.vs_docs", model);

        await Assert
            .That(merge)
            .IsEqualTo(
                """
                MERGE INTO ns.main.vs_docs AS t
                USING (SELECT ? AS id_col, ? AS cat_col, CAST(? AS FLOAT[2]) AS vec_col) AS s
                ON t.id_col = s.id_col
                WHEN MATCHED THEN UPDATE SET cat_col = s.cat_col, vec_col = s.vec_col
                WHEN NOT MATCHED THEN INSERT (id_col, cat_col, vec_col) VALUES (s.id_col, s.cat_col, s.vec_col)
                """);

        var select = DuckDBSqlComposer.BuildSelectByKeySql("ns.main.vs_docs", model, true);
        await Assert.That(select).IsEqualTo("SELECT id_col, cat_col, vec_col FROM ns.main.vs_docs WHERE id_col = ?");

        var delete = DuckDBSqlComposer.BuildDeleteByKeySql("ns.main.vs_docs", model);
        await Assert.That(delete).IsEqualTo("DELETE FROM ns.main.vs_docs WHERE id_col = ?");
    }

    /// <summary>
    /// Builds a <see cref="CollectionModel"/> from the given properties using a permissive model builder that
    /// accepts any CLR type, so these tests do not depend on the real (production) DuckLance model builder's
    /// type validation.
    /// </summary>
    static CollectionModel BuildModel(params VectorStoreProperty[] properties) =>
        new PermissiveModelBuilder().BuildDynamic(
            new() { Properties = properties },
            null);

    /// <summary>
    /// A <see cref="CollectionModelBuilder"/> that accepts any CLR type for data and vector properties and does
    /// not require a vector property, so these tests can construct arbitrary models directly.
    /// </summary>
    sealed class PermissiveModelBuilder : CollectionModelBuilder {
        public PermissiveModelBuilder()
            : base(new() { RequiresAtLeastOneVector = false, SupportsMultipleVectors = true }) { }

        protected override bool IsDataPropertyTypeValid(Type type, [NotNullWhen(false)] out string? supportedTypes) {
            supportedTypes = null;
            return true;
        }

        protected override bool IsVectorPropertyTypeValid(Type type, [NotNullWhen(false)] out string? supportedTypes) {
            supportedTypes = null;
            return true;
        }
    }
}