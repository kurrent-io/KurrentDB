// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace Kurrent.Quack;

public static partial class DuckDBDataSourceOptionsExtensions {
	// Matches either spelling the driver accepts, quoted or bare:
	//   Data Source=/x/y.db;…   DataSource="/x/semi;colon.db";…   datasource=:memory:
	// The quoted alternative comes first so a value carrying the ';' separator stays whole.
	[GeneratedRegex("""(?:^|;)\s*Data ?Source\s*=\s*(?:"(?<value>[^"]*)"|(?<value>[^;]*))""", RegexOptions.IgnoreCase)]
	private static partial Regex DataSourcePattern { get; }

	// Enumerated once from the engine itself, so validation can never drift from the linked
	// DuckDB version. The engine pre-registers known extension settings for autoloading, so
	// extension-provided names such as s3_region are included. Settings are case-insensitive.
	static readonly Lazy<FrozenSet<string>> KnownSettings = new(static () => {
		var count = DuckDB.NET.Native.NativeMethods.Configuration.DuckDBConfigCount();
		var names = new HashSet<string>(count, StringComparer.OrdinalIgnoreCase);
		for (var i = 0; i < count; i++) {
			DuckDB.NET.Native.NativeMethods.Configuration.DuckDBGetConfigFlag(i, out var name, out _);
			names.Add(name);
		}

		return names.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
	});

	extension(DuckDBDataSourceOptions options) {
		/// <summary>
		/// The data source the connection string names: the file path, <c>:memory:</c>, or an
		/// empty string when it names none.
		/// </summary>
		/// <remarks>
		/// Keyed under either spelling, case-insensitively — the rule DuckDB's own connection
		/// string builder applies.
		/// </remarks>
		public string DataSource =>
			DataSourcePattern.Match(options.ConnectionString) is { Success: true } match
				? match.Groups["value"].Value.Trim()
				: "";

		/// <summary>
		/// Whether the connection string names an in-memory database, in either spelling.
		/// </summary>
		/// <remarks>
		/// An in-memory database lives only while a connection to it is open, so the data source
		/// holds one open for its whole lifetime. Both <c>:memory:</c> and the process-shared
		/// <c>:memory:?cache=shared</c> need that, which is the rule the driver itself applies
		/// when it parses a connection string.
		/// </remarks>
		public bool IsInMemory =>
            options.DataSource.Equals(DuckDBDataSourceOptions.InMemoryDataSource, StringComparison.OrdinalIgnoreCase)
         || options.DataSource.Equals(DuckDBDataSourceOptions.InMemorySharedDataSource, StringComparison.OrdinalIgnoreCase);

        public void EnsureValid() {
			// Every problem is collected before the single throw, so one round trip through
			// EnsureValid surfaces the complete correction list, never just the first defect.
			List<string>? problems = null;

			if (options.ConnectionString is not { Length: > 0 })
				Report("ConnectionString is required");

			if (options.MaxIdleConnections <= 0)
				Report($"MaxIdleConnections must be positive, got {options.MaxIdleConnections}");

			if (options.AccessMode is { } mode
			    and not (DuckDBAccessMode.Automatic or DuckDBAccessMode.ReadOnly or DuckDBAccessMode.ReadWrite))
				Report($"unknown access mode '{mode}'");

			if (options.MemoryLimit is { Length: 0 })
				Report("MemoryLimit must not be empty");

			if (options.Threads is <= 0)
				Report($"Threads must be positive, got {options.Threads}");

			foreach (var extension in options.RequiredExtensions) {
				if (extension.IsDefault)
					Report("an extension in RequiredExtensions is unspecified");
			}

			if (options.Settings.GetValueOrDefault("extension_directory") is { Length: 0 })
				Report("extension_directory must not be empty");

			if (options.Settings.GetValueOrDefault("custom_extension_repository") is { Length: 0 })
				Report("custom_extension_repository must not be empty");

			if (options.Settings.TryGetValue("enabled_log_types", out var logTypes)) {
				if (logTypes.Length is 0)
					Report("enabled_log_types must contain at least one log type");
				foreach (var type in logTypes.Split(',', StringSplitOptions.TrimEntries)) {
					if (type.Length > 0 && !DuckDBDataSourceOptions.IsIdentifier(type))
						Report($"log type '{type}' is not a valid identifier");
				}
			}

			foreach (var name in options.Settings.Keys) {
				if (!KnownSettings.Value.Contains(name))
					Report($"'{name}' is not a known DuckDB setting");
			}

			foreach (var attached in options.AttachedDatabases) {
				if (attached.Path.Length is 0)
					Report("an attached database path is empty");
				if (!DuckDBDataSourceOptions.IsIdentifier(attached.Alias))
					Report($"attach alias '{attached.Alias}' is not a valid identifier");
			}

			if (problems is not null)
				throw new ArgumentException($"Invalid options: {string.Join("; ", problems)}.");

			void Report(string problem) => (problems ??= []).Add(problem);
		}
        
		/// <summary>
		/// Renders the effective connection string: <see cref="DuckDBDataSourceOptions.ConnectionString"/>
		/// plus the start-up options, which the engine only accepts as a connection opens.
		/// </summary>
		/// <returns>The connection string every connection is opened with.</returns>
		public string ToConnectionString() {
			var connectionString = options.ConnectionString;

			if (options.AccessMode is { } mode)
				connectionString += ";access_mode=" + mode switch {
					DuckDBAccessMode.ReadOnly  => "read_only",
					DuckDBAccessMode.ReadWrite => "read_write",
					_ => "automatic",
				};

			if (options.AllowUnsignedExtensions)
				connectionString += ";allow_unsigned_extensions=true";

			return connectionString;
		}

		public SqlStatements GenerateSqlStatements() {
            var installExtensions = new List<string>();
            var loadExtensions    = new List<string>();

            foreach (var extension in options.RequiredExtensions) {
				if (extension.InstallSql is { } install)
					installExtensions.Add(install);

				loadExtensions.Add(extension.LoadSql);
			}

            var configureSettings = options.Settings
                .Select(entry => $"SET {entry.Key} = '{DuckDBDataSourceOptions.EscapeLiteral(entry.Value)}';");

            var attachDbs = options.AttachedDatabases
                .Select(x => x.ToString());

            return new(
				string.Join(Environment.NewLine, installExtensions),
				string.Join(Environment.NewLine, loadExtensions),
				string.Join(Environment.NewLine, attachDbs),
				string.Join(Environment.NewLine, configureSettings));
		}
	}
}

public record SqlStatements {
    public SqlStatements(string installExtensions,
        string loadExtensions,
        string attachDatabases,
        string configureSettings) {
        
        InstallExtensions   = installExtensions;
        LoadExtensions      = loadExtensions;
        AttachDatabases     = attachDatabases;
        ConfigureSettings   = configureSettings;
        
        ForConnection       = Join(loadExtensions, attachDatabases, configureSettings);
        ForLockedConnection = Join(loadExtensions, attachDatabases);

        IsEmpty = installExtensions.Length is 0
                    && loadExtensions.Length is 0
                    && attachDatabases.Length is 0
                    && configureSettings.Length is 0;
        
        static string Join(params string[] parts) =>
            string.Join(Environment.NewLine, parts.Where(static part => part.Length > 0));
    }

    public string InstallExtensions { get; init; }
    public string LoadExtensions    { get; init; }
    public string AttachDatabases   { get; init; }
    public string ConfigureSettings { get; init; }
    
    /// <summary>
    /// The per-connection script for a fresh instance: loads, attachments, then settings.
    /// </summary>
    public string ForConnection { get; }

    /// <summary>
    /// The per-connection script for an already-locked instance, which rejects every
    /// <c>SET</c>: loads and attachments only.
    /// </summary>
    public string ForLockedConnection { get; }
    
    public bool IsEmpty { get; }
}

