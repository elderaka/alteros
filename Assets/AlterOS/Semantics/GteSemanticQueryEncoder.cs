using System;
using System.Collections;
using System.Threading.Tasks;
using AlterOS.Content;
using Unity.InferenceEngine;
using Unity.InferenceEngine.Tokenization;
using UnityEngine;

namespace AlterOS.Semantics
{
    public static class GteSemanticAssetPaths
    {
        public const string Model = "AlterOS/Semantics/Models/gte-multilingual-base-fp16";
        public const string Tokenizer = "AlterOS/Semantics/Models/tokenizer";
    }

    public static class GteSemanticQueryEncoder
    {
        const int SequenceLength = 128;
        const int PadTokenId = 1;

        public sealed class InvalidEmbeddingException : InvalidOperationException
        {
            public InvalidEmbeddingException() : base("GTE returned an empty, zero or non-finite embedding.") { }
        }

        public static void ValidateEmbedding(float[] embedding)
        {
            if (embedding == null || embedding.Length == 0)
                throw new InvalidEmbeddingException();
            double squaredNorm = 0;
            foreach (float value in embedding)
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                    throw new InvalidEmbeddingException();
                squaredNorm += (double)value * value;
            }
            if (squaredNorm == 0)
                throw new InvalidEmbeddingException();
        }

        public static float[] EncodeWithCpuFallback(ref Worker worker, ITokenizer tokenizer, string text)
        {
            try { return Encode(worker, tokenizer, text); }
            catch (InvalidEmbeddingException) when (worker.backendType == BackendType.GPUCompute)
            {
                var asset = Resources.Load<ModelAsset>(GteSemanticAssetPaths.Model);
                if (asset == null)
                    throw new InvalidOperationException("GTE model is unavailable for CPU fallback.");
                var cpu = new Worker(ModelLoader.Load(asset), BackendType.CPU);
                float[] embedding;
                try { embedding = Encode(cpu, tokenizer, text); }
                catch { cpu.Dispose(); throw; }
                // Replace the unhealthy GPU worker only after CPU produced valid output.
                worker.Dispose();
                worker = cpu;
                Debug.LogWarning("AlterOS GTE GPU output was invalid; continuing on the validated CPU backend.");
                return embedding;
            }
        }

        public static float[] Encode(Worker worker, ITokenizer tokenizer, string text)
        {
            if (worker == null)
                throw new ArgumentNullException(nameof(worker));
            if (tokenizer == null)
                throw new ArgumentNullException(nameof(tokenizer));
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Query text is empty.", nameof(text));

            CreateInputTensors(tokenizer, text, out Tensor<int> inputIds, out Tensor<int> attentionMask);
            using (inputIds)
            using (attentionMask)
            {
                worker.Schedule(inputIds, attentionMask);
                var output = worker.PeekOutput("embedding") as Tensor<float>;
                if (output == null)
                    throw new InvalidOperationException("GTE model did not produce a float embedding output.");
                using (var cpuOutput = output.ReadbackAndClone() as Tensor<float>)
                {
                    if (cpuOutput == null)
                        throw new InvalidOperationException("GTE embedding readback returned an invalid tensor.");
                    float[] embedding = cpuOutput.DownloadToArray();
                    ValidateEmbedding(embedding);
                    return embedding;
                }
            }
        }

        public static IEnumerator EncodeSteps(
            Worker worker,
            ITokenizer tokenizer,
            string text,
            Action<float[]> onReady,
            Action<string> onStage = null)
        {
            if (worker == null)
                throw new ArgumentNullException(nameof(worker));
            if (tokenizer == null)
                throw new ArgumentNullException(nameof(tokenizer));
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Query text is empty.", nameof(text));
            if (onReady == null)
                throw new ArgumentNullException(nameof(onReady));

            onStage?.Invoke("Encoding query with warmed GTE model");
            yield return null;
            onReady(Encode(worker, tokenizer, text));
        }

        public static void CreateInputTensors(
            ITokenizer tokenizer,
            string text,
            out Tensor<int> inputIdsTensor,
            out Tensor<int> attentionTensor)
        {
            if (tokenizer == null)
                throw new ArgumentNullException(nameof(tokenizer));
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Query text is empty.", nameof(text));

            var encoding = tokenizer.Encode(text);
            var ids = encoding.GetIds();
            var attention = encoding.GetAttentionMask();
            var inputIds = new int[SequenceLength];
            var attentionMask = new int[SequenceLength];
            for (var index = 0; index < inputIds.Length; index++)
            {
                inputIds[index] = PadTokenId;
                attentionMask[index] = 0;
            }

            var count = Mathf.Min(ids.Count, SequenceLength);
            for (var index = 0; index < count; index++)
            {
                inputIds[index] = ids[index];
                attentionMask[index] = attention.Count > index ? attention[index] : 1;
            }

            inputIdsTensor = new Tensor<int>(new TensorShape(1, SequenceLength), inputIds);
            attentionTensor = new Tensor<int>(new TensorShape(1, SequenceLength), attentionMask);
        }
    }

    public sealed class GteSemanticQueryRunner : IDisposable
    {
        Worker m_Worker;
        readonly ITokenizer m_Tokenizer;

        GteSemanticQueryRunner(Worker worker, ITokenizer tokenizer, ContentSemanticSnapshot routeIndex, string indexFingerprint)
        {
            m_Worker = worker;
            m_Tokenizer = tokenizer;
            RouteIndex = routeIndex;
            IndexFingerprint = indexFingerprint;
        }

        public ContentSemanticSnapshot RouteIndex { get; }
        public BackendType Backend => m_Worker.backendType;
        public string ModelId => GteSemanticAssetPaths.Model;
        public string IndexFingerprint { get; }

        public static IEnumerator LoadFromResourcesSteps(
            Action<GteSemanticQueryRunner> onReady,
            Action<string> onStage = null)
        {
            if (onReady == null)
                throw new ArgumentNullException(nameof(onReady));

            onStage?.Invoke("Opening the pinned SQL semantic release");
            ContentResult<RuntimeContentStore> content = RuntimeContentStore.OpenShippedRelease(Application.streamingAssetsPath);
            if (!content.IsReady)
                throw new InvalidOperationException("The pinned SQL content release is unavailable: " +
                    (content.diagnostics.Count == 0 ? content.status : content.diagnostics[0].code));
            ContentSemanticSnapshot routeIndex = content.value.SemanticIndex;
            if (routeIndex == null || routeIndex.RouteCount == 0)
                throw new InvalidOperationException("The pinned SQL release has no ready GTE route index.");

            onStage?.Invoke("Loading GTE model and tokenizer resources");
            var modelRequest = Resources.LoadAsync<ModelAsset>(GteSemanticAssetPaths.Model);
            var tokenizerRequest = Resources.LoadAsync<TextAsset>(GteSemanticAssetPaths.Tokenizer);
            while (!modelRequest.isDone || !tokenizerRequest.isDone)
                yield return null;

            var modelAsset = modelRequest.asset as ModelAsset;
            var tokenizerAsset = tokenizerRequest.asset as TextAsset;
            if (modelAsset == null || tokenizerAsset == null)
                throw new InvalidOperationException("GTE model or tokenizer is missing from Resources.");

            onStage?.Invoke("Parsing tokenizer");
            string tokenizerJson = tokenizerAsset.text;
            Task<ITokenizer> tokenizerTask = Task.Run(
                () => Unity.InferenceEngine.Tokenization.Parsers.HuggingFace.HuggingFaceParser.GetDefault().Parse(tokenizerJson));
            while (!tokenizerTask.IsCompleted)
                yield return null;
            ITokenizer tokenizer = tokenizerTask.GetAwaiter().GetResult();

            string indexFingerprint = routeIndex.IndexFingerprint;
            yield return null;

            onStage?.Invoke("Loading GTE model");
            Model model = ModelLoader.Load(modelAsset);
            yield return null;

            BackendType backend = SystemInfo.supportsComputeShaders ? BackendType.GPUCompute : BackendType.CPU;
            onStage?.Invoke($"Creating {backend} worker");
            Worker worker = new Worker(model, backend);
            bool transferred = false;
            try
            {
                onStage?.Invoke("Warming GTE model");
                GteSemanticQueryEncoder.CreateInputTensors(tokenizer, "AlterOS Topic Playground warm-up", out Tensor<int> inputIds, out Tensor<int> attentionMask);
                using (inputIds)
                using (attentionMask)
                {
                    var schedule = worker.ScheduleIterable(inputIds, attentionMask);
                    try
                    {
                        while (schedule.MoveNext())
                            yield return null;
                    }
                    finally
                    {
                        (schedule as IDisposable)?.Dispose();
                    }

                    var output = worker.PeekOutput("embedding") as Tensor<float>;
                    if (output == null)
                        throw new InvalidOperationException("GTE model did not produce a float embedding output.");
                    output.ReadbackRequest();
                    while (!output.IsReadbackRequestDone())
                        yield return null;
                    using (var warmupOutput = output.ReadbackAndClone())
                    {
                        if (warmupOutput == null)
                            throw new InvalidOperationException("GTE warm-up readback returned no output.");
                    }
                }

                onStage?.Invoke("Finalizing GTE query warm-up");
                float[] warmupEmbedding = GteSemanticQueryEncoder.EncodeWithCpuFallback(ref worker, tokenizer, "AlterOS Topic Playground warm-up");
                if (warmupEmbedding.Length != routeIndex.Dimension)
                    throw new InvalidOperationException("GTE warm-up embedding does not match the baked route index dimension.");

                var runner = new GteSemanticQueryRunner(worker, tokenizer, routeIndex, indexFingerprint);
                onReady(runner);
                transferred = true;
                onStage?.Invoke("Ready");
            }
            finally
            {
                if (!transferred)
                    worker.Dispose();
            }
        }

        public IEnumerator RankSteps(string text, float minimumSimilarity, Action<DialogueRouteMatch[]> onReady, Action<string> onStage = null)
        {
            if (onReady == null)
                throw new ArgumentNullException(nameof(onReady));

            DialogueRouteMatch[] matches = null;
            IEnumerator encoding = EncodeSteps(
                text,
                embedding => matches = RouteIndex.RankRoutes(embedding, minimumSimilarity),
                onStage);
            try
            {
                while (encoding.MoveNext())
                    yield return null;
            }
            finally
            {
                (encoding as IDisposable)?.Dispose();
            }
            onReady(matches);
        }

        public IEnumerator EncodeSteps(string text, Action<float[]> onReady, Action<string> onStage = null)
        {
            if (onReady == null)
                throw new ArgumentNullException(nameof(onReady));

            onStage?.Invoke("Encoding query with warmed GTE model");
            yield return null;
            onReady(GteSemanticQueryEncoder.EncodeWithCpuFallback(ref m_Worker, m_Tokenizer, text));
        }

        public void Dispose()
        {
            m_Worker.Dispose();
        }

    }
}
