// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace Kurrent.Kontext.Embeddings.GlinerOnnx;

/// <summary>
/// Zero-shot span-mode NER over a GLiNER ONNX export: the labels ride in the prompt as
/// <c>&lt;&lt;ENT&gt;&gt;</c> tokens, the model scores every span of up to
/// <see cref="GlinerOnnxOptions.MaxWidth"/> words against every label, and sigmoid + greedy
/// non-overlap keeps the winners. Encoding and decoding mirror Knowledgator/GLiNER.cpp; the
/// tokenizer path is verified against the HF <c>tokenizers</c> ground truth for
/// gliner_small-v2.1. Thread-safe: the tokenizer is stateless and onnxruntime sessions accept
/// concurrent <c>Run</c> calls.
/// </summary>
public sealed partial class GlinerOnnxEntityRecognizer : IDisposable {
	public const string DefaultModelId = "gliner-small-v2.1";

	// DeBERTa-v3 special-token ids — the backbone family of every GLiNER v2.x release.
	const int Pad = 0;
	const int Cls = 1;
	const int Sep = 2;
	const int Unk = 3;

	const string EntityToken          = "<<ENT>>";
	const string PromptSeparatorToken = "<<SEP>>";

	readonly GlinerOnnxOptions      _options;
	readonly SentencePieceTokenizer _tokenizer;
	readonly InferenceSession       _session;

	/// <summary>
	/// Resolves the model named by <see cref="GlinerOnnxOptions.ModelId"/> (defaulting to
	/// <c>gliner-small-v2.1</c>) from <paramref name="registry"/> and builds the recognizer. The
	/// main entry point; no model bytes move until this constructor runs.
	/// </summary>
	public GlinerOnnxEntityRecognizer(OnnxModelRegistry registry, GlinerOnnxOptions? options = null)
		: this(registry.Get((options ?? new GlinerOnnxOptions()).ModelId ?? DefaultModelId), options) { }

	/// <summary>
	/// Builds the recognizer directly from a resolved <see cref="OnnxModel"/> (handy for tests).
	/// Reads the ONNX model and the SentencePiece asset from it; the streams are consumed and
	/// closed here.
	/// </summary>
	public GlinerOnnxEntityRecognizer(OnnxModel model, GlinerOnnxOptions? options = null) {
		_options = options ?? new GlinerOnnxOptions();

		using (var spm = model.ReadAsset(_options.TokenizerAsset))
			_tokenizer = SentencePieceTokenizer.Create(spm, addBeginningOfSentence: false, addEndOfSentence: false,
				specialTokens: new Dictionary<string, int> {
					["[PAD]"] = Pad, ["[CLS]"] = Cls, ["[SEP]"] = Sep, ["[UNK]"] = Unk,
					[EntityToken] = _options.EntityTokenId, [PromptSeparatorToken] = _options.PromptSeparatorTokenId,
				});

		using (var onnx = model.ReadModel())
			_session = OnnxModelLoader.CreateSession(OnnxModelLoader.ReadAllBytes(onnx));

		// Warm-up probe: fails loud at construction when the logits layout does not match the
		// decoder, and keeps first-request timings honest.
		Recognize("probe", ["person"]);
	}

	/// <summary>
	/// Recognizes entity spans of the given labels in <paramref name="text"/>. Spans come back
	/// non-overlapping, in appearance order, each scoring at least
	/// <see cref="GlinerOnnxOptions.Threshold"/>.
	/// </summary>
	public IReadOnlyList<RecognizedSpan> Recognize(string text, IReadOnlyList<string> labels) {
		if (labels.Count == 0)
			return [];

		var words = WordPattern().Matches(text)
			.Select(m => (Text: m.Value, Start: m.Index, End: m.Index + m.Length))
			.ToList();

		var prompt = labels.SelectMany(label => new[] { EntityToken, label }).Append(PromptSeparatorToken).ToList();
		var pieces = prompt.Concat(words.Select(w => w.Text)).Select(t => _tokenizer.EncodeToIds(t)).ToList();

		// The label prompt always rides in full; trailing words are dropped to honor MaxTokens.
		var budget = _options.MaxTokens - 2 - pieces.Take(prompt.Count).Sum(p => p.Count);
		var fit    = 0;

		while (fit < words.Count && (budget -= pieces[prompt.Count + fit].Count) >= 0)
			fit++;

		if (fit < words.Count) {
			words  = words.GetRange(0, fit);
			pieces = pieces.GetRange(0, prompt.Count + fit);
		}

		if (words.Count == 0)
			return [];

		var n = 2 + pieces.Sum(p => p.Count);

		var ids       = new DenseTensor<long>([1, n]);
		var mask      = new DenseTensor<long>([1, n]);
		var wordsMask = new DenseTensor<long>([1, n]);

		ids[0, 0]     = Cls;
		ids[0, n - 1] = Sep;
		for (var i = 0; i < n; i++)
			mask[0, i] = 1;

		// words_mask numbers the first subtoken of each TEXT word 1..N; prompt tokens stay 0.
		var (at, wordId) = (1, 1L);
		for (var p = 0; p < pieces.Count; p++) {
			if (p >= prompt.Count)
				wordsMask[0, at] = wordId++;
			foreach (var id in pieces[p])
				ids[0, at++] = id;
		}

		var numSpans    = words.Count * _options.MaxWidth;
		var spanIdx     = new DenseTensor<long>([1, numSpans, 2]);
		var spanMask    = new DenseTensor<bool>([1, numSpans]);
		var textLengths = new DenseTensor<long>(new[] { (long)words.Count }, [1, 1]);

		for (var i = 0; i < words.Count; i++)
			for (var j = 0; j < Math.Min(_options.MaxWidth, words.Count - i); j++) {
				spanIdx[0, i * _options.MaxWidth + j, 0] = i;
				spanIdx[0, i * _options.MaxWidth + j, 1] = i + j;
				spanMask[0, i * _options.MaxWidth + j]   = true;
			}

		using var output = _session.Run([
			NamedOnnxValue.CreateFromTensor("input_ids", ids),
			NamedOnnxValue.CreateFromTensor("attention_mask", mask),
			NamedOnnxValue.CreateFromTensor("words_mask", wordsMask),
			NamedOnnxValue.CreateFromTensor("text_lengths", textLengths),
			NamedOnnxValue.CreateFromTensor("span_idx", spanIdx),
			NamedOnnxValue.CreateFromTensor("span_mask", spanMask),
		]);

		var logits = output.First().AsEnumerable<float>().ToArray();

		if (logits.Length != words.Count * _options.MaxWidth * labels.Count)
			throw new InvalidOperationException(
				$"Unexpected GLiNER logits layout: {logits.Length} values for {words.Count} words × "
			  + $"{_options.MaxWidth} widths × {labels.Count} labels.");

		List<RecognizedSpan> candidates = [];

		for (var i = 0; i < logits.Length; i++) {
			var score = 1.0 / (1.0 + Math.Exp(-logits[i]));

			if (score < _options.Threshold)
				continue;

			var start = i / (_options.MaxWidth * labels.Count) % words.Count;
			var end   = start + i / labels.Count % _options.MaxWidth;

			if (end >= words.Count)
				continue;

			candidates.Add(new(
				text[words[start].Start..words[end].End], labels[i % labels.Count], score,
				words[start].Start, words[end].End));
		}

		// Flat NER: highest score claims its range, overlapping runners-up drop.
		List<RecognizedSpan> kept = [];

		foreach (var span in candidates.OrderByDescending(s => s.Score))
			if (!kept.Any(k => span.Start < k.End && k.Start < span.End))
				kept.Add(span);

		return kept.OrderBy(k => k.Start).ToList();
	}

	[GeneratedRegex(@"\w+(?:[-_]\w+)*|\S")]
	private static partial Regex WordPattern();

	public void Dispose() => _session.Dispose();
}
