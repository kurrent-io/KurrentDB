// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Models;

/// <summary>
/// Public marker for the KurrentDB.Kontext.Models assembly. Lets callers obtain the assembly that carries the
/// embedded ONNX models via <c>typeof(KontextModelsAssembly).Assembly</c> — reflection-free and AOT-safe (no
/// <c>Assembly.Load(string)</c>). Carries no logic and no dependency on the legacy prototype loader; it exists
/// only so <see cref="System.Reflection.Assembly.GetManifestResourceStream(string)"/> can be reached without
/// loading the assembly by name.
/// </summary>
public sealed class KontextModelsAssembly;
