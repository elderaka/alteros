using System;
using System.Collections.Generic;
using System.Linq;

namespace AlterOS.Scenarios
{
    public sealed class ScenarioTopicOfferSelection
    {
        public readonly string status;
        public readonly ScenarioTopicOffer offer;
        internal ScenarioTopicOfferSelection(string result, ScenarioTopicOffer selected)
        { status = result ?? "unavailable"; offer = selected; }
    }

    /// <summary>Session-local next-interaction offers; these never publish or consume a canonical Topic.</summary>
    public sealed class ScenarioTopicOffers
    {
        private readonly List<ScenarioTopicOffer> offers = new List<ScenarioTopicOffer>();
        private readonly HashSet<string> selected = new HashSet<string>(StringComparer.Ordinal);
        private string createdDuringTurnId = "", lastPresentedTurnId = "";

        public bool Replace(IEnumerable<ScenarioTopicOffer> items, string generatedDuringTurnId)
        {
            var next = items == null ? new List<ScenarioTopicOffer>() : new List<ScenarioTopicOffer>(items);
            if (next.Count > ScenarioDefinitionValidator.MaximumTopicOffers || string.IsNullOrWhiteSpace(generatedDuringTurnId) ||
                next.Any(item => !Valid(item)) || next.Select(item => item.offerId).Distinct(StringComparer.Ordinal).Count() != next.Count)
                return false;
            offers.Clear(); offers.AddRange(next);
            selected.Clear(); createdDuringTurnId = generatedDuringTurnId; lastPresentedTurnId = "";
            return true;
        }

        public IReadOnlyList<ScenarioTopicOffer> ForInteraction(ScenarioRuntimeSnapshot context)
        {
            if (context == null || string.IsNullOrWhiteSpace(context.turnId) || context.turnId == createdDuringTurnId)
                return new ScenarioTopicOffer[0];
            lastPresentedTurnId = context.turnId;
            return Array.AsReadOnly(offers.Where(item => !selected.Contains(item.offerId) && IsEligible(item, context)).ToArray());
        }

        public ScenarioTopicOfferSelection Select(string offerId, ScenarioRuntimeSnapshot context)
        {
            if (selected.Contains(offerId ?? "")) return new ScenarioTopicOfferSelection("already_selected", null);
            if (context == null || string.IsNullOrEmpty(offerId) || context.turnId != lastPresentedTurnId)
                return new ScenarioTopicOfferSelection("unavailable", null);
            ScenarioTopicOffer offer = offers.FirstOrDefault(item => item.offerId == offerId);
            if (offer == null || !IsEligible(offer, context)) return new ScenarioTopicOfferSelection("unavailable", null);
            return new ScenarioTopicOfferSelection("selected", offer);
        }

        public bool CommitSelection(string offerId, ScenarioRuntimeSnapshot context)
        {
            if (selected.Contains(offerId ?? "") || context == null || context.turnId != lastPresentedTurnId) return false;
            ScenarioTopicOffer offer = offers.FirstOrDefault(item => item.offerId == offerId);
            if (offer == null || !IsEligible(offer, context)) return false;
            selected.Add(offer.offerId);
            return true;
        }

        public void Clear()
        { offers.Clear(); selected.Clear(); createdDuringTurnId = lastPresentedTurnId = ""; }

        private static bool IsEligible(ScenarioTopicOffer offer, ScenarioRuntimeSnapshot context)
        {
            return !string.IsNullOrEmpty(offer.topicId)
                ? context.knownTopicIds.Contains(offer.topicId)
                : context.knownIntentIds.Contains(offer.intentId);
        }

        private static bool Valid(ScenarioTopicOffer offer)
        {
            if (offer == null || string.IsNullOrWhiteSpace(offer.offerId) || offer.offerId.Length > 64 ||
                string.IsNullOrWhiteSpace(offer.label) || offer.label.Length > 80 ||
                (string.IsNullOrEmpty(offer.topicId) == string.IsNullOrEmpty(offer.intentId))) return false;
            foreach (char character in offer.label)
                if (char.IsSurrogate(character) || (char.IsLetter(character) && character > '\u024F')) return false;
            return true;
        }
    }
}
