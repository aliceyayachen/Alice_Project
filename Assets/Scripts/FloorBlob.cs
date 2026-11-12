using UnityEngine;

public class FloorBlob : MonoBehaviour
{
    [SerializeField] private BoxCollider floor;
    [SerializeField, Min(0f)] private float driftSpeed = 0.3f;
    [SerializeField, Min(0f)] private float pushSpeed = 2f;
    [SerializeField, Min(0.01f)] private float pushDistance = 0.9f;
    [SerializeField, Min(0f)] private float edgePadding = 0.4f;
    [SerializeField] private Transform calibrationCenter;
    [SerializeField] private float sidewaysTrackingMultiplier = 1f;

    private Vector3 direction = new Vector3(1f, 0f, 0.35f).normalized;
    private float speed;

    private void Start()
    {
        speed = driftSpeed;
    }

    private void Update()
    {
        if (floor == null) return;

        Vector3 position = transform.position;
        Vector3 away = Vector3.zero;
        float nearestDistance = pushDistance * pushDistance;
        bool playerClose = false;

        foreach (GameObject player in GameObject.FindGameObjectsWithTag("Player"))
        {
            
            Vector3 playerPosition = player.transform.position;

            if (calibrationCenter != null)
            {
                float centerZ = calibrationCenter.position.z;
                playerPosition.z = centerZ
                    + (playerPosition.z - centerZ) * sidewaysTrackingMultiplier;
            }

Vector3 offset = position - playerPosition;
            offset.y = 0f; // Compare positions on the floor, ignoring height.

            if (offset.sqrMagnitude >= nearestDistance) continue;

            nearestDistance = offset.sqrMagnitude;
            away = offset;
            playerClose = true;
        }

        if (playerClose)
        {
            if (away.sqrMagnitude > 0.0001f)
                direction = away.normalized;

            speed = pushSpeed;
        }
        else
        {
            speed = Mathf.MoveTowards(speed, driftSpeed, 2f * Time.deltaTime);
        }

        position += direction * speed * Time.deltaTime;

        Bounds bounds = floor.bounds;
        float minX = bounds.min.x + edgePadding;
        float maxX = bounds.max.x - edgePadding;
        float minZ = bounds.min.z + edgePadding;
        float maxZ = bounds.max.z - edgePadding;

        if (position.x < minX) direction.x = Mathf.Abs(direction.x);
        if (position.x > maxX) direction.x = -Mathf.Abs(direction.x);
        if (position.z < minZ) direction.z = Mathf.Abs(direction.z);
        if (position.z > maxZ) direction.z = -Mathf.Abs(direction.z);

        position.x = Mathf.Clamp(position.x, minX, maxX);
        position.z = Mathf.Clamp(position.z, minZ, maxZ);
        transform.position = position;
    }
}