using Kurrent.SemanticKernel.Connectors.DuckLance;

namespace DuckLance.Tests.Search;

/// <summary>
/// Pure unit tests for <see cref="DuckDBSearchSqlComposer"/>: the <c>lance_vector_search</c> statement composed
/// from the query parameters. No DuckDB connection is used; every test asserts the exact SQL text.
/// </summary>
[Category("Search")]
public class DuckDBSearchSqlComposerTests {
    // The canonical non-vector projection for the golden model (key id, data category/tags/content).
    static readonly string[] s_canonicalColumns = ["id", "category", "tags", "content"];

    [Test]
    public async ValueTask build_vector_search_sql_with_refine_no_skip_produces_golden_statement() {
        var sql = DuckDBSearchSqlComposer.BuildVectorSearchSql(
            "ducklance.main.vs_docs", "vec", 4,
            10, true, s_canonicalColumns,
            10, 0);

        await Assert
            .That(sql)
            .IsEqualTo(
                """
                SELECT id, category, tags, content, _distance
                FROM lance_vector_search('ducklance.main.vs_docs', 'vec', CAST(? AS FLOAT[4]), k := 10, prefilter := true, refine_factor := 4)
                ORDER BY _distance LIMIT 10
                """);
    }

    [Test]
    public async ValueTask build_vector_search_sql_without_refine_no_skip_omits_refine_factor() {
        var sql = DuckDBSearchSqlComposer.BuildVectorSearchSql(
            "ducklance.main.vs_docs", "vec", 4,
            10, false, s_canonicalColumns,
            10, 0);

        await Assert
            .That(sql)
            .IsEqualTo(
                """
                SELECT id, category, tags, content, _distance
                FROM lance_vector_search('ducklance.main.vs_docs', 'vec', CAST(? AS FLOAT[4]), k := 10, prefilter := true)
                ORDER BY _distance LIMIT 10
                """);
    }

    [Test]
    public async ValueTask build_vector_search_sql_with_skip_emits_offset_clause() {
        var sql = DuckDBSearchSqlComposer.BuildVectorSearchSql(
            "ducklance.main.vs_docs", "vec", 4,
            30, true, s_canonicalColumns,
            10, 20);

        await Assert
            .That(sql)
            .IsEqualTo(
                """
                SELECT id, category, tags, content, _distance
                FROM lance_vector_search('ducklance.main.vs_docs', 'vec', CAST(? AS FLOAT[4]), k := 30, prefilter := true, refine_factor := 4)
                ORDER BY _distance LIMIT 10 OFFSET 20
                """);
    }

    [Test]
    public async ValueTask build_vector_search_sql_include_vectors_projects_vector_column() {
        string[] columns = ["id", "category", "tags", "content", "vec"];

        var sql = DuckDBSearchSqlComposer.BuildVectorSearchSql(
            "ducklance.main.vs_docs", "vec", 4,
            5, false, columns,
            5, 0);

        await Assert
            .That(sql)
            .IsEqualTo(
                """
                SELECT id, category, tags, content, vec, _distance
                FROM lance_vector_search('ducklance.main.vs_docs', 'vec', CAST(? AS FLOAT[4]), k := 5, prefilter := true)
                ORDER BY _distance LIMIT 5
                """);
    }

    [Test]
    public async ValueTask build_vector_search_sql_respects_column_order() {
        IReadOnlyList<string> columns = new List<string> {
            "gamma",
            "alpha",
            "beta"
        };

        var sql = DuckDBSearchSqlComposer.BuildVectorSearchSql(
            "ns.main.t", "embedding", 8,
            3, false, columns,
            3, 0);

        await Assert
            .That(sql)
            .IsEqualTo(
                """
                SELECT gamma, alpha, beta, _distance
                FROM lance_vector_search('ns.main.t', 'embedding', CAST(? AS FLOAT[8]), k := 3, prefilter := true)
                ORDER BY _distance LIMIT 3
                """);
    }

    [Test]
    public async ValueTask build_hybrid_search_sql_no_where_with_refine_no_skip_produces_golden_statement() {
        var sql = DuckDBSearchSqlComposer.BuildHybridSearchSql(
            "ducklance.main.vs_docs", "vec", 4,
            "content", 10, true,
            s_canonicalColumns, null, 10,
            0);

        await Assert
            .That(sql)
            .IsEqualTo(
                """
                SELECT id, category, tags, content, _hybrid_score, _distance, _score
                FROM lance_hybrid_search('ducklance.main.vs_docs', 'vec', CAST(? AS FLOAT[4]), 'content', ?, k := 10, prefilter := true, alpha := 0.5, refine_factor := 4)
                ORDER BY _hybrid_score DESC LIMIT 10
                """);
    }

    [Test]
    public async ValueTask build_hybrid_search_sql_no_where_without_refine_omits_refine_factor() {
        var sql = DuckDBSearchSqlComposer.BuildHybridSearchSql(
            "ducklance.main.vs_docs", "vec", 4,
            "content", 10, false,
            s_canonicalColumns, null, 10,
            0);

        await Assert
            .That(sql)
            .IsEqualTo(
                """
                SELECT id, category, tags, content, _hybrid_score, _distance, _score
                FROM lance_hybrid_search('ducklance.main.vs_docs', 'vec', CAST(? AS FLOAT[4]), 'content', ?, k := 10, prefilter := true, alpha := 0.5)
                ORDER BY _hybrid_score DESC LIMIT 10
                """);
    }

    [Test]
    public async ValueTask build_hybrid_search_sql_with_where_with_refine_with_skip_emits_where_and_offset() {
        var sql = DuckDBSearchSqlComposer.BuildHybridSearchSql(
            "ducklance.main.vs_docs", "vec", 4,
            "content", 30, true,
            s_canonicalColumns, "category = ?", 10,
            20);

        await Assert
            .That(sql)
            .IsEqualTo(
                """
                SELECT id, category, tags, content, _hybrid_score, _distance, _score
                FROM lance_hybrid_search('ducklance.main.vs_docs', 'vec', CAST(? AS FLOAT[4]), 'content', ?, k := 30, prefilter := true, alpha := 0.5, refine_factor := 4)
                WHERE category = ? ORDER BY _hybrid_score DESC LIMIT 10 OFFSET 20
                """);
    }

    [Test]
    public async ValueTask build_hybrid_search_sql_with_where_without_refine_no_skip_emits_where_only() {
        var sql = DuckDBSearchSqlComposer.BuildHybridSearchSql(
            "ducklance.main.vs_docs", "vec", 4,
            "content", 5, false,
            s_canonicalColumns, "array_has_any(tags, ?)", 5,
            0);

        await Assert
            .That(sql)
            .IsEqualTo(
                """
                SELECT id, category, tags, content, _hybrid_score, _distance, _score
                FROM lance_hybrid_search('ducklance.main.vs_docs', 'vec', CAST(? AS FLOAT[4]), 'content', ?, k := 5, prefilter := true, alpha := 0.5)
                WHERE array_has_any(tags, ?) ORDER BY _hybrid_score DESC LIMIT 5
                """);
    }
}