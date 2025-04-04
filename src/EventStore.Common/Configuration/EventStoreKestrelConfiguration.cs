using System.IO;
using System.Linq;
using EventStore.Common.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace EventStore.Common.Configuration {
	public static class EventStoreKestrelConfiguration {
		public static IConfiguration GetConfiguration(string kestrelConfig = "kestrelsettings.json") {
			var potentialKestrelConfigurationDirectories = Locations.GetPotentialConfigurationDirectories();
			var kestrelConfigurationDirectory =
				potentialKestrelConfigurationDirectories.FirstOrDefault(directory =>
					File.Exists(Path.Combine(directory, kestrelConfig)));
			if (kestrelConfigurationDirectory is null)
				return new ConfigurationBuilder().Build();

			return new ConfigurationBuilder()
				.AddJsonFile(config => {
					config.Optional = true;
					config.FileProvider = new PhysicalFileProvider(kestrelConfigurationDirectory) {
						UseActivePolling = true,
						UsePollingFileWatcher = true
					};
					config.OnLoadException = context => Serilog.Log.Error(context.Exception, "err");
					config.Path = kestrelConfig;
					config.ReloadOnChange = true;
				})
				.Build()
				.GetSection("Kestrel");
		}
	}
}
