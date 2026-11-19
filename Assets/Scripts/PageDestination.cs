using UnityEngine;

/// <summary>
/// One floor zone in front of the wall, linked to one page of the book.
/// Think of it as the page's footprint extended down onto the floor:
/// the blob arrives when it enters ANYWHERE inside this rectangle.
///
/// Size and position come from this object's Transform (use a Cube mesh):
///   Position = centre of the zone, Scale X / Z = its size on the floor.
/// It only READS the blob's position (FloorBlob is not changed) and works out:
///   Proximity: 0 when the blob is farther than Preview Distance from the zone,
///              rising to 1 at the zone's edge (and inside it).
///   Arrival:   the moment the blob enters the zone (optionally after Dwell Time).
/// </summary>
public class PageDestination : MonoBehaviour
{
    public enum Page { Left, Right }

    [Header("Which page this zone belongs to")]
    [SerializeField] private Page page = Page.Left;

    [Header("Blob to watch (leave empty to find the first FloorBlob)")]
    [SerializeField] private Transform blob;

    [Header("Distances (floor units, measured flat)")]
    [Tooltip("Proximity starts rising when the blob is this close to the zone's edge.")]
    [SerializeField, Min(0.01f)] private float previewDistance = 2f;
    [Tooltip("After arriving, the blob must move this far outside the zone before it can arrive again.")]
    [SerializeField, Min(0f)] private float rearmDistance = 0.5f;
    [Tooltip("Seconds the blob must stay inside the zone. 0 = arrive immediately.")]
    [SerializeField, Min(0f)] private float dwellTime = 0f;

    [Header("Zone look")]
    [SerializeField] private Color idleColor = new Color(0.25f, 0.24f, 0.12f);
    [SerializeField] private Color activeColor = new Color(1f, 0.949f, 0.541f); // #FFF28A

    [Header("Live readout (watch these in Play Mode)")]
    [SerializeField] private float distanceToZone;
    [SerializeField, Range(0f, 1f)] private float proximity;
    [SerializeField] private bool blobInside;
    [SerializeField] private int arrivalCount;

    // Read by the wall connection later.
    public Page PageSide => page;
    public float Proximity => proximity;
    public bool ArrivedThisFrame { get; private set; }

    private bool armed = true;
    private float insideTimer;
    private Material zoneMaterial;

    private void Awake()
    {
        if (blob == null)
        {
            FloorBlob found = FindFirstObjectByType<FloorBlob>();
            if (found != null) blob = found.transform;
        }
        if (blob == null)
            Debug.LogWarning(name + ": no blob assigned or found.", this);

        // Separate material copy so each zone can glow on its own.
        Renderer zoneRenderer = GetComponent<Renderer>();
        if (zoneRenderer != null) zoneMaterial = zoneRenderer.material;
    }

    private void Update()
    {
        ArrivedThisFrame = false;
        if (blob == null) return;

        distanceToZone = DistanceToZone(blob.position);

        // 1 at the zone's edge (or inside), fading to 0 at Preview Distance, eased.
        float t = 1f - Mathf.Clamp01(distanceToZone / previewDistance);
        proximity = t * t * (3f - 2f * t);

        blobInside = distanceToZone <= 0f;

        // Ready to arrive again once the blob has moved away from the zone.
        if (!armed && distanceToZone > rearmDistance) armed = true;

        if (blobInside && armed)
        {
            insideTimer += Time.deltaTime;
            if (insideTimer >= dwellTime)
            {
                armed = false;
                insideTimer = 0f;
                arrivalCount++;
                ArrivedThisFrame = true;
                Debug.Log(page + " page: blob arrived (" + arrivalCount + ")", this);
            }
        }
        else
        {
            insideTimer = 0f;
        }

        if (zoneMaterial != null)
            zoneMaterial.color = Color.Lerp(idleColor, activeColor, proximity);
    }

    /// <summary>
    /// Flat distance from a point to this rectangle on the floor (0 when inside).
    /// Assumes a Cube mesh (1 x 1 x 1). Works if the zone is moved, scaled or turned.
    /// </summary>
    private float DistanceToZone(Vector3 worldPoint)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        Vector3 size = transform.lossyScale;
        float dx = Mathf.Max(Mathf.Abs(local.x) - 0.5f, 0f) * Mathf.Abs(size.x);
        float dz = Mathf.Max(Mathf.Abs(local.z) - 0.5f, 0f) * Mathf.Abs(size.z);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // Outlines in the Scene view: solid = the zone, faint = where the preview starts.
    private void OnDrawGizmos()
    {
        Vector3 size = transform.lossyScale;
        float width = Mathf.Abs(size.x);
        float depth = Mathf.Abs(size.z);
        DrawRectangle(width, depth, new Color(1f, 0.95f, 0.54f, 1f));
        DrawRectangle(width + 2f * previewDistance, depth + 2f * previewDistance,
                      new Color(1f, 0.95f, 0.54f, 0.35f));
    }

    private void DrawRectangle(float width, float depth, Color color)
    {
        Gizmos.color = color;
        Vector3 center = transform.position;
        Vector3 halfX = transform.right * (width * 0.5f);
        Vector3 halfZ = transform.forward * (depth * 0.5f);
        Vector3 a = center - halfX - halfZ;
        Vector3 b = center + halfX - halfZ;
        Vector3 c = center + halfX + halfZ;
        Vector3 d = center - halfX + halfZ;
        Gizmos.DrawLine(a, b);
        Gizmos.DrawLine(b, c);
        Gizmos.DrawLine(c, d);
        Gizmos.DrawLine(d, a);
    }

    private void OnDestroy()
    {
        if (zoneMaterial != null)
        {
            if (Application.isPlaying) Destroy(zoneMaterial);
            else DestroyImmediate(zoneMaterial);
        }
    }
}