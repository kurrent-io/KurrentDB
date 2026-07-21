namespace Kurrent.SemanticKernel.Connectors.DuckLance;

/// <summary>
/// Composes the DuckDB <c>lance_vector_search</c> and <c>lance_hybrid_search</c> statements that back
/// <see cref="DuckDBCollection{TKey, TRecord}"/>'s search paths.
/// </summary>
static class DuckDBSearchSqlComposer {
    /// <summary>Builds the plain (unfiltered) <c>lance_vector_search</c> statement.</summary>
    public static string BuildVectorSearchSql(
        string uri,
        string vectorColumn,
        int dimensions,
        int k,
        bool refine,
        IReadOnlyList<string> selectColumns,
        int top,
        int skip
    ) {
        // The query vector is the single positional (?) parameter, wrapped in CAST(? AS FLOAT[N]) so Lance
        // receives a fixed-size float array of the vector's dimensionality. Every other token — the attached
        // qualified table name (e.g. ldb.main.vs_docs), the vector column, the select columns — is a
        // pre-validated identifier and is interpolated directly; only the vector is ever parameter-bound.
        //
        // Results come back nearest-first (ascending _distance). Paging is LIMIT {top} plus an OFFSET {skip}
        // emitted only when skip is non-zero; the caller sets k to top + skip so Lance returns enough
        // neighbors to cover the requested page. No trailing semicolon.
        var columns      = string.Join(", ", selectColumns);

        // refine_factor := 4 is appended when the caller asks for it: PQ-family indexes need the refine pass
        // to avoid scrambling small-k results; Flat and IVF_FLAT do not.
        var refineClause = refine ? ", refine_factor := 4" : "";
        var offsetClause = skip > 0 ? $" OFFSET {skip}" : "";

        return
            $"""
             SELECT {columns}, _distance
             FROM lance_vector_search('{uri}', '{vectorColumn}', CAST(? AS FLOAT[{dimensions}]), k := {k}, prefilter := true{refineClause})
             ORDER BY _distance LIMIT {top}{offsetClause}
             """;
    }

    /// <summary>
    /// Builds the <c>lance_vector_search</c> statement with a <c>WHERE</c> filter spliced between the table
    /// function and the <c>ORDER BY</c>. Identical to <see cref="BuildVectorSearchSql"/> apart from that clause.
    /// </summary>
    public static string BuildFilteredVectorSearchSql(
        string uri,
        string vectorColumn,
        int dimensions,
        int k,
        bool refine,
        IReadOnlyList<string> selectColumns,
        string whereClause,
        int top,
        int skip
    ) {
        // Parameter order: the query vector is still the FIRST positional (?) parameter (it sits in the FROM
        // clause); the filter's own placeholders follow, in the order they appear in whereClause — so the
        // caller binds the query vector first, then the filter values. whereClause carries no leading WHERE.
        //
        // The caller chooses k. A filter that pushes down as a TRUE prefilter (equality only) needs just
        // top + skip. A filter DuckDB post-filters (any containment / array_has_any) needs k to cover the
        // WHOLE table, or matching rows are silently dropped.
        var columns      = string.Join(", ", selectColumns);
        var refineClause = refine ? ", refine_factor := 4" : "";
        var offsetClause = skip > 0 ? $" OFFSET {skip}" : "";

        return
            $"""
             SELECT {columns}, _distance
             FROM lance_vector_search('{uri}', '{vectorColumn}', CAST(? AS FLOAT[{dimensions}]), k := {k}, prefilter := true{refineClause})
             WHERE {whereClause}
             ORDER BY _distance LIMIT {top}{offsetClause}
             """;
    }

    /// <summary>
    /// Builds the <c>lance_hybrid_search</c> statement: dense vector search blended with full-text (keyword)
    /// search, ranked by the descending <c>_hybrid_score</c> (larger = more relevant).
    /// </summary>
    public static string BuildHybridSearchSql(
        string uri,
        string vectorColumn,
        int dimensions,
        string textColumn,
        int k,
        bool refine,
        IReadOnlyList<string> selectColumns,
        string? whereClause,
        int top,
        int skip
    ) {
        // Two positional (?) parameters sit in the FROM clause, in this order: the query vector (wrapped in
        // CAST(? AS FLOAT[N])) and the full-text query string. Any filter's placeholders follow, in the order
        // they appear in whereClause — so the caller binds the vector first, then the query text, then the
        // filter values. whereClause is null for an unfiltered hybrid search, and the caller chooses k
        // (normal vs oversample) under exactly the same prefilter/post-filter rules as the vector search.
        //
        // lance_hybrid_search has no filter parameter of its own, so a filter is expressed as a plain SQL
        // WHERE spliced between the table function and the ORDER BY (validated to work alongside
        // prefilter := true). The select list additionally projects _hybrid_score, _distance, and _score —
        // the last two are cheap diagnostics; the result Score is taken from _hybrid_score alone.
        //
        // alpha := 0.5 is fixed: the MEVD hybrid-search abstraction exposes no dense/sparse weighting knob,
        // so the two modalities are always blended equally. prefilter := true is always emitted, matching
        // the vector-search composer.
        var columns         = string.Join(", ", selectColumns);
        var refineClause    = refine ? ", refine_factor := 4" : "";
        var whereClauseText = whereClause is null ? "" : $"WHERE {whereClause} ";
        var offsetClause    = skip > 0 ? $" OFFSET {skip}" : "";

        return
            $"""
             SELECT {columns}, _hybrid_score, _distance, _score
             FROM lance_hybrid_search('{uri}', '{vectorColumn}', CAST(? AS FLOAT[{dimensions}]), '{textColumn}', ?, k := {k}, prefilter := true, alpha := 0.5{refineClause})
             {whereClauseText}ORDER BY _hybrid_score DESC LIMIT {top}{offsetClause}
             """;
    }
}