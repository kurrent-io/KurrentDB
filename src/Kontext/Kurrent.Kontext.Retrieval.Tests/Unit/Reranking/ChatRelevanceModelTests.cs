// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Reranking;

[Category("Reranking")]
public class ChatRelevanceModelTests {
	[Test]
	public async ValueTask bare_number_reply_becomes_the_score() {
		var scores = await ChatRelevanceModel.Create(options => options.Chat = new FakeChatClient("0.85")).ScoreAsync("query", ["passage"]);

		await Assert.That(scores[0]).IsEqualTo(0.85).Within(1e-12);
	}

	[Test]
	public async ValueTask number_wrapped_in_prose_is_still_read() {
		var scores = await ChatRelevanceModel.Create(options => options.Chat = new FakeChatClient("The score is 0.72.")).ScoreAsync("query", ["passage"]);

		await Assert.That(scores[0]).IsEqualTo(0.72).Within(1e-12);
	}

	[Test]
	public async ValueTask out_of_range_replies_clamp_to_the_unit_interval() {
		var chat = new FakeChatClient(prompt => prompt.Contains("alpha") ? "2.0" : "-0.3");

		var scores = await ChatRelevanceModel.Create(options => options.Chat = chat).ScoreAsync("query", ["alpha", "beta"]);

		await Assert.That(scores[0]).IsEqualTo(1.0);
		await Assert.That(scores[1]).IsEqualTo(0.0);
	}

	[Test]
	public async ValueTask reply_carrying_no_number_falls_back_to_the_neutral_score() {
		var chat = new FakeChatClient(prompt => prompt.Contains("alpha") ? "I cannot say." : "");

		var scores = await ChatRelevanceModel.Create(options => options.Chat = chat).ScoreAsync("query", ["alpha", "beta"]);

		// a missing judgment is neutral, never 0: a 0 would read as "irrelevant" and drop the passage
		// under the reranker's min-score cut on nothing but a chatty or empty answer
		await Assert.That(scores[0]).IsEqualTo(0.5);
		await Assert.That(scores[1]).IsEqualTo(0.5);
	}

	[Test]
	public async ValueTask neutral_fallback_honours_the_configured_score() {
		var model = ChatRelevanceModel.Create(options => {
			options.Chat         = new FakeChatClient("no idea");
			options.NeutralScore = 0.25;
		});

		var scores = await model.ScoreAsync("query", ["passage"]);

		await Assert.That(scores[0]).IsEqualTo(0.25);
	}

	[Test]
	public async ValueTask first_number_in_the_reply_wins() {
		var scores = await ChatRelevanceModel.Create(options => options.Chat = new FakeChatClient("Confidence 100%. Relevance: 0.1")).ScoreAsync("query", ["passage"]);

		// the regex takes the first match, so 100 is what gets parsed and clamped; the labelled 0.1 is
		// never reached
		await Assert.That(scores[0]).IsEqualTo(1.0);
	}

	[Test]
	public async ValueTask temperature_and_output_cap_reach_the_chat_client() {
		var chat  = new FakeChatClient("0.5");
		var model = ChatRelevanceModel.Create(options => {
			options.Chat            = chat;
			options.MaxOutputTokens = 4;
		});

		await model.ScoreAsync("query", ["passage"]);

		await Assert.That(chat.LastOptions!.Temperature).IsEqualTo(0f);
		await Assert.That(chat.LastOptions!.MaxOutputTokens).IsEqualTo(4);
	}

	[Test]
	public async ValueTask one_call_per_passage_and_scores_land_in_input_order() {
		var chat = new FakeChatClient(prompt =>
			prompt.Contains("alpha") ? "0.10" :
			prompt.Contains("beta") ? "0.90" :
			"0.40"
		);

		var scores = await ChatRelevanceModel.Create(options => options.Chat = chat).ScoreAsync("query", ["alpha", "beta", "gamma"]);

		// the replies key off the passage, so a score landing on the wrong index shows up here
		await Assert.That(chat.Calls).IsEqualTo(3);
		await Assert.That(scores[0]).IsEqualTo(0.10).Within(1e-12);
		await Assert.That(scores[1]).IsEqualTo(0.90).Within(1e-12);
		await Assert.That(scores[2]).IsEqualTo(0.40).Within(1e-12);
	}

	[Test]
	public async ValueTask custom_prompt_builder_is_what_the_client_sees() {
		var chat  = new FakeChatClient("0.5");
		var model = ChatRelevanceModel.Create(options => {
			options.Chat        = chat;
			options.BuildPrompt = (query, passage) => $"judge {query} :: {passage}";
		});

		await model.ScoreAsync("latency", ["alpha"]);

		await Assert.That(chat.Prompts[0]).IsEqualTo("judge latency :: alpha");
	}

	[Test]
	public async ValueTask failing_chat_client_surfaces_its_own_failure_and_frees_the_gate() {
		var chat  = new FakeChatClient(_ => throw new TimeoutException("The judge is down."));
		var model = ChatRelevanceModel.Create(options => {
			options.Chat           = chat;
			options.MaxConcurrency = 2;
		});

		IReadOnlyList<string> passages = Enumerable.Range(0, 12).Select(index => $"passage {index}").ToList();

		// six times MaxConcurrency, so most passages queue on the gate: the client's own exception has to
		// come out unwrapped, and a release lost on the failure path would hang here instead of throwing
		await Assert.That(async () => await model.ScoreAsync("query", passages)).Throws<TimeoutException>();
		await Assert.That(chat.Calls).IsEqualTo(12);
	}
}
