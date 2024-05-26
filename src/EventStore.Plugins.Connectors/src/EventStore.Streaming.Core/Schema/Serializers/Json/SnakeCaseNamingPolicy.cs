// ReSharper disable CheckNamespace

using System.Text.RegularExpressions;
using static System.String;

namespace System.Text.Json;

public partial class SnakeCaseNamingPolicy : JsonNamingPolicy {
	static readonly Regex Pattern = SnakeCaseNamingRegEx();

	public override string ConvertName(string name)
		=> !IsNullOrWhiteSpace(name)
			? Pattern.Replace(name, "_$1").Trim().ToLowerInvariant()
			: name;

	[GeneratedRegex("(?<!^)([A-Z][a-z]|(?<=[a-z])[A-Z])", RegexOptions.Compiled)]
	private static partial Regex SnakeCaseNamingRegEx();
}