// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Globalization;

namespace Kurrent.Kontext.Infrastructure.Data.LanceDB;

/// <summary>
/// Build parameters per lance vector index type, as the vendored extension accepts them
/// (<c>rust/ffi/index.rs</c>). A null property is not sent, so the engine's own default applies.
/// </summary>
static class LanceIvf {
    /// <summary>Parameters every IVF family accepts.</summary>
    public static IEnumerable<(string, string)> Common(LanceMetricType metric, int? partitions, string? version, bool replace) {
        var metricType = metric.ToString().ToLowerInvariant();

        yield return ("metric_type", Sql.Text(metricType));
        yield return ("replace", Sql.Flag(replace));

        if (partitions is { } value)
            yield return ("num_partitions", Sql.Number(value));

        if (version is not null)
            yield return ("version", Sql.Text(version));
    }
}

static class Sql {
    public static string Text(string value) => $"'{value.Replace("'", "''")}'";
    public static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    public static string Flag(bool value)   => value ? "true" : "false";
}

/// <summary>Shared IVF settings. Search-time recall is governed by <c>nprobs</c>.</summary>
public abstract class LanceIvfIndexOptionsBase {
    /// <summary>Distance metric. Engine default l2.</summary>
    public LanceMetricType MetricType { get; set; } = LanceMetricType.L2;

    /// <summary>IVF partition count. Engine default 256; lance sizes IVF_PQ and IVF_RQ as rows / 4096.</summary>
    public int? NumPartitions { get; set; }

    /// <summary>Index storage version. Engine default v3.</summary>
    public string? Version { get; set; }

    /// <summary>Replaces an existing index of the same name instead of failing.</summary>
    public bool Replace { get; set; }

    public virtual void EnsureValid() {
        if (NumPartitions is < 1)
            throw new InvalidOperationException($"{nameof(NumPartitions)} must be at least 1.");
    }
}

/// <summary>No quantization: exact vectors in IVF partitions. Largest index, highest recall.</summary>
public sealed class LanceIvfFlatIndexOptions : LanceIvfIndexOptionsBase, ILanceVectorIndexOptions {
    public LanceVectorIndexType IndexType => LanceVectorIndexType.IvfFlat;

    public IEnumerable<(string Name, string Value)> Parameters() =>
        LanceIvf.Common(MetricType, NumPartitions, Version, Replace);
}

/// <summary>
/// IVF + product quantization: the index is <see cref="NumSubVectors"/> bytes per row at 8 bits.
/// Search-time <c>refine_factor</c> re-ranks the quantization error away.
/// </summary>
public sealed class LanceIvfPqIndexOptions : LanceIvfIndexOptionsBase, ILanceVectorIndexOptions {
    public LanceVectorIndexType IndexType => LanceVectorIndexType.IvfPq;

    /// <summary>PQ sub-vectors; must divide the vector dimension. Engine default 16, lance guidance dimension / 8.</summary>
    public int? NumSubVectors { get; set; }

    /// <summary>Bits per PQ code, 4 or 8. Engine default 8.</summary>
    public int? NumBits { get; set; }

    /// <summary>k-means iterations while training. Engine default 50.</summary>
    public int? MaxIterations { get; set; }

    public IEnumerable<(string Name, string Value)> Parameters() {
        foreach (var parameter in LanceIvf.Common(MetricType, NumPartitions, Version, Replace))
            yield return parameter;

        if (NumSubVectors is { } subVectors)
            yield return ("num_sub_vectors", Sql.Number(subVectors));

        if (NumBits is { } bits)
            yield return ("num_bits", Sql.Number(bits));

        if (MaxIterations is { } iterations)
            yield return ("max_iterations", Sql.Number(iterations));
    }

    public override void EnsureValid() {
        base.EnsureValid();

        if (NumSubVectors is < 1)
            throw new InvalidOperationException($"{nameof(NumSubVectors)} must be at least 1.");

        if (NumBits is not (null or 4 or 8))
            throw new InvalidOperationException($"{nameof(NumBits)} must be 4 or 8.");
    }
}

/// <summary>IVF and residual quantization: the most aggressive compression lance offers.</summary>
public sealed class LanceIvfRqIndexOptions : LanceIvfIndexOptionsBase, ILanceVectorIndexOptions {
    public LanceVectorIndexType IndexType => LanceVectorIndexType.IvfRq;

    /// <summary>Bits per RQ code. Engine default 8.</summary>
    public int? NumBits { get; set; }

    public IEnumerable<(string Name, string Value)> Parameters() {
        foreach (var parameter in LanceIvf.Common(MetricType, NumPartitions, Version, Replace))
            yield return parameter;

        if (NumBits is { } bits)
            yield return ("num_bits", Sql.Number(bits));
    }
}

/// <summary>IVF and scalar quantization: roughly a quarter of raw size.</summary>
public sealed class LanceIvfSqIndexOptions : LanceIvfIndexOptionsBase, ILanceVectorIndexOptions {
    public LanceVectorIndexType IndexType => LanceVectorIndexType.IvfSq;

    /// <summary>Bits per scalar code. Engine default 8.</summary>
    public int? NumBits { get; set; }

    /// <summary>Rows sampled per partition while training. Engine default 256.</summary>
    public int? SampleRate { get; set; }

    public IEnumerable<(string Name, string Value)> Parameters() {
        foreach (var parameter in LanceIvf.Common(MetricType, NumPartitions, Version, Replace))
            yield return parameter;

        if (NumBits is { } bits)
            yield return ("num_bits", Sql.Number(bits));

        if (SampleRate is { } rate)
            yield return ("sample_rate", Sql.Number(rate));
    }
}

/// <summary>
/// Shared HNSW graph knobs. Recall for every HNSW family is governed by the search-time <c>ef</c>,
/// which this extension does not expose — measured MRR 0.00-0.375 against 1.0000 for IVF_PQ.
/// </summary>
public abstract class LanceIvfHnswIndexOptionsBase : LanceIvfIndexOptionsBase {
    /// <summary>Graph degree, max connections per node. Engine default 20.</summary>
    public int? HnswM { get; set; }

    /// <summary>Build-time candidate beam. Engine default 150.</summary>
    public int? HnswEfConstruction { get; set; }

    /// <summary>Graph levels. Engine default 7.</summary>
    public int? HnswMaxLevel { get; set; }

    /// <summary>Prefetch distance during traversal. Engine default 2.</summary>
    public int? HnswPrefetchDistance { get; set; }

    protected IEnumerable<(string, string)> HnswParameters() {
        return LanceIvf
            .Common(
                MetricType, NumPartitions, Version,
                Replace)
            .Concat(
                HnswCommon(
                    HnswM, HnswEfConstruction, HnswMaxLevel,
                    HnswPrefetchDistance));
        
        // The HNSW graph knobs the three HNSW families share.
        static IEnumerable<(string, string)> HnswCommon(int? m, int? efConstruction, int? maxLevel, int? prefetchDistance) {
            if (m is { } graphDegree)
                yield return ("hnsw_m", Sql.Number(graphDegree));

            if (efConstruction is { } beam)
                yield return ("hnsw_ef_construction", Sql.Number(beam));

            if (maxLevel is { } levels)
                yield return ("hnsw_max_level", Sql.Number(levels));

            if (prefetchDistance is { } distance)
                yield return ("hnsw_prefetch_distance", Sql.Number(distance));
        }
    }
}

/// <summary>HNSW over exact vectors.</summary>
public sealed class LanceIvfHnswFlatIndexOptions : LanceIvfHnswIndexOptionsBase, ILanceVectorIndexOptions {
    public LanceVectorIndexType IndexType => LanceVectorIndexType.IvfHnswFlat;

    public IEnumerable<(string Name, string Value)> Parameters() => HnswParameters();
}

/// <summary>HNSW over product-quantized vectors.</summary>
public sealed class LanceIvfHnswPqIndexOptions : LanceIvfHnswIndexOptionsBase, ILanceVectorIndexOptions {
    public LanceVectorIndexType IndexType => LanceVectorIndexType.IvfHnswPq;

    /// <summary>PQ sub-vectors; must divide the vector dimension.</summary>
    public int? NumSubVectors { get; set; }

    /// <summary>Bits per PQ code, 4 or 8.</summary>
    public int? NumBits { get; set; }

    public IEnumerable<(string Name, string Value)> Parameters() {
        foreach (var parameter in HnswParameters())
            yield return parameter;

        if (NumSubVectors is { } subVectors)
            yield return ("num_sub_vectors", Sql.Number(subVectors));

        if (NumBits is { } bits)
            yield return ("num_bits", Sql.Number(bits));
    }
}

/// <summary>HNSW over scalar-quantized vectors.</summary>
public sealed class LanceIvfHnswSqIndexOptions : LanceIvfHnswIndexOptionsBase, ILanceVectorIndexOptions {
    public LanceVectorIndexType IndexType => LanceVectorIndexType.IvfHnswSq;

    /// <summary>Bits per scalar code.</summary>
    public int? NumBits { get; set; }

    /// <summary>Rows sampled per partition while training.</summary>
    public int? SampleRate { get; set; }

    public IEnumerable<(string Name, string Value)> Parameters() {
        foreach (var parameter in HnswParameters())
            yield return parameter;

        if (NumBits is { } bits)
            yield return ("num_bits", Sql.Number(bits));

        if (SampleRate is { } rate)
            yield return ("sample_rate", Sql.Number(rate));
    }
}

/// <summary>A scalar index's kind and creation behaviour.</summary>
public sealed class LanceScalarIndexOptions {
    /// <summary>The scalar index kind.</summary>
    public LanceScalarIndexType Type { get; set; } = LanceScalarIndexType.BTree;

    /// <summary>Replaces an existing index of the same name instead of failing.</summary>
    public bool Replace { get; set; }

    public IEnumerable<(string Name, string Value)> Parameters() {
        yield return ("replace", Sql.Flag(Replace));
    }
}

/// <summary>How an existing index is refreshed.</summary>
public sealed class LanceOptimizeIndexOptions {
    /// <summary>The refresh strategy. Engine default append.</summary>
    public LanceOptimizeMode Mode { get; set; } = LanceOptimizeMode.Append;

    /// <summary>Deltas to merge. <see cref="LanceOptimizeMode.Merge"/> only, engine default 1.</summary>
    public int? NumIndicesToMerge { get; set; }

    public IEnumerable<(string Name, string Value)> Parameters() {
        var mode = Mode.ToString().ToLowerInvariant();

        yield return ("mode", Sql.Text(mode));

        if (NumIndicesToMerge is { } merges)
            yield return ("num_indices_to_merge", Sql.Number(merges));
    }

    /// <summary>Throws on the two combinations the engine rejects.</summary>
    public void EnsureValid() {
        if (Mode is not LanceOptimizeMode.Merge && NumIndicesToMerge is not null)
            throw new InvalidOperationException($"{nameof(NumIndicesToMerge)} is only valid for {nameof(LanceOptimizeMode.Merge)}.");

        if (Mode is LanceOptimizeMode.Merge && NumIndicesToMerge is < 1)
            throw new InvalidOperationException($"{nameof(NumIndicesToMerge)} must be greater than zero.");
    }
}

/// <summary>
/// Inverted (FTS) index parameters. <c>analyzer = 'code'</c> is rejected unless
/// <c>base_tokenizer = 'code'</c> rides with it, and <c>lance_tokenizer = 'json'</c> panics the
/// engine on any document that is not parseable JSON and on any query that is not a
/// <c>field,type,value</c> triple.
/// </summary>
public sealed class LanceInvertedIndexOptions {
    /// <summary>Replaces an existing index of the same name instead of failing.</summary>
    public bool Replace { get; set; }

    /// <summary>Preset: <c>text</c> or <c>code</c>.</summary>
    public string? Analyzer { get; set; }

    /// <summary>simple, whitespace, raw, ngram, code, icu, icu/split.</summary>
    public string? BaseTokenizer { get; set; }

    /// <summary>text or json.</summary>
    public string? LanceTokenizer { get; set; }

    /// <summary>Snowball stemmer language, e.g. English.</summary>
    public string? Language { get; set; }

    /// <summary>Stems tokens to their root.</summary>
    public bool? Stem { get; set; }

    /// <summary>Drops stop words.</summary>
    public bool? RemoveStopWords { get; set; }

    /// <summary>Lower-cases tokens.</summary>
    public bool? LowerCase { get; set; }

    /// <summary>Folds accents to ASCII.</summary>
    public bool? AsciiFolding { get; set; }

    /// <summary>Tokens longer than this are dropped silently. Engine default 40.</summary>
    public int? MaxTokenLength { get; set; }

    /// <summary>Records token positions; the prerequisite for phrase queries.</summary>
    public bool? WithPosition { get; set; }

    /// <summary>code tokenizer: splits compound identifiers into subwords.</summary>
    public bool? SplitIdentifiers { get; set; }

    /// <summary>code tokenizer: splits at letter/digit boundaries.</summary>
    public bool? SplitOnNumerics { get; set; }

    /// <summary>code tokenizer: emits the whole identifier alongside its subwords.</summary>
    public bool? PreserveOriginal { get; set; }

    /// <summary>code tokenizer: emits operator characters as tokens.</summary>
    public bool? IndexOperators { get; set; }

    /// <summary>ngram tokenizer: shortest gram. Engine default 3.</summary>
    public int? MinNgramLength { get; set; }

    /// <summary>ngram tokenizer: longest gram. Engine default 3.</summary>
    public int? MaxNgramLength { get; set; }

    /// <summary>ngram tokenizer: indexes prefixes only.</summary>
    public bool? PrefixOnly { get; set; }

    public IEnumerable<(string Name, string Value)> Parameters() {
        yield return ("replace", Sql.Flag(Replace));

        foreach (var (name, value) in new[] {
            ("analyzer", Analyzer),
            ("base_tokenizer", BaseTokenizer),
            ("lance_tokenizer", LanceTokenizer),
            ("language", Language),
        })
            if (value is not null)
                yield return (name, Sql.Text(value));

        foreach (var (name, value) in new (string, bool?)[] {
            ("stem", Stem),
            ("remove_stop_words", RemoveStopWords),
            ("lower_case", LowerCase),
            ("ascii_folding", AsciiFolding),
            ("with_position", WithPosition),
            ("split_identifiers", SplitIdentifiers),
            ("split_on_numerics", SplitOnNumerics),
            ("preserve_original", PreserveOriginal),
            ("index_operators", IndexOperators),
            ("prefix_only", PrefixOnly),
        })
            if (value is { } flag)
                yield return (name, Sql.Flag(flag));

        foreach (var (name, value) in new[] {
            ("max_token_length", MaxTokenLength),
            ("min_ngram_length", MinNgramLength),
            ("max_ngram_length", MaxNgramLength),
        })
            if (value is { } number)
                yield return (name, Sql.Number(number));
    }
}
