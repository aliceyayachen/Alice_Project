using System.Collections.Generic;
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

    [Header("Wandering (when nobody is pushing it)")]
    [Tooltip("How much the blob meanders in slow curves while drifting. 0 = straight lines.")]
    [SerializeField, Min(0f)] private float wanderStrength = 0.5f;

    [Header("Wall (the floor's +X edge, under the book)")]
    [Tooltip("When nobody has pushed the blob recently, it never gets closer than this to the wall edge, " +
             "so it can come close enough to show preview words but never drifts into a page by itself. 0 = off.")]
    [SerializeField, Min(0f)] private float closestToWall = 0.4f;
    [Tooltip("How far before that point the blob starts to gently steer away (bigger = earlier, wider curve).")]
    [SerializeField, Min(0.1f)] private float steerDistance = 1.8f;
    [Tooltip("The sharpest it may turn (bigger = tighter curve). Keep it low so it looks like wandering, not a rejection.")]
    [SerializeField, Min(0.05f)] private float maxTurnRate = 0.5f;
    [Tooltip("Seconds after a push during which the blob is allowed to reach the wall.")]
    [SerializeField, Min(0f)] private float pushMemory = 2f;

    [Header("Bumping into other blobs")]
    [Tooltip("How close two blob centres can get before they bounce off each other (floor units). Roughly the blob's visible radius.")]
    [SerializeField, Min(0f)] private float bumpRadius = 0.55f;
    [Tooltip("How much of a fast blob's speed is passed on to the blob it hits (0 = none, 1 = all).")]
    [SerializeField, Range(0f, 1f)] private float bumpTransfer = 0.7f;

    [Header("Lab Tracking (leave empty on laptop)")]
    [SerializeField] private Transform calibratedDot;
    [SerializeField] private Renderer calibratedDotRenderer;

    private Vector3 direction = new Vector3(1f, 0f, 0.35f).normalized;
    private float speed;
    private Transform lastVisitor;
    private Vector3 lastVisitorPosition;
    private Vector3 visitorVelocity;
    private float lastPushTime = -999f;
    private float wanderPhase;
    private BlobLook look;

    // Every blob that is currently moving on the floor (hidden / absorbed blobs are not in here).
    private static readonly List<FloorBlob> active = new List<FloorBlob>();

    /// <summary>True if a visitor pushed this blob within the last Push Memory seconds.</summary>
    public bool RecentlyPushed => Time.time - lastPushTime <= pushMemory;

    private void Start()
    {
        speed = driftSpeed;
        wanderPhase = Random.Range(0f, 100f); // each blob meanders differently
        look = GetComponent<BlobLook>();
    }

    // Runs at the start, and every time the blob comes back after being absorbed:
    // a fresh random direction, so blobs don't end up travelling together as a group.
    private void OnEnable()
    {
        if (!active.Contains(this)) active.Add(this);
        float angle = Random.Range(0f, Mathf.PI * 2f);
        direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        speed = driftSpeed;
        wanderPhase = Random.Range(0f, 100f);
        lastVisitor = null;
        visitorVelocity = Vector3.zero;
    }

    private void OnDisable()
    {
        active.Remove(this);
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
            lastPushTime = Time.time;
        }

        position += direction * speed * dt;
        BumpOtherBlobs(ref position);

        Bounds bounds = floor.bounds;
        float minX = bounds.min.x + edgePadding;
        float maxX = bounds.max.x - edgePadding;
        float minZ = bounds.min.z + edgePadding;
        float maxZ = bounds.max.z - edgePadding;

        if (position.x < minX) direction.x = Mathf.Abs(direction.x);
        if (position.x > maxX) direction.x = -Mathf.Abs(direction.x);
        if (position.z < minZ) direction.z = Mathf.Abs(direction.z);
        if (position.z > maxZ) direction.z = -Mathf.Abs(direction.z);

        // Not pushed recently: meander in slow curves, and steer gently away from the wall
        // the same way, so avoiding the book looks just like ordinary wandering.
        if (!RecentlyPushed) Wander(position.x, maxX, dt);

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

    // Turns the drift direction a little each frame: a slow, changing meander,
    // blended into a gentle turn away from the wall as the blob gets near it.
    private void Wander(float x, float maxX, float dt)
    {
        float t = Time.time + wanderPhase;
        float turn = (Mathf.Sin(t * 0.4f) + Mathf.Sin(t * 0.23f + wanderPhase)) * 0.5f * wanderStrength;

        if (closestToWall > 0f)
        {
            float closest = maxX - closestToWall;          // nearest the blob may drift on its own
            float start = closest - steerDistance;   // where the steering begins
            if (x > start && direction.x > -0.3f)
            {
                float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, closest, x));
                // Turn toward the side it's already drifting, so it glides along and away.
                float awayTurn = maxTurnRate * (direction.z >= 0f ? 1f : -1f);
                turn = Mathf.Lerp(turn, awayTurn, k);
            }

            // Safety net only (shouldn't normally happen): never into the strip while not pushed.
            if (x >= closest + closestToWall * 0.5f && direction.x > 0f) direction.x = -direction.x;
        }

        turn = Mathf.Clamp(turn, -maxTurnRate, maxTurnRate);
        float a = turn * dt;
        float c = Mathf.Cos(a), s = Mathf.Sin(a);
        direction = new Vector3(direction.x * c - direction.z * s, 0f, direction.x * s + direction.z * c).normalized;
    }

    // Blobs don't float over each other: if two get too close, they move apart and bounce off.
    // A fast (pushed) blob passes some of its speed on, so you can knock one blob with another,
    // and the knocked blob counts as pushed too (it may reach the wall).
    private void BumpOtherBlobs(ref Vector3 position)
    {
        if (bumpRadius <= 0f) return;
        foreach (FloorBlob other in active)
        {
            if (other == this || other.bumpRadius <= 0f) continue;
            Vector3 d = position - other.transform.position;
            d.y = 0f;
            float minDistance = bumpRadius + other.bumpRadius;
            float distance = d.magnitude;
            if (distance >= minDistance) continue;

            Vector3 normal = distance > 0.0001f ? d / distance : new Vector3(direction.z, 0f, -direction.x);
            position += normal * ((minDistance - distance) * 0.5f); // move apart (the other blob does its half)

            if (Vector3.Dot(direction, normal) < 0f)                // heading into it: bounce off
            {
                direction = Vector3.Reflect(direction, normal);
                direction.y = 0f;
                direction.Normalize();
            }

            if (other.speed > speed)                                // knocked by a faster blob
            {
                speed = Mathf.Max(speed, other.speed * bumpTransfer);
                if (other.RecentlyPushed) lastPushTime = Mathf.Max(lastPushTime, other.lastPushTime);
                direction = normal;
            }

            if (look != null) look.Bump(new Vector2(normal.x, normal.z), Mathf.Max(speed, other.speed));
        }
    }
}