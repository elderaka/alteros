using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace AlterOS.Tts
{
    [Serializable]
    public sealed class OpenRouterSpeechPayload
    {
        public string model;
        public string input;
        public string voice;
        public string response_format;
    }

    public sealed class OpenRouterTtsResult
    {
        public AudioClip clip;
        public byte[] audioData;
    }

    public sealed class OpenRouterTtsClient
    {
        public const string Endpoint = "https://openrouter.ai/api/v1/audio/speech";
        public const string DefaultModel = "qwen/qwen-audio-3.0-tts-plus";
        public const string DefaultVoice = "Serena";

        public static OpenRouterSpeechPayload BuildPayload(string text, string voice, string model = DefaultModel)
        {
            return new OpenRouterSpeechPayload
            {
                model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim(),
                input = text ?? string.Empty,
                voice = string.IsNullOrWhiteSpace(voice) ? DefaultVoice : voice.Trim(),
                response_format = "mp3"
            };
        }

        public async Task<OpenRouterTtsResult> SynthesizeAsync(
            string apiKey,
            string text,
            string voice = DefaultVoice,
            string model = DefaultModel)
        {
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            OpenRouterSpeechPayload payload = BuildPayload(text, voice, model);
            byte[] requestBody = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
            using (UnityWebRequest request = new UnityWebRequest(Endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(requestBody);
                request.downloadHandler = new DownloadHandlerAudioClip(Endpoint, AudioType.MPEG);
                request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Accept", "audio/mpeg");
                request.timeout = 90;

                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[AlterOS] OpenRouter TTS request failed ({request.responseCode}): {request.error}");
                    return null;
                }

                return new OpenRouterTtsResult
                {
                    clip = DownloadHandlerAudioClip.GetContent(request),
                    audioData = request.downloadHandler.data
                };
            }
        }
    }
}
