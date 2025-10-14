// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace EventStore.Plugins;

public static class ConfigParser {
	/// <summary>
	///     Deserializes a section of configuration from a given config file into the provided settings type
	/// </summary>
	/// <param name="configPath">The path to the configuration file</param>
	/// <param name="sectionName">The section to deserialize</param>
	/// <typeparam name="T">The type of settings object to create from the configuration</typeparam>
	public static T? ReadConfiguration<T>(string configPath, string sectionName) where T : class {
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
