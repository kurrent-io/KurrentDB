// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;
using System.Text;
using System.Threading.Tasks;
using EventStore.SourceGenerators.Messaging;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace EventStore.SourceGenerators.Tests.Messaging;

using Test = CSharpSourceGeneratorVerifier<MessageSourceGenerator>.Test;

public class GeneratorTests {
	private static uint GenHash(int fileNumber) => (uint)$"/0/Test{fileNumber}.cs".GetHashCode();
	private static string ReadFile(string fileName) => File.ReadAllText($"./Messaging/Cases/{fileName}");

	private static async Task RunTestAsync(
		string sourcePath,
		string expectedPath,
		params DiagnosticResult[] expectedDiagnostics) {

		var test = new Test() {
			TestState = {
				Sources = {
					ReadFile("Message.cs"),
					ReadFile(sourcePath),
				},
				GeneratedSources = {
					(
						typeof(MessageSourceGenerator),
						$"Test0-{GenHash(0)}.g.cs",
						SourceText.From(ReadFile("Message.g.cs"), Encoding.UTF8, SourceHashAlgorithm.Sha256)
					),
					(
						typeof(MessageSourceGenerator),
						$"Test1-{GenHash(1)}.g.cs",
						SourceText.From(ReadFile(expectedPath), Encoding.UTF8, SourceHashAlgorithm.Sha256)
					)
				},
				AdditionalReferences = {
					typeof(DerivedMessageAttribute).Assembly,
				},
			}
		};

		test.TestState.ExpectedDiagnostics.AddRange(expectedDiagnostics);

		await test.RunAsync();
	}

	[Fact]
	public async Task Simple() =>
		await RunTestAsync("Simple.cs", "Simple.g.cs");

	[Fact]
	public async Task Abstract() =>
		await RunTestAsync("Abstract.cs", "Abstract.g.cs");

	[Fact]
	public async Task FileScopedNamespace() =>
		await RunTestAsync("FileScopedNamespace.cs", "FileScopedNamespace.g.cs");

	[Fact]
	public async Task Nested() =>
		await RunTestAsync("Nested.cs", "Nested.g.cs");

	[Fact]
	public async Task NestedDerived() =>
		await RunTestAsync("NestedDerived.cs", "NestedDerived.g.cs");

	[Fact]
	public async Task Impartial() =>
		await RunTestAsync(
			"Impartial.cs",
			"Impartial.NOTCOMPILED.g.cs",
			new DiagnosticResult("ESGEN001", DiagnosticSeverity.Error)
				.WithSpan("/0/Test1.cs", 2, 2, 4, 3)
				.WithMessage("Class \"A\" is not partial"),
			new DiagnosticResult("CS0101", DiagnosticSeverity.Error)
				.WithSpan(
					$"EventStore.SourceGenerators{Path.DirectorySeparatorChar}" +
					$"EventStore.SourceGenerators.Messaging.{nameof(MessageSourceGenerator)}{Path.DirectorySeparatorChar}" +
					$"Test1-{GenHash(1)}.g.cs", 7, 15, 7, 16)
				.WithArguments("A", "EventStore.SourceGenerators.Tests.Messaging.Impartial"));

	[Fact]
	public async Task ImpartialNested() =>
		await RunTestAsync(
			"ImpartialNested.cs",
			"ImpartialNested.NOTCOMPILED.g.cs",
			new DiagnosticResult("ESGEN001", DiagnosticSeverity.Error)
				.WithSpan("/0/Test1.cs", 2, 2, 6, 3)
				.WithMessage("Class \"N\" is not partial"),
			new DiagnosticResult("CS0101", DiagnosticSeverity.Error)
				.WithSpan(
					$"EventStore.SourceGenerators{Path.DirectorySeparatorChar}" +
					$"EventStore.SourceGenerators.Messaging.{nameof(MessageSourceGenerator)}{Path.DirectorySeparatorChar}" +
					$"Test1-{GenHash(1)}.g.cs", 7, 15, 7, 16)
				.WithArguments("N", "EventStore.SourceGenerators.Tests.Messaging.ImpartialNested"));

	[Fact]
	public async Task AbstractWithGroup() =>
		await RunTestAsync(
			"AbstractWithGroup.cs",
			"AbstractWithGroup.NOTCOMPILED.g.cs",
			new DiagnosticResult("ESGEN002", DiagnosticSeverity.Error)
				.WithSpan("/0/Test1.cs", 2, 2, 4, 3)
				.WithMessage("Abstract class \"A\" has a group specified")
				.WithArguments("A"));

	[Fact]
	public async Task ConcreteWithoutGroup() =>
		await RunTestAsync(
			"ConcreteWithoutGroup.cs",
			"ConcreteWithoutGroup.NOTCOMPILED.g.cs",
			new DiagnosticResult("ESGEN003", DiagnosticSeverity.Error)
				.WithSpan("/0/Test1.cs", 2, 2, 4, 3)
				.WithMessage("Concrete class \"A\" does not have a group specified")
				.WithArguments("A"));
}
