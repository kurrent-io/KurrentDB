using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using static System.String;

namespace EventStore.Streaming.Consumers;

public enum ConsumeFilterScope {
	Unspecified = 0,
	Stream      = 1,
	Record      = 2
}

[PublicAPI]
public partial record ConsumeFilter {
	public static readonly ConsumeFilter None = new(ConsumeFilterScope.Unspecified);
	
	public ConsumeFilter(ConsumeFilterScope scope, params string[] prefixes) {
		Scope             = scope;
		Prefixes          = prefixes;
		RegularExpression = new Regex(CreateRegexPattern(prefixes), RegexOptions.Compiled);
	}

	public ConsumeFilter(ConsumeFilterScope scope, Regex regularExpression) {
		Scope             = scope;
		RegularExpression = regularExpression;
		Prefixes          = [];
	}

	public ConsumeFilter(ConsumeFilterScope scope, string regularExpressionPattern) {
		Scope             = scope;
		RegularExpression = new Regex(regularExpressionPattern);
		Prefixes          = [];
	}

	public ConsumeFilterScope Scope             { get; }
	public string[]           Prefixes          { get; }
	public Regex              RegularExpression { get; }
	
	public bool IsStreamFilter => Scope == ConsumeFilterScope.Stream;
	public bool IsRecordFilter => Scope == ConsumeFilterScope.Record;
	public bool HasPrefixes    => Prefixes.Length != 0;
	
	internal static string CreateRegexPattern(string[] prefixes) {
		// Escape special characters in the prefixes and join them with '|'
		var pattern = Join("|", prefixes.Select(Regex.Escape));

		// Add '^' at the start to match the start of the string
		return $"^({pattern})";
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsMatch(ReadOnlySpan<char> value) => RegularExpression.IsMatch(value);

	public override string ToString() => $"{Scope} :: {RegularExpression}";

	public static ConsumeFilter FromPrefixes(ConsumeFilterScope scope, params string[] prefixes) =>
		new ConsumeFilter(scope, prefixes);

	public static ConsumeFilter FromRegex(ConsumeFilterScope scope, Regex regularExpression) =>
		new ConsumeFilter(scope, regularExpression);
	
	public static ConsumeFilter From(ConsumeFilterScope scope, string? regularExpression, params string[] prefixes) =>
		regularExpression is not null 
			? FromRegex(scope, new Regex(regularExpression, RegexOptions.Compiled)) 
			: FromPrefixes(scope, prefixes);
    
    public static ConsumeFilter ExcludeSystemEvents() => ExcludeSystemEventsFilter;
	
	public static ConsumeFilter Streams(params string[] streams) =>
		new ConsumeFilter(ConsumeFilterScope.Stream, streams);
	
	public static ConsumeFilter RecordTypes(params string[] names) =>
		new ConsumeFilter(ConsumeFilterScope.Record, names);

    static readonly ConsumeFilter ExcludeSystemEventsFilter =
        new ConsumeFilter(ConsumeFilterScope.Record, GetSystemEventsRegEx());
    
    [GeneratedRegex(@"^\$.*", RegexOptions.Compiled)]
    private static partial Regex GetSystemEventsRegEx();
}
