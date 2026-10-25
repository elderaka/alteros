using System;
using System.Collections.Generic;
using AlterOS.Dialogue;

internal static class DialogueCoreCheck
{
    private static int Main()
    {
        DialogueDefinition dialogue = new DialogueDefinition
        {
            id = "check.greeting",
            routing = new DialogueRouting
            {
                routeId = "greeting",
                examples = new List<string> { "hello" }
            },
            nodes = new List<DialogueNode>
            {
                new DialogueNode
                {
                    id = "start",
                    text = "Hello.",
                    choices = new List<DialogueChoice>
                    {
                        new DialogueChoice { label = "Stay", nextId = "stay" },
                        new DialogueChoice { label = "Leave" }
                    }
                },
                new DialogueNode
                {
                    id = "stay",
                    text = "Then stay a while."
                }
            }
        };

        Assert(DialogueValidator.Validate(dialogue).Count == 0, "Valid dialogue was rejected.");

        DialoguePlayback playback = new DialoguePlayback(dialogue);
        Assert(playback.Current.id == "start", "Playback did not start at the first node.");
        Assert(playback.Choose(0).id == "stay", "Choice did not reach its target.");
        Assert(playback.Continue() == null && playback.IsComplete, "Playback did not complete.");

        dialogue.nodes[0].choices[0].nextId = "missing";
        Assert(DialogueValidator.Validate(dialogue).Count == 1, "Missing target was not rejected.");

        Console.WriteLine("Dialogue core check passed.");
        return 0;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
