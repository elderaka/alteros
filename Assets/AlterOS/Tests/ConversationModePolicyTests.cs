using NUnit.Framework;
using AlterOS.Dialogue;

namespace AlterOS.Tests
{
    public sealed class ConversationModePolicyTests
    {
        [Test]
        public void DefaultModeIsTopic()
        {
            Assert.That(ConversationModePolicy.Default, Is.EqualTo(ConversationMode.Topic));
        }

        [Test]
        public void TopicModeIsAlwaysSelectable()
        {
            Assert.That(ConversationModePolicy.CanSelect(ConversationMode.Topic, agentAvailable: false), Is.True);
        }

        [Test]
        public void AgentModeRequiresAnAvailableAgent()
        {
            Assert.That(ConversationModePolicy.CanSelect(ConversationMode.Agent, agentAvailable: false), Is.False);
            Assert.That(ConversationModePolicy.CanSelect(ConversationMode.Agent, agentAvailable: true), Is.True);
        }
    }
}
