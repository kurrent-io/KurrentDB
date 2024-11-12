using EventStore.Streaming.Consumers;
using EventStore.Toolkit.Testing.Fixtures;
using EventStore.Toolkit.Testing.Xunit;
using Shouldly;
using static EventStore.Connectors.ConnectorsFeatureConventions;

namespace EventStore.Connectors.Tests;

[Trait("Category", "ConnectorsFeatureConventions")]
public class ConnectorsFeatureConventionsTests : FastFixture  {
    [Theory, ValidFilterPatternTestCases]
    public void valid_filters_should_match(ConsumeFilter filter, string input) =>
        filter.RegEx.IsMatch(input).ShouldBeTrue();

    class ValidFilterPatternTestCases : TestCaseGenerator<ValidFilterPatternTestCases> {
        protected override IEnumerable<object[]> Data() {
            yield return [Filters.ManagementFilter, "$connectors/123"];
            yield return [Filters.ManagementFilter, "$connectors/abc"];
            yield return [Filters.ManagementFilter, "$connectors/$connectors"];
            yield return [Filters.ManagementFilter, "$connectors/^connectors$"];
            yield return [Filters.ManagementFilter, "$connectors/logger-sink-123"];

            yield return [Filters.CheckpointsFilter, "$connectors/logger-sink-123/checkpoints"];
            yield return [Filters.CheckpointsFilter, "$connectors/abc/checkpoints"];
            yield return [Filters.CheckpointsFilter, "$connectors/checkpoints/checkpoints"];
            yield return [Filters.CheckpointsFilter, "$connectors/$connectors/checkpoints"];

            yield return [Filters.LifecycleFilter, "$connectors/logger-sink/lifecycle"];
            yield return [Filters.LifecycleFilter, "$connectors/abc/lifecycle"];
            yield return [Filters.LifecycleFilter, "$connectors/lifecycle/lifecycle"];
            yield return [Filters.LifecycleFilter, "$connectors/$connectors/lifecycle"];
        }
    }
}