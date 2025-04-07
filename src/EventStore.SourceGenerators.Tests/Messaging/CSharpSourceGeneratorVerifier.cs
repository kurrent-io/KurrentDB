// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;

namespace EventStore.SourceGenerators.Tests.Messaging;

// https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.cookbook.md#unit-testing-of-generators
public static class CSharpSourceGeneratorVerifier<TSourceGenerator>
	where TSourceGenerator : ISourceGenerator, new() {

	public class Test : CSharpSourceGeneratorTest<TSourceGenerator, XUnitVerifier> {
		public Test() {
		}

		protected override CompilationOptions CreateCompilationOptions() {
			var compilationOptions = base.CreateCompilationOptions();
			return compilationOptions.WithSpecificDiagnosticOptions(
				 compilationOptions.SpecificDiagnosticOptions.SetItems(GetNullableWarningsFromCompiler()));
		}

		public LanguageVersion LanguageVersion { get; set; } = LanguageVersion.Default;

		private static ImmutableDictionary<string, ReportDiagnostic> GetNullableWarningsFromCompiler() {
			string[] args = { "/warnaserror:nullable" };
			var commandLineArguments = CSharpCommandLineParser.Default.Parse(
				args: args,
				baseDirectory: Environment.CurrentDirectory,
				sdkDirectory: Environment.CurrentDirectory);
			var nullableWarnings = commandLineArguments.CompilationOptions.SpecificDiagnosticOptions;

			return nullableWarnings;
		}

		protected override ParseOptions CreateParseOptions() {
			return ((CSharpParseOptions)base.CreateParseOptions()).WithLanguageVersion(LanguageVersion);
		}
	}
}
