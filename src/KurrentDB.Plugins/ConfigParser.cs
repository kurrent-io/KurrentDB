// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace EventStore.Plugins;

public class ConfigParser(ILogger logger) {
	// For backwards compatibility for (hypothetical) 3rd party plugins
	// there probably aren't any of these, the plugin mechanism was for separating commercial code from oss
	// rather than being a public extension point, but since this is easy to add.
	/// <summary>
	///     Deserializes a section of configuration from a given YAML config file into the provided settings type.
	/// </summary>
	/// <param name="configPath">The path to the YAML configuration file.</param>
	/// <param name="sectionName">The section within the file to deserialize. If not found, the whole file is used.</param>
	/// <typeparam name="T">The settings type to deserialize into.</typeparam>
	public static T? ReadConfiguration<T>(string configPath, string sectionName) where T : class {
		return new ConfigParser(NullLogger.Instance).ReadConfigurationFromPath<T>(configPath, sectionName);
	}

	/// <summary>
	///     Reads plugin configuration, either from a dedicated YAML file or from the main
	///     <see cref="IConfiguration"/>, depending on whether a file path is set at
	///     <c>KurrentDB:{configFileKey}</c>.
	/// </summary>
	/// <param name="configuration">The main application configuration.</param>
	/// <param name="configFileKey">
	///     The key (relative to <c>KurrentDB:</c>) whose value, if present and non-empty, is
	///     treated as a path to a YAML file from which <paramref name="sectionName"/> is read.
	/// </param>
	/// <param name="sectionName">
	///     The section name used when reading from the YAML file, and the path suffix
	///     (relative to <c>KurrentDB:</c>) used when falling back to <paramref name="configuration"/>.
	/// </param>
	/// <typeparam name="T">The settings type to deserialize into.</typeparam>
	public T ReadConfiguration<T>(IConfiguration configuration, string configFileKey, string sectionName) {
		var configPath = configuration.GetValue<string>($"KurrentDB:{configFileKey}");
		var result = string.IsNullOrEmpty(configPath)
			? ReadConfigurationFromIConfiguration<T>(configuration, $"KurrentDB:{sectionName}")
			: ReadConfigurationFromPath<T>(configPath, sectionName);

		return result ?? throw new Exception($"Could not read {sectionName} configuration from {configPath ?? "main configuration"}");
	}

	private T? ReadConfigurationFromIConfiguration<T>(IConfiguration configuration, string sectionName) {
		logger.LogInformation("Reading {SectionName} configuration from main configuration", sectionName);
		return configuration.GetSection(sectionName).Get<T>();
	}

	/// <summary>
	///     Deserializes a section of configuration from a given config file into the provided settings type
	/// </summary>
	/// <param name="configPath">The path to the configuration file</param>
	/// <param name="sectionName">The section to deserialize</param>
	/// <typeparam name="T">The type of settings object to create from the configuration</typeparam>
	private T? ReadConfigurationFromPath<T>(string configPath, string sectionName) {
		logger.LogInformation("Reading {SectionName} configuration from yaml file {ConfigPath}", sectionName, configPath);

		if (!File.Exists(configPath))
			throw new FileNotFoundException($"Configuration file not found.", configPath);

		var yamlStream = new YamlStream();
		var stringReader = new StringReader(File.ReadAllText(configPath));

		try {
			yamlStream.Load(stringReader);
		} catch (Exception ex) {
			throw new(
				$"An invalid configuration file has been specified. {Environment.NewLine}{ex.Message}");
		}

		var yamlNode = (YamlMappingNode)yamlStream.Documents[0].RootNode;
		if (!string.IsNullOrEmpty(sectionName)) {
			Func<KeyValuePair<YamlNode, YamlNode>, bool> predicate = x =>
				x.Key.ToString() == sectionName && x.Value is YamlMappingNode;

			var nodeExists = yamlNode.Children.Any(predicate);
			if (nodeExists)
				yamlNode = (YamlMappingNode)yamlNode.Children.First(predicate).Value;
			else
				logger.LogInformation("Could not find section {SectionName}, interpreting whole file as {SectionName} section", sectionName, sectionName);
		}

		if (yamlNode is null)
			return default;

		using var stream = new MemoryStream();
		using var writer = new StreamWriter(stream);
		using var reader = new StreamReader(stream);

		new YamlStream(new YamlDocument(yamlNode)).Save(writer);
		writer.Flush();
		stream.Position = 0;

		return new Deserializer().Deserialize<T>(reader);
	}
}
