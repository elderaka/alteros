using UnityEngine;

namespace Pleiades.Furniture
{
    /// <summary>Runtime identity for furniture visuals created from a saved home layout.</summary>
    [DisallowMultipleComponent]
    public sealed class FurniturePlacedInstanceIdentity : MonoBehaviour
    {
        [SerializeField] private string instanceId = string.Empty;
        [SerializeField] private string definitionId = string.Empty;

        public string InstanceId { get { return instanceId; } }
        public string DefinitionId { get { return definitionId; } }

        public void Configure(string newInstanceId, string newDefinitionId)
        {
            instanceId = newInstanceId ?? string.Empty;
            definitionId = newDefinitionId ?? string.Empty;
        }
    }
}
