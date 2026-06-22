// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using Catalyst;
using Microsoft.Extensions.Logging;
using Mosaik.Core;

namespace KurrentDB.Kontext.Search;

/// <summary>
/// Content-word extraction via Catalyst's POS tagger.
/// </summary>
public class NounPhraseExtractor(ILogger<NounPhraseExtractor> logger) {
	static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
	{
		"a", "an", "the", "and", "or", "but", "in", "on", "at", "to", "for",
		"of", "with", "by", "from", "is", "it", "its", "are", "was", "were",
		"be", "been", "being", "have", "has", "had", "do", "does", "did",
		"this", "that", "these", "those",
		"very", "quite", "really",
	};

	// POS tags that can appear inside a content phrase
	static readonly HashSet<PartOfSpeech> ContentPOS =
	[
		PartOfSpeech.NOUN,
		PartOfSpeech.PROPN,
		PartOfSpeech.ADJ,
		PartOfSpeech.ADV,
		PartOfSpeech.NUM,
		PartOfSpeech.VERB,
	];

	Pipeline? _pipeline;

	public async Task InitializeAsync() {
		logger.LogInformation("Loading Catalyst English NLP pipeline...");
		Catalyst.Models.English.Register();
		_pipeline = await Pipeline.ForAsync(Language.English);
		logger.LogInformation("Catalyst pipeline ready");
	}

	public string ExtractText(string text) {
		EnsureInitialized();
		if (string.IsNullOrWhiteSpace(text))
			return "";
		var sb = new StringBuilder();
		WalkContentWords(text, sb, AppendToBuilder);
		return sb.ToString();
	}

	public IReadOnlyList<string> ExtractKeywords(string text) {
		EnsureInitialized();
		if (string.IsNullOrWhiteSpace(text))
			return [];
		var list = new List<string>();
		WalkContentWords(text, list, AppendToList);
		return list;
	}

	static readonly Action<StringBuilder, string> AppendToBuilder = static (sb, word) => {
		if (sb.Length > 0)
			sb.Append(' ');
		sb.Append(word);
	};

	static readonly Action<List<string>, string> AppendToList = static (list, word) => list.Add(word);

	void EnsureInitialized() {
		if (_pipeline == null)
			throw new InvalidOperationException($"{nameof(NounPhraseExtractor)} not initialized");
	}

	void WalkContentWords<TSink>(string text, TSink sink, Action<TSink, string> emit) {
		var doc = new Document(text, Language.English);
		_pipeline!.ProcessSingle(doc);

		foreach (var span in doc) {
			foreach (var token in span.Tokens) {
				if (!ContentPOS.Contains(token.POS))
					continue;
				var word = token.Lemma ?? token.Value;
				if (StopWords.Contains(word) || StopWords.Contains(token.Value))
					continue;
				emit(sink, word);
			}
		}
	}
}