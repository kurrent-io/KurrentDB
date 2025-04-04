using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace EventStore.Common.Configuration {
	public class CommandLineSource : IConfigurationSource {
		private readonly IEnumerable<string> _args;

		public CommandLineSource(string[] args) {
			_args = args.Select(NormalizeKeys).Select(NormalizeBooleans);

			static string NormalizeKeys(string x) => x[0] == '-' && x[1] != '-' ? $"-{x}" : x;

			string NormalizeBooleans(string x, int i) {
				if (!x.StartsWith("--"))
					return x;

				if (x.EndsWith('+'))
					return $"{x[..^1]}=true";

				if (x.EndsWith('-'))
					return $"{x[..^1]}=false";

				if (x.Contains('='))
					return x;

				if (i != args.Length - 1 && !args[i + 1].StartsWith("--"))
					return x;

				return $"{x}=true";
			}
		}

		public IConfigurationProvider Build(IConfigurationBuilder builder) => new CommandLine(_args);
	}
}
