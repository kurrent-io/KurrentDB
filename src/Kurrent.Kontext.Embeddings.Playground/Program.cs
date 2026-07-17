// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// Manual developer playground comparing KurrentDB Kontext text-embedding implementations.
// Every column is computed by the REAL generators in the Kurrent.Kontext.Embeddings SDK —
// this file only downloads assets, drives the generators, and renders the comparison.
//
//   A - Prototype EmbeddingService  : the incumbent hand-rolled path (all-MiniLM, hand-rolled
//                                      WordPiece, mean-pool + L2). Baseline; unchanged behavior.
//   B - WordPieceOnnxEmbeddingGenerator: SAME embedded all-MiniLM ONNX + vocab as A, read via
//                                      OnnxModel.FromEmbeddedResources, tokenized with the SDK's
//                                      BERT/WordPiece tokenizer (lowercase + accent-fold + CJK split).
//   C - SentencePieceOnnx / e5-small : multilingual-e5-small fp32, "query: " prefix, mean-pool + L2.
//   D - SentencePieceOnnx / e5-small : IDENTICAL to C but the int8-quantized ONNX. Measures the
//                                      quality cost of quantization vs fp32.
//   + 5 additional multilingual XLM-R / SentencePiece models, ALL via SentencePieceOnnxEmbedding
//     Generator, reusing the shared sentencepiece.bpe.model:
//       paraphrase-mMiniLM-L12 (Mean),  e5-base (Mean, "query: "),  e5-large (Mean, "query: "),
//       paraphrase-mpnet (Mean),        bge-m3 (Cls).
//
// PRIMARY metric = WITHIN-LANGUAGE (monolingual) semantic similarity across 7 languages
// (en/pt/es/fr/de/ja/zh): same-language synonyms (carro≈automóvel, 車≈自動車) should score HIGH and
// same-language off-topic pairs (carro/fotossíntese) LOW. Each model is ranked best → worst by
// mean over languages of (mean similar − mean unrelated), annotated with dim, on-disk ONNX size,
// and a per-language sep breakdown. Cross-lingual alignment (東京/Tokyo, Москва/Moscow) is NOT the
// target and is demoted to a clearly-labeled secondary table.
//
// All large ONNX (+ the shared SentencePiece model) are NOT committed; they are downloaded on
// first run into a gitignored ./models cache. Any model whose download is unavailable (offline)
// is skipped with a note; the remaining columns still print.
//
// Path C can optionally be bit-verified against a transformers.js reference (ref-vectors.json,
// generated with @huggingface/transformers 'Xenova/multilingual-e5-small', "query: " prefix,
// mean pooling + normalize). If ref-vectors.json is absent the verify is skipped with a note.

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Kurrent.Kontext.Embeddings;            // EmbeddingPoolingMode
using Kurrent.Kontext.Embeddings.WordPieceOnnx;
using Kurrent.Kontext.Embeddings.SentencePieceOnnx;
using Kurrent.Kontext.Embeddings.Prototype;  // ModelManager, EmbeddingService (Path A)
using Kurrent.Kontext.Models;                // KontextModelsAssembly (embedded-resource marker)
using Microsoft.Extensions.AI;               // IEmbeddingGenerator<string, Embedding<float>>
using Microsoft.Extensions.Logging;

// ---------------------------------------------------------------------------
// Path resolution. [CallerFilePath] bakes in this file's directory at compile
// time, so the model cache and the ref-vectors fixture resolve next to the
// source regardless of where the binary runs from — no hardcoded absolute paths.
// ---------------------------------------------------------------------------
static string SourceDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

string projectDir = SourceDir();
string modelsDir = Path.Combine(projectDir, "models");
string e5ModelPath = Path.Combine(modelsDir, "model.onnx");
string e5QuantModelPath = Path.Combine(modelsDir, "model_quantized.onnx");
string spmModelPath = Path.Combine(modelsDir, "sentencepiece.bpe.model");
string outDir = AppContext.BaseDirectory;

// e5 assets, downloaded on first run. The fp32 model is the exact file the transformers.js
// reference used (weight-identical), so the C-vs-reference check below is a true bit-verify.
// The int8 model (Path D) is the quantized variant from the same repo.
const string E5ModelUrl = "https://huggingface.co/Xenova/multilingual-e5-small/resolve/main/onnx/model.onnx";
const string E5QuantModelUrl = "https://huggingface.co/Xenova/multilingual-e5-small/resolve/main/onnx/model_quantized.onnx";
const string SpmModelUrl = "https://huggingface.co/intfloat/multilingual-e5-small/resolve/main/sentencepiece.bpe.model";

// The five additional multilingual models. All are XLM-RoBERTa / SentencePiece, so they REUSE the
// shared sentencepiece.bpe.model above; only the (quantized) ONNX differs. Each downloads to a
// gitignored per-model path models/<key>/model_quantized.onnx.
(string Key, string Short, string Url, EmbeddingPoolingMode Pooling, string? Prefix, string ModelId)[] extraModels = [
	("paraphrase-mMiniLM-L12", "pMM12",
		"https://huggingface.co/Xenova/paraphrase-multilingual-MiniLM-L12-v2/resolve/main/onnx/model_quantized.onnx",
		EmbeddingPoolingMode.Mean, null, "paraphrase-multilingual-MiniLM-L12-v2"),
	("e5-base", "e5-base",
		"https://huggingface.co/Xenova/multilingual-e5-base/resolve/main/onnx/model_quantized.onnx",
		EmbeddingPoolingMode.Mean, "query: ", "multilingual-e5-base"),
	("e5-large", "e5-large",
		"https://huggingface.co/Xenova/multilingual-e5-large/resolve/main/onnx/model_quantized.onnx",
		EmbeddingPoolingMode.Mean, "query: ", "multilingual-e5-large"),
	("paraphrase-mpnet", "pMPNet",
		"https://huggingface.co/Xenova/paraphrase-multilingual-mpnet-base-v2/resolve/main/onnx/model_quantized.onnx",
		EmbeddingPoolingMode.Mean, null, "paraphrase-multilingual-mpnet-base-v2"),
	("bge-m3", "bge-m3",
		"https://huggingface.co/Xenova/bge-m3/resolve/main/onnx/model_quantized.onnx",
		EmbeddingPoolingMode.Cls, null, "bge-m3"),
];

// ref-vectors.json is copied to the output dir; fall back to the source dir.
string refVectorsPath = File.Exists(Path.Combine(outDir, "ref-vectors.json"))
	? Path.Combine(outDir, "ref-vectors.json")
	: Path.Combine(projectDir, "ref-vectors.json");

// ---------------------------------------------------------------------------
// 0. ICU sanity gate. BERT accent-stripping (String.Normalize FormD) is a silent
//    no-op under InvariantGlobalization, which would silently reproduce the accent
//    bug in Path B. Assert café -> cafe at startup and fail loudly otherwise.
// ---------------------------------------------------------------------------
static string StripAccents(string s) {
	var d = s.Normalize(NormalizationForm.FormD);
	var sb = new StringBuilder(d.Length);
	foreach (var ch in d)
		if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
			sb.Append(ch);
	return sb.ToString().Normalize(NormalizationForm.FormC);
}

string stripped;
try {
	stripped = StripAccents("café");
} catch (Exception ex) {
	throw new InvalidOperationException(
		"ICU is not active (String.Normalize threw). Set InvariantGlobalization=false. " +
		"Without ICU, BERT accent-folding is a no-op and Path B cannot fix the accent bug.", ex);
}
if (stripped != "cafe")
	throw new InvalidOperationException(
		$"ICU sanity check FAILED: StripAccents(\"café\") = \"{stripped}\", expected \"cafe\". " +
		"The process is running invariant (no ICU); Path B accent-folding would be a silent no-op.");
Console.WriteLine($"[ICU gate] OK: StripAccents(\"café\") = \"{stripped}\"");

// ---------------------------------------------------------------------------
// Shared example set. EXACTLY the string set embedded by the transformers.js reference,
// so every Path C vector can be bit-verified against ref-vectors.json.
// ---------------------------------------------------------------------------
// The reference-verified set: EXACTLY the strings embedded by the transformers.js reference, so
// every Path C vector over THIS set can be bit-verified against ref-vectors.json. Do NOT edit it.
string[] refInputs = [
	// English
	"hello world", "event-native database",
	// Accented Latin
	"café", "cafe", "José", "Jose", "naïve", "naive",
	// Portuguese
	"São Paulo", "Sao Paulo", "informação", "informacao", "cão", "dog",
	// Japanese
	"東京", "Tokyo", "こんにちは世界", "Hello world", "日本語", "Japanese language",
	// Cyrillic
	"Москва", "Moscow", "Россия", "Russia", "Привет мир",
	// FAR control target
	"quantum entanglement",
];

// PRIMARY EVAL — WITHIN-LANGUAGE (monolingual) semantic similarity. For each language, SIMILAR
// synonym/paraphrase pairs should score HIGH and UNRELATED same-language pairs (different topics)
// should score LOW. The target metric is the per-language SEPARATION (mean similar − mean unrelated);
// cross-lingual alignment is explicitly NOT the goal (it is demoted to a secondary table below).
string[] langs = ["en", "pt", "es", "fr", "de", "ja", "zh"];
(string Lang, string A, string B, string Kind)[] withinLang = [
	// English
	("en", "car", "automobile", "similar"),   ("en", "happy", "joyful", "similar"),
	("en", "doctor", "physician", "similar"), ("en", "big", "large", "similar"),
	("en", "car", "photosynthesis", "unrelated"), ("en", "ocean", "keyboard", "unrelated"),
	// Portuguese
	("pt", "carro", "automóvel", "similar"),  ("pt", "feliz", "alegre", "similar"),
	("pt", "médico", "doutor", "similar"),    ("pt", "grande", "enorme", "similar"),
	("pt", "carro", "fotossíntese", "unrelated"), ("pt", "oceano", "teclado", "unrelated"),
	// Spanish
	("es", "coche", "automóvil", "similar"),  ("es", "feliz", "contento", "similar"),
	("es", "médico", "doctor", "similar"),    ("es", "grande", "enorme", "similar"),
	("es", "coche", "fotosíntesis", "unrelated"), ("es", "océano", "teclado", "unrelated"),
	// French
	("fr", "voiture", "automobile", "similar"), ("fr", "heureux", "joyeux", "similar"),
	("fr", "médecin", "docteur", "similar"),  ("fr", "grand", "énorme", "similar"),
	("fr", "voiture", "photosynthèse", "unrelated"), ("fr", "océan", "clavier", "unrelated"),
	// German
	("de", "Auto", "Wagen", "similar"),       ("de", "glücklich", "fröhlich", "similar"),
	("de", "Arzt", "Doktor", "similar"),      ("de", "groß", "riesig", "similar"),
	("de", "Auto", "Photosynthese", "unrelated"), ("de", "Ozean", "Tastatur", "unrelated"),
	// Japanese
	("ja", "車", "自動車", "similar"),           ("ja", "医者", "医師", "similar"),
	("ja", "幸せ", "嬉しい", "similar"),          ("ja", "大きい", "巨大", "similar"),
	("ja", "車", "光合成", "unrelated"),         ("ja", "海", "キーボード", "unrelated"),
	// Chinese
	("zh", "汽车", "轿车", "similar"),           ("zh", "医生", "大夫", "similar"),
	("zh", "高兴", "快乐", "similar"),           ("zh", "大", "巨大", "similar"),
	("zh", "汽车", "光合作用", "unrelated"),      ("zh", "海洋", "键盘", "unrelated"),
];

// Extra examples NOT in the reference fixture (so they are excluded from the C bit-verify): every
// within-language word above, plus the plain-English near / distant words used by the secondary
// cross-lingual table (dog/puppy, banana/photosynthesis).
string[] extraInputs = [
	.. withinLang.SelectMany(p => new[] { p.A, p.B }).Concat(new[] { "puppy", "banana", "photosynthesis" }).Distinct(),
];

// Everything is embedded by every model; only refInputs feed the reference bit-verify.
string[] inputs = [.. refInputs, .. extraInputs];

// SECONDARY EVAL — the original cross-lingual / accent pairs. Kept for context only; this is NOT the
// target metric (see the within-language ranking). Kinds "near"/"accent"/"cross-lingual" should
// score HIGH; "FAR" should score LOW.
(string A, string B, string Group, string Kind)[] pairs = [
	("car",          "automobile",           "English",        "near"),
	("happy",        "joyful",               "English",        "near"),
	("dog",          "puppy",                "English",        "near"),

	("café",         "cafe",                 "Accented Latin", "accent"),
	("José",         "Jose",                 "Accented Latin", "accent"),
	("naïve",        "naive",                "Accented Latin", "accent"),

	("São Paulo",    "Sao Paulo",            "Portuguese",     "accent"),
	("informação",   "informacao",           "Portuguese",     "accent"),
	("cão",          "dog",                  "Portuguese",     "cross-lingual"),

	("東京",          "Tokyo",                "Japanese",       "cross-lingual"),
	("こんにちは世界", "Hello world",          "Japanese",       "cross-lingual"),
	("日本語",        "Japanese language",    "Japanese",       "cross-lingual"),

	("Москва",       "Moscow",               "Cyrillic",       "cross-lingual"),
	("Россия",       "Russia",               "Cyrillic",       "cross-lingual"),
	("Привет мир",   "Hello world",          "Cyrillic",       "cross-lingual"),

	("Москва",       "quantum entanglement", "Control",        "FAR"),
	("dog",          "quantum entanglement", "Control",        "FAR"),
	("banana",       "photosynthesis",       "Control",        "FAR"),
];

// Warning level only: the interesting output goes to Console.WriteLine below, not the loggers.
using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));

// ===========================================================================
// PATH A — the incumbent Prototype EmbeddingService (all-MiniLM, embedded ONNX).
// ===========================================================================
Console.WriteLine("\n[Path A] running Prototype EmbeddingService (hand-rolled WordPiece) …");
var modelManager = new ModelManager(loggerFactory.CreateLogger<ModelManager>());
Dictionary<string, float[]> vecA;
using (var service = new EmbeddingService(modelManager, loggerFactory.CreateLogger<EmbeddingService>())) {
	await service.InitializeAsync();
	vecA = await EmbedAllAsync(service, inputs);
}
int dimA = vecA[inputs[0]].Length;
Console.WriteLine($"[Path A] embedded {vecA.Count} inputs, dim={dimA}");

// ===========================================================================
// PATH B — WordPieceOnnxEmbeddingGenerator over the SAME all-MiniLM ONNX + vocab as A.
// B reads the embedded all-MiniLM directly via OnnxModel.FromEmbeddedResources (the avx2 int8 variant,
// which is what A's ModelManager also loads on ARM), so it is provably the same generator as A — only the
// tokenizer differs — while being decoupled from the prototype ModelManager.
// ===========================================================================
Console.WriteLine("\n[Path B] running WordPieceOnnxEmbeddingGenerator (all-MiniLM + WordPiece) …");
Dictionary<string, float[]> vecB;
var bModel = OnnxModel.FromEmbeddedResources(
	"all-MiniLM-L6-v2",
	typeof(KontextModelsAssembly).Assembly,
	"KurrentDB.Kontext.Models.embedding.model_quint8_avx2.onnx",
	new Dictionary<string, string> { ["vocab.txt"] = "KurrentDB.Kontext.Models.embedding.vocab.txt" });
using (var genB = new WordPieceOnnxEmbeddingGenerator(bModel))
	vecB = await EmbedAllAsync(genB, inputs);
int dimB = vecB[inputs[0]].Length;
// A and B run the same embedded all-MiniLM ONNX (the avx2 int8 variant on this platform), so they share a size.
long sizeAB = modelManager.EmbeddingModel.LongLength;
Console.WriteLine($"[Path B] embedded {vecB.Count} inputs, dim={dimB}");

// ===========================================================================
// PATH C — multilingual-e5-small fp32; PATH D — same, int8-quantized ONNX.
// Both via SentencePieceOnnxEmbeddingGenerator (XLM-R SentencePiece + fairseq remap).
// Assets downloaded on first run; each path is skipped independently if its ONNX is unavailable.
// ===========================================================================
Console.WriteLine("\n[Path C/D] running multilingual-e5-small (e5) fp32 + int8 …");
bool hasSpm = await TryDownloadAsync(spmModelPath, SpmModelUrl);
bool hasC = hasSpm && await TryDownloadAsync(e5ModelPath, E5ModelUrl);
bool hasD = hasSpm && await TryDownloadAsync(e5QuantModelPath, E5QuantModelUrl);

var vecC = new Dictionary<string, float[]>();
var vecD = new Dictionary<string, float[]>();
if (hasC)
	vecC = await EmbedSentencePieceAsync(e5ModelPath, spmModelPath,
		new SentencePieceOnnxOptions { InputPrefix = "query: ", ModelId = "multilingual-e5-small" }, inputs, "Path C fp32");
else
	Console.WriteLine("[Path C] SKIPPED: fp32 e5 asset unavailable (download failed / offline).");
if (hasD)
	vecD = await EmbedSentencePieceAsync(e5QuantModelPath, spmModelPath,
		new SentencePieceOnnxOptions { InputPrefix = "query: ", ModelId = "multilingual-e5-small-int8" }, inputs, "Path D int8");
else
	Console.WriteLine("[Path D] SKIPPED: int8 e5 asset unavailable (download failed / offline).");
long sizeC = hasC ? new FileInfo(e5ModelPath).Length : 0;
long sizeD = hasD ? new FileInfo(e5QuantModelPath).Length : 0;

// ===========================================================================
// 5 ADDITIONAL multilingual models — all XLM-R / SentencePiece, all via the same
// SentencePieceOnnxEmbeddingGenerator, reusing the shared sentencepiece.bpe.model.
// Each is downloaded (quantized ONNX only) resiliently and skipped independently on failure.
// ===========================================================================
var extraVecs = new Dictionary<string, Dictionary<string, float[]>>();
var extraDims = new Dictionary<string, int>();
var extraSizes = new Dictionary<string, long>();
if (hasSpm) {
	foreach (var m in extraModels) {
		Console.WriteLine($"\n[{m.Key}] {m.ModelId} (pooling={m.Pooling}, prefix={(m.Prefix is null ? "none" : $"\"{m.Prefix}\"")}) …");
		var onnxPath = Path.Combine(modelsDir, m.Key, "model_quantized.onnx");
		if (!await TryDownloadAsync(onnxPath, m.Url)) {
			Console.WriteLine($"[{m.Key}] SKIPPED: quantized ONNX unavailable (download failed / offline).");
			continue;
		}
		var opts = new SentencePieceOnnxOptions { PoolingMode = m.Pooling, InputPrefix = m.Prefix, ModelId = m.ModelId };
		var v = await EmbedSentencePieceAsync(onnxPath, spmModelPath, opts, inputs, m.Key);
		extraVecs[m.Key] = v;
		extraDims[m.Key] = v[inputs[0]].Length;
		extraSizes[m.Key] = new FileInfo(onnxPath).Length;

		// Sanity: the cross-lingual pair must be clearly above the FAR control. Absolute levels differ
		// by model (e5 has a ~0.74 floor; bge-m3 CLS runs lower than the mean-pooled models), so the
		// real garbage test is the SEPARATION, not an absolute cutoff. A near≈far result (or a very low
		// near) means the shared XLM-R sentencepiece.bpe.model does not match this model's tokenizer.
		var near = Cosine(v["Москва"], v["Moscow"]);
		var far = Cosine(v["Москва"], v["quantum entanglement"]);
		var verdict = near >= 0.6 && near > far + 0.15 ? "OK" : "CHECK (shared spm may not match this model)";
		Console.WriteLine($"[{m.Key}] sanity: Москва/Moscow={near:F4} (want high), Москва/quantum={far:F4} (want clearly lower) => {verdict}");
	}
} else {
	Console.WriteLine("\n[extra models] SKIPPED: shared sentencepiece.bpe.model unavailable (download failed / offline).");
}

// ===========================================================================
// VERIFY C (fp32) and D (int8) vs transformers.js reference. C should bit-verify (>= 0.9999);
// D should sit a little lower (~0.99) — that gap is the int8 quantization quality cost. Optional.
// ===========================================================================
bool refOk = false;
double minRefCos = double.MaxValue, meanRefCos = 0;
double minRefCosD = double.MaxValue, meanRefCosD = 0;
string worst = "", worstD = "";
Dictionary<string, float[]>? refVecs = null;
if ((hasC || hasD) && File.Exists(refVectorsPath)) {
	Console.WriteLine("\n[Verify] C/D vs transformers.js reference (ref-vectors.json) …");
	refVecs = JsonSerializer.Deserialize<Dictionary<string, float[]>>(File.ReadAllText(refVectorsPath))!;
	// Only the frozen refInputs are expected in the fixture; extraInputs are intentionally absent.
	var missing = refInputs.Where(t => !refVecs.ContainsKey(t)).ToList();
	if (missing.Count > 0)
		Console.WriteLine($"[Verify] MISSING from reference: {string.Join(", ", missing)}");
	if (hasC) {
		(minRefCos, meanRefCos, worst) = RefStats(vecC);
		refOk = missing.Count == 0 && minRefCos >= 0.9999;
		Console.WriteLine($"[Verify] C fp32: min={minRefCos:F6} (worst \"{worst}\"), mean={meanRefCos:F6}  =>  {(refOk ? "PASS (>= 0.9999)" : "FAIL")}");
	}
	if (hasD) {
		(minRefCosD, meanRefCosD, worstD) = RefStats(vecD);
		Console.WriteLine($"[Verify] D int8: min={minRefCosD:F6} (worst \"{worstD}\"), mean={meanRefCosD:F6}  (expected ~0.99 — below fp32)");
	}
} else if (hasC || hasD) {
	Console.WriteLine($"\n[Verify] SKIPPED: reference vectors not found at {refVectorsPath}. Table still prints.");
}

// ===========================================================================
// PRIMARY RANKING — WITHIN-LANGUAGE (monolingual) semantic similarity.
//   For each language L: sepL = mean(cosine of SIMILAR pairs in L) − mean(cosine of UNRELATED pairs in L).
//   withinLangScore = mean(sepL over the 7 languages). Models are ranked best → worst by this score.
// A good model pulls same-language synonyms together AND keeps same-language off-topic pairs apart,
// so a larger per-language gap = better. Cross-lingual alignment is demoted to a secondary table.
// ===========================================================================
var sb = new StringBuilder();
void Emit(string line) { Console.WriteLine(line); sb.AppendLine(line); }

static string SizeMb(long bytes) => bytes > 0 ? $"{bytes / 1048576.0:F0} MB" : "n/a";

string CosOrNa(Dictionary<string, float[]> v, string a, string b) =>
	v.ContainsKey(a) && v.ContainsKey(b) ? Cosine(v[a], v[b]).ToString("F4") : "—";

// Build every available column: (short label, full name, vectors, dim, on-disk model size).
var cols = new List<(string Short, string Full, Dictionary<string, float[]> Vecs, int Dim, long Size)>();
cols.Add(("A", "Prototype EmbeddingService (all-MiniLM, hand-rolled)", vecA, dimA, sizeAB));
cols.Add(("B", "WordPieceOnnx (all-MiniLM)", vecB, dimB, sizeAB));
if (hasC) cols.Add(("C", "multilingual-e5-small fp32 (SentencePieceOnnx)", vecC, vecC[inputs[0]].Length, sizeC));
if (hasD) cols.Add(("D", "multilingual-e5-small int8 (SentencePieceOnnx)", vecD, vecD[inputs[0]].Length, sizeD));
foreach (var m in extraModels)
	if (extraVecs.TryGetValue(m.Key, out var v))
		cols.Add((m.Short, m.ModelId + " (SentencePieceOnnx, " + m.Pooling + ")", v, extraDims[m.Key], extraSizes[m.Key]));

// Per-column, per-language separation + overall within-language score; sorted best → worst.
const double WeakSep = 0.10;   // sep below this = similar not clearly above unrelated (weak language)
var wl = new List<(string Short, string Full, Dictionary<string, float[]> Vecs, int Dim, long Size, double Overall, double[] Sep)>();
foreach (var c in cols) {
	var sep = new double[langs.Length];
	for (var li = 0; li < langs.Length; li++) {
		double simSum = 0, unSum = 0; int simN = 0, unN = 0;
		foreach (var (l, a, b, kind) in withinLang) {
			if (l != langs[li] || !c.Vecs.ContainsKey(a) || !c.Vecs.ContainsKey(b)) continue;
			var cos = Cosine(c.Vecs[a], c.Vecs[b]);
			if (kind == "similar") { simSum += cos; simN++; } else { unSum += cos; unN++; }
		}
		sep[li] = (simN > 0 ? simSum / simN : 0) - (unN > 0 ? unSum / unN : 0);
	}
	wl.Add((c.Short, c.Full, c.Vecs, c.Dim, c.Size, sep.Average(), sep));
}
wl.Sort((x, y) => y.Overall.CompareTo(x.Overall));

// --- Primary ranking table: model × (overall + per-language sep) ---
int rankLineW = 3 + 1 + 30 + 1 + 5 + 1 + 8 + 1 + 8 + langs.Length * 7;
Emit("");
Emit(new string('=', rankLineW));
Emit(" WITHIN-LANGUAGE ranking (best → worst): score = mean over 7 langs of (similar − unrelated)");
Emit(new string('=', rankLineW));
var rh = new StringBuilder();
rh.Append("#".PadRight(3)).Append(' ').Append("model".PadRight(30)).Append(' ')
	.Append("dim".PadRight(5)).Append(' ').Append("size".PadRight(8)).Append(' ').Append("overall".PadRight(8));
foreach (var l in langs) rh.Append(l.PadLeft(7));
Emit(rh.ToString());
Emit(new string('-', rankLineW));
for (var i = 0; i < wl.Count; i++) {
	var r = wl[i];
	var label = Trunc($"{r.Short}: {r.Full}", 30);
	var row = new StringBuilder();
	row.Append((i + 1).ToString().PadRight(3)).Append(' ').Append(label.PadRight(30)).Append(' ')
		.Append(r.Dim.ToString().PadRight(5)).Append(' ').Append(SizeMb(r.Size).PadRight(8)).Append(' ')
		.Append(r.Overall.ToString("F4").PadRight(8));
	foreach (var s in r.Sep) row.Append(s.ToString("F3").PadLeft(7));
	Emit(row.ToString());
}
Emit(new string('-', rankLineW));

// --- Per-model weak-language notes ---
Emit("");
Emit($"Per-model weak languages (within-language sep < {WeakSep:F2} — similar not clearly above unrelated):");
foreach (var r in wl) {
	var weak = new List<string>();
	for (var li = 0; li < langs.Length; li++)
		if (r.Sep[li] < WeakSep) weak.Add($"{langs[li]} ({r.Sep[li]:F3})");
	var note = weak.Count == 0 ? "none — all 7 languages separate cleanly" : string.Join(", ", weak);
	Emit($"  {r.Short,-9} {note}");
}

// --- Within-language cosine per pair (PRIMARY), columns in within-language rank order ---
const int LangW = 4, WPairW = 26, WKindW = 6, WValW = 9;
int wLead = LangW + 1 + WPairW + 1 + WKindW;
int wLineW = wLead + wl.Count * WValW;
Emit("");
Emit(new string('=', wLineW));
Emit(" WITHIN-LANGUAGE cosine per pair (PRIMARY; columns ordered best → worst)");
Emit(new string('=', wLineW));
var wRankRow = new StringBuilder(new string(' ', wLead));
for (var i = 0; i < wl.Count; i++) wRankRow.Append(("#" + (i + 1)).PadLeft(WValW));
Emit(wRankRow.ToString());
var wLabelRow = new StringBuilder();
wLabelRow.Append("lang".PadRight(LangW)).Append(' ').Append("pair".PadRight(WPairW)).Append(' ').Append("kind".PadRight(WKindW));
foreach (var r in wl) wLabelRow.Append(r.Short.PadLeft(WValW));
Emit(wLabelRow.ToString());
Emit(new string('-', wLineW));
string lastLang = "";
foreach (var (l, a, b, kind) in withinLang) {
	if (l != lastLang && lastLang != "") Emit("");
	lastLang = l;
	var kindShort = kind == "unrelated" ? "unrel" : "sim";
	var pairLabel = a + " / " + b;
	var row = new StringBuilder();
	row.Append(l.PadRight(LangW)).Append(' ').Append(Trunc(pairLabel, WPairW).PadRight(WPairW)).Append(' ').Append(kindShort.PadRight(WKindW));
	foreach (var r in wl) row.Append(CosOrNa(r.Vecs, a, b).PadLeft(WValW));
	Emit(row.ToString());
}
Emit(new string('-', wLineW));
Emit("Legend: 'sim' pairs SHOULD be high, 'unrel' pairs LOW; the ranking metric is (sim − unrel) per language.");
Emit("        Cosine is computed WITHIN each model; absolute levels differ by model (e5 has a high floor).");

// ===========================================================================
// SECONDARY — cross-lingual / accent table. NOT the target metric; kept for context. Columns are
// shown in the same (within-language) rank order as above so the two tables line up.
// ===========================================================================
const int GroupW = 14, PairW = 26, KindW = 6, ValW = 9;
int leadW = GroupW + 1 + PairW + 1 + KindW;
int lineW = leadW + wl.Count * ValW;
Emit("");
Emit(new string('=', lineW));
Emit(" SECONDARY — cross-lingual / accent (NOT the target metric; columns in within-language rank order)");
Emit(new string('=', lineW));
var sLabelRow = new StringBuilder();
sLabelRow.Append("group".PadRight(GroupW)).Append(' ').Append("pair".PadRight(PairW)).Append(' ').Append("kind".PadRight(KindW));
foreach (var r in wl) sLabelRow.Append(r.Short.PadLeft(ValW));
Emit(sLabelRow.ToString());
Emit(new string('-', lineW));
string lastGroup = "";
foreach (var (a, b, group, kind) in pairs) {
	if (group != lastGroup && lastGroup != "") Emit("");
	lastGroup = group;
	var kindShort = kind == "cross-lingual" ? "xling" : kind == "accent" ? "acc" : kind;
	var pairLabel = a + " / " + b;
	var row = new StringBuilder();
	row.Append(Trunc(group, GroupW).PadRight(GroupW)).Append(' ')
		.Append(Trunc(pairLabel, PairW).PadRight(PairW)).Append(' ')
		.Append(kindShort.PadRight(KindW));
	foreach (var r in wl) row.Append(CosOrNa(r.Vecs, a, b).PadLeft(ValW));
	Emit(row.ToString());
}
Emit(new string('-', lineW));
Emit("Legend: cross-lingual pairs (東京/Tokyo, Москва/Moscow) are NOT the target — informational only.");
if (!hasC) Emit("Note: column C is n/a (fp32 e5 asset unavailable this run).");
if (!hasD) Emit("Note: column D is n/a (int8 e5 asset unavailable this run).");
foreach (var m in extraModels)
	if (!extraVecs.ContainsKey(m.Key) && hasSpm)
		Emit($"Note: column {m.Short} is n/a ({m.Key} asset unavailable this run).");

// B reproduces A on plain ASCII (same ONNX; WordPieceOnnx tokenizer == hand-rolled WordPiece for ASCII).
Emit("");
Emit("=== B reproduces A on plain ASCII: cosine(A_vec, B_vec) (expect ~1.0) ===");
string[] asciiInputs = ["hello world", "event-native database"];
double minAsciiAgree = double.MaxValue;
foreach (var text in asciiInputs) {
	var agree = Cosine(vecA[text], vecB[text]);
	if (agree < minAsciiAgree) minAsciiAgree = agree;
	Emit($"  {text,-24} cosine(A,B) = {agree:F6}");
}
Emit($"  => {(minAsciiAgree >= 0.9999 ? "PASS" : "CHECK")}: B is the same generator as A on ASCII (min {minAsciiAgree:F6})");

// Quantization drift: how far int8 (D) moves each vector away from fp32 (C).
if (hasC && hasD) {
	Emit("");
	Emit("=== Quantization drift: cosine(C fp32, D int8) per input ===");
	Emit($"{"input",-24} cos(C,D)");
	Emit(new string('-', 40));
	double minCD = double.MaxValue, sumCD = 0; string worstCD = "";
	foreach (var text in inputs) {
		var c = Cosine(vecC[text], vecD[text]);
		sumCD += c;
		if (c < minCD) { minCD = c; worstCD = text; }
		Emit($"{Trunc(text, 22),-24} {c:F6}");
	}
	Emit(new string('-', 40));
	Emit($"min = {minCD:F6} (worst: \"{worstCD}\"), mean = {sumCD / inputs.Length:F6}  (1.0 = int8 identical to fp32)");
}

// e5 correctness vs transformers.js reference (fp32 should bit-verify; int8 sits a bit lower).
if (refVecs is not null) {
	Emit("");
	Emit("=== e5-small correctness vs transformers.js reference (per input) ===");
	Emit($"{"input",-24} {"cos(C,ref)",-12} {"cos(D,ref)",-12}");
	Emit(new string('-', 50));
	foreach (var text in inputs) {
		if (!refVecs.TryGetValue(text, out var r)) continue;
		var cC = hasC ? Cosine(vecC[text], r).ToString("F6") : "—";
		var cD = hasD ? Cosine(vecD[text], r).ToString("F6") : "—";
		Emit($"{Trunc(text, 22),-24} {cC,-12} {cD,-12}");
	}
	Emit(new string('-', 50));
	if (hasC) Emit($"C fp32: min={minRefCos:F6} mean={meanRefCos:F6}  =>  {(refOk ? "PASS (>= 0.9999): C# e5 path is correct" : "FAIL")}");
	if (hasD) Emit($"D int8: min={minRefCosD:F6} mean={meanRefCosD:F6}  (quantization quality cost vs fp32)");
}

// ---------------------------------------------------------------------------
// Persist artifacts into the (gitignored) build output dir so the source tree stays clean.
// ---------------------------------------------------------------------------
var jsonOpts = new JsonSerializerOptions { WriteIndented = false };
File.WriteAllText(Path.Combine(outDir, "vectors-A.json"), JsonSerializer.Serialize(vecA, jsonOpts));
File.WriteAllText(Path.Combine(outDir, "vectors-B.json"), JsonSerializer.Serialize(vecB, jsonOpts));
if (hasC)
	File.WriteAllText(Path.Combine(outDir, "vectors-C.json"), JsonSerializer.Serialize(vecC, jsonOpts));
if (hasD)
	File.WriteAllText(Path.Combine(outDir, "vectors-D.json"), JsonSerializer.Serialize(vecD, jsonOpts));
foreach (var (key, v) in extraVecs)
	File.WriteAllText(Path.Combine(outDir, $"vectors-{key}.json"), JsonSerializer.Serialize(v, jsonOpts));
File.WriteAllText(Path.Combine(outDir, "comparison-results.txt"), sb.ToString());
Console.WriteLine($"\n[done] wrote vectors-*.json + comparison-results.txt to {outDir}");
if (hasC && refVecs is not null)
	Console.WriteLine($"[done] C-vs-reference: {(refOk ? "PASS" : "FAIL")} (min cosine {minRefCos:F6})");
if (hasD && refVecs is not null)
	Console.WriteLine($"[done] D-vs-reference: min cosine {minRefCosD:F6}, mean {meanRefCosD:F6}");

// ===========================================================================
// Helpers
// ===========================================================================

// Runs a whole input set through a generator and captures each vector by its source text.
static async Task<Dictionary<string, float[]>> EmbedAllAsync(IEmbeddingGenerator<string, Embedding<float>> generator, string[] inputs) {
	var generated = await generator.GenerateAsync(inputs);
	var vectors = new Dictionary<string, float[]>(inputs.Length);
	for (var i = 0; i < inputs.Length; i++)
		vectors[inputs[i]] = generated[i].Vector.ToArray();
	return vectors;
}

// Builds a SentencePiece/XLM-R generator from an ONNX file + the shared SentencePiece model, runs
// the input set through it, and reports the dimension. It builds an OnnxModel over the ONNX file + the
// shared SentencePiece asset; the generator opens them and owns the resulting session.
static async Task<Dictionary<string, float[]>> EmbedSentencePieceAsync(
	string onnxPath, string spmPath, SentencePieceOnnxOptions options, string[] inputs, string label) {
	var model = OnnxModel.FromFiles(options.ModelId ?? label, onnxPath,
		new Dictionary<string, string> { ["sentencepiece.bpe.model"] = spmPath });
	using var generator = new SentencePieceOnnxEmbeddingGenerator(model, options);
	var vectors = await EmbedAllAsync(generator, inputs);
	Console.WriteLine($"[{label}] embedded {vectors.Count} inputs, dim={vectors[inputs[0]].Length}");
	return vectors;
}

// Per-file download into the gitignored cache. Creates the destination's parent directory and
// returns false (skip that column) on any failure.
static async Task<bool> TryDownloadAsync(string destPath, string url) {
	try {
		Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
		using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
		await DownloadIfMissingAsync(http, url, destPath);
		return File.Exists(destPath) && new FileInfo(destPath).Length > 0;
	} catch (Exception ex) {
		Console.WriteLine($"[download] failed for {url}: {ex.Message}");
		return false;
	}
}

static async Task DownloadIfMissingAsync(HttpClient http, string url, string destPath) {
	if (File.Exists(destPath) && new FileInfo(destPath).Length > 0) return;
	Console.WriteLine($"[download] {url}\n           -> {destPath}");
	var tmp = destPath + ".partial";
	using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead)) {
		resp.EnsureSuccessStatusCode();
		await using var src = await resp.Content.ReadAsStreamAsync();
		await using var dst = File.Create(tmp);
		await src.CopyToAsync(dst);
	}
	File.Move(tmp, destPath, overwrite: true);
	Console.WriteLine($"[download] done ({new FileInfo(destPath).Length:N0} bytes)");
}

// Cosine stats (min / mean / worst input) of an embedding set vs the reference vectors.
(double Min, double Mean, string Worst) RefStats(Dictionary<string, float[]> v) {
	double min = double.MaxValue, sum = 0; int n = 0; string w = "";
	foreach (var text in inputs) {
		if (!refVecs!.TryGetValue(text, out var r)) continue;
		var c = Cosine(v[text], r);
		sum += c; n++;
		if (c < min) { min = c; w = text; }
	}
	return (min, n > 0 ? sum / n : 0, w);
}

static double Cosine(float[] a, float[] b) {
	double dot = 0, na = 0, nb = 0;
	for (var i = 0; i < a.Length; i++) {
		dot += (double)a[i] * b[i];
		na += (double)a[i] * a[i];
		nb += (double)b[i] * b[i];
	}
	return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
}

static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
