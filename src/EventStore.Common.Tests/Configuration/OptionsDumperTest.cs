using System.Collections;
using System.ComponentModel;
using EventStore.Common.Configuration;
using Microsoft.Extensions.Configuration;

namespace EventStore.Common.Tests.Configuration;

public class OptionsDumperTest {
	internal class TestOptions {
		internal enum TestOptionEnum {
			OptionA = 0,
			OptionB = 1
		}
		public readonly IConfigurationRoot ConfigurationRoot;

		public class FirstSection {
			[Description("A normal text option.")]
			public string NormalTextOption { get; init; } = "default-normal";

			[Description("An array of strings.")]
			public string[] ArrayOfStrings { get; init; } = Array.Empty<string>();

			[Description("A sensitive value like a password."), Sensitive]
			public string SensitiveOption { get; init; } = "default-sensitive";
		}

		public class SecondSection {
			[Description("An enum option.")] public TestOptionEnum EnumOption { get; init; } = TestOptionEnum.OptionA;

			[Description("A normal bool option.")] public bool FlagOption { get; init; } = false;

			[Description("A sensitive non-text value."), Sensitive]
			public int SecretNumber { get; init; } = 7;
		}

		private static IEnumerable<KeyValuePair<string, object?>> GetDefaultValues(Type type) {
			var defaultInstance = Activator.CreateInstance(type)!;

			return type.GetProperties().Select(property =>
				new KeyValuePair<string, object?>(property.Name, property.PropertyType switch {
					{ IsArray: true } => string.Join(",",
						((Array)(property.GetValue(defaultInstance) ?? Array.Empty<object>())).OfType<object>()),
					_ => property.GetValue(defaultInstance)
				}));
		}

		public TestOptions(Dictionary<string, string> environmentVariables, string[] commandLine) {
			var defaultOptions = GetDefaultValues(typeof(FirstSection))
				.Concat(GetDefaultValues(typeof(SecondSection)));
			ConfigurationRoot = new ConfigurationBuilder()
				.Add(new DefaultSource(defaultOptions))
				.Add(new EnvironmentVariablesSource(new Hashtable(environmentVariables)))
				.Add(new CommandLineSource(commandLine))
				.Build();
		}
	}

	public readonly OptionsDumper OptionsDumper;

	public OptionsDumperTest() {
		OptionsDumper = new(new[]
		{ typeof(TestOptions.FirstSection), typeof(TestOptions.SecondSection) });
	}

	[Fact]
	public void printable_options_do_not_contain_sensitive_values() {
		var secretText = "secret_text";
		var options = new TestOptions(new Dictionary<string, string>(), new[] {
			$"--sensitive-option={secretText}",
		});
		var printable = OptionsDumper.GetOptionSourceInfo(options.ConfigurationRoot);

		Assert.True(printable.TryGetValue(nameof(TestOptions.FirstSection.SensitiveOption), out var actualText));
		Assert.DoesNotContain(secretText, actualText.Value);
		Assert.Equal(new string('*', 8), actualText.Value);

		Assert.True(printable.TryGetValue(nameof(TestOptions.SecondSection.SecretNumber), out var actualNumber));
		Assert.DoesNotContain("7", actualNumber.Value);
		Assert.Equal(new string('*', 8), actualNumber.Value);
	}

	[Fact]
	public void printable_options_show_allowed_values() {
		var options = new TestOptions(new Dictionary<string, string>(), new[] {
			$"--enum-option={TestOptions.TestOptionEnum.OptionB}"
		});
		var printable = OptionsDumper.GetOptionSourceInfo(options.ConfigurationRoot);

		Assert.True(printable.TryGetValue(nameof(TestOptions.SecondSection.EnumOption), out var actualText));
		Assert.Equal(TestOptions.TestOptionEnum.OptionB.ToString(), actualText.Value);
		var allowedValues = typeof(TestOptions.TestOptionEnum).GetEnumNames();
		Assert.Equal(allowedValues, actualText.AllowedValues);
	}

	[Fact]
	public void array_options_are_comma_separated() {
		var expectedValues = "foo,bar,baz";
		var options = new TestOptions(new Dictionary<string, string>(), new[] {
			$"--array-of-strings={expectedValues}"
		});
		var printable = OptionsDumper.GetOptionSourceInfo(options.ConfigurationRoot);

		Assert.True(printable.TryGetValue(nameof(TestOptions.FirstSection.ArrayOfStrings), out var actualText));
		Assert.Equal(expectedValues, actualText.Value);
	}

	[Fact]
	public void option_set_by_another_source_shows_the_correct_source() {
		var options = new TestOptions(new Dictionary<string, string> {
			{ "EVENTSTORE_Flag_Option", "true" }
		}, new[] {
			"--normal-text-option=something",
		});
		var printable = OptionsDumper.GetOptionSourceInfo(options.ConfigurationRoot);

		Assert.True(printable.TryGetValue(nameof(TestOptions.FirstSection.NormalTextOption), out var commandLine));
		Assert.Equal("CommandLine", commandLine.Source.Name);

		Assert.True(printable.TryGetValue(nameof(TestOptions.SecondSection.FlagOption), out var envVar));
		Assert.Equal("EnvironmentVariables", envVar.Source.Name);

		Assert.True(printable.TryGetValue(nameof(TestOptions.FirstSection.ArrayOfStrings), out var defaultVar));
		Assert.Equal(typeof(Default), defaultVar.Source);
	}

	[Fact]
	public void ignores_unknown_option() {
		var options = new TestOptions(new Dictionary<string, string>(), new[] { "--unknown-option=something" });
		var printable = OptionsDumper.GetOptionSourceInfo(options.ConfigurationRoot);
		Assert.False(printable.TryGetValue("UnknownOption", out _));
	}

	[Fact]
	public void option_set_by_multiple_sources_shows_the_source_with_precedence() {
		var expected = "foo";
		var options = new TestOptions(new Dictionary<string, string> {
			{ "EVENTSTORE_NORMAL_TEXT_OPTION", "bar" }
		}, new[] {
			$"--normal-text-option={expected}",
		});
		var printable = OptionsDumper.GetOptionSourceInfo(options.ConfigurationRoot);

		Assert.True(printable.TryGetValue(nameof(TestOptions.FirstSection.NormalTextOption), out var actualOption));
		Assert.Equal("CommandLine", actualOption.Source.Name);
		Assert.Equal(expected, actualOption.Value);
	}

	[Theory]
	[MemberData(nameof(TestCases))]
	public void group_and_description_is_correct(string name, string group, string description) {
		var options = new TestOptions(new Dictionary<string, string>(), Array.Empty<string>());
		var printable = OptionsDumper.GetOptionSourceInfo(options.ConfigurationRoot);

		Assert.True(printable.TryGetValue(name, out var actualOption));
		Assert.Equal(group, actualOption.Group);
		Assert.Equal(description, actualOption.Description);
	}

	public static IEnumerable<object[]> TestCases() => new List<object[]> {
		new object[] {nameof(TestOptions.FirstSection.NormalTextOption), nameof(TestOptions.FirstSection), "A normal text option."},
		new object[]{nameof(TestOptions.FirstSection.ArrayOfStrings), nameof(TestOptions.FirstSection), "An array of strings."},
		new object[]{nameof(TestOptions.FirstSection.SensitiveOption), nameof(TestOptions.FirstSection), "A sensitive value like a password."},
		new object[]{nameof(TestOptions.SecondSection.EnumOption), nameof(TestOptions.SecondSection), "An enum option."},
		new object[]{nameof(TestOptions.SecondSection.FlagOption), nameof(TestOptions.SecondSection), "A normal bool option."},
		new object[]{nameof(TestOptions.SecondSection.SecretNumber), nameof(TestOptions.SecondSection), "A sensitive non-text value."}
	};
}
