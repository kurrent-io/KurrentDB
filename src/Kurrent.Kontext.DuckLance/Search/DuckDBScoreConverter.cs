using Microsoft.Extensions.VectorData;

namespace Kurrent.SemanticKernel.Connectors.DuckLance;

/// <summary>
/// Translates the raw <c>_distance</c> returned by Lance's <c>lance_vector_search</c> into the score contract
/// of a vector property's configured distance function, and validates that function is one the provider serves.
/// </summary>
static class DuckDBScoreConverter {
    /// <summary>Converts a raw Lance <c>_distance</c> into the score for the given distance function.</summary>
    public static double ConvertScore(string? distanceFunction, double rawDistance) =>
        // Lance reports lower-is-closer distances; the mapping to the requested score depends only on the
        // DECLARED distance function, never on which index (if any) served the search.
        distanceFunction switch {
            // null defaults to cosine distance, which — like squared Euclidean distance — is returned unchanged.
            null                                      => rawDistance,
            DistanceFunction.CosineDistance           => rawDistance,
            DistanceFunction.EuclideanSquaredDistance => rawDistance,

            // Lance reports 1 - similarity for these metrics, so 1 - _distance recovers the similarity.
            DistanceFunction.CosineSimilarity     => 1 - rawDistance,
            DistanceFunction.DotProductSimilarity => 1 - rawDistance,

            // Defensive only: ValidateDistanceFunction has already rejected unsupported values before any
            // query ran.
            _ => throw BuildUnsupportedException(distanceFunction)
        };

    /// <summary>
    /// Validates that the distance function is one the provider can serve, throwing before any SQL runs when
    /// it is not (a <see langword="null"/> function is the connector default: cosine distance).
    /// </summary>
    public static void ValidateDistanceFunction(string? distanceFunction) {
        switch (distanceFunction) {
            case null:
            case DistanceFunction.CosineDistance:
            case DistanceFunction.CosineSimilarity:
            case DistanceFunction.DotProductSimilarity:
            case DistanceFunction.EuclideanSquaredDistance:
                return;

            default: throw BuildUnsupportedException(distanceFunction);
        }
    }

    /// <summary>Determines whether a converted score (from <see cref="ConvertScore"/>) passes a caller-supplied threshold.</summary>
    public static bool PassesThreshold(string? distanceFunction, double score, double threshold) {
        // Runs AFTER score conversion and AFTER the top-k rows are fetched and ordered: a row within the
        // requested Top that fails the threshold is dropped, so fewer than Top results may come back (the
        // Microsoft.Extensions.VectorData score-threshold convention). Similarity metrics are higher-is-better,
        // so they pass at >= threshold; distance metrics — including the null default, cosine distance — are
        // lower-is-better, so they pass at <= threshold.
        return IsSimilarityFunction(distanceFunction) ? score >= threshold : score <= threshold;

        // Similarity metrics score higher-is-closer; the null default is cosine DISTANCE, i.e. not a similarity.
        static bool IsSimilarityFunction(string? distanceFunction) => distanceFunction is DistanceFunction.CosineSimilarity or DistanceFunction.DotProductSimilarity;
    }

    /// <summary>Determines whether a hybrid-search <c>_hybrid_score</c> passes a caller-supplied threshold.</summary>
    public static bool PassesHybridThreshold(double score, double threshold) =>
        // Lance blends the dense and sparse relevance into a score where higher is ALWAYS better, so the
        // comparison direction never varies. Applied post-fetch, exactly like PassesThreshold.
        score >= threshold;

    static NotSupportedException BuildUnsupportedException(string? distanceFunction) =>
        new(
            $"The distance function '{distanceFunction}' is not supported by the DuckLance provider. Supported distance functions are: "
          + $"{DistanceFunction.CosineDistance}, {DistanceFunction.CosineSimilarity}, {DistanceFunction.DotProductSimilarity}, "
          + $"{DistanceFunction.EuclideanSquaredDistance} (the default, when unset, is {DistanceFunction.CosineDistance}).");
}