using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using EventStore.Common.Configuration;
using EventStore.Common.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using NUnit.Framework;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace EventStore.Core.Tests {
	[TestFixture]
	public class Options {
		[TestCaseSource(typeof(ParseCaseData), nameof(ParseCaseData.TestCases))]
		public object are_parsed_correctly_when_well_formed(string option, string[] args) {
			var sut = new TestOptions(args);
			return typeof(TestOptions).GetProperty(option)
				.GetValue(sut);
		}

		class FakeConfigurationRoot : IConfigurationRoot {
			private readonly IConfigurationRoot _inner;
			private readonly Action<string> _onKeyRead;

			public FakeConfigurationRoot(IConfigurationRoot inner, Action<string> onKeyRead) {
				_inner = inner;
				_onKeyRead = onKeyRead;
			}

			public IEnumerable<IConfigurationSection> GetChildren() => _inner.GetChildren();

			public IChangeToken GetReloadToken() => _inner.GetReloadToken();

			public IConfigurationSection GetSection(string key) {
				_onKeyRead(key);
				return _inner.GetSection(key);
			}

			public string this[string key] {
				get => _inner[key];
				set => _inner[key] = value;
			}

			public void Reload() {
				_inner.Reload();
			}

			public IEnumerable<IConfigurationProvider> Providers => _inner.Providers;
		}

		[Test]
		public void all_keys_are_read_from_configuration() {
			string[] excluded = new[] {
				nameof(ClusterVNodeOptions.Subsystems), 
				nameof(ClusterVNodeOptions.ServerCertificate), 
				nameof(ClusterVNodeOptions.TrustedRootCertificates), 
				nameof(ClusterVNodeOptions.IndexBitnessVersion), 
				nameof(ClusterVNodeOptions.Cluster.QuorumSize),
				nameof(ClusterVNodeOptions.Database.ChunkSize), //qq do we want to exclude this
				nameof(ClusterVNodeOptions.Database.StatsStorage),
				nameof(ClusterVNodeOptions.Unknown.Options),
			};
			var actual = new List<string>();
			ClusterVNodeOptions.FromConfiguration(new FakeConfigurationRoot(
				new ConfigurationBuilder().Add(new DefaultSource(ClusterVNodeOptions.DefaultValues)).Build(),
				actual.Add));

			var expected = typeof(ClusterVNodeOptions).GetProperties().Where(x => !excluded.Contains(x.Name))
				.SelectMany(property => property.PropertyType.GetProperties().Where(x => !excluded.Contains(x.Name)))
				.Where(x=> !excluded.Contains(x.Name))
				.Select(property => property.Name);

			CollectionAssert.AreEquivalent(expected, actual);
		}

		public static IEnumerable<PrecedenceCase> PrecedenceCases() {
			yield return new(
				new Dictionary<string, object> {[nameof(ClusterVNodeOptions.Application.StatsPeriodSec)] = 1},
				string.Empty,
				new Dictionary<string, string>(),
				Array.Empty<string>(),
				1
			);
			yield return new(
				new Dictionary<string, object> {[nameof(ClusterVNodeOptions.Application.StatsPeriodSec)] = 1},
				$"{nameof(ClusterVNodeOptions.Application.StatsPeriodSec)}: 2",
				new Dictionary<string, string>(),
				Array.Empty<string>(),
				2
			);
			yield return new(
				new Dictionary<string, object> {[nameof(ClusterVNodeOptions.Application.StatsPeriodSec)] = 1},
				$"{nameof(ClusterVNodeOptions.Application.StatsPeriodSec)}: 2",
				new Dictionary<string, string> {["EVENTSTORE_STATS_PERIOD_SEC"] = "3"},
				Array.Empty<string>(),
				3
			);
			yield return new(
				new Dictionary<string, object> {[nameof(ClusterVNodeOptions.Application.StatsPeriodSec)] = 1},
				$"{nameof(ClusterVNodeOptions.Application.StatsPeriodSec)}: 2",
				new Dictionary<string, string> {["EVENTSTORE_STATS_PERIOD_SEC"] = "3"},
				new[] {"--stats-period-sec=4"},
				4
			);
		}

		public record PrecedenceCase(IEnumerable<KeyValuePair<string, object>> defaultValues,
			string configurationFileContent, IDictionary environment, string[] args, int expected) {
			public override string ToString() =>
				$"Default: {string.Join(", ", defaultValues.Select(p => $"{p.Key}={p.Value}"))}" +
				Environment.NewLine +
				$"Conf: {configurationFileContent}" +
				Environment.NewLine +
				$"Env: {string.Join(", ", environment.Keys.OfType<object>().Select(key => $"{key}={environment[key]}"))}" +
				Environment.NewLine +
				$"Args: {string.Join(" ", args)} " +
				Environment.NewLine +
				$"Expected: {expected}";
		}

		[TestCaseSource(nameof(PrecedenceCases))]
		public async Task precedence(PrecedenceCase testCase) {
			var (defaultValues, configurationFileContent, environment, args, expected) = testCase;
			var configurationFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():n}.conf"));
			try {
				await using var writer = configurationFile.CreateText();
				await writer.WriteAsync(configurationFileContent);
				await writer.FlushAsync();
				var options = ClusterVNodeOptions.FromConfiguration(new ConfigurationBuilder()
					.AddEventStore(nameof(ClusterVNodeOptions.ApplicationOptions.Config), args, environment, defaultValues.Concat(new[] {
						new KeyValuePair<string, object>(nameof(ClusterVNodeOptions.Application.Config),
							configurationFile.FullName)
					}))
					.Build());

				Assert.AreEqual(expected, options.Application.StatsPeriodSec);
			} finally {
				configurationFile.Delete();
			}
		}

		[Test]
		public void print_help_text() {
			var helpText = ClusterVNodeOptions.HelpText;
			Console.WriteLine(helpText);
		}

		[Test]
		public void dump() {
			var dumpedOptions = ClusterVNodeOptions.FromConfiguration(new[] {"--mem-db"}, new Hashtable {
				["EVENTSTORE_MAX_APPEND_SIZE"] = "10"
			}).DumpOptions();
			Console.WriteLine(dumpedOptions);
		}

		[Test]
		public async Task reading_from_disk() {
			var yamlConfiguration = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
			try {
				await WriteConfiguration();

				var options = ClusterVNodeOptions.FromConfiguration(new[] {"--config", yamlConfiguration.FullName},
					new Hashtable());

				Assert.AreEqual(yamlConfiguration.FullName, options.Application.Config);
				Assert.IsTrue(options.Database.MemDb);
				Assert.AreEqual("127.0.0.1:1113,127.0.0.1:2113",
					string.Join(",", options.Cluster.GossipSeed.Select(x => $"{x.GetHost()}:{x.GetPort()}")));
			} finally {
				yamlConfiguration.Delete();
			}

			async Task WriteConfiguration() {
				await using var stream = yamlConfiguration.Create();
				await using var writer = new StreamWriter(stream);
				await writer.WriteAsync(new StringBuilder()
					.AppendLine("---")
					.AppendLine("MemDb: true")
					.AppendLine("GossipSeed: ['127.0.0.1:1113', '127.0.0.1:2113']")
					.AppendLine("SECTION:")
					.AppendLine("  Something: Value")
				);
				await writer.FlushAsync();
			}
		}

		[Test]
		public void do_not_reveal_sensitive_information_when_dumped() {
			var optionsDumper = new OptionsDumper(new[] {typeof(TestOptions)});
			var dumpedOptions = optionsDumper
				.Dump(new ConfigurationBuilder()
					.Add(new CommandLineSource(new[] {"--sensitive=123"}))
					.Build());
			var certificatePasswordLine = dumpedOptions
				.Split(Environment.NewLine)
				.FirstOrDefault(x => x.Contains("SENSITIVE"));

			Assert.NotNull(certificatePasswordLine);
			Assert.False(certificatePasswordLine.Contains("123"));
			Assert.True(certificatePasswordLine.Contains("****"));
		}
		
		private class ParseCaseData {
			public static IEnumerable TestCases() {
				const string array = nameof(TestOptions.ArrayOfStrings);

				yield return new TestCaseData(array, new[] {"--array-of-strings", "a,b,c"})
					.Returns(new[] {"a", "b", "c"});
				yield return new TestCaseData(array, new[] {"-ArrayOfStrings", "a,b,c"})
					.Returns(new[] {"a", "b", "c"});

				const string flag = nameof(TestOptions.Flag);

				yield return new TestCaseData(flag, new[] {"--flag"}).Returns(true);
				yield return new TestCaseData(flag, new[] {"--flag=true"}).Returns(true);
				yield return new TestCaseData(flag, new[] {"--flag", "true"}).Returns(true);
				yield return new TestCaseData(flag, new[] {"-flag"}).Returns(true);
				yield return new TestCaseData(flag, new[] {"-flag=true"}).Returns(true);
				yield return new TestCaseData(flag, new[] {"-flag", "true"}).Returns(true);
				yield return new TestCaseData(flag, new[] {"-flag+"}).Returns(true);
				yield return new TestCaseData(flag, new[] {"--flag+"}).Returns(true);
				yield return new TestCaseData(flag, new[] {"--flag-"}).Returns(false);
				yield return new TestCaseData(flag, new[] {"-flag-"}).Returns(false);
				yield return new TestCaseData(flag, new[] {"--flag", "--extra-flag"}).Returns(true);
				yield return new TestCaseData(flag, new[] {"--extra-flag", "--flag"}).Returns(true);

				const string ipAddresses = nameof(TestOptions.IPEndPoints);

				yield return new TestCaseData(ipAddresses,
					new[] {"--ip-endpoints=127.0.0.1:2122,127.0.0.1:2132"}).Returns(new[] {
					new IPEndPoint(IPAddress.Loopback, 2122),
					new IPEndPoint(IPAddress.Loopback, 2132),
				});
				yield return new TestCaseData(ipAddresses,
					new[] {"--ip-endpoints", "127.0.0.1:2122,127.0.0.1:2132"}).Returns(new[] {
					new IPEndPoint(IPAddress.Loopback, 2122),
					new IPEndPoint(IPAddress.Loopback, 2132),
				});
			}
		}

		private class TestOptions {
			private readonly IConfigurationRoot _configurationRoot;

			[Description("A flag."), DefaultValue(false)]
			public bool Flag => _configurationRoot.GetValue<bool>(nameof(Flag));

			[Description("An array of strings.")]
			public string[] ArrayOfStrings =>
				_configurationRoot.GetValue<string>(nameof(ArrayOfStrings))?.Split(',') ?? Array.Empty<string>();

			[Description("An array of IP Endpoints.")]
			public IPEndPoint[] IPEndPoints => _configurationRoot.GetValue<string>(nameof(IPEndPoints))?.Split(',')
				.Select(IPEndPoint.Parse).ToArray() ?? Array.Empty<IPEndPoint>();

			[Description("A sensitive value like a password."), Sensitive]
			public string Sensitive => _configurationRoot.GetValue<string>(nameof(Sensitive));

			public TestOptions(string[] args) {
				_configurationRoot = new ConfigurationBuilder()
					.Add(new DefaultSource(new Dictionary<string, object> {
						[nameof(Flag)] = false,
						[nameof(ArrayOfStrings)] = null,
						[nameof(IPEndPoints)] = null,
						[nameof(Sensitive)] = null
					}))
					.Add(new CommandLineSource(args))
					.Build();
			}
		}
	}
}
