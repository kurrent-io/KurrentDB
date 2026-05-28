// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using Google.Protobuf;
using KurrentDB.Protocol.Registry.V2;

namespace KurrentDB.SchemaRegistry.Tests.Modules.Schemas.Validators;

public class SchemaNameValidatorTests {
    // Construct NFC/NFD forms at runtime so the test inputs are unambiguous regardless of the source file's encoding.
    static readonly string NfcCafe = "café".Normalize(NormalizationForm.FormC);
    static readonly string NfdCafe = "café".Normalize(NormalizationForm.FormD);

    [Test]
    public void nfc_schema_name_is_accepted() {
        var result = SchemaNameValidator.Instance.Validate(NfcCafe);

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void nfd_schema_name_is_rejected() {
        var result = SchemaNameValidator.Instance.Validate(NfdCafe);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("NFC"));
    }

    [Test]
    public void empty_schema_name_is_rejected() {
        var result = SchemaNameValidator.Instance.Validate("");

        result.IsValid.ShouldBeFalse();
    }

    [Test]
    public void get_schema_request_rejects_nfd_schema_name() {
        var request = new GetSchemaRequest { SchemaName = NfdCafe };

        var result = GetSchemaRequestValidator.Instance.Validate(request);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(GetSchemaRequest.SchemaName));
    }

    [Test]
    public void create_schema_request_rejects_nfd_schema_name() {
        var request = new CreateSchemaRequest {
            SchemaName       = NfdCafe,
            Details          = new SchemaDetails { DataFormat = SchemaDataFormat.Json, Compatibility = CompatibilityMode.None },
            SchemaDefinition = ByteString.CopyFromUtf8("{}")
        };

        var result = CreateSchemaRequestValidator.Instance.Validate(request);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(CreateSchemaRequest.SchemaName));
    }
}
