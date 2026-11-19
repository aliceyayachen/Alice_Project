using System.Collections;
using UnityEngine;

/// <summary>
/// Makes the book behave like a sponge for this blob.
/// Put it on the same object as FloorBlob (e.g. Blob (1)).
///
///   1. Pull-in:  when the blob gets close to a page's strip at the wall edge, the strip draws it in
///                (stronger the closer it is). Only pages that are free (idle) pull.
///   2. Absorb:   when the blob arrives, it slides into the strip and shrinks away,
///                at the same moment the light rises into that page on the wall.
///   3. Return:   with Return Right Away ON, it comes back after Respawn Delay as a "new" blob,
///                so the same number of blobs are always floating around.
///                With it OFF, it stays hidden until that page has fully finished, then comes back.
///
/// Works with several blobs: put one BlobAbsorber on each blob. A page takes one blob at a time;
/// another blob that reaches a busy page just bounces off the wall edge as usual.
///
/// FloorBlob itself is not edited: this script just switches it off while the blob
/// is being absorbed or is hidden, and back on when the blob returns.
/// </summary>
[RequireComponent(typeof(FloorBlob))]
public class BlobAbsorber : MonoBehaviour
{
    [Header("Page zones and wall connection (leave empty to find them)")]
    [SerializeField] private PageDestination[] zones;
    [SerializeField] private WallBookBridge bridge;

    [Header("Floor (used to find the far side for the return)")]
    [SerializeField] private BoxCollider floor;

    [Header("Pull-in: the sponge drawing the blob in")]
    [Tooltip("How far from a strip the pull starts (floor units). 0 = no pull.")]
    [SerializeField, Min(0f)] private float pullDistance = 1.5f;
    [Tooltip("How strong the pull is at the strip's edge.")]
    [SerializeField, Min(0f)] private float pullStrength = 4f;

    [Header("Absorb")]
    [Tooltip("Seconds for the blob to slide in and shrink away.")]
    [SerializeField, Min(0.05f)] private float absorbTime = 0.5f;

    [Header("Return")]
    [Tooltip("Where the blob comes back. Put an empty object where you want it (somewhere you can see it in Display 2). If empty, the far side of the floor is used.")]
    [SerializeField] private Transform returnPoint;
    [Tooltip("The blob reappears up to this far either side of the Return Point, along the wall's direction. 0 = exactly at the point.")]
    [SerializeField, Min(0f)] private float returnSpread = 1.5f;
    [Tooltip("Only used without a Return Point: how far in from the far edge (opposite the wall) the blob reappears.")]
    [SerializeField, Min(0f)] private float returnInset = 1f;
    [Tooltip("ON: the blob comes back soon after being absorbed, so the floor always has the same number of blobs. OFF: it waits until its page has fully finished.")]
    [SerializeField] private bool returnRightAway = true;
    [Tooltip("With Return Right Away: seconds after being absorbed before the blob grows back in.")]
    [SerializeField, Min(0f)] private float respawnDelay = 1.5f;
    [Tooltip("Seconds for the blob to grow back in.")]
    [SerializeField, Min(0.05f)] private float reappearTime = 0.8f;
    [Tooltip("If the book isn't connected, wait this long before the blob returns.")]
    [SerializeField, Min(0f)] private float fallbackWait = 16f;

    [Header("Live readout (Play Mode)")]
    [SerializeField] private string status = "free";

    private FloorBlob floorBlob;
    private Vector3 baseScale;
    private bool busy;
    private PageDestination claimedZone;

    private void Awake()
    {
        floorBlob = GetComponent<FloorBlob>();
        baseScale = transform.localScale;

        if (zones == null || zones.Length == 0)
            zones = FindObjectsByType<PageDestination>();
        if (bridge == null)
            bridge = FindAnyObjectByType<WallBookBridge>();
        if (floor == null)
            Debug.LogWarning("BlobAbsorber: assign the Floor so the blob knows where the far side is.", this);
    }

    private void OnDisable()
    {
        // If this is switched off mid-absorb, put the blob back to normal.
        StopAllCoroutines();
        ReleaseClaim();
        if (busy)
        {
            transform.localScale = baseScale;
            SetVisible(true);
            if (floorBlob != null) floorBlob.enabled = true;
            busy = false;
            status = "free";
        }
    }

    // Has the book told us what this page is doing? ("-" means no report yet.)
    private bool StateKnown(PageDestination zone)
    {
        if (bridge == null || !bridge.BookConnected) return false;
        string state = bridge.StateOf(zone.PageSide);
        return state == "idle" || state == "revealing" || state == "holding" || state == "covering";
    }

    // A page can take the blob if the book says it's idle, or if we don't know (no book / no report yet).
    // Another blob being absorbed into this page also makes it busy.
    private bool PageIsFree(PageDestination zone)
    {
        if (zone.Claimer != null && zone.Claimer != transform) return false;
        if (!StateKnown(zone)) return true;
        return bridge.StateOf(zone.PageSide) == "idle";
    }

    // LateUpdate runs after FloorBlob has moved and the zones have measured,
    // so the pull adds on top of the blob's own motion.
    private void LateUpdate()
    {
        if (busy || zones == null) return;

        // Arrival at a free page: absorb.
        foreach (PageDestination zone in zones)
        {
            if (zone != null && zone.ArrivedThisFrameBy(transform) && PageIsFree(zone) && zone.TryClaim(transform))
            {
                claimedZone = zone;
                StartCoroutine(Absorb(zone));
                return;
            }
        }

        // Otherwise, pull toward the nearest free strip.
        if (pullDistance <= 0f) return;
        PageDestination nearest = null;
        float nearestDistance = float.MaxValue;
        foreach (PageDestination zone in zones)
        {
            if (zone == null || !PageIsFree(zone)) continue;
            float distance = zone.DistanceTo(transform.position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = zone;
            }
        }
        if (nearest == null || nearestDistance >= pullDistance) return;

        float weight = 1f - nearestDistance / pullDistance;
        weight *= weight; // gentle far away, strong up close
        Vector3 position = transform.position;
        Vector3 target = nearest.PointOnZone(position);
        target.y = position.y;
        position += (target - position) * (1f - Mathf.Exp(-pullStrength * weight * Time.deltaTime));
        transform.position = position;
    }

    private IEnumerator Absorb(PageDestination zone)
    {
        busy = true;
        status = "absorbing into the " + zone.PageSide + " page";
        floorBlob.enabled = false;

        // Slide into the strip while shrinking away.
        Vector3 start = transform.position;
        Vector3 end = zone.PointOnZone(start);
        end.y = start.y;
        for (float t = 0f; t < 1f; t += Time.deltaTime / absorbTime)
        {
            float e = t * t * (3f - 2f * t);
            transform.position = Vector3.Lerp(start, end, e);
            transform.localScale = baseScale * (1f - e);
            yield return null;
        }

        // Hide it and move it to the far side now, so the strip stops "seeing" it.
        SetVisible(false);
        transform.localScale = Vector3.zero;
        transform.position = ReturnPoint(start.y);

        if (returnRightAway)
        {
            // Keep the page claimed until the book has started it (from then on the book itself
            // says it's busy), so other blobs bounce off in the meantime.
            status = "respawning soon";
            float waited = 0f;
            while (waited < 2f && StateKnown(zone) && bridge.StateOf(zone.PageSide) == "idle")
            {
                waited += Time.deltaTime;
                yield return null;
            }
            ReleaseClaim();
            while (waited < respawnDelay)
            {
                waited += Time.deltaTime;
                yield return null;
            }
        }
        else
        {
            status = "waiting for the " + zone.PageSide + " page to finish";
            yield return WaitForPage(zone);
            ReleaseClaim(); // the page is free for any blob again
        }

        // Grow back in and drift again.
        status = "returning";
        SetVisible(true);
        for (float t = 0f; t < 1f; t += Time.deltaTime / reappearTime)
        {
            float e = t * t * (3f - 2f * t);
            transform.localScale = baseScale * e;
            yield return null;
        }
        transform.localScale = baseScale;
        floorBlob.enabled = true;
        busy = false;
        status = "free";
    }

    private IEnumerator WaitForPage(PageDestination zone)
    {
        float waited = 0f;
        if (StateKnown(zone))
        {
            // Give the book a moment to start the page...
            while (waited < 2f && StateKnown(zone) && bridge.StateOf(zone.PageSide) == "idle")
            {
                waited += Time.deltaTime;
                yield return null;
            }
            // ...then wait until it has finished (back to idle).
            if (StateKnown(zone) && bridge.StateOf(zone.PageSide) != "idle")
            {
                while (StateKnown(zone) && bridge.StateOf(zone.PageSide) != "idle")
                    yield return null;
                yield break;
            }
        }
        // The book hasn't reported this page's state: use a fixed wait instead.
        while (waited < fallbackWait)
        {
            waited += Time.deltaTime;
            yield return null;
        }
    }

    // Where the blob comes back: near the Return Point if one is set, otherwise the far side of the floor.
    private Vector3 ReturnPoint(float y)
    {
        Vector3 point = FindReturnPoint(y);
        Debug.Log("BlobAbsorber: blob will return at " + point.ToString("F2"), this);
        return point;
    }

    private Vector3 FindReturnPoint(float y)
    {
        if (returnPoint != null)
        {
            // Spread along the wall's direction (the page zones' width), then keep it on the floor.
            Vector3 along = Vector3.forward;
            foreach (PageDestination zone in zones)
                if (zone != null) { along = zone.transform.forward; break; }
            along.y = 0f;
            along.Normalize();

            Vector3 p = returnPoint.position + along * Random.Range(-returnSpread, returnSpread);
            if (floor != null)
            {
                Bounds fb = floor.bounds;
                p.x = Mathf.Clamp(p.x, fb.min.x + 0.5f, fb.max.x - 0.5f);
                p.z = Mathf.Clamp(p.z, fb.min.z + 0.5f, fb.max.z - 0.5f);
            }
            p.y = y;
            return p;
        }

        if (floor == null) return transform.position;
        Bounds b = floor.bounds;

        float wallX = 0f;
        int count = 0;
        foreach (PageDestination zone in zones)
        {
            if (zone == null) continue;
            wallX += zone.transform.position.x;
            count++;
        }
        bool wallOnPlusX = count == 0 || wallX / count > b.center.x;

        float x = wallOnPlusX ? b.min.x + returnInset : b.max.x - returnInset;
        float z = Random.Range(b.min.z + returnInset, b.max.z - returnInset);
        return new Vector3(x, y, z);
    }

    private void ReleaseClaim()
    {
        if (claimedZone != null) claimedZone.Release(transform);
        claimedZone = null;
    }

    private void SetVisible(bool visible)
    {
        // Looked up each time, so the glow made by BlobLook is included too.
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            if (r != null) r.enabled = visible;
    }
}