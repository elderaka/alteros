using NUnit.Framework;
using AlterOS.Security;

namespace AlterOS.Tests
{
    public class OpenRouterCredentialStoreTests
    {
        [Test]
        public void EnvironmentKeyTakesPrecedenceOverStoredKey()
        {
            Assert.AreEqual("env-key", OpenRouterCredentialStore.ResolveApiKey("  env-key  ", "stored-key"));
        }

        [Test]
        public void StoredKeyIsUsedWhenEnvironmentKeyIsMissing()
        {
            Assert.AreEqual("stored-key", OpenRouterCredentialStore.ResolveApiKey("", " stored-key "));
        }

        [Test]
        public void MissingKeysResolveToEmpty()
        {
            Assert.AreEqual(string.Empty, OpenRouterCredentialStore.ResolveApiKey(null, "  "));
        }
    }
}
