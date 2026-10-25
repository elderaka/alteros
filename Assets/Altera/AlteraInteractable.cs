using AlterOS;
using AlterOS.Conversation;
using AlterOS.Dialogue;
using Pleiades.Player;
using UnityEngine;

public class AlteraInteractable : MonoBehaviour, IPlayerInteractable
{
    [Header("Dialogue")]
    [SerializeField] private DialogueRouter dialogueRouter;
    [SerializeField] private string greetingDialoguePath = "greetings.json";
    [SerializeField] private float interactionCooldown = 0.5f;
    [SerializeField] private float postDialogueLockDuration = 0.15f;

    [Header("Physics")]
    [SerializeField] private bool makeNonPushable = true;
    [SerializeField] private Rigidbody targetRigidbody;
    [SerializeField] private ObjectTracker objectTracker;

    [Header("Prompt")]
    [SerializeField] private Transform promptAnchor;
    [SerializeField] private Vector3 promptOffset = new Vector3(0f, 1.8f, 0f);
    [SerializeField] private string promptText = "E";

    private float _lastInteractTime = -999f;
    private bool _interactionLocked;

    public string PromptText => promptText;

    private void Awake()
    {
        if (dialogueRouter == null)
        {
            dialogueRouter = GetComponent<DialogueRouter>();
        }

        if (dialogueRouter == null)
        {
            dialogueRouter = GetComponentInParent<DialogueRouter>();
        }

        if (targetRigidbody == null)
        {
            targetRigidbody = GetComponent<Rigidbody>();
        }

        if (targetRigidbody == null)
        {
            targetRigidbody = GetComponentInParent<Rigidbody>();
        }

        if (objectTracker == null)
        {
            objectTracker = GetComponent<ObjectTracker>();
        }

        if (objectTracker == null)
        {
            objectTracker = GetComponentInParent<ObjectTracker>();
        }

        if (makeNonPushable && targetRigidbody != null)
        {
            targetRigidbody.isKinematic = true;
            targetRigidbody.useGravity = false;
            targetRigidbody.constraints = RigidbodyConstraints.FreezeAll;
        }

        PreloadInteractionAssets();
    }

    private void Update()
    {
        if (!_interactionLocked || dialogueRouter == null || dialogueRouter.dialogueManager == null)
        {
            return;
        }

        if (dialogueRouter.dialogueManager.BlocksPlayerControl)
        {
            return;
        }

        if (Time.time >= _lastInteractTime + interactionCooldown + postDialogueLockDuration)
        {
            _interactionLocked = false;
        }
    }

    public Vector3 GetPromptWorldPosition()
    {
        if (promptAnchor != null)
        {
            return promptAnchor.position + promptOffset;
        }

        return transform.position + promptOffset;
    }

    public bool CanInteract(PlayerController player)
    {
        if (dialogueRouter == null)
        {
            return false;
        }

        if (Time.time < _lastInteractTime + interactionCooldown)
        {
            return false;
        }

        if (_interactionLocked)
        {
            return false;
        }

        if (dialogueRouter.dialogueManager != null && dialogueRouter.dialogueManager.BlocksPlayerControl)
        {
            return false;
        }

        return player != null && !player.IsInDialogueMode;
    }

    public void Interact(PlayerController player)
    {
        if (!CanInteract(player))
        {
            return;
        }

        _lastInteractTime = Time.time;
        _interactionLocked = true;
        if (objectTracker != null)
        {
            objectTracker.ForceFacePlayer();
        }

        DirectConversationBridge directBridge = dialogueRouter.dialogueManager != null
            ? dialogueRouter.dialogueManager.directConversationBridge : null;
        if (directBridge != null && directBridge.TryResumeScenarioInteraction())
        {
            return;
        }

        if (dialogueRouter.TryLoadSequence(greetingDialoguePath, out DialogueSequence greeting))
        {
            directBridge?.AppendScenarioTopicOffers(greeting);
            dialogueRouter.PlayCinematic(greeting);
            return;
        }

        dialogueRouter.Play(greetingDialoguePath);
    }

    private void PreloadInteractionAssets()
    {
        if (dialogueRouter != null)
        {
            dialogueRouter.Preload(greetingDialoguePath);
        }

        if (dialogueRouter != null && dialogueRouter.ambientBubble != null)
        {
            dialogueRouter.ambientBubble.PreloadFillerPool(dialogueRouter.ambientBubble.dialogueFillerPoolPath);
            dialogueRouter.ambientBubble.PreloadFillerPool(dialogueRouter.ambientBubble.ttsFillerPoolPath);
        }
    }
}
