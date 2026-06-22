// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reflection;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Kontext.Embeddings;

/// <summary>
/// Loads ONNX models embedded in the KurrentDB.Kontext.Models assembly into memory.
/// InferenceSession and WordPieceTokenizer both accept byte arrays directly, so we
/// don't need to extract anything to disk.
/// </summary>
public class ModelManager {
	const string ModelsAssemblyName = "KurrentDB.Kontext.Models";

	readonly ILogger<ModelManager> _logger;
	readonly Assembly _modelsAssembly;

	public ModelManager(ILogger<ModelManager> logger) {
		_logger = logger;
		_modelsAssembly = Assembly.Load(ModelsAssemblyName);
	}

	public byte[] EmbeddingModel => field ??=
		Load($"embedding.{BestQuantizedModel}.onnx");
	public byte[] EmbeddingVocab => field ??=
		Load("embedding.vocab.txt");
	public byte[] CrossEncoderModel => field ??=
		Load($"cross-encoder.{BestQuantizedModel}.onnx");
	public byte[] CrossEncoderVocab => field ??=
		Load("cross-encoder.vocab.txt");

	string? _bestQuantizedModel;
	/// <summary>
	/// Selects the AVX-512 INT8 quantized variant when the CPU supports AVX-512F,
	/// otherwise the AVX2 variant. Both the embedding and cross-encoder models follow
	/// the same naming convention, so the same suffix applies to both.
	/// </summary>
	string BestQuantizedModel => _bestQuantizedModel ??= DetectBestQuantizedModel();

	string DetectBestQuantizedModel() {
		if (System.Runtime.Intrinsics.X86.Avx512F.IsSupported) {
			_logger.LogInformation("CPU supports AVX-512, using AVX-512 quantized models");
			return "model_qint8_avx512";
		}

		_logger.LogInformation("Using AVX2 quantized models");
		return "model_quint8_avx2";
	}

	byte[] Load(string resourceSuffix) {
		var resourceName = $"{ModelsAssemblyName}.{resourceSuffix}";
		using var stream = _modelsAssembly.GetManifestResourceStream(resourceName)
			?? throw new FileNotFoundException(
				$"Embedded resource not found: {resourceName}. " +
				$"Ensure the {ModelsAssemblyName} project is referenced and models are downloaded.");

		var buffer = new byte[stream.Length];
		stream.ReadExactly(buffer);
		_logger.LogInformation("Loaded {Resource} ({Size:N0} bytes)", resourceSuffix, buffer.Length);
		return buffer;
	}
}