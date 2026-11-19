using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One floor zone in front of the wall, linked to one page of the book.
/// Think of it as the page's footprint along the base of the wall:
/// a blob arrives when it enters ANYWHERE inside this rectangle.
///
/// Size and position come from this object's Transform (use a Cube mesh):
///   Position = centre of the zone, Scale X = depth (from the wall edge), Scale Z = width along the wall.
///
/// Works with SEVERAL blobs. It only READS their positions (FloorBlob is not changed) and works out:
///   Proximity: from the NEAREST blob. 0 outside the faint outline (Preview Distance),
///              jumping to Hint Start as soon as a blob touches it, rising to 1 at the zone itself.
///   Arrival:   the moment any blob enters the zone (optionally after Dwell Time).
///              Each blob has its own arrival / re-arm, so blobs don't interfere with each other.
///   Claim:     while one blob is being absorbed into this page, it "claims" the page,
///              so other blobs can't be absorbed into it at the same time (they bounce off instead).
/// </summary>
public class PageDestination : MonoBehaviour
{
    public enum Page { Left, Right }

    [Header("Which page this zone belongs to")]
    [SerializeField] private Page page = Page.Left;

    [Header("Blobs to watch (leave empty to find every FloorBlob)")]
    [SerializeField] private Transform[] blobs;

    [Header("Distances (floor units, measured flat)")]
    [Tooltip("Proximity starts rising when a blob is this close to the zone's edge.")]
    [SerializeField, Min(0.01f)] private float previewDistance = 2f;
    [Tooltip("How strong the hint is the moment a blob touches the outer (faint) outline. " +
             "0 = it starts from nothing and builds up; higher = words appear straight away.")]
    [SerializeField, Range(0f, 1f)] private float hintStart = 0.45f;
    [Tooltip("After arriving, a blob must move this far outside the zone before it can arrive again.")]
    [SerializeField, Min(0f)] private float rearmDistance = 0.5f;
    [Tooltip("Seconds a blob must stay inside the zone. 0 = arrive immediately.")]
    [SerializeField, Min(0f)] private float dwellTime = 0f;

    [Header("Zone look (only if its Mesh Renderer is switched on)")]
    [SerializeField] private Color idleColor = new Color(0.25f, 0.24f, 0.12f);
    [SerializeField] private Color activeColor = new Color(1f, 0.949f, 0.541f); // #FFF28A

    [Header("Live readout (watch these in Play Mode)")]
    [SerializeField] private int blobsWatched;
    [SerializeField] private float distanceToZone;
    [SerializeField, Range(0f, 1f)] private float proximity;
    [SerializeField] private bool blobInside;
    [SerializeField] private int arrivalCount;
    [SerializeField] private string claimedBy = "";

    // Read by WallBookBridge and BlobAbsorber.
    public Page PageSide => page;
    public float Proximity => proximity;
    /// <summary>Distance from the zone to the NEAREST blob.</summary>
    public float DistanceToZone => distanceToZone;
    /// <summary>True if any blob arrived this frame.</summary>
    public bool ArrivedThisFrame => arrivedThisFrame.Count > 0;
    /// <summary>The blob that arrived most recently (used for where the light enters the page).</summary>
    public Transform LastArrivedBlob { get; private set; }
    /// <summary>Did this particular blob arrive this frame?</summary>
    public bool ArrivedThisFrameBy(Transform blob) => arrivedThisFrame.Contains(blob);

    /// <summary>The blob currently being absorbed into this page, or null.</summary>
    public Transform Claimer { get; private set; }
    /// <summary>A blob claims the page while it's absorbed. Returns false if another blob already has it.</summary>
    public bool TryClaim(Transform blob)
    {
        if (Claimer != null && Claimer != blob) return false;
        Claimer = blob;
        claimedBy = blob != null ? blob.name : "";
        return true;
    }
    public void Release(Transform blob)
    {
        if (Claimer != blob) return;
        Claimer = null;
        claimedBy = "";
    }

    private readonly HashSet<Transform> arrivedThisFrame = new HashSet<Transform>();
    private bool[] armed;
    private float[] insideTimer;
    private Material zoneMaterial;

    private void Awake()
    {
        if (blobs == null || blobs.Length == 0)
        {
            FloorBlob[] found = FindObjectsByType<FloorBlob>();
            blobs = new Transform[found.Length];
            for (int i = 0; i < found.Length; i++) blobs[i] = found[i].transform;
        }
        if (blobs.Length == 0)
            Debug.LogWarning(name + ": no blobs assigned or found.", this);

        armed = new bool[blobs.Length];
        insideTimer = new float[blobs.Length];
        for (int i = 0; i < armed.Length; i++) armed[i] = true;
        blobsWatched = blobs.Length;

        // Separate material copy so each zone can glow on its own.
        Renderer zoneRenderer = GetComponent<Renderer>();
        if (zoneRenderer != null) zoneMaterial = zoneRenderer.material;
    }

    private void Update()
    {
        arrivedThisFrame.Clear();
        if (Claimer != null && !Claimer.gameObject.activeInHierarchy) Release(Claimer);

        float nearest = float.MaxValue;
        bool anyInside = false;

        for (int i = 0; i < blobs.Length; i++)
        {
            Transform blob = blobs[i];
            if (blob == null || !blob.gameObject.activeInHierarchy) continue;

            float distance = DistanceTo(blob.position);
            nearest = Mathf.Min(nearest, distance);
            bool inside = distance <= 0f;
            anyInside |= inside;

            // Ready to arrive again once this blob has moved away from the zone.
            if (!armed[i] && distance > rearmDistance) armed[i] = true;

            if (inside && armed[i])
            {
                insideTimer[i] += Time.deltaTime;
                if (insideTimer[i] >= dwellTime)
                {
                    armed[i] = false;
                    insideTimer[i] = 0f;
                    arrivalCount++;
                    arrivedThisFrame.Add(blob);
                    LastArrivedBlob = blob;
                    Debug.Log(page + " page: " + blob.name + " arrived (" + arrivalCount + ")", this);
                }
            }
            else
            {
                insideTimer[i] = 0f;
            }
        }

        distanceToZone = nearest == float.MaxValue ? 999f : nearest;
        blobInside = anyInside;

        // 0 outside the faint outline. As soon as a blob touches it: Hint Start,
        // then rising to 1 at the zone's edge (or inside), eased.
        if (distanceToZone >= previewDistance)
        {
            proximity = 0f;
        }
        else
        {
            float t = 1f - distanceToZone / previewDistance;
            proximity = Mathf.Lerp(hintStart, 1f, t * t * (3f - 2f * t));
        }

        if (zoneMaterial != null)
            zoneMaterial.color = Color.Lerp(idleColor, activeColor, proximity);
    }

    /// <summary>
    /// Flat distance from a point to this rectangle on the floor (0 when inside).
    /// Assumes a Cube mesh (1 x 1 x 1). Works if the zone is moved, scaled or turned.
    /// </summary>
    public float DistanceTo(Vector3 worldPoint)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        Vector3 size = transform.lossyScale;
        float dx = Mathf.Max(Mathf.Abs(local.x) - 0.5f, 0f) * Mathf.Abs(size.x);
        float dz = Mathf.Max(Mathf.Abs(local.z) - 0.5f, 0f) * Mathf.Abs(size.z);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>
    /// The point on the zone's middle line (along the wall) nearest to a position.
    /// Used to pull a blob in and to absorb it.
    /// </summary>
    public Vector3 PointOnZone(Vector3 worldPoint)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        local.x = 0f;
        local.y = 0f;
        local.z = Mathf.Clamp(local.z, -0.5f, 0.5f);
        return transform.TransformPoint(local);
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