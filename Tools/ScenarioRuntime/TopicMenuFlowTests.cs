using System;
using System.Collections.Generic;
using NUnit.Framework;
using AlterOS.Conversation;
using AlterOS.Topics;

public sealed class TopicMenuFlowTests
{
    [Test]
    public void ExplicitTopicMenuStartsOnlyItsAvailableTopic()
    {
        string started = null;
        bool available = TopicMenuSelection.IsEligible("topic_start", "draw_together",
            id => id == "draw_together");

        bool handled = TopicMenuSelection.TryStart("TOPIC_START", "draw_together",
            id => id == "draw_together", id => started = id);

        Assert.IsTrue(available);
        Assert.IsTrue(handled);
        Assert.AreEqual("draw_together", started);
    }

    [Test]
    public void ExplicitTopicMenuFailsClosedForUnknownUnavailableOrMissingSelection()
    {
        int starts = 0;
        Func<string, bool> available = id => id == "known";
        Action<string> start = id => starts++;

        Assert.IsTrue(TopicMenuSelection.IsEligible("activity_start", "known", available));
        Assert.IsFalse(TopicMenuSelection.IsEligible("topic_start", "unknown", available));
        Assert.IsFalse(TopicMenuSelection.IsEligible("topic_start", " ", available));
        Assert.IsFalse(TopicMenuSelection.TryStart("activity_start", "known", available, start));
        Assert.IsFalse(TopicMenuSelection.TryStart("topic_start", "unknown", available, start));
        Assert.IsFalse(TopicMenuSelection.TryStart("topic_start", "known", available, null));
        Assert.AreEqual(0, starts);
    }

    [Test]
    public void ExplicitMenuTopicCanBeTheOnlyCandidateWithoutSemanticRouteMatches()
    {
        var catalogue = new Dictionary<string, TopicDefinition>(StringComparer.Ordinal)
        {
            { "draw_together", new TopicDefinition { topicId = "draw_together", prompt = "Draw together?" } }
        };
        TopicCandidateSet candidates = TopicCandidateRetriever.FromRouteMatches(
            new AlterOS.Semantics.DialogueRouteMatch[0], "warmed-index", catalogue);

        bool included = TopicCandidateRetriever.TryIncludeExplicitTopic(candidates, "draw_together", catalogue);

        Assert.That(included, Is.True);
        Assert.That(candidates.cards.Count, Is.EqualTo(1));
        Assert.That(candidates.cards[0].topicId, Is.EqualTo("draw_together"));
        Assert.That(candidates.cards[0].similarity, Is.EqualTo(0f));
    }
}
