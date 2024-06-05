using System.Runtime.CompilerServices;

namespace EventStore.Testing.Fixtures;

public partial class EventStoreFixture {
	public string GenerateShortId() => Guid.NewGuid().ToString()[30..];
	
	public async Task RestartService(TimeSpan delay) {
		await Service.Restart(delay);
		Logger.Information("service restarted");
	}
	
	public string NewStreamId([CallerMemberName] string? name = null) => 
		$"{name.Underscore()}-{GenerateShortId()}".ToLowerInvariant();

	public string NewProcessorName(string? prefix = null) =>
		prefix is null ? $"{GenerateShortId()}-prx" : $"{prefix.Underscore()}-{GenerateShortId()}-prx";

	public string NewInputTopicName(string processorName)  => $"{processorName}.input.tst".ToLowerInvariant();
	public string NewOutputTopicName(string processorName) => $"{processorName}.output.tst".ToLowerInvariant();
	
}