using System;
using System.Collections;
using System.Threading.Tasks;
using AlterOS.Content;
using Unity.InferenceEngine;
using Unity.InferenceEngine.Tokenization;
using Unity.InferenceEngine.Tokenization.Parsers.HuggingFace;
using UnityEngine;

namespace AlterOS.Semantics
{
    public enum GteSemanticState
    {
        NotStarted,
        Loading,
        Ready,
        Failed,
    }

    public sealed class GteSemanticRuntime : MonoBehaviour
    {
        const int WarmupLayersPerFrame = 1;

        static GteSemanticRuntime s_Current;

        Worker m_Worker;
        ITokenizer m_Tokenizer;
        ContentSemanticSnapshot m_SemanticIndex;
        IEnumerator m_InitializationSteps;
        string m_StartupStage;
        string m_IndexIdentity;

        public static GteSemanticRuntime Current => s_Current;
        public GteSemanticState State { get; private set; } = GteSemanticState.NotStarted;
        public string FailureReason { get; private set; }
        public string StartupStage => m_StartupStage;
        public int AnchorCount => m_SemanticIndex?.AnchorCount ?? 0;
        public int RouteCount => m_SemanticIndex?.RouteCount ?? 0;
        public string IndexIdentity => State == GteSemanticState.Ready ? m_IndexIdentity : string.Empty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void CreateAtApplicationStartup()
        {
            if (s_Current != null)
                return;
            var host = new GameObject("AlterOS Semantic Runtime");
            DontDestroyOnLoad(host);
            host.AddComponent<GteSemanticRuntime>();
        }

        void Awake()
        {
            if (s_Current != null && s_Current != this)
            {
                Destroy(gameObject);
                return;
            }
            s_Current = this;
            StartCoroutine(Initialize());
        }

        void OnDestroy()
        {
            StopAllCoroutines();
            (m_InitializationSteps as IDisposable)?.Dispose();
            m_InitializationSteps = null;
            if (s_Current == this)
                s_Current = null;
            m_Worker?.Dispose();
            m_Worker = null;
        }

        public bool TryEmbed(string text, out float[] embedding)
        {
            embedding = null;
            if (State != GteSemanticState.Ready || string.IsNullOrWhiteSpace(text))
                return false;
            embedding = Encode(text);
            return true;
        }

        public bool TryRetryInitialization()
        {
            if (State != GteSemanticState.Failed || !isActiveAndEnabled)
                return false;

            FailureReason = null;
            m_Tokenizer = null;
            m_SemanticIndex = null;
            m_IndexIdentity = string.Empty;
            StartCoroutine(Initialize());
            return true;
        }

        public bool TryRoute(string text, out DialogueRouteMatch[] matches, float minimumSimilarity = 0.45f)
        {
            matches = null;
            if (!TryEmbed(text, out var embedding))
                return false;
            matches = m_SemanticIndex.RankRoutes(embedding, minimumSimilarity);
            return true;
        }

        IEnumerator Initialize()
        {
            m_StartupStage = "starting";
            SetState(GteSemanticState.Loading);
            yield return null;
            // Give the startup scene a rendered frame before requesting heavy assets.
            yield return null;

            var steps = InitializeSteps();
            m_InitializationSteps = steps;
            long longestStepTicks = 0;
            string longestStepStage = "none";
            try
            {
                while (true)
                {
                    bool moved = false;
                    Exception failure = null;
                    long stepStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                    try
                    {
                        moved = steps.MoveNext();
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }
                    long stepTicks = System.Diagnostics.Stopwatch.GetTimestamp() - stepStarted;
                    if (stepTicks > longestStepTicks)
                    {
                        longestStepTicks = stepTicks;
                        longestStepStage = m_StartupStage;
                    }

                    if (failure != null)
                    {
                        FailureReason = failure.ToString();
                        SetState(GteSemanticState.Failed);
                        Debug.LogError($"AlterOS GTE semantic runtime failed to initialize: {failure}");
                        yield break;
                    }
                    if (!moved)
                    {
                        Debug.Log($"AlterOS GTE longest initialization step: {(double)longestStepTicks / System.Diagnostics.Stopwatch.Frequency:F3}s ({longestStepStage}).");
                        yield break;
                    }
                    yield return steps.Current;
                }
            }
            finally
            {
                (steps as IDisposable)?.Dispose();
                m_InitializationSteps = null;
                if (State == GteSemanticState.Failed)
                {
                    m_Worker?.Dispose();
                    m_Worker = null;
                }
            }
        }

        IEnumerator InitializeSteps()
        {
            m_StartupStage = "SQL content release";
            ContentResult<RuntimeContentStore> content = RuntimeContentStore.OpenShippedRelease(Application.streamingAssetsPath);
            if (!content.IsReady)
                throw new InvalidOperationException("The pinned SQL content release is unavailable: " +
                    (content.diagnostics.Count == 0 ? content.status : content.diagnostics[0].code));
            m_SemanticIndex = content.value.SemanticIndex;
            if (m_SemanticIndex == null || AnchorCount == 0 || RouteCount == 0)
                throw new InvalidOperationException("The pinned SQL release has no ready GTE route index.");
            m_IndexIdentity = m_SemanticIndex.IndexFingerprint;

            m_StartupStage = "model and tokenizer resource requests";
            var modelRequest = Resources.LoadAsync<ModelAsset>(GteSemanticAssetPaths.Model);
            var tokenizerRequest = Resources.LoadAsync<TextAsset>(GteSemanticAssetPaths.Tokenizer);
            yield return modelRequest;
            yield return tokenizerRequest;

            var modelAsset = modelRequest.asset as ModelAsset;
            var tokenizerAsset = tokenizerRequest.asset as TextAsset;
            if (modelAsset == null || tokenizerAsset == null)
                throw new InvalidOperationException("GTE model or tokenizer assets are missing from Resources.");

            // Only managed JSON parsing leaves the main thread; Unity asset/GPU access stays here.
            string tokenizerJson = tokenizerAsset.text;
            m_StartupStage = "tokenizer parsing";
            var tokenizerTask = Task.Run(() => HuggingFaceParser.GetDefault().Parse(tokenizerJson));
            while (!tokenizerTask.IsCompleted)
                yield return null;
            m_Tokenizer = tokenizerTask.GetAwaiter().GetResult();
            m_StartupStage = "route index";
            yield return null;

            m_StartupStage = "model loading";
            yield return null;
            var model = ModelLoader.Load(modelAsset);
            yield return null;
            var backend = SystemInfo.supportsComputeShaders ? BackendType.GPUCompute : BackendType.CPU;
            m_StartupStage = "worker creation";
            yield return null;
            m_Worker = new Worker(model, backend);
            yield return null;

            m_StartupStage = "warm-up scheduling";
            yield return null;
            GteSemanticQueryEncoder.CreateInputTensors(m_Tokenizer, "warm up", out var inputIds, out var attentionMask);
            using (inputIds)
            using (attentionMask)
            {
                var schedule = m_Worker.ScheduleIterable(inputIds, attentionMask);
                try
                {
                    int scheduled = 0;
                    while (schedule.MoveNext())
                    {
                        if (++scheduled % WarmupLayersPerFrame == 0)
                            yield return null;
                    }
                }
                finally
                {
                    (schedule as IDisposable)?.Dispose();
                }

                m_StartupStage = "GPU/CPU readback";
                var output = m_Worker.PeekOutput("embedding") as Tensor<float>;
                if (output == null)
                    throw new InvalidOperationException("GTE model did not produce a float embedding output.");
                output.ReadbackRequest();
                while (!output.IsReadbackRequestDone())
                    yield return null;
                // Consume the request before pooled GPU buffers outlive its available data.
                using var warmupOutput = output.ReadbackAndClone();
            }

            m_StartupStage = "validating semantic output";
            float[] warmupEmbedding = GteSemanticQueryEncoder.EncodeWithCpuFallback(ref m_Worker, m_Tokenizer, "warm up");
            if (warmupEmbedding.Length != m_SemanticIndex.Dimension)
                throw new InvalidOperationException("GTE warm-up embedding does not match the SQL semantic index dimension.");
            SetState(GteSemanticState.Ready);
            Debug.Log($"AlterOS GTE semantic runtime ready ({AnchorCount} anchors, {RouteCount} routes, {m_Worker.backendType}).");
        }

        float[] Encode(string text)
        {
            return GteSemanticQueryEncoder.EncodeWithCpuFallback(ref m_Worker, m_Tokenizer, text);
        }

        void SetState(GteSemanticState state)
        {
            State = state;
        }

    }
}
