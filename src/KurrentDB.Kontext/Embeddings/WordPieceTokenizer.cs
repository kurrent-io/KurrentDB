// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;

namespace KurrentDB.Kontext.Embeddings;

/// <summary>
/// Minimal BERT WordPiece tokenizer. Produces input_ids, attention_mask, and token_type_ids
/// compatible with BERT-based ONNX models.
/// </summary>
public class WordPieceTokenizer {
	readonly Dictionary<string, int> _wordVocab;
	readonly Dictionary<string, int> _subwordVocab; // words starting with ##

	readonly int _clsId;
	readonly int _sepId;
	readonly int _unkId;
	readonly int _maxModelTokens;

	readonly long[] _ones;
	readonly long[] _zeros;
	readonly long[] _inputIds;
	readonly long[] _tokenTypePair;
	int _n;

	const int MaxWordLength = 200;

	public WordPieceTokenizer(ReadOnlySpan<byte> vocabUtf8, int maxModelTokens) {
		_maxModelTokens = maxModelTokens;
		_wordVocab = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		_subwordVocab = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		LoadVocab(vocabUtf8);

		_clsId = _wordVocab["[CLS]"];
		_sepId = _wordVocab["[SEP]"];
		_unkId = _wordVocab["[UNK]"];

		_ones = new long[maxModelTokens];
		Array.Fill(_ones, 1L);
		_zeros = new long[maxModelTokens];
		_inputIds = new long[maxModelTokens];
		_tokenTypePair = new long[maxModelTokens];
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
	/// Tight-fit encoding of a single sentence.
	/// </summary>
	public (ReadOnlyMemory<long> InputIds, ReadOnlyMemory<long> AttentionMask, ReadOnlyMemory<long> TokenTypeIds) Encode(string text) {
		_n = 0;
		_inputIds[_n++] = _clsId;
		Tokenize(text, _maxModelTokens - 2); // excluding [CLS] and [SEP]
		_inputIds[_n++] = _sepId;

		return (_inputIds.AsMemory(0, _n), _ones.AsMemory(0, _n), _zeros.AsMemory(0, _n));
	}

	/// <summary>
	/// Tight-fit encoding of a query / document pair for cross-encoder scoring. Reserves
	/// 3 tokens for [CLS], [SEP], [SEP]; the query takes priority and the document is
	/// truncated to fit whatever budget remains.
	/// </summary>
	public (ReadOnlyMemory<long> InputIds, ReadOnlyMemory<long> AttentionMask, ReadOnlyMemory<long> TokenTypeIds) EncodePair(string query, string document) {
		var budget = _maxModelTokens - 3; // excluding [CLS] and [SEP] x 2

		_n = 0;
		_inputIds[_n++] = _clsId;

		var queryStart = _n;
		Tokenize(query, budget);
		var queryWritten = _n - queryStart;

		_inputIds[_n++] = _sepId;

		var documentStart = _n;
		Tokenize(document, budget - queryWritten);
		_inputIds[_n++] = _sepId;

		var tokenTypePair = _tokenTypePair;
		Array.Clear(tokenTypePair, 0, documentStart);
		Array.Fill(tokenTypePair, 1L, documentStart, _n - documentStart);

		return (_inputIds.AsMemory(0, _n), _ones.AsMemory(0, _n), tokenTypePair.AsMemory(0, _n));
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