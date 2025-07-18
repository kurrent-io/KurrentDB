// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// using KurrentDB.Protocol.Registry.V2;
// using static KurrentDB.SchemaRegistry.DestroySchemaRequestValidator;
//
// namespace KurrentDB.SchemaRegistry.Tests.Commands.Validators;
//
// public class DestroySchemaRequestValidatorTests {
//     [Test, InvalidSchemaVersionNameTestCases]
//     public void validate_with_empty_name_should_not_be_valid(string name) {
//         var request = new DestroySchemaRequest {
//             SchemaName = name
//         };
//
//         var result = Instance.Validate(request);
//
//         result.IsValid.Should().BeFalse();
//         result.Errors.Should().Contain(v => v.PropertyName == nameof(DestroySchemaRequest.SchemaName));
//     }
//
//     public class InvalidSchemaVersionNameTestCases : TestCaseGenerator<string> {
//         protected override IEnumerable<string> Data() {
//             yield return "";
//             yield return "   ";
//         }
//     }
// }