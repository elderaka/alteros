using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AlterOS.Content;
using AlterOS.Dialogue;

namespace AlterOS.ContentWorkbench
{
    internal static class WorkbenchDialoguePreview
    {
        private const int MaximumEvents = 256;

        public static WorkbenchResult Run(WorkbenchPreviewSnapshot snapshot, IDictionary<string, object> payload, string fixtureId)
        {
            DialogueRecord[] matches = (snapshot.Package.dialogues ?? new List<DialogueRecord>())
                .Where(item => item != null && item.id == snapshot.RootSourceId).ToArray();
            if (matches.Length != 1)
                return WorkbenchResult.Failure("invalid", "preview_dialogue_projection_missing", "/document/id",
                    "The detached package does not contain exactly one imported Dialogue definition for this source.");

            string displayLanguage = JsonWire.String(payload, "displayLanguage", "en");
            string speechLanguage = JsonWire.String(payload, "speechLanguage", "ja");
            var dialogue = new DialogueDefinition
            {
                id = matches[0].id,
                source = DialogueDefinition.GenericSource,
                routing = new DialogueRouting { routeId = matches[0].id, examples = new List<string> { "synthetic dialogue preview" } },
                nodes = new List<CanonicalDialogueNode>()
            };
            BeatRecord[] beats = (matches[0].beats ?? new List<BeatRecord>()).OrderBy(item => item == null ? int.MaxValue : item.sequenceOrder).ToArray();
            for (int index = 0; index < beats.Length; index++)
            {
                BeatRecord beat = beats[index];
                TextRecord text = beat == null ? null : (snapshot.Package.texts ?? new List<TextRecord>()).FirstOrDefault(item => item != null &&
                    beat.textRef != null && item.id == beat.textRef.id && item.revision == beat.textRef.revision &&
                    item.semanticHash == beat.textRef.expectedHash);
                if (beat == null || text == null || !ReleaseContains(snapshot, "text", text.id, text.revision, text.semanticHash))
                    return WorkbenchResult.Failure("invalid", "preview_dialogue_text_unavailable", "/document/beats/" + index + "/text",
                        "A Dialogue Beat does not resolve to its exact Text revision inside the ephemeral package release.");
                ContentResult<ContentTextResolution> resolved = ContentLanguageResolver.Resolve(text, displayLanguage, speechLanguage);
                if (!resolved.IsReady)
                {
                    ContentDiagnostic diagnostic = resolved.diagnostics.FirstOrDefault();
                    return WorkbenchResult.Failure("invalid", diagnostic == null ? "preview_dialogue_text_unavailable" : diagnostic.code,
                        diagnostic == null ? "/document/beats/" + index + "/text" : diagnostic.path,
                        diagnostic == null ? "The localized Dialogue Beat could not be resolved." : diagnostic.message);
                }
                IDictionary<string, object> presentation = JsonWire.AsObject(JsonWire.Deserialize(beat.presentationJson ?? "{}"));
                dialogue.nodes.Add(new CanonicalDialogueNode
                {
                    id = beat.id,
                    speaker = JsonWire.String(presentation, "speaker", "alterra"),
                    text = resolved.value.speechText,
                    displayText = resolved.value.displayText,
                    language = resolved.value.speechLanguage,
                    voice = JsonWire.String(presentation, "voice"),
                    audioKey = JsonWire.String(presentation, "audioKey"),
                    nextId = index + 1 < beats.Length ? beats[index + 1].id : "",
                    presentation = new DialoguePresentation
                    {
                        expression = beat.expression ?? "",
                        animation = JsonWire.String(presentation, "animation")
                    },
                });
            }

            DialoguePlayback playback;
            try { playback = new DialoguePlayback(dialogue); }
            catch (Exception exception)
            { return WorkbenchResult.Failure("invalid", "preview_dialogue_runtime_invalid", "/document", "DialoguePlayback rejected the projected content: " + exception.Message); }

            if (!payload.TryGetValue("events", out object rawEvents)) rawEvents = new object[0];
            IEnumerable events = JsonWire.AsArray(rawEvents);
            if (events == null || rawEvents is string)
                return WorkbenchResult.Failure("invalid", "preview_events_invalid", "/events", "events must be a JSON array.");
            var reports = new List<object>();
            int count = 0;
            foreach (object rawEvent in events)
            {
                if (++count > MaximumEvents)
                    return WorkbenchResult.Failure("invalid", "preview_events_too_many", "/events", "Preview supports at most 256 events.");
                IDictionary<string, object> input = JsonWire.AsObject(rawEvent);
                if (input == null)
                    return WorkbenchResult.Failure("invalid", "preview_event_invalid", "/events", "Each Dialogue event must be an object.");
                string action = JsonWire.String(input, "action");
                try
                {
                    CanonicalDialogueNode node;
                    if (action == "continue") node = playback.Continue();
                    else if (action == "choose" && JsonWire.TryInt(input, "index", out int index)) node = playback.Choose(index);
                    else return WorkbenchResult.Failure("invalid", "preview_event_invalid", "/events", "Dialogue events support continue or choose with an integer index.");
                    reports.Add(new Dictionary<string, object> { { "action", action }, { "currentBeat", NodeView(node) } });
                }
                catch (Exception exception)
                { return WorkbenchResult.Failure("invalid", "preview_event_rejected", "/events", exception.Message); }
            }

            return WorkbenchResult.Ok(new Dictionary<string, object>
            {
                { "kindId", "dialogue" }, { "fixtureId", fixtureId }, { "fixtureOnly", true },
                { "rootIsSynthetic", snapshot.RootIsSynthetic }, { "sourceSnapshot", snapshot.SourceSnapshot },
                { "ephemeralReleaseId", snapshot.Release.releaseId },
                { "playbackMode", "direct authored Dialogue Playback; no route or semantic matching" },
                { "dialogueId", dialogue.id }, { "currentBeat", NodeView(playback.Current) },
                { "isComplete", playback.IsComplete }, { "events", reports },
                { "providerCalls", 0 }, { "ttsCalls", 0 },
            });
        }

        private static bool ReleaseContains(WorkbenchPreviewSnapshot snapshot, string kind, string id, string revision, string hash)
        {
            return snapshot.Release.dependencies.Any(item => item != null && item.kind == kind && item.store == snapshot.Package.storeId &&
                item.id == id && item.revision == revision && item.expectedHash == hash);
        }

        private static object NodeView(CanonicalDialogueNode node)
        {
            if (node == null) return null;
            return new Dictionary<string, object>
            {
                { "id", node.id }, { "speaker", node.speaker }, { "text", node.text },
                { "displayText", node.displayText }, { "language", node.language },
                { "expression", node.presentation == null ? "" : node.presentation.expression },
                { "nextId", node.nextId },
            };
        }
    }
}
