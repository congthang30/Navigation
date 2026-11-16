using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Vps.UnityClient.Navigation
{
    public interface IGoogleTtsPlatformBridge
    {
        bool IsReady { get; }
        bool IsSpeaking { get; }
        string EngineLabel { get; }
        string LastError { get; }
        void Initialize();
        void Speak(string text, float rate, float pitch);
        void Stop();
        void Shutdown();
    }

    public interface IGoogleCloudTtsTransport
    {
        IEnumerator Synthesize(string apiKey, string requestJson, Action<long, string, string> onCompleted);
    }

    public interface IGoogleTranslateTtsTransport
    {
        IEnumerator SynthesizeClip(string url, Action<long, string, AudioClip, string> onCompleted);
    }

    /// <summary>
    /// Builds Google Translate TTS request URLs and safely splits long descriptions into
    /// natural clauses under the endpoint's 200-character limit.
    /// </summary>
    public static class GoogleTranslateTtsClient
    {
        public const string EndpointBaseUrl = "https://translate.google.com/translate_tts";
        public const string DefaultLanguageCode = "vi";
        public const string DefaultClient = "tw-ob";
        public const int MaxChunkChars = 180;

        public static List<string> SplitTextIntoChunks(string text, int maxChunkChars = MaxChunkChars)
        {
            var chunks = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return chunks;

            int limit = Mathf.Clamp(maxChunkChars, 20, 200);
            string normalized = NormalizeWhitespace(text);
            int index = 0;

            while (index < normalized.Length)
            {
                int remaining = normalized.Length - index;
                if (remaining <= limit)
                {
                    AddTrimmedChunk(chunks, normalized.Substring(index));
                    break;
                }

                int splitPos = FindBestSplitIndex(normalized, index, limit);
                AddTrimmedChunk(chunks, normalized.Substring(index, splitPos - index));
                index = splitPos;
                while (index < normalized.Length && char.IsWhiteSpace(normalized[index]))
                {
                    index++;
                }
            }

            return chunks;
        }

        public static string BuildRequestUrl(
            string textChunk,
            string languageCode = DefaultLanguageCode,
            float speakingRate = 1.0f)
        {
            string lang = string.IsNullOrWhiteSpace(languageCode) ? DefaultLanguageCode : languageCode.Trim();
            float ttsSpeed = speakingRate < 0.92f
                ? Mathf.Clamp(speakingRate, 0.50f, 1.00f)
                : 1.00f;
            string speedStr = ttsSpeed.ToString("0.##", CultureInfo.InvariantCulture);

            return string.Concat(
                EndpointBaseUrl,
                "?ie=UTF-8&tl=",
                Uri.EscapeDataString(lang),
                "&client=",
                DefaultClient,
                "&ttsspeed=",
                speedStr,
                "&q=",
                Uri.EscapeDataString((textChunk ?? string.Empty).Trim()));
        }

        private static string NormalizeWhitespace(string input)
        {
            var sb = new StringBuilder(input.Length);
            bool lastWasSpace = false;
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (c == '\r' || c == '\n' || char.IsWhiteSpace(c))
                {
                    if (!lastWasSpace && sb.Length > 0)
                    {
                        sb.Append(' ');
                        lastWasSpace = true;
                    }
                }
                else
                {
                    sb.Append(c);
                    lastWasSpace = false;
                }
            }
            return sb.ToString().Trim();
        }

        private static int FindBestSplitIndex(string text, int start, int limit)
        {
            int end = start + limit;
            int minPreferred = start + Mathf.Max(1, limit / 3);

            for (int i = end - 1; i >= minPreferred; i--)
            {
                char c = text[i];
                if (c == '.' || c == '!' || c == '?')
                {
                    return i + 1;
                }
            }

            for (int i = end - 1; i >= minPreferred; i--)
            {
                char c = text[i];
                if (c == ',' || c == ';' || c == ':')
                {
                    return i + 1;
                }
            }

            for (int i = end - 1; i > start; i--)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    return i;
                }
            }

            return end;
        }

        private static void AddTrimmedChunk(List<string> chunks, string slice)
        {
            string trimmed = slice.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                chunks.Add(trimmed);
            }
        }
    }

    /// <summary>
    /// Decodes uncompressed RIFF/WAVE LINEAR16 PCM responses returned by Google Cloud Text-to-Speech.
    /// </summary>
    public static class GoogleTtsWavDecoder
    {
        public static bool TryDecodePcm16Wav(
            byte[] wavBytes,
            out float[] samples,
            out int sampleRate,
            out int channels,
            out string error)
        {
            samples = null;
            sampleRate = 0;
            channels = 0;
            error = null;

            if (wavBytes == null || wavBytes.Length < 44)
            {
                error = "Dữ liệu WAV không đủ độ dài tối thiểu.";
                return false;
            }

            if (!IsAsciiTag(wavBytes, 0, "RIFF") || !IsAsciiTag(wavBytes, 8, "WAVE"))
            {
                error = "Header không phải định dạng RIFF/WAVE.";
                return false;
            }

            bool foundFmt = false;
            int audioFormat = 0;
            int bitsPerSample = 0;
            int dataOffset = -1;
            int dataSize = 0;

            int pos = 12;
            while (pos + 8 <= wavBytes.Length)
            {
                int chunkSize = ReadInt32LE(wavBytes, pos + 4);
                if (chunkSize < 0)
                {
                    error = "Kích thước chunk WAV không hợp lệ.";
                    return false;
                }

                int payloadOffset = pos + 8;
                int clampedAvailable = Mathf.Max(0, wavBytes.Length - payloadOffset);
                if (IsAsciiTag(wavBytes, pos, "fmt ") && chunkSize >= 16 && clampedAvailable >= 16)
                {
                    audioFormat = ReadUInt16LE(wavBytes, payloadOffset);
                    channels = ReadUInt16LE(wavBytes, payloadOffset + 2);
                    sampleRate = ReadInt32LE(wavBytes, payloadOffset + 4);
                    bitsPerSample = ReadUInt16LE(wavBytes, payloadOffset + 14);
                    foundFmt = true;
                }
                else if (IsAsciiTag(wavBytes, pos, "data"))
                {
                    dataOffset = payloadOffset;
                    dataSize = Mathf.Min(chunkSize, clampedAvailable);
                    break;
                }

                long nextPos = (long)payloadOffset + chunkSize + ((chunkSize & 1) != 0 ? 1 : 0);
                if (nextPos > wavBytes.Length || nextPos <= pos)
                {
                    break;
                }
                pos = (int)nextPos;
            }

            if (!foundFmt || audioFormat != 1 || bitsPerSample != 16 || channels < 1 || channels > 2 || sampleRate <= 0)
            {
                error = $"Định dạng PCM WAV không được hỗ trợ (fmt={foundFmt}, codec={audioFormat}, bits={bitsPerSample}, ch={channels}, hz={sampleRate}).";
                return false;
            }

            if (dataOffset < 0 || dataSize < 2)
            {
                error = "WAV không chứa dữ liệu mẫu âm thanh.";
                return false;
            }

            int sampleCount = dataSize / 2;
            samples = new float[sampleCount];
            const float scale = 1f / 32768f;
            for (int i = 0; i < sampleCount; i++)
            {
                int byteIdx = dataOffset + i * 2;
                short pcm = (short)(wavBytes[byteIdx] | (wavBytes[byteIdx + 1] << 8));
                samples[i] = pcm * scale;
            }

            return true;
        }

        private static bool IsAsciiTag(byte[] buffer, int offset, string tag)
        {
            if (offset < 0 || offset + 4 > buffer.Length) return false;
            return buffer[offset] == tag[0]
                && buffer[offset + 1] == tag[1]
                && buffer[offset + 2] == tag[2]
                && buffer[offset + 3] == tag[3];
        }

        private static int ReadUInt16LE(byte[] buffer, int offset)
        {
            return buffer[offset] | (buffer[offset + 1] << 8);
        }

        private static int ReadInt32LE(byte[] buffer, int offset)
        {
            return buffer[offset]
                | (buffer[offset + 1] << 8)
                | (buffer[offset + 2] << 16)
                | (buffer[offset + 3] << 24);
        }
    }

    /// <summary>
    /// Request/response helper for Google Cloud Text-to-Speech v1 REST API.
    /// </summary>
    public static class GoogleCloudTtsClient
    {
        public const string EndpointUrl = "https://texttospeech.googleapis.com/v1/text:synthesize";
        public const string DefaultLanguageCode = "vi-VN";
        public const string DefaultVoiceName = "vi-VN-Wavenet-A";
        public const int DefaultSampleRateHertz = 24000;

        public static string BuildSynthesizeRequestJson(
            string text,
            string languageCode,
            string voiceName,
            float speakingRate,
            int sampleRateHertz = DefaultSampleRateHertz)
        {
            var voice = new JObject
            {
                ["languageCode"] = string.IsNullOrWhiteSpace(languageCode) ? DefaultLanguageCode : languageCode.Trim(),
            };
            if (!string.IsNullOrWhiteSpace(voiceName))
            {
                voice["name"] = voiceName.Trim();
            }
            else
            {
                voice["ssmlGender"] = "FEMALE";
            }

            var payload = new JObject
            {
                ["input"] = new JObject { ["text"] = text ?? string.Empty },
                ["voice"] = voice,
                ["audioConfig"] = new JObject
                {
                    ["audioEncoding"] = "LINEAR16",
                    ["speakingRate"] = Math.Round(Mathf.Clamp(speakingRate, 0.5f, 2.0f), 2),
                    ["sampleRateHertz"] = Mathf.Clamp(sampleRateHertz, 8000, 48000),
                },
            };

            return payload.ToString(Formatting.None);
        }

        public static bool TryParseSynthesizeResponse(string responseJson, out byte[] audioBytes, out string error)
        {
            audioBytes = null;
            error = null;

            if (string.IsNullOrWhiteSpace(responseJson))
            {
                error = "Google Cloud TTS trả về phản hồi rỗng.";
                return false;
            }

            try
            {
                var root = JObject.Parse(responseJson);
                if (root["error"] is JObject errObj)
                {
                    string msg = errObj["message"]?.Value<string>();
                    error = string.IsNullOrWhiteSpace(msg) ? "Google Cloud TTS trả về lỗi." : msg;
                    return false;
                }

                string audioBase64 = root["audioContent"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(audioBase64))
                {
                    error = "Phản hồi Google Cloud TTS thiếu trường audioContent.";
                    return false;
                }

                audioBytes = Convert.FromBase64String(audioBase64);
                return audioBytes.Length > 0;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    internal sealed class UnityWebRequestCloudTtsTransport : IGoogleCloudTtsTransport
    {
        private const int TimeoutSeconds = 6;

        public IEnumerator Synthesize(string apiKey, string requestJson, Action<long, string, string> onCompleted)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(requestJson ?? "{}");
            using (var request = new UnityWebRequest(GoogleCloudTtsClient.EndpointUrl, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = TimeoutSeconds;
                request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
                request.SetRequestHeader("X-Goog-Api-Key", apiKey ?? string.Empty);

                yield return request.SendWebRequest();

                string body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                string err = request.result == UnityWebRequest.Result.Success ? null : request.error;
                onCompleted?.Invoke(request.responseCode, body, err);
            }
        }
    }

    internal sealed class UnityWebRequestTranslateTtsTransport : IGoogleTranslateTtsTransport
    {
        private const int TimeoutSeconds = 6;

        public IEnumerator SynthesizeClip(string url, Action<long, string, AudioClip, string> onCompleted)
        {
            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
            {
                request.timeout = TimeoutSeconds;
                yield return request.SendWebRequest();

                string contentType = request.GetResponseHeader("Content-Type") ?? string.Empty;
                bool isAudio = string.IsNullOrEmpty(contentType) ||
                    contentType.IndexOf("audio", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    contentType.IndexOf("octet-stream", StringComparison.OrdinalIgnoreCase) >= 0;

                if (request.result != UnityWebRequest.Result.Success ||
                    request.responseCode < 200 ||
                    request.responseCode >= 300 ||
                    !isAudio)
                {
                    string err = !string.IsNullOrEmpty(request.error)
                        ? request.error
                        : $"HTTP {request.responseCode} ({contentType})";
                    onCompleted?.Invoke(request.responseCode, contentType, null, err);
                    yield break;
                }

                AudioClip clip = null;
                try
                {
                    clip = DownloadHandlerAudioClip.GetContent(request);
                }
                catch (Exception ex)
                {
                    onCompleted?.Invoke(request.responseCode, contentType, null, ex.Message);
                    yield break;
                }

                if (clip == null || clip.length <= 0.01f)
                {
                    if (clip != null) UnityEngine.Object.Destroy(clip);
                    onCompleted?.Invoke(request.responseCode, contentType, null, "Không giải mã được audio MP3 từ Google Translate TTS.");
                    yield break;
                }

                onCompleted?.Invoke(request.responseCode, contentType, clip, null);
            }
        }
    }

    internal sealed class NoopGoogleTtsPlatformBridge : IGoogleTtsPlatformBridge
    {
        public bool IsReady => false;
        public bool IsSpeaking => false;
        public string EngineLabel => "Google Android TTS (chỉ khả dụng trên Android)";
        public string LastError => string.Empty;

        public void Initialize() { }
        public void Speak(string text, float rate, float pitch) { }
        public void Stop() { }
        public void Shutdown() { }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    internal sealed class AndroidGoogleTtsBridge : IGoogleTtsPlatformBridge
    {
        private const string BridgeClassName = "com.vps.navigation.tts.VpsGoogleTtsBridge";
        private AndroidJavaClass bridgeClass;
        private bool disposed;

        public bool IsReady => CallStatic<bool>("isReady", false);
        public bool IsSpeaking => CallStatic<bool>("isSpeaking", false);

        public string EngineLabel
        {
            get
            {
                string summary = CallStatic<string>("getStatusSummary", string.Empty);
                return string.IsNullOrWhiteSpace(summary) ? "Google Android TTS" : summary;
            }
        }

        public string LastError => CallStatic<string>("getLastError", string.Empty);

        public void Initialize()
        {
            EnsureBridgeClass();
            if (bridgeClass == null) return;
            try
            {
                bridgeClass.CallStatic("init");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GoogleTTS] Android init failed: " + ex.Message);
            }
        }

        public void Speak(string text, float rate, float pitch)
        {
            EnsureBridgeClass();
            if (bridgeClass == null) return;
            try
            {
                bridgeClass.CallStatic("speak", text, rate, pitch);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GoogleTTS] Android speak failed: " + ex.Message);
            }
        }

        public void Stop()
        {
            if (bridgeClass == null) return;
            try
            {
                bridgeClass.CallStatic("stop");
            }
            catch (Exception) { }
        }

        public void Shutdown()
        {
            if (disposed) return;
            disposed = true;
            if (bridgeClass != null)
            {
                try
                {
                    bridgeClass.CallStatic("shutdown");
                }
                catch (Exception) { }
                bridgeClass.Dispose();
                bridgeClass = null;
            }
        }

        private void EnsureBridgeClass()
        {
            if (disposed || bridgeClass != null) return;
            try
            {
                bridgeClass = new AndroidJavaClass(BridgeClassName);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GoogleTTS] Cannot resolve VpsGoogleTtsBridge: " + ex.Message);
            }
        }

        private T CallStatic<T>(string methodName, T fallback)
        {
            if (disposed || bridgeClass == null) return fallback;
            try
            {
                return bridgeClass.CallStatic<T>(methodName);
            }
            catch (Exception)
            {
                return fallback;
            }
        }
    }
#endif

    /// <summary>
    /// Unified Google Text-to-Speech controller:
    /// uses Google Translate TTS by default (zero-config, chunked up to 180 chars with LRU caching),
    /// supports optional Google Cloud TTS REST API when an API key is provided,
    /// and seamlessly falls back to Android's native Google Text-to-Speech engine (com.google.android.tts) when offline.
    /// </summary>
    public sealed class GoogleTextToSpeechService : MonoBehaviour
    {
        public const float MinSpeakingRate = 0.75f;
        public const float MaxSpeakingRate = 1.35f;
        public const float DefaultSpeakingRate = 1.00f;
        private const int MaxCachedClips = 16;

        private sealed class CachedAudioEntry
        {
            public string CacheKey;
            public AudioClip Clip;
        }

        private readonly List<CachedAudioEntry> clipCache = new List<CachedAudioEntry>();
        private IGoogleTtsPlatformBridge platformBridge;
        private IGoogleCloudTtsTransport cloudTransport;
        private IGoogleTranslateTtsTransport translateTransport;
        private AudioSource audioSource;
        private Coroutine activeSpeechCoroutine;
        private int activeRequestSerial;
        private bool networkSynthesisPending;
        private bool usedNativeFallbackRecently;
        private string lastNetworkError = string.Empty;
        private string lastSpokenText = string.Empty;

        private bool ttsEnabled = true;
        private float speakingRate = DefaultSpeakingRate;
        private string cloudApiKey = string.Empty;
        private string cloudVoiceName = GoogleCloudTtsClient.DefaultVoiceName;

        public bool Enabled
        {
            get => ttsEnabled;
            set
            {
                ttsEnabled = value;
                if (!ttsEnabled)
                {
                    Stop();
                }
            }
        }

        public float SpeakingRate
        {
            get => speakingRate;
            set => speakingRate = Mathf.Clamp(value, MinSpeakingRate, MaxSpeakingRate);
        }

        public string CloudApiKey
        {
            get => cloudApiKey;
            set
            {
                string normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
                if (!string.Equals(cloudApiKey, normalized, StringComparison.Ordinal))
                {
                    cloudApiKey = normalized;
                    usedNativeFallbackRecently = false;
                    lastNetworkError = string.Empty;
                }
            }
        }

        public string CloudVoiceName
        {
            get => cloudVoiceName;
            set => cloudVoiceName = string.IsNullOrWhiteSpace(value) ? GoogleCloudTtsClient.DefaultVoiceName : value.Trim();
        }

        public bool HasCloudApiKey => !string.IsNullOrWhiteSpace(cloudApiKey);
        public string LastSpokenText => lastSpokenText;
        public string LastCloudError => lastNetworkError;
        public string LastNetworkError => lastNetworkError;

        public bool IsSpeaking =>
            networkSynthesisPending ||
            (audioSource != null && audioSource.isPlaying) ||
            (platformBridge != null && platformBridge.IsSpeaking);

        public string StatusSummary
        {
            get
            {
                if (!ttsEnabled)
                {
                    return "Tự động đọc đang tắt";
                }

                EnsureBridgesInitialized();
                string activeMode = HasCloudApiKey
                    ? $"Google Cloud TTS ({cloudVoiceName})"
                    : "Google Translate TTS (vi)";

                if (usedNativeFallbackRecently)
                {
                    string nativeLabel = platformBridge != null ? platformBridge.EngineLabel : "Google Android TTS";
                    return string.IsNullOrEmpty(lastNetworkError)
                        ? $"{activeMode} -> {nativeLabel}"
                        : $"{activeMode} ({lastNetworkError}) -> {nativeLabel}";
                }

                return activeMode;
            }
        }

        public void SetPlatformBridgeForTesting(IGoogleTtsPlatformBridge bridge)
        {
            if (platformBridge != null && platformBridge != bridge)
            {
                platformBridge.Shutdown();
            }
            platformBridge = bridge;
            platformBridge?.Initialize();
        }

        public void SetCloudTransportForTesting(IGoogleCloudTtsTransport transport)
        {
            cloudTransport = transport;
        }

        public void SetTranslateTransportForTesting(IGoogleTranslateTtsTransport transport)
        {
            translateTransport = transport;
        }

        public void Prewarm()
        {
            EnsureBridgesInitialized();
        }

        /// <summary>
        /// Speaks the specified text when TTS is enabled (respecting user mute setting).
        /// </summary>
        public void Speak(string text)
        {
            if (!ttsEnabled) return;
            SpeakInternal(text);
        }

        /// <summary>
        /// Speaks immediately for explicit user actions (e.g. Replay / Test voice button), even if auto-announce is muted.
        /// </summary>
        public void SpeakManual(string text)
        {
            SpeakInternal(text);
        }

        public void Stop()
        {
            activeRequestSerial++;
            networkSynthesisPending = false;
            if (activeSpeechCoroutine != null)
            {
                StopCoroutine(activeSpeechCoroutine);
                activeSpeechCoroutine = null;
            }
            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
            }
            platformBridge?.Stop();
        }

        private void SpeakInternal(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText)) return;

            string text = rawText.Trim();
            Stop();
            EnsureBridgesInitialized();
            lastSpokenText = text;
            int serial = activeRequestSerial;

            if (HasCloudApiKey)
            {
                string cacheKey = BuildCacheKey("cloud", text, speakingRate, cloudVoiceName);
                if (TryGetCachedClip(cacheKey, out AudioClip cachedClip) && TryPlayAudioClip(cachedClip))
                {
                    usedNativeFallbackRecently = false;
                    lastNetworkError = string.Empty;
                    return;
                }

                networkSynthesisPending = true;
                activeSpeechCoroutine = StartCoroutine(SynthesizeAndPlayCloudCoroutine(serial, text, cacheKey));
                return;
            }

            List<string> chunks = GoogleTranslateTtsClient.SplitTextIntoChunks(text);
            if (chunks.Count == 0) return;

            networkSynthesisPending = true;
            activeSpeechCoroutine = StartCoroutine(SynthesizeAndPlayTranslateCoroutine(serial, text, chunks));
        }

        private IEnumerator SynthesizeAndPlayTranslateCoroutine(
            int requestSerial,
            string fullText,
            List<string> chunks)
        {
            AudioClip prefetchedClip = null;
            string prefetchedError = null;

            for (int i = 0; i < chunks.Count; i++)
            {
                if (requestSerial != activeRequestSerial)
                {
                    if (prefetchedClip != null && !IsClipCached(prefetchedClip)) Destroy(prefetchedClip);
                    yield break;
                }

                AudioClip currentClip = null;
                string chunkError = null;

                if (i > 0 && (prefetchedClip != null || !string.IsNullOrEmpty(prefetchedError)))
                {
                    currentClip = prefetchedClip;
                    chunkError = prefetchedError;
                    prefetchedClip = null;
                    prefetchedError = null;
                }
                else
                {
                    IEnumerator fetchStep = FetchTranslateChunkCoroutine(
                        requestSerial,
                        chunks[i],
                        (clip, err) =>
                        {
                            currentClip = clip;
                            chunkError = err;
                        });
                    while (fetchStep.MoveNext())
                    {
                        yield return fetchStep.Current;
                    }
                }

                if (requestSerial != activeRequestSerial)
                {
                    if (currentClip != null && !IsClipCached(currentClip)) Destroy(currentClip);
                    yield break;
                }

                if (!TryPlayAudioClip(currentClip))
                {
                    networkSynthesisPending = false;
                    activeSpeechCoroutine = null;
                    string fallbackText = JoinRemainingChunks(chunks, i, fullText);
                    FallbackToAndroidTts(
                        string.IsNullOrEmpty(chunkError) ? "Không phát được âm thanh" : chunkError,
                        fallbackText);
                    yield break;
                }

                usedNativeFallbackRecently = false;
                lastNetworkError = string.Empty;

                // Prefetch next chunk in parallel while current chunk plays in PlayMode
                if (i + 1 < chunks.Count)
                {
                    string nextErr = null;
                    AudioClip nextClip = null;
                    IEnumerator prefetchStep = FetchTranslateChunkCoroutine(
                        requestSerial,
                        chunks[i + 1],
                        (clip, err) =>
                        {
                            nextClip = clip;
                            nextErr = err;
                        });
                    while (prefetchStep.MoveNext())
                    {
                        yield return prefetchStep.Current;
                    }

                    if (requestSerial != activeRequestSerial)
                    {
                        if (nextClip != null && !IsClipCached(nextClip)) Destroy(nextClip);
                        yield break;
                    }
                    prefetchedClip = nextClip;
                    prefetchedError = nextErr;
                }

                while (Application.isPlaying && audioSource != null && audioSource.isPlaying)
                {
                    if (requestSerial != activeRequestSerial)
                    {
                        if (prefetchedClip != null && !IsClipCached(prefetchedClip)) Destroy(prefetchedClip);
                        yield break;
                    }
                    yield return null;
                }
            }

            if (requestSerial == activeRequestSerial)
            {
                networkSynthesisPending = false;
                activeSpeechCoroutine = null;
            }
        }

        private IEnumerator FetchTranslateChunkCoroutine(
            int requestSerial,
            string chunk,
            Action<AudioClip, string> onResult)
        {
            string url = GoogleTranslateTtsClient.BuildRequestUrl(
                chunk,
                GoogleTranslateTtsClient.DefaultLanguageCode,
                speakingRate);
            string cacheKey = BuildCacheKey("gtranslate", chunk, speakingRate, url);

            if (TryGetCachedClip(cacheKey, out AudioClip cached))
            {
                onResult?.Invoke(cached, null);
                yield break;
            }

            AudioClip downloadedClip = null;
            string errorMsg = null;

            IEnumerator routine = translateTransport.SynthesizeClip(url, (code, contentType, clip, err) =>
            {
                if (!string.IsNullOrEmpty(err))
                {
                    errorMsg = err;
                }
                else if (clip == null)
                {
                    errorMsg = $"HTTP {code}";
                }
                else
                {
                    downloadedClip = clip;
                }
            });

            if (routine != null)
            {
                while (routine.MoveNext())
                {
                    yield return routine.Current;
                }
            }

            if (requestSerial != activeRequestSerial)
            {
                if (downloadedClip != null) Destroy(downloadedClip);
                yield break;
            }

            if (downloadedClip != null)
            {
                CacheAudioClip(cacheKey, downloadedClip);
                onResult?.Invoke(downloadedClip, null);
            }
            else
            {
                onResult?.Invoke(null, string.IsNullOrEmpty(errorMsg) ? "Google Translate TTS thất bại" : errorMsg);
            }
        }

        private IEnumerator SynthesizeAndPlayCloudCoroutine(int requestSerial, string text, string cacheKey)
        {
            string requestJson = GoogleCloudTtsClient.BuildSynthesizeRequestJson(
                text,
                GoogleCloudTtsClient.DefaultLanguageCode,
                cloudVoiceName,
                speakingRate);

            long statusCode = 0;
            string responseBody = null;
            string transportError = null;

            IEnumerator transportRoutine = cloudTransport.Synthesize(cloudApiKey, requestJson, (code, body, err) =>
            {
                statusCode = code;
                responseBody = body;
                transportError = err;
            });
            if (transportRoutine != null)
            {
                while (transportRoutine.MoveNext())
                {
                    yield return transportRoutine.Current;
                }
            }

            if (requestSerial != activeRequestSerial)
            {
                yield break;
            }

            networkSynthesisPending = false;
            activeSpeechCoroutine = null;

            bool success = string.IsNullOrEmpty(transportError) && statusCode >= 200 && statusCode < 300;
            byte[] wavBytes = null;
            string parseError = null;
            float[] samples = null;
            int sampleRate = 0;
            int channels = 0;
            string wavError = null;

            if (success &&
                GoogleCloudTtsClient.TryParseSynthesizeResponse(responseBody, out wavBytes, out parseError) &&
                GoogleTtsWavDecoder.TryDecodePcm16Wav(wavBytes, out samples, out sampleRate, out channels, out wavError))
            {
                AudioClip clip = CreateAndCachePcmAudioClip(cacheKey, samples, channels, sampleRate);
                if (TryPlayAudioClip(clip))
                {
                    usedNativeFallbackRecently = false;
                    lastNetworkError = string.Empty;
                    yield break;
                }
                wavError = "Không thể phát AudioClip";
            }

            string errDetail = !string.IsNullOrEmpty(parseError)
                ? parseError
                : (!string.IsNullOrEmpty(wavError)
                    ? wavError
                    : (!string.IsNullOrEmpty(transportError) ? transportError : $"HTTP {statusCode}"));
            FallbackToAndroidTts(errDetail, text);
        }

        private void FallbackToAndroidTts(string reason, string text)
        {
            usedNativeFallbackRecently = true;
            lastNetworkError = reason ?? string.Empty;
            Debug.LogWarning("[GoogleTTS] Fallback to Android TTS: " + lastNetworkError);
            platformBridge?.Speak(text, speakingRate, 1.0f);
        }

        private static string JoinRemainingChunks(List<string> chunks, int startIndex, string fullText)
        {
            if (startIndex <= 0 || chunks == null || startIndex >= chunks.Count)
            {
                return fullText;
            }

            var sb = new StringBuilder();
            for (int i = startIndex; i < chunks.Count; i++)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(chunks[i]);
            }
            return sb.ToString();
        }

        private void EnsureBridgesInitialized()
        {
            if (platformBridge == null)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                platformBridge = new AndroidGoogleTtsBridge();
#else
                platformBridge = new NoopGoogleTtsPlatformBridge();
#endif
                platformBridge.Initialize();
            }

            if (cloudTransport == null)
            {
                cloudTransport = new UnityWebRequestCloudTtsTransport();
            }

            if (translateTransport == null)
            {
                translateTransport = new UnityWebRequestTranslateTtsTransport();
            }
        }

        private bool TryPlayAudioClip(AudioClip clip)
        {
            if (clip == null) return false;
            EnsureAudioPlaybackReady();
            if (audioSource == null) return false;
            audioSource.clip = clip;
            audioSource.Play();
            return true;
        }

        private void EnsureAudioPlaybackReady()
        {
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null)
                {
                    audioSource = gameObject.AddComponent<AudioSource>();
                }
                audioSource.playOnAwake = false;
                audioSource.loop = false;
                audioSource.spatialBlend = 0f;
                audioSource.volume = 1f;
            }

            AudioListener existing = FindFirstObjectByType<AudioListener>(FindObjectsInactive.Exclude);
            if (existing == null)
            {
                GameObject listenerHost = Camera.main != null ? Camera.main.gameObject : gameObject;
                if (listenerHost.GetComponent<AudioListener>() == null)
                {
                    listenerHost.AddComponent<AudioListener>();
                }
            }
        }

        private AudioClip CreateAndCachePcmAudioClip(string cacheKey, float[] samples, int channels, int sampleRate)
        {
            if (samples == null || samples.Length == 0 || channels <= 0 || sampleRate <= 0)
            {
                return null;
            }

            int frames = samples.Length / channels;
            if (frames <= 0) return null;

            AudioClip clip = AudioClip.Create("GoogleTtsPcm", frames, channels, sampleRate, false);
            if (clip == null) return null;
            clip.SetData(samples, 0);
            CacheAudioClip(cacheKey, clip);
            return clip;
        }

        private void CacheAudioClip(string cacheKey, AudioClip clip)
        {
            if (clip == null || string.IsNullOrEmpty(cacheKey)) return;

            for (int i = 0; i < clipCache.Count; i++)
            {
                if (string.Equals(clipCache[i].CacheKey, cacheKey, StringComparison.Ordinal))
                {
                    clipCache.RemoveAt(i);
                    break;
                }
            }

            if (clipCache.Count >= MaxCachedClips)
            {
                CachedAudioEntry oldest = clipCache[0];
                clipCache.RemoveAt(0);
                if (oldest.Clip != null && oldest.Clip != clip) Destroy(oldest.Clip);
            }

            clipCache.Add(new CachedAudioEntry { CacheKey = cacheKey, Clip = clip });
        }

        private bool IsClipCached(AudioClip clip)
        {
            if (clip == null) return false;
            for (int i = 0; i < clipCache.Count; i++)
            {
                if (clipCache[i].Clip == clip) return true;
            }
            return false;
        }

        private bool TryGetCachedClip(string cacheKey, out AudioClip clip)
        {
            for (int i = 0; i < clipCache.Count; i++)
            {
                if (string.Equals(clipCache[i].CacheKey, cacheKey, StringComparison.Ordinal))
                {
                    CachedAudioEntry entry = clipCache[i];
                    if (entry.Clip == null)
                    {
                        clipCache.RemoveAt(i);
                        break;
                    }
                    clipCache.RemoveAt(i);
                    clipCache.Add(entry);
                    clip = entry.Clip;
                    return true;
                }
            }

            clip = null;
            return false;
        }

        private static string BuildCacheKey(string provider, string text, float rate, string extra)
        {
            return string.Concat(
                provider,
                "|",
                extra ?? string.Empty,
                "|",
                rate.ToString("0.00", CultureInfo.InvariantCulture),
                "|",
                text ?? string.Empty);
        }

        private void Awake()
        {
            EnsureBridgesInitialized();
        }

        private void OnDestroy()
        {
            Stop();
            for (int i = 0; i < clipCache.Count; i++)
            {
                if (clipCache[i].Clip != null) Destroy(clipCache[i].Clip);
            }
            clipCache.Clear();
            platformBridge?.Shutdown();
            platformBridge = null;
        }
    }
}
