using System.Collections.Generic;
using NUnit.Framework;
using AlterOS.Scenarios;

namespace AlterOS.Tests.Scenarios
{
    public sealed class ScenarioOfferTests
    {
        private static List<ScenarioTopicOffer> Offers()
        {
            return new List<ScenarioTopicOffer>
            {
                new ScenarioTopicOffer { offerId = "cats", label = "Cats", topicId = "cats" },
                new ScenarioTopicOffer { offerId = "drawing", label = "Drawing", topicId = "drawing_together" },
                new ScenarioTopicOffer { offerId = "tea", label = "Tea", topicId = "tea" },
            };
        }

        [Test]
        public void OffersAppearOnlyOnLaterInteractionAndSelectionIsIdempotent()
        {
            var offers = new ScenarioTopicOffers();
            offers.Replace(Offers(), "turn-1");
            ScenarioRuntimeSnapshot sameInteraction = ScenarioTestData.RuntimeSnapshot();
            Assert.IsEmpty(offers.ForInteraction(sameInteraction));

            ScenarioRuntimeSnapshot nextInteraction = ScenarioTestData.WithTurn(ScenarioTestData.RuntimeSnapshot(), "turn-2");
            Assert.AreEqual(3, offers.ForInteraction(nextInteraction).Count);
            ScenarioTopicOfferSelection selected = offers.Select("cats", nextInteraction);
            Assert.AreEqual("selected", selected.status);
            Assert.AreEqual("cats", selected.offer.topicId);
            Assert.AreEqual(3, offers.ForInteraction(nextInteraction).Count, "Selecting must not consume an offer before its turn is accepted.");
            Assert.IsTrue(offers.CommitSelection("cats", nextInteraction));
            Assert.IsFalse(offers.CommitSelection("cats", nextInteraction));
            Assert.AreEqual("already_selected", offers.Select("cats", nextInteraction).status);
        }

        [Test]
        public void OffersAreBoundedAndRecheckedAgainstCurrentTopicAvailability()
        {
            var offers = new ScenarioTopicOffers();
            Assert.IsFalse(offers.Replace(new List<ScenarioTopicOffer>
            {
                new ScenarioTopicOffer { offerId = "1", label = "One", topicId = "cats" },
                new ScenarioTopicOffer { offerId = "2", label = "Two", topicId = "cats" },
                new ScenarioTopicOffer { offerId = "3", label = "Three", topicId = "cats" },
                new ScenarioTopicOffer { offerId = "4", label = "Four", topicId = "cats" },
            }, "turn-1"));
            offers.Replace(Offers(), "turn-1");
            ScenarioRuntimeSnapshot blocked = ScenarioTestData.WithKnownTopics(ScenarioTestData.WithTurn(ScenarioTestData.RuntimeSnapshot(), "turn-2"), "tea");
            Assert.AreEqual(1, offers.ForInteraction(blocked).Count);
            Assert.AreEqual("unavailable", offers.Select("cats", blocked).status);
            Assert.AreEqual("unavailable", offers.Select("missing", blocked).status);
        }

        [Test]
        public void QuitClearsSessionLocalOffers()
        {
            var session = new ScenarioSession();
            session.TopicOffers.Replace(Offers(), "turn-1");
            session.Start(ScenarioTestData.DialogueWaitDefinition(), "run-1", ScenarioTestData.RuntimeSnapshot());
            session.Quit();
            Assert.IsEmpty(session.TopicOffers.ForInteraction(ScenarioTestData.WithTurn(ScenarioTestData.RuntimeSnapshot(), "turn-2")));
        }

        [Test]
        public void IntentOfferRequiresAHostValidatedIntentBinding()
        {
            var offers = new ScenarioTopicOffers();
            Assert.IsTrue(offers.Replace(new List<ScenarioTopicOffer>
            {
                new ScenarioTopicOffer { offerId = "future-cats", label = "Tell me about cats", intentId = "intent.cats" }
            }, "turn-1"));
            ScenarioRuntimeSnapshot nextTurn = ScenarioTestData.WithTurn(ScenarioTestData.RuntimeSnapshot(), "turn-2");
            Assert.IsEmpty(offers.ForInteraction(nextTurn));

            ScenarioRuntimeSnapshot authorized = ScenarioTestData.WithKnownIntents(nextTurn, "intent.cats");
            Assert.AreEqual(1, offers.ForInteraction(authorized).Count);
            Assert.AreEqual("selected", offers.Select("future-cats", authorized).status);
        }
    }
}
