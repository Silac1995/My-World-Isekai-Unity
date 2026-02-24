using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace MWI.World.Zones
{
    [RequireComponent(typeof(BoxCollider))]
    public class Zone : MonoBehaviour
    {
        public ZoneType zoneType;
        public string zoneName;

        private BoxCollider _boxCollider;
        private List<GameObject> _charactersInside = new List<GameObject>();

        private void Awake()
        {
            _boxCollider = GetComponent<BoxCollider>();
            _boxCollider.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Character"))
            {
                if (!_charactersInside.Contains(other.gameObject))
                {
                    _charactersInside.Add(other.gameObject);
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Character"))
            {
                _charactersInside.Remove(other.gameObject);
            }
        }

        public Vector3 GetRandomPointInZone()
        {
            Vector3 center = _boxCollider.bounds.center;
            Vector3 size = _boxCollider.bounds.size;

            // Generate a random position within the box collider bounds
            float randomX = Random.Range(center.x - size.x / 2, center.x + size.x / 2);
            float randomY = center.y; // Keep the same Y level initially
            float randomZ = Random.Range(center.z - size.z / 2, center.z + size.z / 2);

            Vector3 randomPoint = new Vector3(randomX, randomY, randomZ);

            // Ensure the point is on the NavMesh
            NavMeshHit hit;
            if (NavMesh.SamplePosition(randomPoint, out hit, 2.0f, NavMesh.AllAreas))
            {
                return hit.position;
            }

            // Fallback to center if sampling fails
            return center;
        }
    }
}
