// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Frozen;
using System.Text;
using System.Text.RegularExpressions;

namespace Kurrent.Kontext.Modules.Entities.Extraction;

/// <summary>
/// Entity-name hygiene shared by every extractor and resolver: one normalization rule and one
/// validity rule, so the same surface form can never produce two different canonical keys
/// depending on which stage touched it.
/// </summary>
public static partial class EntityName {
	const int MinLength = 2;

	/// <summary>
	/// Canonical form used for matching and dedup keys: internal whitespace runs collapsed to
	/// single spaces, each word trimmed to its outermost letter or digit, lower-cased invariant.
	/// </summary>
	public static string Normalize(string name) {
		if (string.IsNullOrWhiteSpace(name))
			return string.Empty;

		var source  = name.AsSpan();
		var builder = new StringBuilder(name.Length);

		for (var index = 0; index < source.Length;) {
			while (index < source.Length && char.IsWhiteSpace(source[index]))
				index++;

			var start = index;

			while (index < source.Length && !char.IsWhiteSpace(source[index]))
				index++;

			var word = TrimToWord(source[start..index]);

			if (word.IsEmpty)
				continue;

			if (builder.Length > 0)
				builder.Append(' ');

			foreach (var ch in word)
				builder.Append(char.ToLowerInvariant(ch));
		}

		return builder.ToString();
	}

	// Trims to the outermost letter or digit: punctuation the surrounding prose owns ("chen?",
	// "(acme)", and the sentence-final period an NER span drags in) is never part of a name, while
	// internal marks ("o'brien", "at&t", "coca-cola", "https://kurrent.io") always are.
	static ReadOnlySpan<char> TrimToWord(ReadOnlySpan<char> word) {
		var start = 0;
		var end   = word.Length - 1;

		while (start <= end && !char.IsLetterOrDigit(word[start]))
			start++;

		while (end >= start && !char.IsLetterOrDigit(word[end]))
			end--;

		return word[start..(end + 1)];
	}

	/// <summary>
	/// Whether a surface form can be a named entity at all: long enough, not a stopword, not
	/// purely numeric. Filters the pronouns/articles/filler that NER stages routinely mislabel;
	/// punctuation-only forms are already gone, <see cref="Normalize"/> leaves them empty.
	/// </summary>
	public static bool IsValid(string name) {
		var normalized = Normalize(name);

		return normalized.Length >= MinLength
		       && !Stopwords.Contains(normalized)
		       && !NumericPattern().IsMatch(normalized);
	}

	[GeneratedRegex(@"^[\d\s.,%-]+$")]
	private static partial Regex NumericPattern();

	// Ported from the reference implementation's ENTITY_STOPWORDS: pronouns, common verbs,
	// articles/determiners, prepositions, conjunctions, adverbs, over-generic nouns, ordinals,
	// filler words, and conversation artifacts.
	static readonly FrozenSet<string> Stopwords = FrozenSet.ToFrozenSet([
		// pronouns
		"i", "me", "my", "myself", "we", "our", "ours", "ourselves",
		"you", "your", "yours", "yourself", "yourselves",
		"he", "him", "his", "himself", "she", "her", "hers", "herself",
		"it", "its", "itself", "they", "them", "their", "theirs", "themselves",
		"what", "which", "who", "whom", "this", "that", "these", "those",

		// common verbs
		"am", "is", "are", "was", "were", "be", "been", "being",
		"have", "has", "had", "having", "do", "does", "did", "doing",
		"would", "should", "could", "ought", "might", "must", "shall", "will", "can",

		// articles and determiners
		"a", "an", "the", "some", "any", "no", "every", "each", "either", "neither",

		// prepositions
		"in", "on", "at", "by", "for", "with", "about", "against", "between", "into",
		"through", "during", "before", "after", "above", "below", "to", "from",
		"up", "down", "out", "off", "over", "under",

		// conjunctions
		"and", "but", "or", "nor", "so", "yet", "both", "not", "only", "than",
		"when", "where", "while", "if", "because", "although",

		// adverbs
		"here", "there", "why", "how", "all", "few", "more", "most", "other", "such",
		"own", "same", "too", "very", "just", "also", "now", "then", "once",
		"always", "never", "often", "still", "already",

		// over-generic nouns
		"thing", "things", "stuff", "way", "ways", "something", "anything", "nothing",
		"someone", "anyone", "everyone", "nobody", "everybody", "somebody",
		"people", "person", "man", "woman", "men", "women", "guy", "guys",
		"time", "times", "day", "days", "year", "years", "today", "tomorrow", "yesterday",

		// generic references
		"one", "ones", "two", "first", "second", "third", "last", "next",

		// filler words
		"like", "really", "actually", "basically", "literally", "maybe", "probably",
		"perhaps", "well", "okay", "ok", "yes", "yeah", "yep", "nope",

		// conversation artifacts
		"um", "uh", "ah", "oh", "hmm", "hm", "er", "eh",
	], StringComparer.Ordinal);
}
