// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Memory;
using MemoryContracts = Kurrent.Kontext.Contracts.Memory;

namespace Kurrent.Kontext.Tests;

/// <summary>
/// The retain contract's mechanically-checkable rules. Each one exists because the contract states a
/// guarantee that would otherwise be documentation only.
/// </summary>
public class RetainRequestValidatorTests {
	static readonly RetainRequestValidator Validator = new();

	static MemoryContracts.RetainRequest Request(params MemoryContracts.Memory[] memories) {
		var request = new MemoryContracts.RetainRequest();
		request.Memories.AddRange(memories);
		return request;
	}

	static MemoryContracts.Memory Memory(MemoryContracts.MemoryType type = MemoryContracts.MemoryType.Fact) =>
		new() { MemoryType = type, Content = "a memory that stands on its own" };

	static MemoryContracts.Evidence WebCitation(params string[] excerpts) {
		var web = new MemoryContracts.Evidence.Types.WebRef { Uri = "https://example.test/spec" };
		web.Excerpts.AddRange(excerpts);
		return new() { Web = web };
	}

	static string Passage(int length) => new('x', length);

	[Test]
	public async ValueTask accepts_a_memory_with_no_evidence() {
		// Arrange
		var request = Request(Memory());

		// Act
		var result = Validator.Validate(request);

		// Assert — the common case: most memories cite nothing, because a check you ran yourself is not a
		// citable source. Evidence buys auditability, never rank.
		await Assert.That(result.IsValid).IsTrue();
	}

	[Test]
	public async ValueTask rejects_an_empty_batch() {
		// Arrange
		var request = Request();

		// Act
		var result = Validator.Validate(request);

		// Assert
		await Assert.That(result.IsValid).IsFalse();
	}

	#region ->> Web citation excerpts <<-

	[Test]
	public async ValueTask rejects_a_web_citation_with_no_excerpt() {
		// Arrange — without the quoted passage the citation is a bookmark, and it dies with the page.
		var memory = Memory(MemoryContracts.MemoryType.Fact);
		memory.Evidence.Add(WebCitation());

		var request = Request(memory);

		// Act
		var result = Validator.Validate(request);

		// Assert
		await Assert.That(result.IsValid).IsFalse();
	}

	[Test]
	public async ValueTask rejects_a_web_excerpt_below_the_floor() {
		// Arrange — a degenerate excerpt ("yes") satisfies "at least one" while carrying no evidence.
		var memory = Memory(MemoryContracts.MemoryType.Fact);
		memory.Evidence.Add(WebCitation(Passage(19)));

		var request = Request(memory);

		// Act
		var result = Validator.Validate(request);

		// Assert
		await Assert.That(result.IsValid).IsFalse();
	}

	[Test]
	public async ValueTask rejects_a_web_excerpt_above_the_ceiling() {
		// Arrange — past the ceiling it stops being a passage and becomes a copy of the source.
		var memory = Memory(MemoryContracts.MemoryType.Fact);
		memory.Evidence.Add(WebCitation(Passage(1001)));

		var request = Request(memory);

		// Act
		var result = Validator.Validate(request);

		// Assert
		await Assert.That(result.IsValid).IsFalse();
	}

	[Test]
	public async ValueTask rejects_more_web_excerpts_than_the_cap() {
		// Arrange — six passages from one page is a summary of it, which is a memory of its own.
		var memory   = Memory(MemoryContracts.MemoryType.Fact);
		var excerpts = Enumerable.Repeat(Passage(50), 6).ToArray();
		memory.Evidence.Add(WebCitation(excerpts));

		var request = Request(memory);

		// Act
		var result = Validator.Validate(request);

		// Assert
		await Assert.That(result.IsValid).IsFalse();
	}

	[Test]
	public async ValueTask accepts_web_excerpts_at_both_bounds() {
		// Arrange — the inclusive edges: 20 and 1000 characters, five of them.
		var memory   = Memory(MemoryContracts.MemoryType.Fact);
		var excerpts = new[] { Passage(20), Passage(1000), Passage(50), Passage(50), Passage(50) };
		memory.Evidence.Add(WebCitation(excerpts));

		var request = Request(memory);

		// Act
		var result = Validator.Validate(request);

		// Assert
		await Assert.That(result.IsValid).IsTrue();
	}

	#endregion

	#region ->> Git citation excerpt <<-

	[Test]
	public async ValueTask accepts_a_git_citation_with_no_excerpt() {
		// Arrange — optional: our own history is immutable, so the commit already anchors it.
		var memory = Memory(MemoryContracts.MemoryType.Fact);
		memory.Evidence.Add(new MemoryContracts.Evidence {
			Git = new() { Commit = "c93c6ae82", Path = "src/x.cs", Symbol = "Foo.Bar" }
		});

		var request = Request(memory);

		// Act
		var result = Validator.Validate(request);

		// Assert
		await Assert.That(result.IsValid).IsTrue();
	}

	[Test]
	public async ValueTask rejects_a_memory_citation_that_is_not_a_uuid() {
		// Arrange — the server mints every id as a UUID, so anything else was invented by the caller.
		var memory = Memory();
		memory.Evidence.Add(new MemoryContracts.Evidence { Memory = new() { Id = "the-devex-lead-memory" } });

		var request = Request(memory);

		// Act
		var result = Validator.Validate(request);

		// Assert
		await Assert.That(result.IsValid).IsFalse();
	}

	[Test]
	public async ValueTask accepts_a_memory_citation_carrying_a_uuid() {
		// Arrange
		var memory = Memory();
		memory.Evidence.Add(new MemoryContracts.Evidence { Memory = new() { Id = Guid.NewGuid().ToString() } });

		var request = Request(memory);

		// Act
		var result = Validator.Validate(request);

		// Assert
		await Assert.That(result.IsValid).IsTrue();
	}

	[Test]
	public async ValueTask rejects_a_git_excerpt_below_the_floor() {
		// Arrange
		var memory = Memory(MemoryContracts.MemoryType.Fact);
		memory.Evidence.Add(new MemoryContracts.Evidence {
			Git = new() { Commit = "c93c6ae82", Excerpt = Passage(19) }
		});

		var request = Request(memory);

		// Act
		var result = Validator.Validate(request);

		// Assert
		await Assert.That(result.IsValid).IsFalse();
	}

	#endregion

	#region ->> Supersedes <<-

	static MemoryContracts.Memory Superseding(params string[] ids) {
		var memory = Memory();
		memory.Supersedes.AddRange(ids);
		return memory;
	}

	[Test]
	public async ValueTask accepts_a_memory_that_supersedes_one_id() {
		// Arrange
		var request = Request(Superseding(Guid.NewGuid().ToString()));

		// Act
		var result = Validator.Validate(request);

		// Assert
		await Assert.That(result.IsValid).IsTrue();
	}

	[Test]
	public async ValueTask rejects_a_supersedes_that_is_not_a_uuid() {
		// Arrange — the server mints every id as a UUID, so anything else was invented by the caller.
		var request = Request(Superseding("the-devex-lead-memory"));

		// Act
		var result = Validator.Validate(request);

		// Assert
		await Assert.That(result.IsValid).IsFalse();
	}

	[Test]
	public async ValueTask rejects_the_same_target_named_twice_by_one_memory() {
		// Arrange
		var target  = Guid.NewGuid().ToString();
		var request = Request(Superseding(target, target));

		// Act
		var result = Validator.Validate(request);

		// Assert — superseded_by holds ONE successor, so a target can be claimed only once.
		await Assert.That(result.IsValid).IsFalse();
	}

	[Test]
	public async ValueTask rejects_the_same_target_claimed_by_two_memories_in_one_batch() {
		// Arrange — the scope the store cannot see: the batch's own effects are not in it yet.
		var target  = Guid.NewGuid().ToString();
		var request = Request(Superseding(target), Superseding(target));

		// Act
		var result = Validator.Validate(request);

		// Assert — unchecked, the writer folds both into one row state and the last one silently
		// wins, picking a successor nobody chose.
		await Assert.That(result.IsValid).IsFalse();
	}

	#endregion
}
