using NUnit.Framework;
using AlterOS.Tts;

namespace AlterOS.Tests
{
    public class OpenRouterTtsClientTests
    {
        [Test]
        public void BuildsTheOpenRouterSpeechPayload()
        {
            OpenRouterSpeechPayload payload = OpenRouterTtsClient.BuildPayload("Hello", "Serena");

            Assert.AreEqual("qwen/qwen-audio-3.0-tts-plus", payload.model);
            Assert.AreEqual("Hello", payload.input);
            Assert.AreEqual("Serena", payload.voice);
            Assert.AreEqual("mp3", payload.response_format);
        }
    }
}
