// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;

namespace Kurrent.Kontext.Embeddings.PrototypeV2;

/// <summary>
/// The hand-rolled BERT WordPiece tokenizer that shipped as the default local embedding path, ported
/// verbatim from <c>KurrentDB.Kontext</c> so implementation A stays byte-faithful to the vectors already
/// stored in existing indexes. Only the single-sentence encode path is kept (the cross-encoder pair path
/// is not needed here). The tokenization logic is unchanged: <see cref="StringComparer.OrdinalIgnoreCase"/>
/// lookups, no Unicode normalization, punctuation/symbol splitting, greedy WordPiece, [CLS]/[SEP] wrapping,
/// and all-zero token type ids.
/// </summary>
/// <remarks>
/// The ported logic uses shared per-instance buffers and is therefore not re-entrant. The original code
/// solved this by pooling tokenizers above the seam; here a lock guards the (fast) tokenization so this
/// wrapper can be shared by the singleton generator. The lock covers only tokenization — the returned
/// <see cref="EncodedInput"/> holds freshly copied arrays, so ONNX inference still runs unserialized.
/// </remarks>
internal sealed class HandRolledWordPieceTokenizer {
	readonly Lock _gate = new();

	readonly Dictionary<string, int> _wordVocab;
	readonly Dictionary<string, int> _subwordVocab; // words starting with ##

	readonly int _clsId;
	readonly int _sepId;
	readonly int _unkId;
	readonly int _maxModelTokens;

	readonly long[] _ones;
	readonly long[] _inputIds;
	int _n;

	const int MaxWordLength = 200;

	HandRolledWordPieceTokenizer(ReadOnlySpan<byte> vocabUtf8, int maxModelTokens) {
		_maxModelTokens = maxModelTokens;
		_wordVocab = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		_subwordVocab = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		LoadVocab(vocabUtf8);

		_clsId = _wordVocab["[CLS]"];
		_sepId = _wordVocab["[SEP]"];
		_unkId = _wordVocab["[UNK]"];

		_ones = new long[maxModelTokens];
		Array.Fill(_ones, 1L);
		_inputIds = new long[maxModelTokens];
	}

	public static HandRolledWordPieceTokenizer Create(Stream vocab, int maxModelTokens) {
		ArgumentNullException.ThrowIfNull(vocab);
		using var buffer = new MemoryStream();
		vocab.CopyTo(buffer);
		return new HandRolledWordPieceTokenizer(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), maxModelTokens);
	}

	void LoadVocab(ReadOnlySpan<byte> vocabUtf8) {
		var i = 0;
		foreach (var range in vocabUtf8.Split((byte)'\n')) {
			var line = vocabUtf8[range];
			if (!line.IsEmpty && line[^1] == (byte)'\r')
				line = line[..^1];
			var key = Encoding.UTF8.GetString(line);
			if (key.StartsWith("##", StringComparison.Ordinal))
				_subwordVocab[key[2..]] = i;
			else
				_wordVocab[key] = i;
			i++;
		}
	}

	/// <summary>
	/// Tight-fit encoding of a single sentence: [CLS] … [SEP], all-ones attention mask, no token type ids
	/// (single-sentence segments are all zero — the engine zero-fills when the model declares that input).
	/// </summary>
	public EncodedInput Encode(string text) {
		ArgumentNullException.ThrowIfNull(text);

		lock (_gate) {
			_n = 0;
			_inputIds[_n++] = _clsId;
			Tokenize(text, _maxModelTokens - 2); // excluding [CLS] and [SEP]
			_inputIds[_n++] = _sepId;

			return new EncodedInput(_inputIds[.._n], _ones[.._n], TokenTypeIds: null);
		}
	}

	void Tokenize(string text, int maxTokens) {
		var written = 0;
		var i = 0;
		while (i < text.Length && written < maxTokens) {
			var c = text[i];
			if (char.IsWhiteSpace(c)) { i++; continue; }

			int start, length;
			if (char.IsPunctuation(c) || char.IsSymbol(c)) {
				start = i;
				length = 1;
				i++;
			} else {
				start = i;
				do { i++; } while (i < text.Length && !IsBreak(text[i]));
				length = i - start;
			}

			written += TokenizeWord(text, start, length, maxTokens - written);
		}
	}

	static bool IsBreak(char c) =>
		char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c);

	int TokenizeWord(string text, int start, int length, int budget) {
		if (budget <= 0)
			return 0;

		if (length > MaxWordLength) {
			_inputIds[_n++] = _unkId;
			return 1;
		}

		var word = text.AsSpan(start, length);
		var written = 0;
		var s = 0;
		while (s < length && written < budget) {
			var (matchEnd, id) = TryMatchLongest(word, s);
			if (matchEnd < 0) {
				_inputIds[_n++] = _unkId;
				return written + 1;
			}
			_inputIds[_n++] = id;
			written++;
			s = matchEnd;
		}
		return written;
	}

	(int End, int Id) TryMatchLongest(ReadOnlySpan<char> word, int start) {
		var lookup = (start == 0 ? _wordVocab : _subwordVocab)
			.GetAlternateLookup<ReadOnlySpan<char>>();
		var end = word.Length;
		while (start < end) {
			if (lookup.TryGetValue(word.Slice(start, end - start), out var id))
				return (end, id);
			end--;
		}
		return (-1, 0);
	}
}
