// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Embeddings.SentencePieceOnnx;
using Kurrent.Kontext.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Kurrent.Kontext.Embeddings;

/// <summary>
/// Interim composition-root wiring for the multilingual production model, used until the
/// <c>HuggingFace.ModelDownloader</c> lands (see
/// <c>.claude/context/docs/designs/2026-07-14-onnx-model-loading-bridge/design.md</c>). It resolves
/// paraphrase-multilingual-MiniLM-L12-v2 (pMM12) straight from the ONNX models embedded in the
/// <c>KurrentDB.Kontext.Models</c> assembly via <see cref="OnnxModel.FromEmbeddedResources"/> — no
/// <see cref="OnnxModelRegistry"/>, no on-disk cache, and zero coupling to the legacy prototype loader.
/// <para>
/// When the downloader lands the registry path takes over: point <c>AddOnnxModelRegistry</c> at the populated
/// cache directory, switch to the <c>(OnnxModelRegistry, options)</c> constructor, and delete this helper, the
/// build-time embed target, and <see cref="KontextModelsAssembly"/>. Only the acquire side changes.
/// </para>
/// </summary>
public static class InterimEmbeddingsServiceCollectionExtensions {
	// Stable logical-names of the pMM12 resources embedded by the KurrentDB.Kontext.Models build target.
	const string Pmm12ModelResource = "KurrentDB.Kontext.Models.pmm12.model.onnx";
	const string Pmm12TokenizerResource = "KurrentDB.Kontext.Models.pmm12.sentencepiece.bpe.model";

	extension(IServiceCollection services) {
		/// <summary>
		/// Registers the interim multilingual generator — pMM12 read from the embedded models assembly and run
		/// through the SentencePiece / XLM-R generator (implementation C). pMM12 uses no input prefix.
		/// </summary>
		public EmbeddingGeneratorBuilder<string, Embedding<float>> AddInterimPmm12Embeddings() {
			// FromEmbeddedResources is lazy — no bytes move here; the generator opens them when it is built.
			var model = OnnxModel.FromEmbeddedResources(
				"paraphrase-multilingual-MiniLM-L12-v2",
				typeof(KontextModelsAssembly).Assembly,
				Pmm12ModelResource,
				new Dictionary<string, string> { ["sentencepiece.bpe.model"] = Pmm12TokenizerResource });

			return services.AddEmbeddingGenerator(_ =>
				new SentencePieceOnnxEmbeddingGenerator(model, new SentencePieceOnnxOptions { InputPrefix = null }));
		}
	}
}
