// using System.Runtime.CompilerServices;
// using Kurrent.Kontext.Contracts;
//
// namespace Kurrent.Kontext;
//
// //
// // // Per-retrieved-memory score breakdown — logged on MemoriesRecalled, NOT returned to the agent
// // // (which gets order + trust, not ranking internals). Ranking value:
// // //   final_score = certainty × (alpha_recency·recency_norm + alpha_importance·importance_norm +
// // //   alpha_relevance·relevance_norm)
// // // each *_norm is the min-max normalization of *_raw across the candidate pool (bounds in
// // // ScoringConfig).
// // message ScoredMemory {
// //   // the memory this row scored
// //   string memory_id = 1;
// //
// //   // Recency (temporal decay). last_accessed_at is the clock the recency term decays from; retrieval
// //   // refreshes it to `recalled_at` every time a memory is retrieved (reconsolidation — the only
// //   // write in the recency mechanism). recency_raw = e^(-(recalled_at - last_accessed_at) / tau),
// //   // oriented so fresher = higher; tau lives in ScoringConfig. Logging last_accessed_at +
// //   // recalled_at + tau makes the recency term fully reproducible from the log.
// //   google.protobuf.Timestamp last_accessed_at = 2;
// //
// //   // decayed recency (formula above); fresher = higher
// //   double recency_raw = 3;
// //  
// //   // salience [0,1] mapped from the memory's MemoryImportance
// //   double importance_raw = 4;
// //  
// //   // query-embedding cosine similarity
// //   double relevance_raw = 5;
// //
// //   // min-max normalized across the candidate pool — the values that actually fed the weighted sum
// //   double recency_norm    = 6;  // normalized recency
// //   double importance_norm = 7;  // normalized importance
// //   double relevance_norm  = 8;  // normalized relevance
// //
// //   // Certainty multiplier applied to base_score — the trust gate. For a RAW memory (no evidence) it
// //   // is the flat per-type weight from ScoringConfig.certainty_weights (a firsthand OBSERVATION high,
// //   // a HEARSAY low). For a DERIVED memory (evidence present) it is the INHERITED certainty: the mean
// //   // of the certainties of the memories it cites (its provenance) — so a synthesis built mostly on
// //   // hearsay stays penalised (20 hearsay + 1 observation → ~0.14), preventing a single observation
// //   // from laundering gossip into truth. Multiplicative, so a contradicted lie loses decisively (1.0
// //   // vs 0.1) while strong gossip can still outrank a stale observation on the base score.
// //   double certainty   = 9;
// //   double base_score  = 10;  // alpha-weighted sum of the *_norm dimensions (before certainty)
// //   double final_score = 11;  // base_score × certainty — the value actually ranked on
// // }
// //
// // // Scoring config in effect for a recall — logged on MemoriesRecalled so the ranking is reproducible
// // // and the weights are tunable from the log alone, even after the config is later changed.
// // message ScoringConfig {
// //   // Weights of the three ranked dimensions in the base score (before certainty). Higher = that
// //   // dimension matters more; typically kept normalized so they sum to 1.
// //   double alpha_recency    = 1;  // weight of the recency (temporal-decay) term
// //   double alpha_importance = 2;  // weight of the importance (salience) term
// //   double alpha_relevance  = 3;  // weight of the relevance (query-similarity) term
// //
// //   double recency_tau_seconds = 4;  // decay constant: recency_raw = e^(-(dt seconds) / tau)
// //
// //   // Per-type certainty multipliers — the flat trust weight for a RAW memory of each type (e.g.
// //   // OBSERVATION high, HEARSAY low). Derived memories don't use this directly; they inherit certainty
// //   // from the memories they cite (see ScoredMemory.certainty).
// //   repeated CertaintyWeight certainty_weights = 5;
// //
// //   Bounds recency_bounds    = 6;  // bounds for the recency dimension
// //   Bounds importance_bounds = 7;  // bounds for the importance dimension
// //   Bounds relevance_bounds  = 8;  // bounds for the relevance dimension
// //
// //   // Maps each MemoryImportance level to the salience [0,1] used as importance_raw before
// //   // normalization. Distinct from `alpha_importance` (the weight of the importance dimension in the
// //   // sum) and `importance_bounds` (its normalization range): this is the level→number mapping the
// //   // agent's coarse importance enum resolves to. Tunable; logged here for reproducibility.
// //   repeated ImportanceWeight importance_weights = 9;
// //
// //   // One entry of the type→certainty map (see the `certainty` field on ScoredMemory).
// //   message CertaintyWeight {
// //     MemoryType type       = 1;  // the memory type this weight applies to
// //     double     multiplier = 2;  // its trust multiplier (~0..1)
// //   }
// //
// //   // One entry of the importance-level→salience map.
// //   message ImportanceWeight {
// //     MemoryImportance level      = 1;  // the importance bucket
// //     double           multiplier = 2;  // the salience [0,1] it resolves to
// //   }
// //
// //   // A min/max pair for min-max normalizing one dimension across the candidate pool.
// //   message Bounds {
// //     double min = 1;  // lowest raw value in the pool
// //     double max = 2;  // highest raw value in the pool
// //   }
// // }
//
//
// public sealed class Memory {
//     public string MemoryId { get; set; } = "";
//
//     public MemoryType MemoryType { get; set; }
//
//     public string Content { get; set; } = "";
//
//     public MemoryImportance Importance { get; set; }
//
//     public Evidence? Evidence { get; set; }
//
//     public IReadOnlyList<Tag> Tags { get; set; } = [];
//
//     public MemorySentiment Sentiment { get; set; } = MemorySentiment.Neutral;
//
//     public MemoryUrgency Urgency { get; set; } = MemoryUrgency.Medium;
//
//     public TemporalContext? Validity { get; set; }
//
//     public IReadOnlyList<string> Supersedes { get; set; } = [];
//
//     public DateTimeOffset RetainedAt { get; set; }
//
//     public DateTimeOffset? LastAccessedAt { get; set; }
//
//     public DateTimeOffset? RetractedAt { get; set; }
//
//     public DateTimeOffset? SupersededAt { get; set; }
//
//     public string? SupersededBy { get; set; }
// }
//
// public sealed class RetrieveResult {
//     public string QueryId { get; set; } = "";
//
//     public IReadOnlyList<StoredMemory> Memories { get; set; } = [];
// }
//
// public interface IKontextRetriever { // duckdbconnection
//     IAsyncEnumerable<Contracts.RecallResponse> RetrieveAsync(string query, int limit, IReadOnlyCollection<Contracts.Tag> tags, [EnumeratorCancellation] CancellationToken ct = default);
//     
//     
//     
//     
//     string query_id = 1;
//
//     // Natural-language query
//     string query = 2;
//
//     // Max memories to return after ranking
//     int32 limit = 3;
//
//     // Optional: filter out memories below this score
//     double min_score = 4;
//
//     // Pre-filter: only memories carrying these tags enter the candidate pool
//     repeated Tag tags = 5;
//
//     // one per returned memory, best-first
//     repeated ScoredMemory memories = 6;
//
//     // config in effect: weights, tau, certainty map, normalization bounds
//     ScoringConfig config = 7;
//
//     // the "now" used for recency decay that will affect the last accessed timestamp of
//     // each memory recalled
//     google.protobuf.Timestamp recalled_at = 8;
// }