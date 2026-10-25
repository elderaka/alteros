using System;
using System.Collections.Generic;
using AlterOS.Dialogue;
using AlterOS.Semantics;
using AlterOS.Topics;

namespace AlterOS.Conversation
{
    public static class UnifiedConversationPlanner
    {
        public static ConversationPlan PlanV2(string requestId, ConversationJobV2 job, ConversationJudgmentV2 judgment,
            ConversationCoveragePolicy coveragePolicy, IReadOnlyDictionary<string, TopicDefinition> catalogue,
            IReadOnlyDictionary<string, TopicInstance> authoredInstances, TopicContext context, TopicHistory history,
            DateTimeOffset nowUtc, double draw, ISet<string> allowedExpressions)
        {
            var plan = new ConversationPlan { requestId = requestId ?? "" };
            if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 128 || job == null || judgment == null ||
                catalogue == null || authoredInstances == null || context == null || history == null ||
                !TopicDefinitionValidator.Finite(draw) || draw < 0 || draw >= 1)
                return Unavailable(plan, "InvalidHostContext");
            if (ConversationProtocol.ValidateJudgment(job, judgment, allowedExpressions).Count != 0)
                return Unavailable(plan, "InvalidJudgment");

            var request = new ConversationRequest
            {
                schemaVersion = 1, requestId = requestId, sessionId = job.request.sessionId,
                channel = job.request.channel, text = job.request.text,
                activeResponseId = job.request.activeResponseId, activeTopicId = job.request.activeTopicId
            };
            var candidates = new TopicCandidateSet
            {
                indexIdentity = job.candidates.indexIdentity,
                source = job.candidates.source,
                cards = new List<TopicCandidateCard>()
            };
            foreach (ConversationCandidateCardV2 card in job.candidates.cards)
                candidates.cards.Add(new TopicCandidateCard
                {
                    topicId = card.topicId, prompt = card.prompt, examples = new List<string>(card.examples),
                    similarity = (float)card.similarity, supportCount = card.supportCount,
                    authoredAvailability = card.authoredAvailability
                });

            if (judgment.kind == "existing_uncovered")
            {
                if (!ConversationCoverage.AllowsUncoveredDiscussion(coveragePolicy, judgment.topicId) ||
                    ConversationCoverage.ValidatePolicy(coveragePolicy, catalogue).Count != 0)
                    return Unavailable(plan, "UncoveredDiscussionNotAllowed");
                foreach (string topicId in judgment.referencedTopicIds)
                {
                    if (!ReferenceResolves(topicId, catalogue, authoredInstances))
                        return Unavailable(plan, "UnknownReferencedTopic");
                    if (topicId != judgment.topicId &&
                        !ReferenceIsAvailable(topicId, candidates, request, catalogue, authoredInstances, context, history, nowUtc, draw))
                        return Unavailable(plan, "ReferencedTopicUnavailable");
                }
                plan.outcome = "generated";
                plan.topicId = judgment.topicId;
                plan.reply = ConversationProtocol.ToCanonicalReply(judgment.reply);
                plan.suggestedReplies = ToLegacySuggestions(judgment.suggestedReplies);
                return plan;
            }

            var legacyJudgment = new ConversationJudgment
            {
                schemaVersion = 1, requestId = requestId, kind = judgment.kind, topicId = judgment.topicId,
                referencedTopicIds = new List<string>(judgment.referencedTopicIds),
                reply = judgment.reply == null ? null : ConversationProtocol.ToCanonicalReply(judgment.reply),
                suggestedReplies = ToLegacySuggestions(judgment.suggestedReplies)
            };
            return Plan(request, candidates, legacyJudgment, catalogue, authoredInstances, context, history,
                nowUtc, draw, allowedExpressions);
        }

        public static ConversationPlan Plan(ConversationRequest request, TopicCandidateSet candidates, ConversationJudgment judgment,
            IReadOnlyDictionary<string, TopicDefinition> catalogue, IReadOnlyDictionary<string, TopicInstance> authoredInstances,
            TopicContext context, TopicHistory history, DateTimeOffset nowUtc, double draw, ISet<string> allowedExpressions)
        {
            var plan = new ConversationPlan { requestId = request == null ? "" : request.requestId };
            if (ConversationContractValidator.ValidateRequest(request).Count != 0)
                return Unavailable(plan, "InvalidRequest");
            if (catalogue == null || authoredInstances == null || context == null || history == null ||
                !TopicDefinitionValidator.Finite(draw) || draw < 0 || draw >= 1)
                return Unavailable(plan, "InvalidHostContext");
            if (ConversationContractValidator.ValidateCandidateSet(candidates, true).Count != 0)
                return Unavailable(plan, "InvalidCandidates");

            return judgment == null
                ? PlanLocal(request, candidates, catalogue, authoredInstances, context, history, nowUtc, draw, plan)
                : PlanJudged(request, candidates, judgment, catalogue, authoredInstances, context, history, nowUtc, draw, allowedExpressions, plan);
        }

        private static ConversationPlan PlanLocal(ConversationRequest request, TopicCandidateSet candidates,
            IReadOnlyDictionary<string, TopicDefinition> catalogue, IReadOnlyDictionary<string, TopicInstance> instances,
            TopicContext context, TopicHistory history, DateTimeOffset nowUtc, double draw, ConversationPlan plan)
        {
            if (!string.IsNullOrEmpty(request.explicitTopicId))
                return PlanAuthored(request.explicitTopicId, request.channel, candidates, catalogue, instances,
                    context, history, nowUtc, draw, plan);
            if (!string.IsNullOrEmpty(request.activeTopicId))
            {
                TopicCandidateCard active = FindCandidate(candidates, request.activeTopicId);
                if (active != null && active.authoredAvailability == "ready")
                    return PlanAuthored(request.activeTopicId, request.channel, candidates, catalogue, instances,
                        context, history, nowUtc, draw, plan);
            }
            var matches = new List<DialogueRouteMatch>();
            foreach (TopicCandidateCard card in candidates.cards)
                if (card.authoredAvailability == "ready")
                    matches.Add(new DialogueRouteMatch(card.topicId, card.similarity, card.similarity, card.supportCount,
                        card.examples.Count == 0 ? "" : card.examples[0]));
            TopicRouteDecision decision = TopicRoutePolicy.Decide(matches.ToArray(), TopicFamilySemanticMatcher.DefaultMinimumSimilarity, "");
            if (decision.UsedFallback) return Unavailable(plan, decision.Reason.ToString());
            return PlanAuthored(decision.SelectedRouteId, request.channel, candidates, catalogue, instances, context, history, nowUtc, draw, plan);
        }

        private static ConversationPlan PlanJudged(ConversationRequest request, TopicCandidateSet candidates, ConversationJudgment judgment,
            IReadOnlyDictionary<string, TopicDefinition> catalogue, IReadOnlyDictionary<string, TopicInstance> instances,
            TopicContext context, TopicHistory history, DateTimeOffset nowUtc, double draw, ISet<string> allowedExpressions, ConversationPlan plan)
        {
            if (ConversationContractValidator.ValidateJudgment(request, candidates, judgment, allowedExpressions).Count != 0)
                return Unavailable(plan, "InvalidJudgment");

            bool generated = judgment.kind == "continue" || judgment.kind == "clarify" || judgment.kind == "novel_candidate";
            if (generated)
            {
                foreach (string topicId in judgment.referencedTopicIds)
                {
                    if (!ReferenceResolves(topicId, catalogue, instances))
                        return Unavailable(plan, "UnknownReferencedTopic");
                    bool activeContinuation = judgment.kind == "continue" && topicId == request.activeTopicId;
                    if (!activeContinuation && !ReferenceIsAvailable(topicId, candidates, request, catalogue, instances, context, history, nowUtc, draw))
                        return Unavailable(plan, "ReferencedTopicUnavailable");
                }
                if (judgment.kind == "novel_candidate" && !string.IsNullOrEmpty(request.activeTopicId) &&
                    !ReferenceIsAvailable(request.activeTopicId, candidates, request, catalogue, instances, context, history, nowUtc, draw))
                    return Unavailable(plan, "ReferencedTopicUnavailable");
            }

            switch (judgment.kind)
            {
                case "existing":
                    if (FindCandidate(candidates, judgment.topicId).authoredAvailability != "ready")
                        return Unavailable(plan, "AuthoredContentUnavailable");
                    return PlanAuthored(judgment.topicId, request.channel, candidates, catalogue, instances, context, history, nowUtc, draw, plan);
                case "existing_uncovered":
                    return Unavailable(plan, "KnownTopicCoverageDeferred");
                case "clarify":
                    plan.outcome = "clarify";
                    plan.reply = CopyReply(judgment.reply);
                    plan.suggestedReplies = CopySuggestions(judgment.suggestedReplies);
                    return plan;
                case "continue":
                    if (!string.IsNullOrEmpty(request.activeTopicId) && !catalogue.ContainsKey(request.activeTopicId))
                        return Unavailable(plan, "UnknownActiveTopic");
                    plan.outcome = "generated";
                    plan.topicId = request.activeTopicId;
                    plan.reply = CopyReply(judgment.reply);
                    plan.suggestedReplies = CopySuggestions(judgment.suggestedReplies);
                    return plan;
                case "novel_candidate":
                    plan.outcome = "generated";
                    plan.topicId = "";
                    plan.reply = CopyReply(judgment.reply);
                    plan.suggestedReplies = CopySuggestions(judgment.suggestedReplies);
                    plan.isNovelCandidate = true;
                    return plan;
                default:
                    return Unavailable(plan, "InvalidJudgment");
            }
        }

        private static ConversationPlan PlanAuthored(string topicId, string channel, TopicCandidateSet candidates,
            IReadOnlyDictionary<string, TopicDefinition> catalogue, IReadOnlyDictionary<string, TopicInstance> instances,
            TopicContext context, TopicHistory history, DateTimeOffset nowUtc, double draw, ConversationPlan plan)
        {
            TopicCandidateCard card = FindCandidate(candidates, topicId);
            TopicDefinition definition;
            TopicInstance instance;
            if (card == null || card.authoredAvailability != "ready" || !catalogue.TryGetValue(topicId, out definition) || definition == null ||
                !instances.TryGetValue(topicId, out instance) || instance == null)
                return Unavailable(plan, "AuthoredContentUnavailable");
            TopicSelectionResult selection = TopicScenarioSelector.SelectForConversation(definition, instance, context, history, nowUtc, draw, channel);
            if (selection.SelectedScenario == null) return Unavailable(plan, "AuthoredContentUnavailable");
            plan.outcome = "authored";
            plan.topicId = topicId;
            plan.scenarioId = selection.SelectedScenario.scenarioId;
            return plan;
        }

        private static bool ReferenceIsAvailable(string topicId, TopicCandidateSet candidates, ConversationRequest request,
            IReadOnlyDictionary<string, TopicDefinition> catalogue, IReadOnlyDictionary<string, TopicInstance> instances,
            TopicContext context, TopicHistory history, DateTimeOffset nowUtc, double draw)
        {
            TopicCandidateCard card = FindCandidate(candidates, topicId);
            if (card != null && card.authoredAvailability != "ready") return false;
            if (card == null && topicId != request.activeTopicId) return false;
            TopicDefinition definition;
            TopicInstance instance;
            if (!catalogue.TryGetValue(topicId, out definition) || definition == null || !instances.TryGetValue(topicId, out instance) || instance == null)
                return false;
            return TopicScenarioSelector.SelectForConversation(definition, instance, context, history, nowUtc, draw, request.channel).SelectedScenario != null;
        }

        private static bool ReferenceResolves(string topicId, IReadOnlyDictionary<string, TopicDefinition> catalogue,
            IReadOnlyDictionary<string, TopicInstance> instances)
        {
            TopicDefinition definition;
            TopicInstance instance;
            return !string.IsNullOrEmpty(topicId) && catalogue.TryGetValue(topicId, out definition) && definition != null &&
                instances.TryGetValue(topicId, out instance) && instance != null;
        }

        private static TopicCandidateCard FindCandidate(TopicCandidateSet candidates, string topicId)
        { return candidates.cards.Find(card => card.topicId == topicId); }

        private static ConversationReply CopyReply(ConversationReply source)
        {
            if (source == null) return null;
            var reply = new ConversationReply();
            foreach (CanonicalDialogueNode node in source.nodes)
            {
                var copy = new CanonicalDialogueNode
                {
                    id = node.id, speaker = node.speaker, text = node.text, displayText = node.displayText,
                    language = node.language, voice = node.voice, audioKey = node.audioKey, nextId = node.nextId,
                    presentation = node.presentation == null ? null : new DialoguePresentation
                    { expression = node.presentation.expression, animation = node.presentation.animation },
                    choices = new List<CanonicalDialogueChoice>(),
                };
                if (node.choices != null)
                    foreach (CanonicalDialogueChoice choice in node.choices)
                        copy.choices.Add(new CanonicalDialogueChoice { label = choice.label, nextId = choice.nextId });
                reply.nodes.Add(copy);
            }
            return reply;
        }

        private static List<SuggestedPlayerReply> CopySuggestions(List<SuggestedPlayerReply> source)
        {
            var copy = new List<SuggestedPlayerReply>();
            if (source == null) return copy;
            foreach (SuggestedPlayerReply suggestion in source)
            {
                if (suggestion != null && IsEnglishSuggestion(suggestion.label, suggestion.prompt))
                    copy.Add(new SuggestedPlayerReply { label = suggestion.label, prompt = suggestion.prompt });
            }
            return copy;
        }

        private static List<SuggestedPlayerReply> ToLegacySuggestions(IReadOnlyList<ConversationSuggestedReplyV2> source)
        {
            var copy = new List<SuggestedPlayerReply>();
            if (source != null)
                foreach (ConversationSuggestedReplyV2 suggestion in source)
                    if (suggestion != null && IsEnglishSuggestion(suggestion.label, suggestion.prompt))
                        copy.Add(new SuggestedPlayerReply { label = suggestion.label, prompt = suggestion.prompt });
            return copy;
        }

        private static bool IsEnglishSuggestion(string label, string prompt)
        {
            return IsEnglishDisplayText(label) && IsEnglishDisplayText(prompt);
        }

        private static bool IsEnglishDisplayText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            // shortcut: English is the only display locale; add locale-tagged suggestions before UI localization.
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (char.IsSurrogate(character) || (char.IsLetter(character) && character > '\u024F')) return false;
            }
            return true;
        }

        private static ConversationPlan Unavailable(ConversationPlan plan, string diagnostic)
        { plan.outcome = "unavailable"; plan.diagnosticCode = diagnostic; return plan; }
    }
}
