using System.Collections.Generic;
using AlterOS.Dialogue;
using AlterOS.UI;
using Pleiades.Player;
using UnityEngine;

namespace AlterOS
{
    public class AlteraProximityAmbientTrigger : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private DialogueRouter dialogueRouter;
        [SerializeField] private Transform playerTarget;
        [SerializeField] private PlayerController playerController;

        [Header("Pool")]
        [SerializeField] private string poolJsonPath = "greet/greeting_pool.json";

        [Header("Distance")]
        [SerializeField] private float triggerDistance = 3.25f;
        [SerializeField] private float resetDistance = 4.5f;

        [Header("Cooldown")]
        [SerializeField] private float cooldownSeconds = 20f;
        [SerializeField] private bool triggerOnStartInRange;

        private AmbientLinePool _loadedPool;
        private bool _hasTriggeredThisApproach;
        private float _lastTriggerTime = -999f;

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

            if (playerController == null)
            {
                playerController = FindFirstObjectByType<PlayerController>();
            }

            if (playerTarget == null && playerController != null)
            {
                playerTarget = playerController.transform;
            }

        }

        private void Start()
        {
            LoadPool();
            _hasTriggeredThisApproach = !triggerOnStartInRange;
        }

        private void Update()
        {
            if (dialogueRouter == null || playerTarget == null)
            {
                return;
            }

            float distance = Vector3.Distance(transform.position, playerTarget.position);
            if (distance >= resetDistance)
            {
                _hasTriggeredThisApproach = false;
            }

            if (_hasTriggeredThisApproach || distance > triggerDistance)
            {
                return;
            }

            if (Time.time < _lastTriggerTime + cooldownSeconds)
            {
                return;
            }

            if (dialogueRouter.dialogueManager != null && dialogueRouter.dialogueManager.BlocksPlayerControl)
            {
                return;
            }

            if (dialogueRouter.ambientBubble != null && dialogueRouter.ambientBubble.IsSpatting)
            {
                return;
            }

            LoadPool();
            AmbientLine line = PickEligibleLine(distance);
            if (line == null)
            {
                return;
            }

            dialogueRouter.PlayAmbientLine(line.text, line.translatedText, line.language);
            _lastTriggerTime = Time.time;
            _hasTriggeredThisApproach = true;
        }

        private void LoadPool()
        {
            if (dialogueRouter == null || string.IsNullOrEmpty(poolJsonPath))
            {
                return;
            }

            if (!dialogueRouter.TryLoadJson(poolJsonPath, out string json)) return;
            _loadedPool = AmbientLinePool.FromJson(json);
        }

        private AmbientLine PickEligibleLine(float playerDistance)
        {
            if (_loadedPool == null || _loadedPool.lines == null || _loadedPool.lines.Count == 0)
            {
                return null;
            }

            TriggerContext context = TriggerContext.CreateDefault();
            context.playerDistance = playerDistance;

            if (!TriggerConditionEvaluator.AreMet(_loadedPool.metadata.triggerConditions, context))
            {
                return null;
            }

            List<AmbientLine> eligible = new List<AmbientLine>();
            foreach (AmbientLine line in _loadedPool.lines)
            {
                if (TriggerConditionEvaluator.AreMet(line.triggerConditions, context))
                {
                    eligible.Add(line);
                }
            }

            if (eligible.Count == 0)
            {
                return null;
            }

            return eligible[Random.Range(0, eligible.Count)];
        }
    }
}
