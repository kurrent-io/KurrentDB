// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Embeddings.Tests;

/// <summary>
/// GLiNER zero-shot NER over gliner_small-v2.1 (int8). The labels are free strings riding in the
/// prompt, so the capability under test is span extraction against labels the model never trained
/// a head for — plus the decoder's contracts: exact character offsets, threshold as a floor, and
/// flat (non-overlapping, appearance-ordered) output.
/// </summary>
[Category("Integration")]
public class GlinerOnnxEntityRecognizerTests {
	static GlinerOnnxEntityRecognizer _recognizer = null!;

	[Before(Class)]
	public static Task Setup(ClassHookContext context) {
		if (!File.Exists(EmbeddingsTestSupport.GlinerModelPath))
			throw new FileNotFoundException(
				$"GLiNER model not found at {EmbeddingsTestSupport.GlinerModelPath}. Download onnx/model_quantized.onnx " +
				"and spm.model from https://huggingface.co/onnx-community/gliner_small-v2.1 into that layout — " +
				"these tests point at the playground's models/ cache.", EmbeddingsTestSupport.GlinerModelPath);

		_recognizer = new GlinerOnnxEntityRecognizer(
			OnnxModel.FromFiles("gliner-small-v2.1", EmbeddingsTestSupport.GlinerModelPath,
			new Dictionary<string, string> { ["spm.model"] = EmbeddingsTestSupport.GlinerSentencePiecePath }));
		return Task.CompletedTask;
	}

	[After(Class)]
	public static Task Teardown(ClassHookContext context) {
		_recognizer?.Dispose();
		return Task.CompletedTask;
	}

	[Test]
	public async ValueTask recognizes_entities_zero_shot() {
		// The point of GLiNER: "person" and "location" are prompt strings, not trained heads.
		const string Text = "My name is James Bond and I live in London.";

		// Act
		var spans = _recognizer.Recognize(Text, ["person", "location"]);

		// Assert
		await Assert.That(spans.Select(s => (s.Text, s.Label)))
			.Contains(("James Bond", "person"));
		await Assert.That(spans.Select(s => (s.Text, s.Label)))
			.Contains(("London", "location"));
	}

	[Test]
	public async ValueTask offsets_slice_the_exact_surface_form_from_the_source() {
		const string Text = "Marie Curie moved from Warsaw to Paris in 1891.";

		// Act
		var spans = _recognizer.Recognize(Text, ["person", "location", "date"]);

		// Assert
		await Assert.That(spans).IsNotEmpty();
		foreach (var span in spans)
			await Assert.That(Text[span.Start..span.End]).IsEqualTo(span.Text);
	}

	[Test]
	public async ValueTask spans_come_back_non_overlapping_in_appearance_order_scoring_above_threshold() {
		const string Text = "Tim Cook, the CEO of Apple, met Satya Nadella of Microsoft in Cupertino.";

		// Act
		var spans = _recognizer.Recognize(Text, ["person", "organization", "location"]);

		// Assert
		await Assert.That(spans).IsNotEmpty();
		for (var i = 0; i < spans.Count; i++) {
			await Assert.That(spans[i].Score).IsGreaterThanOrEqualTo(0.5);
			if (i > 0)
				await Assert.That(spans[i].Start).IsGreaterThanOrEqualTo(spans[i - 1].End);
		}
	}

	[Test]
	public async ValueTask returns_nothing_for_an_empty_label_set() {
		// Act
		var spans = _recognizer.Recognize("James Bond lives in London.", []);

		// Assert
		await Assert.That(spans).IsEmpty();
	}

	[Test]
	public async ValueTask a_raised_threshold_only_removes_spans() {
		const string Text = "Albert Einstein was born in Ulm and later worked in Bern.";
		string[] labels = ["person", "location"];

		using var strict = new GlinerOnnxEntityRecognizer(
			OnnxModel.FromFiles("gliner-small-v2.1", EmbeddingsTestSupport.GlinerModelPath,
			new Dictionary<string, string> { ["spm.model"] = EmbeddingsTestSupport.GlinerSentencePiecePath }),
			new GlinerOnnxOptions { Threshold = 0.9 });

		// Act
		var relaxed = _recognizer.Recognize(Text, labels);
		var kept = strict.Recognize(Text, labels);

		// Assert
		await Assert.That(kept.Count).IsLessThanOrEqualTo(relaxed.Count);
		foreach (var span in kept) {
			await Assert.That(span.Score).IsGreaterThanOrEqualTo(0.9);
			await Assert.That(relaxed).Contains(span);
		}
	}

	[Test]
	public async ValueTask drops_trailing_words_beyond_the_token_budget_instead_of_throwing() {
		// The label prompt always rides in full; a text far past MaxTokens loses its tail, so the
		// leading entity survives and the trailing one is never scored.
		var text = "James Bond said " + string.Concat(Enumerable.Repeat("nothing at all and then ", 200)) + "left London.";

		// Act
		var spans = _recognizer.Recognize(text, ["person", "location"]);

		// Assert
		await Assert.That(spans.Select(s => (s.Text, s.Label)))
			.Contains(("James Bond", "person"));
		await Assert.That(spans.Select(s => s.Text)).DoesNotContain("London");
	}
}
