// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using KurrentDB.Surge.Testing.Messages.Telemetry;
using KurrentDB.SchemaRegistry.Infrastructure.Eventuous;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using KurrentDB.SchemaRegistry.Services.Domain;
using CompatibilityMode = KurrentDB.Protocol.Registry.V2.CompatibilityMode;
using SchemaFormat = KurrentDB.Protocol.Registry.V2.SchemaDataFormat;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Domain;

public class RegisterSchemaVersionCommandTests : SchemaApplicationTestFixture {
	[Test]
	public async Task registers_new_schema_version_successfully(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var newDefinition = Faker.Lorem.Text();

		await Apply(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = NewJsonSchema().ToByteString(),
				Details = new SchemaDetails {
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.None
				}
			},
			cancellationToken
		);

		var expectedEvent = new SchemaVersionRegistered {
			SchemaName = schemaName,
			SchemaDefinition = ByteString.CopyFromUtf8(newDefinition),
			DataFormat = SchemaFormat.Json,
			VersionNumber = 2,
			RegisteredAt = Timestamp.FromDateTimeOffset(TimeProvider.GetUtcNow())
		};

		// Act
		var result = await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(newDefinition)
			},
			cancellationToken
		);

		// Assert
		var versionRegistered = result.Changes.Should().HaveCount(1).And.Subject.GetSingleEvent<SchemaVersionRegistered>()
			.With(x => x.SchemaVersionId = expectedEvent.SchemaVersionId);
		versionRegistered.Should()
			.BeEquivalentTo(expectedEvent, o => o.Excluding(e => e.SchemaVersionId));
	}

	[Test]
	public async Task registers_multiple_schema_versions_with_incrementing_version_numbers(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var secondDefinition = Faker.Lorem.Text();
		var thirdDefinition = Faker.Lorem.Text();

		await Apply(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = NewJsonSchema().ToByteString(),
				Details = new SchemaDetails {
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.None
				}
			},
			cancellationToken
		);

		// Act
		var secondResult = await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(secondDefinition)
			},
			cancellationToken
		);

		var thirdResult = await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(thirdDefinition)
			},
			cancellationToken
		);

		// Assert
		var secondVersionRegistered = secondResult.Changes.Should().HaveCount(1).And.Subject.GetSingleEvent<SchemaVersionRegistered>();
		secondVersionRegistered.VersionNumber.Should().Be(2);
		secondVersionRegistered.SchemaDefinition.ToStringUtf8().Should().Be(secondDefinition);

		var thirdVersionRegistered = thirdResult.Changes.Should().HaveCount(1).And.Subject.GetSingleEvent<SchemaVersionRegistered>();
		thirdVersionRegistered.VersionNumber.Should().Be(3);
		thirdVersionRegistered.SchemaDefinition.ToStringUtf8().Should().Be(thirdDefinition);
	}

	[Test, Timeout(20_000)]
	public async Task throws_exception_when_schema_not_found(CancellationToken cancellationToken) {
		// Arrange
		var nonExistentSchemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";
		var schemaDefinition = Faker.Lorem.Text();

		// Act
		var registerVersion = async () => await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = nonExistentSchemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(schemaDefinition)
			},
			cancellationToken
		);

		// Assert
		await registerVersion.ShouldThrowAsync<DomainExceptions.EntityNotFound>();
	}

	[Test]
	public async Task throws_exception_when_schema_is_deleted(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var newDefinition = Faker.Lorem.Text();

		await Apply(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = NewJsonSchema().ToByteString(),
				Details = new SchemaDetails {
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.None
				}
			},
			cancellationToken
		);

		await Apply(new DeleteSchemaRequest { SchemaName = schemaName }, cancellationToken);

		// Act
		var registerVersion = async () => await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(newDefinition)
			},
			cancellationToken
		);

		// Assert
		await registerVersion.ShouldThrowAsync<DomainExceptions.EntityNotFound>();
	}

	[Test]
	public async Task throws_exception_when_schema_definition_has_not_changed(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var schemaDefinition = NewJsonSchema();

		await Apply(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = schemaDefinition.ToByteString(),
				Details = new SchemaDetails {
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.None
				}
			},
			cancellationToken
		);

		// Act
		var registerVersion = async () => await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = schemaDefinition.ToByteString()
			},
			cancellationToken
		);

		// Assert
		await registerVersion.ShouldThrowAsync<DomainExceptions.EntityException>()
			.WithMessage("Schema definition has not changed");
	}

	[Test]
	public async Task preserves_original_data_format_in_registered_version(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var newDefinition = Faker.Lorem.Text();

		await Apply(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = NewJsonSchema().ToByteString(),
				Details = new SchemaDetails {
					DataFormat = SchemaFormat.Protobuf,
					Compatibility = CompatibilityMode.None
				}
			},
			cancellationToken
		);

		// Act
		var result = await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(newDefinition)
			},
			cancellationToken
		);

		// Assert
		var versionRegistered = result.Changes.Should().HaveCount(1).And.Subject.GetSingleEvent<SchemaVersionRegistered>();
		versionRegistered.DataFormat.Should().Be(SchemaFormat.Protobuf);
	}

	[Test]
	public async Task generates_unique_schema_version_ids_for_different_versions(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var secondDefinition = Faker.Lorem.Text();
		var thirdDefinition = Faker.Lorem.Text();

		await Apply(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = NewJsonSchema().ToByteString(),
				Details = new SchemaDetails {
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.None
				}
			},
			cancellationToken
		);

		// Act
		var secondResult = await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(secondDefinition)
			},
			cancellationToken
		);

		var thirdResult = await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(thirdDefinition)
			},
			cancellationToken
		);

		// Assert
		var secondVersionId = secondResult.Changes.GetSingleEvent<SchemaVersionRegistered>().SchemaVersionId;
		var thirdVersionId = thirdResult.Changes.GetSingleEvent<SchemaVersionRegistered>().SchemaVersionId;

		secondVersionId.Should().NotBeEmpty();
		thirdVersionId.Should().NotBeEmpty();
		secondVersionId.Should().NotBe(thirdVersionId);
	}

	[Test]
	public async Task registers_version_with_empty_schema_definition(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();

		await Apply(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = NewJsonSchema().ToByteString(),
				Details = new SchemaDetails {
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.None
				}
			},
			cancellationToken
		);

		// Act
		var result = await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.Empty
			},
			cancellationToken
		);

		// Assert
		var versionRegistered = result.Changes.Should().HaveCount(1).And.Subject.GetSingleEvent<SchemaVersionRegistered>();
		versionRegistered.SchemaDefinition.Should().BeEquivalentTo(ByteString.Empty);
		versionRegistered.VersionNumber.Should().Be(2);
	}

	[Test]
	[Arguments(SchemaFormat.Json)]
	[Arguments(SchemaFormat.Protobuf)]
	[Arguments(SchemaFormat.Avro)]
	[Arguments(SchemaFormat.Bytes)]
	public async Task registers_version_for_different_data_formats(
		SchemaFormat dataFormat, CancellationToken cancellationToken
	) {
		// Arrange
		var schemaName = NewSchemaName();
		var newDefinition = Faker.Lorem.Text();

		await Apply(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = NewJsonSchema().ToByteString(),
				Details = new SchemaDetails {
					DataFormat = dataFormat,
					Compatibility = CompatibilityMode.None
				}
			},
			cancellationToken
		);

		// Act
		var result = await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(newDefinition)
			},
			cancellationToken
		);

		// Assert
		var versionRegistered = result.Changes.Should().HaveCount(1).And.Subject.GetSingleEvent<SchemaVersionRegistered>();
		versionRegistered.DataFormat.Should().Be(dataFormat);
		versionRegistered.VersionNumber.Should().Be(2);
	}

	[Test]
	[Arguments(CompatibilityMode.Backward)]
	[Arguments(CompatibilityMode.Forward)]
	[Arguments(CompatibilityMode.Full)]
	[Arguments(CompatibilityMode.None)]
	public async Task registers_version_for_different_compatibility_modes(
		CompatibilityMode compatibilityMode, CancellationToken cancellationToken
	) {
		// Arrange
		var schemaName = NewSchemaName();
		var newDefinition = Faker.Lorem.Text();

		await Apply(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = NewJsonSchema().ToByteString(),
				Details = new SchemaDetails {
					DataFormat = SchemaFormat.Json,
					Compatibility = compatibilityMode
				}
			},
			cancellationToken
		);

		// Act
		var result = await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(newDefinition)
			},
			cancellationToken
		);

		// Assert
		var versionRegistered = result.Changes.Should().HaveCount(1).And.Subject.GetSingleEvent<SchemaVersionRegistered>();
		versionRegistered.SchemaName.Should().Be(schemaName);
		versionRegistered.VersionNumber.Should().Be(2);
	}

	[Test]
	public async Task sets_registered_at_timestamp_correctly(CancellationToken cancellationToken) {
		// Arrange
		var schemaName = NewSchemaName();
		var newDefinition = Faker.Lorem.Text();
		var beforeRegistration = TimeProvider.GetUtcNow();

		await Apply(
			new CreateSchemaRequest {
				SchemaName = schemaName,
				SchemaDefinition = NewJsonSchema().ToByteString(),
				Details = new SchemaDetails {
					DataFormat = SchemaFormat.Json,
					Compatibility = CompatibilityMode.None
				}
			},
			cancellationToken
		);

		// Act
		var result = await Apply(
			new RegisterSchemaVersionRequest {
				SchemaName = schemaName,
				SchemaDefinition = ByteString.CopyFromUtf8(newDefinition)
			},
			cancellationToken
		);

		var afterRegistration = TimeProvider.GetUtcNow();

		// Assert
		var versionRegistered = result.Changes.Should().HaveCount(1).And.Subject.GetSingleEvent<SchemaVersionRegistered>();
		var registeredAt = versionRegistered.RegisteredAt.ToDateTimeOffset();

		registeredAt.Should().BeOnOrAfter(beforeRegistration);
		registeredAt.Should().BeOnOrBefore(afterRegistration);
	}
}
