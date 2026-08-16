// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Globalization;

namespace Kurrent.Quack;

/// <summary>
/// Configures a <see cref="DuckDBDataSource"/>. Unset properties leave the engine or
/// data-source defaults untouched.
/// </summary>
/// <remarks>
/// <para>
/// Everything declared here except extension installation and start-up options is <em>replayed
/// on every connection the data source creates</em>. Attachments, loaded extensions and settings
/// live in the database instance, and the instance dies with the last open connection — applying
/// them once would silently lose them when the pool drains. Replay makes them hold for the data
/// source's whole lifetime.
/// </para>
/// <para>
/// Extension installation runs once per data source, on the first connection: it writes to the
/// machine-wide extension cache and may download from the network, which does not belong on the
/// steady-state connection path. Start-up options travel in the connection string and apply when
/// connections open.
/// </para>
/// </remarks>
public sealed class DuckDBDataSourceOptions {
	/// <summary>
	/// The connection string. Required.
	/// </summary>
	public string ConnectionString { get; set; } = "";

	/// <summary>
	/// The maximum number of idle connections retained for reuse. The underlying pool may round
	/// the requested bound up.
	/// </summary>
	public int MaxIdleConnections { get; set; } = 32;

	/// <summary>
	/// How the database is opened. A start-up option, applied through the connection string.
	/// </summary>
	public DuckDBAccessMode? AccessMode { get; set; }

	/// <summary>
	/// The maximum memory of the database instance, such as <c>2GB</c> or <c>512MB</c>. Applied
	/// on every connection.
	/// </summary>
	public string? MemoryLimit {
		get => Settings.GetValueOrDefault("memory_limit");
		set {
			if (string.IsNullOrEmpty(value))
				Settings.Remove("memory_limit");
			else
				Settings["memory_limit"] = value; // indexer: reassignment overwrites, Add would throw
		}
	}

	/// <summary>
	/// The number of threads the database instance uses. A typed view over
	/// <c>Settings["threads"]</c>; assigning null removes the entry.
	/// </summary>
	public int? Threads {
		get => Settings.TryGetValue("threads", out var value) && int.TryParse(value, out var threads) ? threads : null;
		set {
			if (value is { } threads)
				Settings["threads"] = threads.ToString();
			else
				Settings.Remove("threads");
		}
	}

	/// <summary>
	/// Arbitrary DuckDB settings, applied on every connection. The engine casts the string value
	/// to the option's own type: <c>"2"</c> for <c>threads</c>, <c>"true"</c> for booleans.
	/// </summary>
	/// <remarks>
	/// Each setting is applied as a plain <c>SET</c>, so it lands in the option's own scope:
	/// global options configure the database instance, session-only options such as
	/// <c>enable_progress_bar</c> configure each connection. Names are validated against the
	/// engine's own configuration catalog by <see cref="EnsureValid"/> — a typo fails at
	/// construction, not at first use.
	/// </remarks>
	public Dictionary<string, string> Settings { get; set; } = [];

	/// <summary>
	/// The databases to attach on every connection. <see cref="AttachDatabase"/> is the
	/// chainable way to add one.
	/// </summary>
	public List<DuckDBAttachedDatabase> AttachedDatabases { get; set; } = [];

	/// <summary>
	/// The callback run on every connection the data source creates, after the settings.
	/// <see cref="UseInitializer"/> is the chainable way to add one; multiple callbacks run in
	/// registration order.
	/// </summary>
	public Action<DuckDBAdvancedConnection>? Initializer { get; set; }

	/// <summary>
	/// The extensions every connection needs, created through the <see cref="DuckDBExtension"/>
	/// factory methods. <see cref="Extensions"/> offers the chainable helpers.
	/// </summary>
	public List<DuckDBExtension> RequiredExtensions { get; set; } = [];

	/// <summary>
	/// Allows loading unsigned extensions. A start-up option, applied through the connection
	/// string when connections open — the engine refuses to change it at runtime. Only load
	/// unsigned extensions from sources you trust.
	/// </summary>
	public bool AllowUnsignedExtensions { get; set; }

	/// <summary>
	/// Locks the configuration after the settings and initializers have run, so no later
	/// <c>SET</c> can alter it.
	/// </summary>
	/// <remarks>
	/// The lock is emitted last, after <see cref="UseInitializer"/> callbacks. A locked instance
	/// rejects every <c>SET</c>, global and session alike, so on connections joining an
	/// already-locked instance the replay skips setting statements — they are by definition the
	/// values this data source locked in — while extension loads and attachments still replay.
	/// </remarks>
	public bool LockConfiguration { get; set; }

    /// <summary>
    /// Configures extension-related options through a grouped view that writes directly into
    /// these options — the settings land in <see cref="Settings"/>.
    /// </summary>
    /// <param name="configure">The configuration callback.</param>
    /// <returns>The options.</returns>
    public DuckDBDataSourceOptions Extensions(Action<DuckDBExtensionsOptions> configure) {
        configure(new(this));
        return this;
    }

    /// <summary>
    /// Configures DuckDB's logging through a grouped view that writes directly into
    /// <see cref="Settings"/>. Calling this turns logging on unless the callback sets
    /// <see cref="DuckDBLoggingOptions.Enabled"/> to <see langword="false"/>.
    /// </summary>
    /// <param name="configure">The configuration callback.</param>
    /// <returns>The options.</returns>
    public DuckDBDataSourceOptions Logging(Action<DuckDBLoggingOptions> configure) {
        var logging = new DuckDBLoggingOptions(this) { Enabled = true };
        configure(logging);
        return this;
    }

	/// <summary>
	/// Sets <see cref="ConnectionString"/> to the database file.
	/// </summary>
	/// <param name="file">The database file path.</param>
	/// <returns>The options.</returns>
	public DuckDBDataSourceOptions ConnectToFile(string file) {
		// An empty file maps to an empty connection string, which construction rejects as missing.
		ConnectionString = string.IsNullOrEmpty(file) ? "" : $"Data Source={file}";
		return this;
	}

	/// <summary>
	/// Sets <see cref="ConnectionString"/> to an in-memory database.
	/// </summary>
	/// <remarks>
	/// The data source already shares one in-memory database between all of its own connections.
	/// <paramref name="shared"/> widens that to the whole process: every connection to the shared
	/// in-memory data source in this process, from any pool, reaches the same database.
	/// </remarks>
	/// <param name="shared">Whether the database is shared process-wide.</param>
	/// <returns>The options.</returns>
	public DuckDBDataSourceOptions ConnectToMemory(bool shared = false) {
		ConnectionString = $"Data Source={(shared ? InMemorySharedDataSource : InMemoryDataSource)}";
		return this;
	}

	/// <summary>
	/// Attaches the database on every connection, under the given alias. The callback composes
	/// the generic ATTACH options list — core keys and extension keys alike. Validated by
	/// <see cref="EnsureValid"/>.
	/// </summary>
	/// <param name="path">The database to attach, including any storage-extension prefix such as <c>lance:</c>.</param>
	/// <param name="alias">The catalog name the attached database is addressed by.</param>
	/// <param name="configure">Composes the ATTACH options list. Null attaches with none.</param>
	/// <returns>The options.</returns>
	public DuckDBDataSourceOptions AttachDatabase(string path, string alias, Action<DuckDBAttachOptions>? configure = null) {
		DuckDBAttachOptions? attachOptions = null;
        configure?.Invoke(attachOptions = new());
        AttachedDatabases.Add(new(path, alias, attachOptions));
		return this;
	}

	/// <summary>
	/// Attaches the database on every connection, with the ATTACH options given as one raw
	/// fragment, rendered verbatim — see <see cref="DuckDBAttachOptions.Raw"/>.
	/// </summary>
	/// <param name="path">The database to attach, including any storage-extension prefix such as <c>lance:</c>.</param>
	/// <param name="alias">The catalog name the attached database is addressed by.</param>
	/// <param name="rawOptions">The fragment, e.g. <c>TYPE LANCE, ENDPOINT 'http://…'</c>.</param>
	/// <returns>The options.</returns>
	/// <exception cref="ArgumentException"><paramref name="rawOptions"/> is empty.</exception>
	public DuckDBDataSourceOptions AttachDatabase(string path, string alias, string rawOptions) =>
		AttachDatabase(path, alias, attach => attach.Raw(rawOptions));

	/// <summary>
	/// Runs the callback on every connection the data source creates, after the settings.
	/// </summary>
	/// <param name="initializer">The callback. Multiple callbacks run in registration order.</param>
	/// <returns>The options.</returns>
	public DuckDBDataSourceOptions UseInitializer(Action<DuckDBAdvancedConnection> initializer) {
		Initializer += initializer;
		return this;
	}
    
	// The two in-memory data sources, mirroring the driver's own constants.
	internal const string InMemoryDataSource       = ":memory:";
	internal const string InMemorySharedDataSource = ":memory:?cache=shared";

	internal static string EscapeLiteral(string value) => value.Replace("'", "''");

	internal static void ValidateIdentifier(string value, string paramName) {
		if (!IsIdentifier(value))
			throw new ArgumentException(
				"Expected a bare identifier: ASCII letters, digits or underscores, not starting with a digit.",
				paramName);
	}

	// Deliberately stricter than DuckDB's quoted-identifier rules: names inlined into SQL never
	// need quoting, so anything that would is rejected instead of escaped.
	internal static bool IsIdentifier(string value) {
		if (string.IsNullOrEmpty(value) || char.IsAsciiDigit(value[0]))
			return false;

		foreach (var c in value) {
			if (!char.IsAsciiLetterOrDigit(c) && c is not '_')
				return false;
		}

		return true;
	}
}

/// <summary>
/// A database attached on every connection: the database at <paramref name="Path"/>, addressed by
/// the catalog name <paramref name="Alias"/>, with the ATTACH options in
/// <paramref name="Options"/>.
/// </summary>
/// <param name="Path">The database to attach, including any storage-extension prefix such as <c>lance:</c>.</param>
/// <param name="Alias">The catalog name the attached database is addressed by.</param>
/// <param name="Options">The ATTACH options list. Null attaches with none.</param>
public readonly record struct DuckDBAttachedDatabase(string Path, string Alias, DuckDBAttachOptions? Options = null) {
	/// <summary>
	/// Renders the <c>ATTACH</c> statement this attachment describes.
	/// </summary>
	public override string ToString() {
		var attachOptions = Options?.ToString() ?? "";

		if (attachOptions.Length > 0)
			attachOptions = $" {attachOptions}";

		return $"ATTACH IF NOT EXISTS '{DuckDBDataSourceOptions.EscapeLiteral(Path)}' AS {Alias}{attachOptions};";
	}
}

/// <summary>
/// The ATTACH options list: a generic parenthesized key/value set the parser collects without
/// validating keys. DuckDB core consumes the keys it owns (<c>TYPE</c>, <c>READ_ONLY</c>,
/// <c>BLOCK_SIZE</c>, …); the storage extension consumes its own (lance: <c>ENDPOINT</c>,
/// <c>BEARER_TOKEN</c>, …); a key neither consumes fails the attach engine-side
/// ("Unrecognized option for attach"). Every TYPE brings its own option vocabulary, so anything
/// passes through here — keys are validated as bare identifiers, string values are rendered as
/// quote-escaped literals.
/// </summary>
public sealed class DuckDBAttachOptions {
	// Each entry is its own finished fragment: quoting and escaping happen HERE, where the value
	// still arrives on its own and the boundary between structure and data is known. Registration
	// order is render order — the engine takes the options as a set, so order only buys
	// deterministic, readable SQL.
	 List<string> Entries { get; } = [];

	/// <summary>Adds a value-less flag option, such as <c>READ_ONLY</c>.</summary>
	/// <param name="key">The option name.</param>
	/// <returns>The options.</returns>
	/// <exception cref="ArgumentException"><paramref name="key"/> is not a valid identifier.</exception>
	public DuckDBAttachOptions Option(string key) => Add(key, value: null);

	/// <summary>
	/// Adds a string-valued option, such as <c>ENDPOINT 'http://…'</c>. The value is rendered as
	/// a quoted literal, escaped.
	/// </summary>
	/// <param name="key">The option name.</param>
	/// <param name="value">The option value.</param>
	/// <returns>The options.</returns>
	/// <exception cref="ArgumentException"><paramref name="key"/> is not a valid identifier.</exception>
	public DuckDBAttachOptions Option(string key, string value) =>
		Add(key, $"'{DuckDBDataSourceOptions.EscapeLiteral(value)}'");

	/// <summary>Adds a numeric option, such as <c>BLOCK_SIZE 16384</c>.</summary>
	/// <param name="key">The option name.</param>
	/// <param name="value">The option value.</param>
	/// <returns>The options.</returns>
	/// <exception cref="ArgumentException"><paramref name="key"/> is not a valid identifier.</exception>
	public DuckDBAttachOptions Option(string key, long value) =>
		// Invariant: a culture whose NegativeSign is not ASCII '-' would otherwise emit a
		// character the SQL parser does not accept.
		Add(key, value.ToString(CultureInfo.InvariantCulture));

	/// <summary>
	/// The database type, such as <c>LANCE</c>, rendered unquoted per the documented form:
	/// <c>TYPE LANCE</c>.
	/// </summary>
	/// <param name="type">The database type.</param>
	/// <returns>The options.</returns>
	/// <exception cref="ArgumentException"><paramref name="type"/> is not a valid identifier.</exception>
	public DuckDBAttachOptions Type(string type) {
		DuckDBDataSourceOptions.ValidateIdentifier(type, nameof(type));
		return Add("TYPE", type);
	}

	/// <summary>Attaches read-only: the core <c>READ_ONLY</c> flag.</summary>
	/// <returns>The options.</returns>
	public DuckDBAttachOptions ReadOnly() => Option("READ_ONLY");

	/// <summary>
	/// Adds a raw options fragment, rendered verbatim into the list. Mixes freely with the typed
	/// entries.
	/// </summary>
	/// <remarks>
	/// Nothing is validated, quoted or escaped: the fragment is the caller's, in full. Escaping it
	/// here is impossible — structure and data arrive already mixed, so the quotes that delimit a
	/// literal are indistinguishable from quotes inside one. A value that comes from configuration
	/// or any other runtime source therefore belongs in <see cref="Option(string, string)"/>, which
	/// receives it alone and escapes it; interpolating one into a raw fragment is an injection.
	/// </remarks>
	/// <param name="options">The fragment, e.g. <c>TYPE LANCE, ENDPOINT 'http://…'</c>.</param>
	/// <returns>The options.</returns>
	/// <exception cref="ArgumentException"><paramref name="options"/> is empty.</exception>
	public DuckDBAttachOptions Raw(string options) {
		ArgumentException.ThrowIfNullOrEmpty(options);

		Entries.Add(options);
		return this;
	}

	/// <summary>
	/// Renders the parenthesized options list in registration order, or an empty string when there
	/// are none — an empty pair of parentheses is not valid <c>ATTACH</c> syntax. Each entry
	/// arrived finished, so this only joins them.
	/// </summary>
	public override string ToString() => Entries.Count is 0 ? "" : $"({string.Join(", ", Entries)})";

	DuckDBAttachOptions Add(string key, string? value) {
		DuckDBDataSourceOptions.ValidateIdentifier(key, nameof(key));
		Entries.Add(value is null ? key : $"{key} {value}");
		return this;
	}
}

/// <summary>
/// Identifies the repository an extension is installed from.
/// </summary>
public readonly struct DuckDBRepository {
	private readonly string? _rendered;

	private DuckDBRepository(string rendered) => _rendered = rendered;

	/// <summary>
	/// The default repository of extensions built and signed by the DuckDB team.
	/// </summary>
	public static readonly DuckDBRepository Core = new("core");

	/// <summary>
	/// Nightly builds of the core extensions.
	/// </summary>
	public static readonly DuckDBRepository CoreNightly = new("core_nightly");

	/// <summary>
	/// Community extensions, built by third parties and distributed by the DuckDB team.
	/// </summary>
	public static readonly DuckDBRepository Community = new("community");

	/// <summary>
	/// A local repository-structured directory.
	/// </summary>
	/// <param name="path">The directory path.</param>
	/// <returns>The repository.</returns>
	/// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
	public static DuckDBRepository FromPath(string path) {
		ArgumentException.ThrowIfNullOrEmpty(path);

		return new($"'{DuckDBDataSourceOptions.EscapeLiteral(path)}'");
	}

	/// <summary>
	/// A repository served over HTTP, HTTPS or S3.
	/// </summary>
	/// <param name="url">The repository URL.</param>
	/// <returns>The repository.</returns>
	/// <exception cref="ArgumentException"><paramref name="url"/> is empty.</exception>
	public static DuckDBRepository FromUrl(string url) {
		ArgumentException.ThrowIfNullOrEmpty(url);

		return new($"'{DuckDBDataSourceOptions.EscapeLiteral(url)}'");
	}

	internal bool IsDefault => _rendered is null;

	internal string Rendered => _rendered!;
}

/// <summary>
/// Identifies an extension to use, created through its factory methods.
/// </summary>
public readonly struct DuckDBExtension {
    internal readonly string? InstallSql;
    internal readonly string  LoadSql;

    DuckDBExtension(string? installSql, string loadSql) {
        InstallSql = installSql;
        LoadSql    = loadSql;
    }

	/// <summary>
	/// Installs the extension from the default repository once per data source, and loads it on
	/// every connection.
	/// </summary>
	/// <param name="name">The extension name.</param>
	/// <returns>The extension.</returns>
	/// <exception cref="ArgumentException"><paramref name="name"/> is not a valid identifier.</exception>
	public static DuckDBExtension Install(string name) {
		DuckDBDataSourceOptions.ValidateIdentifier(name, nameof(name));

		return new($"INSTALL {name};", $"LOAD {name};");
	}

	/// <summary>
	/// Installs the extension from the given repository once per data source, and loads it on
	/// every connection.
	/// </summary>
	/// <param name="name">The extension name.</param>
	/// <param name="from">
	/// The repository: <see cref="DuckDBRepository.Community"/>,
	/// <see cref="DuckDBRepository.CoreNightly"/>, <see cref="DuckDBRepository.FromPath"/>, or
	/// <see cref="DuckDBRepository.FromUrl"/>.
	/// </param>
	/// <returns>The extension.</returns>
	/// <exception cref="ArgumentException"><paramref name="name"/> is not a valid identifier, or <paramref name="from"/> is unspecified.</exception>
	public static DuckDBExtension Install(string name, DuckDBRepository from) {
		DuckDBDataSourceOptions.ValidateIdentifier(name, nameof(name));
		if (from.IsDefault)
			throw new ArgumentException("Repository is not specified.", nameof(from));

		return new($"INSTALL {name} FROM {from.Rendered};", $"LOAD {name};");
	}

	/// <summary>
	/// Loads the already-installed extension on every connection, without installing it.
	/// </summary>
	/// <param name="name">The extension name.</param>
	/// <returns>The extension.</returns>
	/// <exception cref="ArgumentException"><paramref name="name"/> is not a valid identifier.</exception>
	public static DuckDBExtension Load(string name) {
		DuckDBDataSourceOptions.ValidateIdentifier(name, nameof(name));

		return new(installSql: null, $"LOAD {name};");
	}

	/// <summary>
	/// Loads the extension file directly on every connection, bypassing installation.
	/// </summary>
	/// <remarks>
	/// A locally built or third-party <c>.duckdb_extension</c> file is typically unsigned, which
	/// additionally requires <see cref="DuckDBExtensionsOptions.AllowUnsigned"/>. Only load
	/// unsigned extensions from sources you trust.
	/// </remarks>
	/// <param name="path">The path of the <c>.duckdb_extension</c> file.</param>
	/// <returns>The extension.</returns>
	/// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
	public static DuckDBExtension LoadFrom(string path) {
		ArgumentException.ThrowIfNullOrEmpty(path);

		return new(installSql: null, $"LOAD '{DuckDBDataSourceOptions.EscapeLiteral(path)}';");
	}

	internal bool IsDefault => LoadSql is null;
}

/// <summary>
/// Extension-related options: a grouped view over the owning
/// <see cref="DuckDBDataSourceOptions"/>. It holds no state — settings-backed properties read
/// and write <see cref="DuckDBDataSourceOptions.Settings"/> directly.
/// </summary>
public sealed class DuckDBExtensionsOptions {
	private readonly DuckDBDataSourceOptions _options;

	internal DuckDBExtensionsOptions(DuckDBDataSourceOptions options) => _options = options;

	/// <summary>
	/// The extensions every connection needs, created through the <see cref="DuckDBExtension"/>
	/// factory methods. Installation runs once per data source; every load runs on every
	/// connection. The <see cref="Install(string)"/>, <see cref="Load"/> and
	/// <see cref="LoadFrom"/> helpers are the chainable way to add one.
	/// </summary>
	public List<DuckDBExtension> Required => _options.RequiredExtensions;

	/// <inheritdoc cref="DuckDBExtension.Install(string)"/>
	public DuckDBExtensionsOptions Install(string name) {
		Required.Add(DuckDBExtension.Install(name));
		return this;
	}

	/// <inheritdoc cref="DuckDBExtension.Install(string, DuckDBRepository)"/>
	public DuckDBExtensionsOptions InstallFrom(string name, DuckDBRepository from) {
		Required.Add(DuckDBExtension.Install(name, from));
		return this;
	}

	/// <inheritdoc cref="DuckDBExtension.Load"/>
	public DuckDBExtensionsOptions Load(string name) {
		Required.Add(DuckDBExtension.Load(name));
		return this;
	}

	/// <inheritdoc cref="DuckDBExtension.LoadFrom"/>
	public DuckDBExtensionsOptions LoadFrom(string path) {
		Required.Add(DuckDBExtension.LoadFrom(path));
		return this;
	}

	/// <summary>
	/// Allows loading unsigned extensions. A start-up option, applied through the connection
	/// string when connections open — the engine refuses to change it at runtime. Only load
	/// unsigned extensions from sources you trust.
	/// </summary>
	public bool AllowUnsigned {
		get => _options.AllowUnsignedExtensions;
		set => _options.AllowUnsignedExtensions = value;
	}

	/// <summary>
	/// The directory extensions are installed to and loaded from. A typed view over
	/// <c>Settings["extension_directory"]</c>; assigning null or empty removes the entry.
	/// </summary>
	public string? DefaultDirectory {
		get => _options.Settings.GetValueOrDefault("extension_directory");
		set {
			if (string.IsNullOrEmpty(value)) _options.Settings.Remove("extension_directory");
			else _options.Settings["extension_directory"] = value;
		}
	}

	/// <summary>
	/// The repository extensions are installed from by default: a URL or the path of a local
	/// repository-structured directory. A typed view over
	/// <c>Settings["custom_extension_repository"]</c>; assigning null or empty removes the entry.
	/// </summary>
	public string? DefaultRepository {
		get => _options.Settings.GetValueOrDefault("custom_extension_repository");
		set {
			if (string.IsNullOrEmpty(value)) _options.Settings.Remove("custom_extension_repository");
			else _options.Settings["custom_extension_repository"] = value;
		}
	}

	/// <summary>
	/// Controls automatic installation of known extensions on first use. A typed view over
	/// <c>Settings["autoinstall_known_extensions"]</c>; assigning null removes the entry.
	/// </summary>
	public bool? AutoInstall {
		get => _options.Settings.TryGetValue("autoinstall_known_extensions", out var value)
		       && bool.TryParse(value, out var enabled) ? enabled : null;
		set {
			if (value is { } enabled) _options.Settings["autoinstall_known_extensions"] = enabled.ToString();
			else _options.Settings.Remove("autoinstall_known_extensions");
		}
	}

	/// <summary>
	/// Controls automatic loading of known extensions on first use. A typed view over
	/// <c>Settings["autoload_known_extensions"]</c>; assigning null removes the entry.
	/// </summary>
	public bool? AutoLoad {
		get => _options.Settings.TryGetValue("autoload_known_extensions", out var value)
		       && bool.TryParse(value, out var enabled) ? enabled : null;
		set {
			if (value is { } enabled) _options.Settings["autoload_known_extensions"] = enabled.ToString();
			else _options.Settings.Remove("autoload_known_extensions");
		}
	}
}

/// <summary>
/// Logging options: a grouped view over the owning <see cref="DuckDBDataSourceOptions"/>. It
/// holds no state — every property reads and writes
/// <see cref="DuckDBDataSourceOptions.Settings"/> directly.
/// </summary>
public sealed class DuckDBLoggingOptions {
	private readonly DuckDBDataSourceOptions _options;

	internal DuckDBLoggingOptions(DuckDBDataSourceOptions options) => _options = options;

	/// <summary>
	/// Whether logging is on. A typed view over <c>Settings["enable_logging"]</c>; assigning
	/// null removes the entry. <see cref="DuckDBDataSourceOptions.Logging"/> sets it to
	/// <see langword="true"/> before the callback runs.
	/// </summary>
	public bool? Enabled {
		get => _options.Settings.TryGetValue("enable_logging", out var value)
		       && bool.TryParse(value, out var enabled) ? enabled : null;
		set {
			if (value is { } enabled) _options.Settings["enable_logging"] = enabled.ToString();
			else _options.Settings.Remove("enable_logging");
		}
	}

	/// <summary>
	/// The verbosity of the logs. A typed view over <c>Settings["logging_level"]</c>; assigning
	/// null removes the entry.
	/// </summary>
	public DuckDBLogLevel? Level {
		// IsDefined guards TryParse accepting numeric strings, which would otherwise surface
		// arbitrary integers as enum values. The engine parses enum settings case-insensitively,
		// so the member names round-trip as-is.
		get => Enum.TryParse<DuckDBLogLevel>(_options.Settings.GetValueOrDefault("logging_level"),
			ignoreCase: true, out var level) && Enum.IsDefined(level) ? level : null;
		set {
			if (value is { } level) _options.Settings["logging_level"] = level.ToString();
			else _options.Settings.Remove("logging_level");
		}
	}

	/// <summary>
	/// Where log entries are written. A typed view over <c>Settings["logging_storage"]</c>;
	/// assigning null removes the entry.
	/// </summary>
	public DuckDBLogStorage? Storage {
		get {
			return _options.Settings.GetValueOrDefault("logging_storage") switch {
				"memory" => DuckDBLogStorage.Memory,
				"stdout" => DuckDBLogStorage.Stdout,
				"file" => DuckDBLogStorage.File,
				_ => null,
			};
		}
		set {
			if (value is { } storage)
				_options.Settings["logging_storage"] = storage switch {
					DuckDBLogStorage.Stdout => "stdout",
					DuckDBLogStorage.File   => "file",
					_ => "memory",
				};
			else
				_options.Settings.Remove("logging_storage");
		}
	}

	/// <summary>
	/// Restricts logging to these log types, such as <c>HTTP</c> or <c>QueryLog</c>. A typed
	/// view over <c>Settings["enabled_log_types"]</c> (comma-separated); assigning null removes
	/// the entry.
	/// </summary>
	public IReadOnlyList<string>? Types {
		get => _options.Settings.TryGetValue("enabled_log_types", out var value) ? value.Split(',') : null;
		set {
			if (value is null) _options.Settings.Remove("enabled_log_types");
			else _options.Settings["enabled_log_types"] = string.Join(',', value);
		}
	}
}

/// <summary>
/// Describes the verbosity of DuckDB's logs.
/// </summary>
public enum DuckDBLogLevel {
	/// <summary>Only error messages.</summary>
	Error = 0,

	/// <summary>Warnings and errors.</summary>
	Warning,

	/// <summary>General information, warnings and errors. The engine default.</summary>
	Info,

	/// <summary>Detailed debugging information.</summary>
	Debug,

	/// <summary>Very detailed tracing information.</summary>
	Trace,
}

/// <summary>
/// Describes where DuckDB writes its log entries.
/// </summary>
public enum DuckDBLogStorage {
	/// <summary>An in-memory buffer, readable through <c>duckdb_logs</c>. The engine default.</summary>
	Memory = 0,

	/// <summary>The stdout of the current process, in CSV format.</summary>
	Stdout,

	/// <summary>CSV file storage.</summary>
	File,
}

/// <summary>
/// Describes how <see cref="DuckDBDataSourceOptions.AccessMode"/> opens the database.
/// </summary>
public enum DuckDBAccessMode {
	/// <summary>
	/// The engine decides: read-write for a new database, whatever the file allows otherwise.
	/// </summary>
	Automatic = 0,

	/// <summary>
	/// The database is opened read-only. Writes are rejected.
	/// </summary>
	ReadOnly,

	/// <summary>
	/// The database is opened for reading and writing.
	/// </summary>
	ReadWrite,
}
