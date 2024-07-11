using System.Runtime.CompilerServices;

namespace EventStore.Toolkit.Testing.FluentAssertions;

static class FluentAssertionsInitializer {
	[ModuleInitializer]
	public static void Initialize() =>
		AssertionOptions.AssertEquivalencyUsing(
			options => options
				.Using<ReadOnlyMemory<byte>>(ctx => ctx.Subject.Span.SequenceEqual(ctx.Expectation.Span).Should().BeTrue(ctx.Because, ctx.BecauseArgs))
				.WhenTypeIs<ReadOnlyMemory<byte>>()
				.Using<Memory<byte>>(ctx => ctx.Subject.Span.SequenceEqual(ctx.Expectation.Span).Should().BeTrue(ctx.Because, ctx.BecauseArgs))
				.WhenTypeIs<Memory<byte>>()
		);
}