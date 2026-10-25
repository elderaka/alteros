using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AlterOS.Security;
using AlterOS.Tts;
using UnityEngine;

public class TTSSystem : MonoBehaviour
{
    [Header("OpenRouter TTS Settings")]
    [SerializeField] private string openRouterModel = OpenRouterTtsClient.DefaultModel;
    [SerializeField] private string openRouterVoice = OpenRouterTtsClient.DefaultVoice;

    private AudioSource _audioSource;
    private readonly Queue<AudioClip> _speechQueue = new Queue<AudioClip>();
    private readonly OpenRouterTtsClient _openRouterClient = new OpenRouterTtsClient();
    private bool _isPlayingQueue;

    public event Action OnClipFinished;
    public event Action OnQueueEmpty;

    public string OpenRouterVoice => string.IsNullOrWhiteSpace(openRouterVoice)
        ? OpenRouterTtsClient.DefaultVoice
        : openRouterVoice.Trim();

    public bool HasOpenRouterApiKey => !string.IsNullOrWhiteSpace(OpenRouterCredentialStore.GetApiKey());

    public async Task<OpenRouterTtsResult> SynthesizeAsync(string text, string language)
    {
        string apiKey = OpenRouterCredentialStore.GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Debug.LogWarning("[AlterOS] OpenRouter API key is not configured; using text-only fallback.");
            return null;
        }

        return await _openRouterClient.SynthesizeAsync(apiKey, text, OpenRouterVoice, openRouterModel);
    }

    public bool SaveUserApiKey(string apiKey) => OpenRouterCredentialStore.SaveUserApiKey(apiKey);

    public void ClearUserApiKey() => OpenRouterCredentialStore.ClearUserApiKey();

    public AudioSource AudioSource => _audioSource;
    public bool IsPlaying => _audioSource != null && _audioSource.isPlaying;

    private void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
        }
    }

    private void Update()
    {
        if (_audioSource == null || _audioSource.isPlaying || !_isPlayingQueue)
        {
            return;
        }

        if (_speechQueue.Count > 0)
        {
            PlayNextInQueue();
            return;
        }

        _isPlayingQueue = false;
        OnQueueEmpty?.Invoke();
    }

    public void EnqueueClip(AudioClip clip)
    {
        if (clip == null)
        {
            return;
        }

        _speechQueue.Enqueue(clip);
        if (!_isPlayingQueue)
        {
            _isPlayingQueue = true;
            PlayNextInQueue();
        }
    }

    public void PlayClipImmediate(AudioClip clip)
    {
        if (_audioSource == null || clip == null)
        {
            return;
        }

        _audioSource.Stop();
        _speechQueue.Clear();
        _isPlayingQueue = false;
        _audioSource.clip = clip;
        _audioSource.Play();
    }

    public void StopAll()
    {
        if (_audioSource != null)
        {
            _audioSource.Stop();
        }

        _speechQueue.Clear();
        _isPlayingQueue = false;
    }

    private void PlayNextInQueue()
    {
        if (_audioSource == null || _speechQueue.Count == 0)
        {
            return;
        }

        AudioClip clip = _speechQueue.Dequeue();
        _audioSource.clip = clip;
        _audioSource.Play();
        OnClipFinished?.Invoke();
    }
}
