using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using Vps.UnityClient.Navigation;

namespace Vps.UnityClient.Tests.EditMode
{
    public sealed class GoogleTextToSpeechTests
    {
        private sealed class FakePlatformBridge : IGoogleTtsPlatformBridge
        {
            public readonly List<(string text, float rate, float pitch)> Calls = new List<(string, float, float)>();
            public int StopCalls;
            public bool IsReady { get; set; } = true;
            public bool IsSpeaking { get; set; }
            public string EngineLabel { get; set; } = "Google Android TTS (vi-VN)";
            public string LastError { get; set; } = string.Empty;

            public void Initialize() { }

            public void Speak(string text, float rate, float pitch)
            {
                Calls.Add((text, rate, pitch));
                IsSpeaking = true;
            }

            public void Stop()
            {
                StopCalls++;
                IsSpeaking = false;
            }

            public void Shutdown() { }
        }

        private sealed class FakeCloudTransport : IGoogleCloudTtsTransport
        {
            private readonly long statusCode;
            private readonly string responseBody;
            private readonly string error;

            public int RequestCount;
            public string LastApiKey;

            public FakeCloudTransport(long statusCode, string responseBody, string error = null)
            {
                this.statusCode = statusCode;
                this.responseBody = responseBody;
                this.error = error;
            }

            public IEnumerator Synthesize(string apiKey, string requestJson, Action<long, string, string> onCompleted)
            {
                RequestCount++;
                LastApiKey = apiKey;
                onCompleted?.Invoke(statusCode, responseBody, error);
                yield break;
            }
        }

        private sealed class FakeTranslateTransport : IGoogleTranslateTtsTransport
        {
            private readonly long statusCode;
            private readonly string contentType;
            private readonly bool returnValidClip;
            private readonly string error;

            public readonly List<string> RequestedUrls = new List<string>();

            public FakeTranslateTransport(long statusCode, string contentType, bool returnValidClip, string error = null)
            {
                this.statusCode = statusCode;
                this.contentType = contentType;
                this.returnValidClip = returnValidClip;
                this.error = error;
            }

            public IEnumerator SynthesizeClip(string url, Action<long, string, AudioClip, string> onCompleted)
            {
                RequestedUrls.Add(url);
                AudioClip clip = returnValidClip
                    ? AudioClip.Create("MockTranslateTts", 240, 1, 24000, false)
                    : null;
                onCompleted?.Invoke(statusCode, contentType, clip, error);
                yield break;
            }
        }

        private GameObject host;

        [TearDown]
        public void TearDown()
        {
            if (host != null)
            {
                UnityEngine.Object.DestroyImmediate(host);
                host = null;
            }
        }

        [Test]
        public void TranslateClientSplitsLongTextAtNaturalBoundariesUnderLimitAndBuildsUrl()
        {
            string longVietnamese =
                "Chào mừng bạn đến phòng họp lớn tầng hai! " +
                "Ở đây có đầy đủ máy chiếu, màn hình tương tác và hệ thống âm thanh hội nghị trực tuyến. " +
                "Lối đi sang khu vực căn tin và thang máy nằm ngay phía bên tay phải hành lang, rất thuận tiện cho khách tham quan khi di chuyển giữa các tầng.";

            List<string> chunks = GoogleTranslateTtsClient.SplitTextIntoChunks(longVietnamese, 120);
            Assert.That(chunks.Count, Is.GreaterThanOrEqualTo(3));
            for (int i = 0; i < chunks.Count; i++)
            {
                Assert.That(chunks[i].Length, Is.LessThanOrEqualTo(120));
                Assert.That(chunks[i], Is.Not.Empty);
            }
            Assert.That(string.Join(" ", chunks), Does.StartWith("Chào mừng bạn đến phòng họp lớn tầng hai!"));

            string url = GoogleTranslateTtsClient.BuildRequestUrl("Đã tới Cổng chính.", "vi", 1.0f);
            Assert.That(url, Does.StartWith("https://translate.google.com/translate_tts?ie=UTF-8&tl=vi&client=tw-ob"));
            Assert.That(url, Does.Contain("q="));
        }

        [Test]
        public void DefaultModeUsesGoogleTranslateTtsCachesChunksRespectsMuteAndFallsBackToAndroidOnError()
        {
            host = new GameObject("TtsTranslateHost");
            var tts = host.AddComponent<GoogleTextToSpeechService>();
            var bridge = new FakePlatformBridge();
            var translateOk = new FakeTranslateTransport(200, "audio/mpeg", true);
            tts.SetPlatformBridgeForTesting(bridge);
            tts.SetTranslateTransportForTesting(translateOk);

            Assert.That(tts.StatusSummary, Is.EqualTo("Google Translate TTS (vi)"));

            tts.Speak("Đã tới Cổng chính. Bên phải là quầy lễ tân.");
            Assert.That(translateOk.RequestedUrls.Count, Is.EqualTo(1));
            Assert.That(translateOk.RequestedUrls[0], Does.Contain("client=tw-ob"));
            Assert.That(bridge.Calls.Count, Is.EqualTo(0));

            // Identical text reuses cached AudioClip without extra HTTP fetch
            tts.Speak("Đã tới Cổng chính. Bên phải là quầy lễ tân.");
            Assert.That(translateOk.RequestedUrls.Count, Is.EqualTo(1));
            Assert.That(bridge.Calls.Count, Is.EqualTo(0));

            // Long description (> 180 chars) splits into multiple chunks cleanly
            string longIntro =
                "Chào mừng đến sảnh đón tiếp trung tâm của tòa nhà. " +
                "Khu vực này có quầy hỗ trợ khách hàng, khu ngồi chờ thoải mái và bảng thông tin hướng dẫn tham quan. " +
                "Nếu bạn cần tìm phòng họp hoặc thang máy, hãy tiếp tục chọn điểm đến mới trên màn hình.";
            tts.Speak(longIntro);
            Assert.That(translateOk.RequestedUrls.Count, Is.GreaterThanOrEqualTo(3));
            Assert.That(bridge.Calls.Count, Is.EqualTo(0));

            // Disabling auto TTS stops active playback and blocks Speak(), while SpeakManual() still speaks
            tts.Enabled = false;
            Assert.That(bridge.StopCalls, Is.GreaterThanOrEqualTo(1));
            int urlsBeforeMute = translateOk.RequestedUrls.Count;
            tts.Speak("Câu không nên tự phát");
            Assert.That(translateOk.RequestedUrls.Count, Is.EqualTo(urlsBeforeMute));

            tts.SpeakManual("Phát thủ công khi bấm loa");
            Assert.That(translateOk.RequestedUrls.Count, Is.EqualTo(urlsBeforeMute + 1));

            // Re-enable and simulate HTTP 429 / offline failure -> seamless fallback to Android Google TTS
            tts.Enabled = true;
            var translateFail = new FakeTranslateTransport(429, "text/html", false, "HTTP 429 Too Many Requests");
            tts.SetTranslateTransportForTesting(translateFail);

            tts.Speak("Thông báo dự phòng khi offline");
            Assert.That(translateFail.RequestedUrls.Count, Is.EqualTo(1));
            Assert.That(bridge.Calls.Count, Is.EqualTo(1));
            Assert.That(bridge.Calls[0].text, Is.EqualTo("Thông báo dự phòng khi offline"));
            Assert.That(tts.StatusSummary, Does.Contain("Google Android TTS"));
        }

        [Test]
        public void CloudClientBuildsVietnameseLinear16PayloadAndParsesAudioContent()
        {
            string json = GoogleCloudTtsClient.BuildSynthesizeRequestJson(
                "Đã tới Phòng họp lớn.",
                GoogleCloudTtsClient.DefaultLanguageCode,
                GoogleCloudTtsClient.DefaultVoiceName,
                1.15f,
                24000);

            JObject obj = JObject.Parse(json);
            Assert.That((string)obj["input"]?["text"], Is.EqualTo("Đã tới Phòng họp lớn."));
            Assert.That((string)obj["voice"]?["languageCode"], Is.EqualTo("vi-VN"));
            Assert.That((string)obj["voice"]?["name"], Is.EqualTo("vi-VN-Wavenet-A"));
            Assert.That((string)obj["audioConfig"]?["audioEncoding"], Is.EqualTo("LINEAR16"));
            Assert.That((double)obj["audioConfig"]?["speakingRate"], Is.EqualTo(1.15d).Within(0.01d));

            byte[] wav = BuildTestPcm16Wav(24000, new short[] { 0, 16384, -16384, 32767 });
            string responseJson = "{\"audioContent\":\"" + Convert.ToBase64String(wav) + "\"}";
            Assert.That(GoogleCloudTtsClient.TryParseSynthesizeResponse(responseJson, out byte[] decoded, out string err), Is.True, err);
            Assert.That(decoded, Is.EqualTo(wav));
        }

        [Test]
        public void WavDecoderParsesPcm16WithExtendedChunkAndRejectsCorruptHeader()
        {
            byte[] wavWithJunk = BuildTestPcm16Wav(24000, new short[] { 0, 16384, -16384 }, includeJunkChunk: true);
            Assert.That(
                GoogleTtsWavDecoder.TryDecodePcm16Wav(wavWithJunk, out float[] samples, out int hz, out int ch, out string err),
                Is.True,
                err);
            Assert.That(hz, Is.EqualTo(24000));
            Assert.That(ch, Is.EqualTo(1));
            Assert.That(samples.Length, Is.EqualTo(3));
            Assert.That(samples[1], Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(samples[2], Is.EqualTo(-0.5f).Within(0.001f));

            Assert.That(GoogleTtsWavDecoder.TryDecodePcm16Wav(new byte[12], out _, out _, out _, out _), Is.False);
        }

        [Test]
        public void CloudApiKeyOverridesDefaultCachesResultAndFallsBackToAndroidOnError()
        {
            host = new GameObject("TtsCloudHost");
            var tts = host.AddComponent<GoogleTextToSpeechService>();
            var bridge = new FakePlatformBridge();
            tts.SetPlatformBridgeForTesting(bridge);

            byte[] wav = BuildTestPcm16Wav(24000, new short[240]);
            var okTransport = new FakeCloudTransport(
                200,
                "{\"audioContent\":\"" + Convert.ToBase64String(wav) + "\"}");
            tts.SetCloudTransportForTesting(okTransport);
            tts.CloudApiKey = "test-google-cloud-key";

            tts.Speak("Chào mừng tới phòng họp");
            Assert.That(okTransport.RequestCount, Is.EqualTo(1));
            Assert.That(okTransport.LastApiKey, Is.EqualTo("test-google-cloud-key"));
            Assert.That(bridge.Calls.Count, Is.EqualTo(0));
            Assert.That(tts.LastCloudError, Is.Empty);

            tts.Speak("Chào mừng tới phòng họp");
            Assert.That(okTransport.RequestCount, Is.EqualTo(1));

            var failTransport = new FakeCloudTransport(
                403,
                "{\"error\":{\"message\":\"API key not valid\"}}",
                "HTTP/1.1 403 Forbidden");
            tts.SetCloudTransportForTesting(failTransport);

            tts.Speak("Thông báo fallback");
            Assert.That(failTransport.RequestCount, Is.EqualTo(1));
            Assert.That(bridge.Calls.Count, Is.EqualTo(1));
            Assert.That(bridge.Calls[0].text, Is.EqualTo("Thông báo fallback"));
            Assert.That(tts.StatusSummary, Does.Contain("Google Android TTS"));
        }

        private static byte[] BuildTestPcm16Wav(int sampleRate, short[] pcmSamples, bool includeJunkChunk = false)
        {
            int dataSize = pcmSamples.Length * 2;
            int junkSize = includeJunkChunk ? 10 : 0;
            int riffChunkSize = 4 + 24 + junkSize + 8 + dataSize;
            var bytes = new byte[8 + riffChunkSize];
            int p = 0;

            WriteAscii(bytes, ref p, "RIFF");
            WriteInt32(bytes, ref p, riffChunkSize);
            WriteAscii(bytes, ref p, "WAVE");

            WriteAscii(bytes, ref p, "fmt ");
            WriteInt32(bytes, ref p, 16);
            WriteInt16(bytes, ref p, 1);
            WriteInt16(bytes, ref p, 1);
            WriteInt32(bytes, ref p, sampleRate);
            WriteInt32(bytes, ref p, sampleRate * 2);
            WriteInt16(bytes, ref p, 2);
            WriteInt16(bytes, ref p, 16);

            if (includeJunkChunk)
            {
                WriteAscii(bytes, ref p, "JUNK");
                WriteInt32(bytes, ref p, 2);
                bytes[p++] = 0xAA;
                bytes[p++] = 0xBB;
            }

            WriteAscii(bytes, ref p, "data");
            WriteInt32(bytes, ref p, dataSize);
            for (int i = 0; i < pcmSamples.Length; i++)
            {
                WriteInt16(bytes, ref p, pcmSamples[i]);
            }

            return bytes;
        }

        private static void WriteAscii(byte[] buffer, ref int pos, string text)
        {
            byte[] ascii = Encoding.ASCII.GetBytes(text);
            Buffer.BlockCopy(ascii, 0, buffer, pos, ascii.Length);
            pos += ascii.Length;
        }

        private static void WriteInt16(byte[] buffer, ref int pos, short value)
        {
            buffer[pos++] = (byte)(value & 0xff);
            buffer[pos++] = (byte)((value >> 8) & 0xff);
        }

        private static void WriteInt32(byte[] buffer, ref int pos, int value)
        {
            buffer[pos++] = (byte)(value & 0xff);
            buffer[pos++] = (byte)((value >> 8) & 0xff);
            buffer[pos++] = (byte)((value >> 16) & 0xff);
            buffer[pos++] = (byte)((value >> 24) & 0xff);
        }
    }
}
