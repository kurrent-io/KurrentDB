using Microsoft.Extensions.VectorData;

namespace Kurrent.SemanticKernel.Connectors.DuckLance;

/// <summary>
/// Maps the Microsoft.Extensions.VectorData <see cref="IndexKind"/> and <see cref="DistanceFunction"/> string
/// constants onto the Lance <c>USING</c> index type and <c>metric_type</c> values recognized by the DuckDB
/// <c>lance</c> extension's <c>CREATE INDEX</c> DDL.
/// </summary>
static class DuckDBIndexKindMapper {
    /// <summary>
    /// Maps an <see cref="IndexKind"/> constant (<see langword="null"/> = the provider default, Dynamic) to the
    /// Lance <c>USING</c> index type, or <see langword="null"/> for Flat — no index, brute-force flat search.
    /// </summary>
    public static string? GetLanceIndexType(string? indexKind) =>
        indexKind switch {
            null or IndexKind.Dynamic or IndexKind.QuantizedFlat => "IVF_PQ",
            IndexKind.IvfFlat                                    => "IVF_FLAT",
            IndexKind.Hnsw                                       => "IVF_HNSW_PQ",
            IndexKind.Flat                                       => null,

            _ => throw new NotSupportedException($"The index kind '{indexKind}' is not supported by the DuckLance provider.")
        };

    /// <summary>
    /// Maps a <see cref="DistanceFunction"/> constant (<see langword="null"/> = the provider default, cosine)
    /// to the Lance <c>metric_type</c> value.
    /// </summary>
    public static string GetLanceMetric(string? distanceFunction) =>
        distanceFunction switch {
            null or DistanceFunction.CosineDistance or DistanceFunction.CosineSimilarity => "cosine",
            DistanceFunction.DotProductSimilarity                                        => "dot",
            DistanceFunction.EuclideanSquaredDistance                                    => "l2",

            _ => throw new NotSupportedException($"The distance function '{distanceFunction}' is not supported by the DuckLance provider.")
        };

    /// <summary>
    /// Whether the index kind maps to a product-quantized (PQ) Lance family (<c>IVF_PQ</c> or
    /// <c>IVF_HNSW_PQ</c>), which requires the <c>num_sub_vectors</c> and <c>num_bits</c> <c>WITH</c> parameters.
    /// </summary>
    public static bool IsPqFamily(string? indexKind) =>
        indexKind switch {
            null or IndexKind.Dynamic or IndexKind.QuantizedFlat or IndexKind.Hnsw => true,
            IndexKind.Flat or IndexKind.IvfFlat                                    => false,

            _ => throw new NotSupportedException($"The index kind '{indexKind}' is not supported by the DuckLance provider.")
        };

    /// <summary>
    /// Computes the <c>num_sub_vectors</c> parameter for a PQ-family index: the largest divisor of the
    /// dimension count that is at most 16.
    /// </summary>
    public static int GetNumSubVectors(int dimensions) {
        // PQ training requires dimensions % num_sub_vectors == 0 — Lance errors otherwise. Capping the
        // search at 16 keeps the sub-vector width reasonable for typical embedding sizes; a prime or
        // otherwise poorly factorable dimension count (e.g. 17) falls back to num_sub_vectors = 1, which is
        // always valid.
        var upperBound = Math.Min(dimensions, 16);

        for (var candidate = upperBound; candidate >= 1; candidate--) {
            if (dimensions % candidate == 0)
                return candidate;
        }

        // Unreachable: 1 always divides any dimensions >= 1, so the loop above always returns before this point.
        return 1;
    }
}