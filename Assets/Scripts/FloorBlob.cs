using UnityEngine;

public class FloorBlob : MonoBehaviour
{
    [SerializeField] private BoxCollider floor;
    [SerializeField, Min(0f)] private float driftSpeed = 0.3f;
    [SerializeField, Min(0f)] private float pushSpeed = 2f;
    [SerializeField, Min(0f)] private float speedBoost = 1.5f;
    [SerializeField, Min(0f)] private float maxPushSpeed = 5f;
    [SerializeField, Min(0.01f)] private float pushDistance = 0.9f;
    [SerializeField, Min(0f)] private float edgePadding = 0.4f;

    [Header("Lab Tracking (leave empty on laptop)")]
    [SerializeField] private Transform calibratedDot;
    [SerializeField] private Renderer calibratedDotRenderer;

    private Vector3 direction = new Vector3(1f, 0f, 0.35f).normalized;
    private float speed;
    private Transform lastVisitor;
    private Vector3 lastVisitorPosition;
    private Vector3 visitorVelocity;

    private void Start()
    {
        speed = driftSpeed;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (floor == null || dt <= 0f) return;

        Vector3 position = transform.position;
        Vector3 away = Vector3.zero;
        Transform closestVisitor = null;
        Vector3 closestVisitorPosition = Vector3.zero;
        float nearestDistanceSquared = float.PositiveInfinity;

        // Sample the visitor before they enter push range so we know
        // how fast they were already moving when they reached the blob.
        if (calibratedDot != null)
        {
            if (calibratedDot.gameObject.activeInHierarchy &&
                calibratedDotRenderer != null &&
                calibratedDotRenderer.enabled)
            {
                CheckPosition(calibratedDot);
            }
        }
        else
        {
            foreach (GameObject player in GameObject.FindGameObjectsWithTag("Player"))
            {
                CheckPosition(player.transform);
            }
        }

        if (closestVisitor != null && closestVisitor == lastVisitor)
        {
            Vector3 measuredVelocity =
                (closestVisitorPosition - lastVisitorPosition) / dt;
            measuredVelocity.y = 0f;

            // Smooth the speed estimate without smoothing the visitor's position.
            visitorVelocity = Vector3.Lerp(
                visitorVelocity, measuredVelocity, 1f - Mathf.Exp(-15f * dt));
        }
        else
        {
            visitorVelocity = Vector3.zero;
        }

        lastVisitor = closestVisitor;
        lastVisitorPosition = closestVisitorPosition;

        speed = Mathf.MoveTowards(speed, driftSpeed, 2f * dt);

        if (nearestDistanceSquared < pushDistance * pushDistance)
        {
            float approachSpeed = 0f;

            if (away.sqrMagnitude > 0.0001f)
            {
                direction = away.normalized;
                approachSpeed = Mathf.Max(0f, Vector3.Dot(visitorVelocity, direction));
            }

            float hitSpeed = Mathf.Min(
                maxPushSpeed, pushSpeed + approachSpeed * speedBoost);

            // Keep the momentum from a strong nudge as the blob slows down.
            speed = Mathf.Max(speed, hitSpeed);
        }

        position += direction * speed * dt;

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

        void CheckPosition(Transform visitor)
        {
            Vector3 visitorPosition = visitor.position;
            Vector3 offset = position - visitorPosition;
            offset.y = 0f;

            float distanceSquared = offset.sqrMagnitude;
            if (distanceSquared >= nearestDistanceSquared) return;

            nearestDistanceSquared = distanceSquared;
            closestVisitor = visitor;
            closestVisitorPosition = visitorPosition;
            away = offset;
        }
    }
}