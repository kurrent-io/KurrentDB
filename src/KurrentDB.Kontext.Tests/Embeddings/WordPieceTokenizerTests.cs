// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Tests.Embeddings;

public class WordPieceTokenizerTests {
	static byte[] VocabBytes => File.ReadAllBytes(Path.Combine(
		AppContext.BaseDirectory, "Fixtures", "test-vocab.txt"));

	static WordPieceTokenizer New(int maxModelTokens) => new(VocabBytes, maxModelTokens);

	static bool All(ReadOnlyMemory<long> mem, long value) {
		var span = mem.Span;
		for (var i = 0; i < span.Length; i++)
			if (span[i] != value)
				return false;
		return true;
	}

	[Test]
	public async Task Encode_Produces_Tight_Fit_Length() {
		var (inputIds, attentionMask, tokenTypeIds) = New(16).Encode("hello world");

		// [CLS] hello world [SEP] = 4 tokens, no padding.
		await Assert.That(inputIds.Length).IsEqualTo(4);
		await Assert.That(attentionMask.Length).IsEqualTo(4);
		await Assert.That(tokenTypeIds.Length).IsEqualTo(4);
	}

	[Test]
	public async Task Encode_Starts_With_CLS_And_Ends_With_SEP() {
		var (inputIds, _, _) = New(16).Encode("hello world");

		// [CLS] = 2, [SEP] = 3 in our test vocab
		await Assert.That(inputIds.Span[0]).IsEqualTo(2L); // [CLS]
		await Assert.That(inputIds.Span[^1]).IsEqualTo(3L); // [SEP]
	}

	[Test]
	public async Task Encode_AttentionMask_Is_All_Ones() {
		var (_, attentionMask, _) = New(16).Encode("hello world");

		// Tight-fit: no [PAD] tokens, so every position is attended.
		await Assert.That(All(attentionMask, 1L)).IsTrue();
	}

	[Test]
	public async Task Encode_TokenTypeIds_Are_All_Zero_For_Single_Sentence() {
		var (_, _, tokenTypeIds) = New(16).Encode("hello world");

		await Assert.That(All(tokenTypeIds, 0L)).IsTrue();
	}

	[Test]
	public async Task Encode_Lowercases_Input() {
		var tokenizer = New(8);
		var (idsLower, _, _) = tokenizer.Encode("hello");
		var lower = idsLower.ToArray();
		var (idsUpper, _, _) = tokenizer.Encode("HELLO");
		var upper = idsUpper.ToArray();

		await Assert.That(lower).IsEquivalentTo(upper);
	}

	[Test]
	public async Task Encode_Unknown_Token_Gets_UNK_Id() {
		// "zzzznotinvocab" is not in our test vocab
		var (inputIds, _, _) = New(8).Encode("zzzznotinvocab");

		// [CLS]=2, [UNK]=1, [SEP]=3
		await Assert.That(inputIds.Span[1]).IsEqualTo(1L); // [UNK]
	}

	[Test]
	public async Task Encode_Truncates_Long_Input() {
		// maxModelTokens=6 means at most 4 real tokens (6 - [CLS] - [SEP])
		var (inputIds, _, _) = New(6).Encode("hello world order placed shipped delivered");

		await Assert.That(inputIds.Length).IsEqualTo(6);
	}

	[Test]
	public async Task Encode_Empty_String_Has_Only_CLS_SEP() {
		var (inputIds, attentionMask, _) = New(8).Encode("");

		await Assert.That(inputIds.Length).IsEqualTo(2);
		await Assert.That(inputIds.Span[0]).IsEqualTo(2L); // [CLS]
		await Assert.That(inputIds.Span[1]).IsEqualTo(3L); // [SEP]
		await Assert.That(attentionMask.Span[0]).IsEqualTo(1L);
		await Assert.That(attentionMask.Span[1]).IsEqualTo(1L);
	}

	[Test]
	public async Task Encode_Tokenizes_Punctuation_As_Separate_Tokens() {
		var (inputIds, _, _) = New(16).Encode("hello, world!");

		// [CLS] hello , world ! [SEP] = 6 tokens
		await Assert.That(inputIds.Length).IsEqualTo(6);
	}

	[Test]
	public async Task EncodePair_Produces_Correct_TokenTypeIds() {
		var (_, _, tokenTypeIds) = New(16).EncodePair("hello", "world");

		// Structure: [CLS] hello [SEP] world [SEP]
		await Assert.That(tokenTypeIds.Length).IsEqualTo(5);
		await Assert.That(tokenTypeIds.Span[0]).IsEqualTo(0L); // [CLS]
		await Assert.That(tokenTypeIds.Span[1]).IsEqualTo(0L); // hello
		await Assert.That(tokenTypeIds.Span[2]).IsEqualTo(0L); // [SEP]
		await Assert.That(tokenTypeIds.Span[3]).IsEqualTo(1L); // world
		await Assert.That(tokenTypeIds.Span[4]).IsEqualTo(1L); // [SEP]
	}

	[Test]
	public async Task EncodePair_AttentionMask_Is_All_Ones() {
		var (_, attentionMask, _) = New(16).EncodePair("hello", "world");

		// Tight-fit: no [PAD] tokens.
		await Assert.That(All(attentionMask, 1L)).IsTrue();
	}

	[Test]
	public async Task EncodePair_Truncates_When_Exceeds_MaxLength() {
		// maxModelTokens=7, budget = 7-3 = 4 real tokens for both segments combined
		var (inputIds, _, _) = New(7).EncodePair(
			"hello world order", "placed shipped delivered");

		await Assert.That(inputIds.Length).IsEqualTo(7);
	}

	[Test]
	public async Task Encode_WordPiece_Subword_Splitting() {
		// "orders" should split into "order" + "##s" if both are in vocab
		var (inputIds, _, _) = New(8).Encode("orders");

		// [CLS] order ##s [SEP] = 4 tokens
		await Assert.That(inputIds.Length).IsEqualTo(4);
	}

	[Test]
	public async Task Encode_WordPiece_Subword_Splitting_Is_Case_Insensitive() {
		var tokenizer = New(8);
		var (lower, _, _) = tokenizer.Encode("orders");
		var lowerArr = lower.ToArray();
		var (upper, _, _) = tokenizer.Encode("Orders");
		var upperArr = upper.ToArray();

		// Mixed-case input goes through word[start..end] + "##" prefix lookups; OrdinalIgnoreCase
		// on _vocab must resolve them identically to the lowercase form.
		await Assert.That(lowerArr).IsEquivalentTo(upperArr);
	}

	[Test]
	public async Task EncodePair_Lowercases_Input() {
		var tokenizer = New(16);
		var (lower, _, _) = tokenizer.EncodePair("hello", "world");
		var lowerArr = lower.ToArray();
		var (upper, _, _) = tokenizer.EncodePair("HELLO", "WORLD");
		var upperArr = upper.ToArray();

		await Assert.That(lowerArr).IsEquivalentTo(upperArr);
	}
}