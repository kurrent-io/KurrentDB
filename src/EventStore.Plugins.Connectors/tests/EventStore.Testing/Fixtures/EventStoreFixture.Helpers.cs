// using System.Diagnostics.CodeAnalysis;
// using System.Runtime.CompilerServices;
//
// namespace EventStore.Testing.Fixtures;
//
// [SuppressMessage("Performance", "CA1822:Mark members as static")]
// public partial class EventStoreFixture {
//     public string NewStreamId([CallerMemberName] string? name = null) =>
//         $"{name.Underscore()}-{Identifiers.GenerateShortId()}".ToLowerInvariant();
//
//     public string NewInputTopicName(string serviceName) =>
//         $"{serviceName}.input.tst".ToLowerInvariant();
//
//     public string NewOutputTopicName(string serviceName) =>
//         $"{serviceName}.output.tst".ToLowerInvariant();
// }