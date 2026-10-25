using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlterOS.Memory;

namespace AlterOS.Conversation
{
    public sealed class CompanionProcessClient : IDisposable, IMemoryAwareDirectConversationProvider, IScenarioDirectConversationProvider
    {
        private const string ApiKeyEnvironmentVariable = "OPENROUTER_API_KEY";
        private readonly string executablePath;
        private readonly string arguments;
        private readonly Func<string> credentialResolver;
        private readonly TimeSpan readinessTimeout;
        private readonly TimeSpan scenarioTurnTimeout;
        private readonly SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim requestLock = new SemaphoreSlim(1, 1);
        private readonly object stateLock = new object();
        private readonly HashSet<string> abandonedRequestIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> abandonedOrder = new Queue<string>();
        private Process process;
        private Task stdoutPump;
        private Task stderrPump;
        private CancellationTokenSource lifetime;
        private TaskCompletionSource<ConversationProtocolMessage> pendingReply;
        private TaskCompletionSource<ConversationProtocolMessage> pendingScenarioReply;
        private Action<ScenarioStageResult> pendingScenarioStage;
        private readonly HashSet<string> receivedScenarioStages = new HashSet<string>(StringComparer.Ordinal);
        private string pendingRequestId = "";
        private string activeRequestId = "";
        private string lastCompletedRequestId = "";
        private bool ready;
        private int protocolVersion;
        private string memoryPlayerId = "";
        private string memoryPersistentRoot = "";
        private bool memoryGenerationAllowed;
        private PlayerMemoryReadyV3 memoryCapabilities;
        private ScenarioReadyV4 scenarioCapabilities;
        private string pendingScenarioTurnId = "";
        private int pendingScenarioTurnVersion;
        private bool disposed;

        public CompanionProcessClient(string absoluteExecutablePath, string helperArguments, Func<string> resolveCredential, TimeSpan readinessLimit)
            : this(absoluteExecutablePath, helperArguments, resolveCredential, readinessLimit, TimeSpan.FromSeconds(60))
        { }

        public CompanionProcessClient(string absoluteExecutablePath, string helperArguments, Func<string> resolveCredential,
            TimeSpan readinessLimit, TimeSpan scenarioTurnLimit)
        {
            if (string.IsNullOrWhiteSpace(absoluteExecutablePath) || !Path.IsPathRooted(absoluteExecutablePath))
                throw new ArgumentException("Helper executable must be an absolute path.", nameof(absoluteExecutablePath));
            if (resolveCredential == null) throw new ArgumentNullException(nameof(resolveCredential));
            if (readinessLimit <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(readinessLimit));
            if (scenarioTurnLimit <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(scenarioTurnLimit));
            executablePath = absoluteExecutablePath;
            arguments = helperArguments ?? "";
            credentialResolver = resolveCredential;
            readinessTimeout = readinessLimit;
            scenarioTurnTimeout = scenarioTurnLimit;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (ready && process != null && !process.HasExited) return;
            if (process != null) await StopAsync().ConfigureAwait(false);
            await StartWithCredentialAsync(credentialResolver(), cancellationToken).ConfigureAwait(false);
        }

        public async Task RefreshCredentialAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (protocolVersion == 3 || protocolVersion == 4)
            {
                int memoryProtocol = protocolVersion;
                if (!memoryGenerationAllowed) return;
                string memoryActive = activeRequestId;
                if (!string.IsNullOrEmpty(memoryActive)) await CancelAsync(memoryActive).ConfigureAwait(false);
                await StopAsync().ConfigureAwait(false);
                if (memoryProtocol == 4)
                    await StartScenarioAsync(memoryPlayerId, memoryPersistentRoot, true, cancellationToken).ConfigureAwait(false);
                else
                    await StartMemoryAsync(memoryPlayerId, memoryPersistentRoot, true, cancellationToken).ConfigureAwait(false);
                return;
            }
            string active = activeRequestId;
            string credential = credentialResolver();
            if (!string.IsNullOrEmpty(active) && !string.IsNullOrWhiteSpace(credential))
            { credential = null; return; }
            try
            {
                if (!string.IsNullOrEmpty(active)) await CancelAsync(active).ConfigureAwait(false);
                await StopAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(credential)) return;
                await StartWithCredentialAsync(credential, cancellationToken).ConfigureAwait(false);
            }
            finally { credential = null; }
        }

        private async Task StartWithCredentialAsync(string credential, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(credential)) throw new InvalidOperationException("credential_missing");
            string bootstrapId = "bootstrap-" + Guid.NewGuid().ToString("N");
            await StartWithBootstrapAsync(bootstrapId, ConversationProtocol.SerializeBootstrapEnvelope(bootstrapId, credential),
                2, cancellationToken).ConfigureAwait(false);
            credential = null;
        }

        public async Task<PlayerMemoryReadyV3> StartMemoryAsync(string playerId, string persistentDataPath,
            CancellationToken cancellationToken)
        { return await StartMemoryAsync(playerId, persistentDataPath, false, cancellationToken).ConfigureAwait(false); }

        public async Task<PlayerMemoryReadyV3> StartMemoryAsync(string playerId, string persistentDataPath,
            bool allowOnlineGeneration, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(playerId) || playerId.Length > 128 || string.IsNullOrWhiteSpace(persistentDataPath) ||
                !Path.IsPathRooted(persistentDataPath)) throw new ArgumentException("Memory identity or storage root is invalid.");
            string fullPersistentRoot = Path.GetFullPath(persistentDataPath);
            if (ready && process != null && !process.HasExited && protocolVersion == 3 &&
                string.Equals(memoryPlayerId, playerId, StringComparison.Ordinal) &&
                string.Equals(memoryPersistentRoot, fullPersistentRoot, StringComparison.OrdinalIgnoreCase) &&
                memoryGenerationAllowed == allowOnlineGeneration) return memoryCapabilities;
            lock (stateLock)
            {
                if (pendingReply != null || requestLock.CurrentCount == 0 || Volatile.Read(ref activeTurn) != 0)
                    throw new InvalidOperationException("helper_busy");
            }
            if (process != null) await StopAsync().ConfigureAwait(false);
            memoryPlayerId = playerId;
            memoryPersistentRoot = fullPersistentRoot;
            memoryGenerationAllowed = allowOnlineGeneration;
            string credential = allowOnlineGeneration ? credentialResolver() ?? "" : "";
            try
            {
                string memoryRoot = Path.Combine(fullPersistentRoot, "Memory", "player-memory.sqlite3");
                string requestId = "bootstrap-memory-" + Guid.NewGuid().ToString("N");
                string envelope = PlayerMemoryProtocol.SerializeBootstrapEnvelope(requestId, credential, playerId, memoryRoot);
                await StartWithBootstrapAsync(requestId, envelope, 3, cancellationToken).ConfigureAwait(false);
                return memoryCapabilities;
            }
            finally { credential = null; }
        }

        public async Task<ScenarioReadyV4> StartScenarioAsync(string playerId, string persistentDataPath,
            bool allowOnlineGeneration, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(playerId) || playerId.Length > 128 || string.IsNullOrWhiteSpace(persistentDataPath) ||
                !Path.IsPathRooted(persistentDataPath)) throw new ArgumentException("Scenario memory identity or storage root is invalid.");
            string fullPersistentRoot = Path.GetFullPath(persistentDataPath);
            if (ready && process != null && !process.HasExited && protocolVersion == 4 &&
                string.Equals(memoryPlayerId, playerId, StringComparison.Ordinal) &&
                string.Equals(memoryPersistentRoot, fullPersistentRoot, StringComparison.OrdinalIgnoreCase) &&
                memoryGenerationAllowed == allowOnlineGeneration) return scenarioCapabilities;
            lock (stateLock)
            {
                if (pendingReply != null || pendingScenarioReply != null || requestLock.CurrentCount == 0 ||
                    Volatile.Read(ref activeTurn) != 0) throw new InvalidOperationException("helper_busy");
            }
            if (process != null) await StopAsync().ConfigureAwait(false);
            memoryPlayerId = playerId;
            memoryPersistentRoot = fullPersistentRoot;
            memoryGenerationAllowed = allowOnlineGeneration;
            string credential = allowOnlineGeneration ? credentialResolver() ?? "" : "";
            try
            {
                string root = Path.Combine(fullPersistentRoot, "Memory", "player-memory.sqlite3");
                string requestId = "bootstrap-scenario-" + Guid.NewGuid().ToString("N");
                string envelope = ScenarioTurnProtocol.SerializeBootstrapEnvelope(requestId, credential, playerId, root);
                await StartWithBootstrapAsync(requestId, envelope, 4, cancellationToken).ConfigureAwait(false);
                return scenarioCapabilities;
            }
            finally { credential = null; }
        }

        private async Task StartWithBootstrapAsync(string bootstrapId, string bootstrapEnvelope, int expectedVersion,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(executablePath)) throw new InvalidOperationException("helper_missing");
            protocolVersion = expectedVersion;

            var start = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(executablePath),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false, true),
                StandardErrorEncoding = new UTF8Encoding(false, true)
            };
            start.EnvironmentVariables.Remove(ApiKeyEnvironmentVariable);
            var child = new Process { StartInfo = start, EnableRaisingEvents = true };
            try
            {
                if (!child.Start()) throw new InvalidOperationException("helper_start_failed");
            }
            catch (Exception) { child.Dispose(); throw new InvalidOperationException("helper_start_failed"); }

            process = child;
            lifetime = new CancellationTokenSource();
            stdoutPump = PumpStdoutAsync(child.StandardOutput.BaseStream, lifetime.Token);
            stderrPump = DrainStderrAsync(child.StandardError.BaseStream, lifetime.Token);
            try
            {
                ConversationProtocolMessage response = expectedVersion == 4
                    ? await SendScenarioAndWaitAsync(bootstrapId, bootstrapEnvelope, readinessTimeout, cancellationToken, null, null).ConfigureAwait(false)
                    : await SendAndWaitAsync(bootstrapId, null, bootstrapEnvelope, readinessTimeout, cancellationToken).ConfigureAwait(false);
                bootstrapEnvelope = null;
                if (response.type == "error")
                {
                    ConversationErrorV2 error = response.payload as ConversationErrorV2;
                    PlayerMemoryErrorV3 memoryError = response.payload as PlayerMemoryErrorV3;
                    ScenarioErrorV4 scenarioError = response.payload as ScenarioErrorV4;
                    throw new InvalidOperationException(error != null ? error.code : memoryError != null ? memoryError.code :
                        scenarioError != null ? scenarioError.code : "helper_not_ready");
                }
                if (response.type != "ready") throw new InvalidOperationException("helper_not_ready");
                if (response.protocolVersion != expectedVersion) throw new InvalidOperationException("helper_not_ready");
                if (expectedVersion == 3)
                {
                    memoryCapabilities = response.payload as PlayerMemoryReadyV3;
                    if (memoryCapabilities == null || !memoryCapabilities.memoryAvailable) throw new InvalidOperationException("helper_not_ready");
                }
                if (expectedVersion == 4)
                {
                    scenarioCapabilities = response.payload as ScenarioReadyV4;
                    if (scenarioCapabilities == null || !scenarioCapabilities.memoryAvailable || !scenarioCapabilities.scenarioTurnAvailable)
                        throw new InvalidOperationException("helper_not_ready");
                }
                ready = true;
                protocolVersion = expectedVersion;
            }
            catch
            {
                await StopAsync().ConfigureAwait(false);
                throw;
            }
            finally { bootstrapEnvelope = null; }
        }

        public async Task<ConversationProtocolMessage> SendTurnAsync(string requestId, ConversationJobV2 job, CancellationToken cancellationToken)
        {
            if (!ready || process == null || process.HasExited) throw new InvalidOperationException("helper_not_ready");
            if (protocolVersion == 3) throw new InvalidOperationException("memory_policy_required");
            if (Interlocked.CompareExchange(ref activeTurn, 1, 0) != 0) throw new InvalidOperationException("turn_in_flight");
            activeRequestId = requestId;
            try
            {
                string envelope = ConversationProtocol.SerializeTurnEnvelope(requestId, job);
                ConversationProtocolMessage response = await SendAndWaitAsync(requestId, null, envelope, TimeSpan.FromSeconds(60), cancellationToken).ConfigureAwait(false);
                if (response.type != "result" && response.type != "error") throw new FormatException("Unexpected helper response type.");
                return response;
            }
            finally
            {
                activeRequestId = "";
                Interlocked.Exchange(ref activeTurn, 0);
            }
        }

        public async Task<ConversationProtocolMessage> SendMemoryTurnAsync(string requestId, ConversationJobV2 job,
            PlayerMemoryTurnPolicyV3 memory, CancellationToken cancellationToken)
        {
            if (!ready || protocolVersion != 3 || process == null || process.HasExited || memoryCapabilities == null ||
                !memoryCapabilities.memoryAvailable || !memoryCapabilities.generationAvailable)
                throw new InvalidOperationException("helper_not_ready");
            if (Interlocked.CompareExchange(ref activeTurn, 1, 0) != 0) throw new InvalidOperationException("turn_in_flight");
            activeRequestId = requestId;
            try
            {
                string envelope = PlayerMemoryProtocol.SerializeMemoryTurnEnvelope(requestId, job, memory);
                ConversationProtocolMessage response = await SendAndWaitAsync(requestId, null, envelope,
                    TimeSpan.FromSeconds(60), cancellationToken).ConfigureAwait(false);
                if (response.type != "result" && response.type != "error") throw new FormatException("Unexpected memory turn response type.");
                if (response.type == "error")
                {
                    PlayerMemoryErrorV3 error = response.payload as PlayerMemoryErrorV3;
                    if (error == null) throw new FormatException("Memory turn error payload is invalid.");
                }
                else if (!(response.payload is ConversationResultV2)) throw new FormatException("Memory turn result payload is invalid.");
                return response;
            }
            catch (OperationCanceledException)
            {
                try { await CancelAsync(requestId).ConfigureAwait(false); }
                catch (Exception) { }
                throw;
            }
            finally
            {
                activeRequestId = "";
                Interlocked.Exchange(ref activeTurn, 0);
            }
        }

        public async Task<ScenarioTurnReceipt> SendScenarioTurnAsync(string requestId, ScenarioTurnJobV4 job,
            Action<ScenarioStageResult> onStage, CancellationToken cancellationToken)
        {
            if (!ready || protocolVersion != 4 || process == null || process.HasExited || scenarioCapabilities == null ||
                !scenarioCapabilities.scenarioTurnAvailable) throw new InvalidOperationException("helper_not_ready");
            if (Interlocked.CompareExchange(ref activeTurn, 1, 0) != 0) throw new InvalidOperationException("turn_in_flight");
            activeRequestId = requestId;
            try
            {
                string envelope = ScenarioTurnProtocol.SerializeTurnEnvelope(requestId, job);
                ConversationProtocolMessage response = await SendScenarioAndWaitAsync(requestId, envelope,
                    scenarioTurnTimeout, cancellationToken, onStage, job).ConfigureAwait(false);
                if (response.type == "error")
                {
                    ScenarioErrorV4 error = response.payload as ScenarioErrorV4;
                    throw new InvalidOperationException(error == null ? "helper_protocol_failed" : error.code);
                }
                if (response.type != "scenario_complete" || !(response.payload is ScenarioTurnReceipt))
                    throw new FormatException("Unexpected Scenario helper response type.");
                return (ScenarioTurnReceipt)response.payload;
            }
            catch (OperationCanceledException)
            {
                try { await CancelAsync(requestId).ConfigureAwait(false); }
                catch (Exception) { }
                throw;
            }
            catch (TimeoutException)
            {
                try { await CancelAsync(requestId).ConfigureAwait(false); }
                catch (Exception) { }
                throw;
            }
            finally
            {
                activeRequestId = "";
                Interlocked.Exchange(ref activeTurn, 0);
            }
        }

        private int activeTurn;

        public async Task<PlayerMemoryResultV3> SendMemoryCommandAsync(string requestId, string envelope,
            CancellationToken cancellationToken)
        {
            if (!ready || (protocolVersion != 3 && protocolVersion != 4) || process == null || process.HasExited)
                throw new InvalidOperationException("helper_not_ready");
            if (string.IsNullOrWhiteSpace(requestId)) throw new ArgumentException("Request identity is required.", nameof(requestId));
            activeRequestId = requestId;
            try
            {
                ConversationProtocolMessage response = await SendAndWaitAsync(requestId, null, envelope,
                    TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
                if (response.type == "error")
                {
                    PlayerMemoryErrorV3 error = response.payload as PlayerMemoryErrorV3;
                    throw new InvalidOperationException(error == null ? "memory_unavailable" : error.code);
                }
                if (response.type != "result") throw new FormatException("Unexpected memory response type.");
                PlayerMemoryResultV3 result = response.payload as PlayerMemoryResultV3;
                if (result == null) throw new FormatException("Memory response payload is invalid.");
                return result;
            }
            catch (OperationCanceledException)
            {
                try { await CancelAsync(requestId).ConfigureAwait(false); }
                catch (Exception) { }
                throw;
            }
            finally { if (activeRequestId == requestId) activeRequestId = ""; }
        }

        public async Task CancelAsync(string requestId)
        {
            if (string.IsNullOrEmpty(requestId) || requestId != activeRequestId || process == null || process.HasExited) return;
            string envelope = protocolVersion == 4 ? ScenarioTurnProtocol.SerializeControlEnvelope("cancel", requestId) :
                protocolVersion == 3 ? PlayerMemoryProtocol.SerializeControlEnvelope("cancel", requestId) :
                ConversationProtocol.SerializeControlEnvelope("cancel", requestId);
            await WriteLineAsync(envelope, CancellationToken.None).ConfigureAwait(false);
            Abandon(requestId);
        }

        public async Task StopAsync()
        {
            Process child = process;
            if (child == null) return;
            ready = false;
            try
            {
                if (!child.HasExited)
                {
                    string requestId = "shutdown-" + Guid.NewGuid().ToString("N");
                    string envelope = protocolVersion == 4 ? ScenarioTurnProtocol.SerializeControlEnvelope("shutdown", requestId) :
                        protocolVersion == 3 ? PlayerMemoryProtocol.SerializeControlEnvelope("shutdown", requestId) :
                        ConversationProtocol.SerializeControlEnvelope("shutdown", requestId);
                    await WriteLineAsync(envelope, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception) { }
            bool exited = false;
            try { exited = await Task.Run(() => child.WaitForExit(2000)).ConfigureAwait(false); }
            catch (Exception) { }
            if (!exited)
            {
                try { if (!child.HasExited) child.Kill(); }
                catch (Exception) { }
            }
            if (lifetime != null) { lifetime.Cancel(); lifetime.Dispose(); lifetime = null; }
            lock (stateLock)
            {
                if (pendingReply != null) pendingReply.TrySetException(new InvalidOperationException("helper_stopped"));
                if (pendingScenarioReply != null) pendingScenarioReply.TrySetException(new InvalidOperationException("helper_stopped"));
                pendingReply = null; pendingRequestId = "";
                pendingScenarioReply = null; pendingScenarioStage = null; receivedScenarioStages.Clear();
            }
            process = null;
            protocolVersion = 0;
            memoryCapabilities = null;
            scenarioCapabilities = null;
            try { child.Dispose(); } catch (Exception) { }
            try { if (stdoutPump != null) await stdoutPump.ConfigureAwait(false); } catch (Exception) { }
            try { if (stderrPump != null) await stderrPump.ConfigureAwait(false); } catch (Exception) { }
            stdoutPump = null; stderrPump = null;
        }

        private async Task<ConversationProtocolMessage> SendScenarioAndWaitAsync(string requestId, string envelope,
            TimeSpan timeout, CancellationToken cancellationToken, Action<ScenarioStageResult> onStage, ScenarioTurnJobV4 job)
        {
            await requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            var completion = new TaskCompletionSource<ConversationProtocolMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (stateLock)
            {
                if (pendingReply != null || pendingScenarioReply != null) throw new InvalidOperationException("turn_in_flight");
                pendingScenarioReply = completion;
                pendingScenarioStage = onStage;
                pendingRequestId = requestId;
                pendingScenarioTurnId = job == null ? "" : job.turnId;
                pendingScenarioTurnVersion = job == null ? 0 : job.turnVersion;
                receivedScenarioStages.Clear();
            }
            try
            {
                await WriteLineAsync(envelope, cancellationToken).ConfigureAwait(false);
                envelope = null;
                Task delay = Task.Delay(timeout, cancellationToken);
                Task finished = await Task.WhenAny(completion.Task, delay).ConfigureAwait(false);
                if (finished != completion.Task)
                {
                    Abandon(requestId);
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new TimeoutException("helper_response_timeout");
                }
                ConversationProtocolMessage response = await completion.Task.ConfigureAwait(false);
                if (job == null && response.type != "ready" && response.type != "error")
                    throw new FormatException("Unexpected Scenario bootstrap response type.");
                if (job != null && response.type != "scenario_complete" && response.type != "error")
                    throw new FormatException("Unexpected Scenario turn response type.");
                return response;
            }
            finally
            {
                lock (stateLock)
                {
                    if (ReferenceEquals(pendingScenarioReply, completion))
                    {
                        pendingScenarioReply = null; pendingScenarioStage = null; pendingRequestId = "";
                        pendingScenarioTurnId = ""; pendingScenarioTurnVersion = 0; receivedScenarioStages.Clear();
                    }
                }
                requestLock.Release();
            }
        }

        private async Task<ConversationProtocolMessage> SendAndWaitAsync(string requestId, string expectedType,
            string envelope, TimeSpan timeout, CancellationToken cancellationToken)
        {
            await requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            var completion = new TaskCompletionSource<ConversationProtocolMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (stateLock)
            {
                if (pendingReply != null) throw new InvalidOperationException("turn_in_flight");
                pendingReply = completion;
                pendingRequestId = requestId;
            }
            try
            {
                await WriteLineAsync(envelope, cancellationToken).ConfigureAwait(false);
                envelope = null;
                Task delay = Task.Delay(timeout, cancellationToken);
                Task finished = await Task.WhenAny(completion.Task, delay).ConfigureAwait(false);
                if (finished != completion.Task)
                {
                    Abandon(requestId);
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new TimeoutException("helper_response_timeout");
                }
                ConversationProtocolMessage response = await completion.Task.ConfigureAwait(false);
                if (expectedType != null && response.type != expectedType) throw new FormatException("Unexpected helper response type.");
                return response;
            }
            finally
            {
                lock (stateLock)
                {
                    if (ReferenceEquals(pendingReply, completion)) { pendingReply = null; pendingRequestId = ""; }
                }
                requestLock.Release();
            }
        }

        private async Task WriteLineAsync(string envelope, CancellationToken cancellationToken)
        {
            Process child = process;
            if (child == null || child.HasExited) throw new InvalidOperationException("helper_not_running");
            byte[] line = new UTF8Encoding(false, true).GetBytes(envelope + "\n");
            if (line.Length > ConversationProtocol.MaximumEnvelopeBytes + 1) throw new FormatException("Outbound protocol line exceeds its limit.");
            await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                try
                {
                    await child.StandardInput.BaseStream.WriteAsync(line, 0, line.Length, cancellationToken).ConfigureAwait(false);
                    await child.StandardInput.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { throw new InvalidOperationException("helper_stdin_failed"); }
            }
            finally { writeLock.Release(); Array.Clear(line, 0, line.Length); }
        }

        private async Task PumpStdoutAsync(Stream stream, CancellationToken cancellationToken)
        {
            var line = new List<byte>();
            byte[] buffer = new byte[4096];
            try
            {
                while (true)
                {
                    int count = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                    if (count == 0)
                    {
                        if (line.Count != 0) throw new FormatException("helper_incomplete_line");
                        SignalFailure(new InvalidOperationException("helper_exited"));
                        return;
                    }
                    for (int index = 0; index < count; index++)
                    {
                        byte value = buffer[index];
                        if (value == (byte)'\n')
                        {
                            if (line.Count > 0 && line[line.Count - 1] == (byte)'\r') line.RemoveAt(line.Count - 1);
                            string json = new UTF8Encoding(false, true).GetString(line.ToArray());
                            line.Clear();
                            Deliver(ConversationProtocol.ParseEnvelope(json));
                        }
                        else
                        {
                            if (line.Count >= ConversationProtocol.MaximumEnvelopeBytes) throw new FormatException("helper_line_too_long");
                            line.Add(value);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { SignalFailure(new InvalidOperationException("helper_protocol_failed")); TerminateOwnedProcess(); }
        }

        private async Task DrainStderrAsync(Stream stream, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[4096];
            try
            {
                while (await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false) != 0) { }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
        }

        private void Deliver(ConversationProtocolMessage message)
        {
            if (message.protocolVersion == 4) { DeliverScenario(message); return; }
            lock (stateLock)
            {
                if (message.requestId == lastCompletedRequestId)
                { SignalFailure(new InvalidOperationException("helper_duplicate_response")); return; }
                if (abandonedRequestIds.Contains(message.requestId)) return;
                if (pendingReply == null || message.requestId != pendingRequestId)
                { SignalFailure(new InvalidOperationException("helper_unexpected_response")); return; }
                lastCompletedRequestId = message.requestId;
                pendingReply.TrySetResult(message);
            }
        }

        private void DeliverScenario(ConversationProtocolMessage message)
        {
            Action<ScenarioStageResult> callback = null;
            ScenarioStageResult stage = null;
            Exception failure = null;
            lock (stateLock)
            {
                if (message.requestId == lastCompletedRequestId)
                { failure = new InvalidOperationException("helper_duplicate_response"); }
                else if (abandonedRequestIds.Contains(message.requestId)) return;
                else if (pendingScenarioReply == null || message.requestId != pendingRequestId)
                { failure = new InvalidOperationException("helper_unexpected_response"); }
                else if (message.type == "scenario_stage")
                {
                    stage = message.payload as ScenarioStageResult;
                    if (stage == null || stage.turnId != pendingScenarioTurnId || stage.turnVersion != pendingScenarioTurnVersion ||
                        pendingScenarioStage == null || !receivedScenarioStages.Add(stage.stage))
                        failure = new InvalidOperationException("helper_protocol_failed");
                    else callback = pendingScenarioStage;
                }
                else if (message.type == "scenario_complete")
                {
                    ScenarioTurnReceipt receipt = message.payload as ScenarioTurnReceipt;
                    if (receipt == null || receipt.turnId != pendingScenarioTurnId || receipt.turnVersion != pendingScenarioTurnVersion)
                        failure = new InvalidOperationException("helper_protocol_failed");
                    else
                    {
                        lastCompletedRequestId = message.requestId;
                        pendingScenarioReply.TrySetResult(message);
                    }
                }
                else if (message.type == "ready" || message.type == "error")
                {
                    lastCompletedRequestId = message.requestId;
                    pendingScenarioReply.TrySetResult(message);
                }
                else failure = new InvalidOperationException("helper_protocol_failed");
            }
            if (failure != null) { SignalFailure(failure); TerminateOwnedProcess(); return; }
            if (callback != null)
            {
                try { callback(stage); }
                catch (Exception) { SignalFailure(new InvalidOperationException("scenario_stage_callback_failed")); }
            }
        }

        private void SignalFailure(Exception exception)
        {
            lock (stateLock)
            {
                if (pendingReply != null) pendingReply.TrySetException(exception);
                if (pendingScenarioReply != null) pendingScenarioReply.TrySetException(exception);
            }
            ready = false;
        }

        private void Abandon(string requestId)
        {
            lock (stateLock)
            {
                if (pendingReply != null && pendingRequestId == requestId) pendingReply.TrySetCanceled();
                if (pendingScenarioReply != null && pendingRequestId == requestId) pendingScenarioReply.TrySetCanceled();
                if (abandonedRequestIds.Add(requestId)) abandonedOrder.Enqueue(requestId);
                while (abandonedOrder.Count > 64) abandonedRequestIds.Remove(abandonedOrder.Dequeue());
            }
        }

        private void TerminateOwnedProcess()
        {
            Process child = process;
            try { if (child != null && !child.HasExited) child.Kill(); }
            catch (Exception) { }
        }

        private void ThrowIfDisposed()
        { if (disposed) throw new ObjectDisposedException(nameof(CompanionProcessClient)); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            StopAsync().GetAwaiter().GetResult();
            writeLock.Dispose();
            requestLock.Dispose();
        }
    }
}
