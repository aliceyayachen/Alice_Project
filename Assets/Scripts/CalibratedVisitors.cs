using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Uses the saved floor calibration in the EXHIBIT scene (lab only).
/// Put it on an empty object called "CalibratedVisitors".
///
/// For every tracked person (a ZED avatar tagged Player) it makes an invisible marker at the
/// CORRECTED floor position and tags THAT marker Player instead. So FloorBlob, the zones and
/// everything else keep looking for "Player" as before, but now get the calibrated position.
/// The ZED plugin and its avatars are not changed (only the avatar's tag is switched off at runtime).
///
/// Don't add this to Laptop_Test: there the cylinder is already in the right place.
/// </summary>
public class CalibratedVisitors : MonoBehaviour
{
    [SerializeField] private string fileName = FloorCalibration.DefaultFileName;
    [Tooltip("Show a small dot at each corrected position (handy for checking; switch off for the exhibition).")]
    [SerializeField] private bool showMarkers = true;
    [SerializeField, Min(0.02f)] private float markerSize = 0.2f;

    [Header("Smoothing (camera jitter)")]
    [Tooltip("How quickly the visitor follows the tracked position, in seconds. Higher = smoother but a little laggy. 0 = no smoothing.")]
    [SerializeField, Min(0f)] private float smoothTime = 0.15f;
    [Tooltip("A sudden jump bigger than this (floor units) is ignored unless it lasts longer than Jump Confirm Time (tracking glitches).")]
    [SerializeField, Min(0.1f)] private float maxJump = 1.5f;
    [SerializeField, Min(0f)] private float jumpConfirmTime = 0.3f;

    [Header("Live readout (Play Mode)")]
    [SerializeField] private bool calibrationLoaded;
    [SerializeField] private string calibrationInfo = "";
    [SerializeField] private int visitorsTracked;

    private FloorCalibration calibration;
    private readonly Dictionary<Transform, Transform> markers = new Dictionary<Transform, Transform>();
    private readonly List<Transform> gone = new List<Transform>();
    private readonly HashSet<GameObject> ownMarkers = new HashSet<GameObject>();
    private readonly Dictionary<Transform, float> jumpTimers = new Dictionary<Transform, float>();
    private readonly HashSet<Transform> placedOnce = new HashSet<Transform>();

    private void Start()
    {
        calibration = FloorCalibration.Load(fileName);
        calibrationLoaded = calibration != null;
        if (calibration == null)
        {
            calibration = new FloorCalibration(); // no change to positions
            calibrationInfo = "No calibration file found: positions are NOT corrected. Run the calibration scene first.";
            Debug.LogWarning("CalibratedVisitors: " + calibrationInfo + " (" + FloorCalibration.PathFor(fileName) + ")", this);
        }
        else
        {
            TrackedBody.UseHips = calibration.usedHips; // read the same body part the calibration used
            calibrationInfo = "Saved " + calibration.savedAt + ", " + calibration.pointsUsed + " dots, average error " +
                              calibration.averageError.ToString("0.00") + " floor units";
            Debug.Log("CalibratedVisitors: loaded calibration (" + calibrationInfo + ")", this);
        }
    }

    // Markers are updated every frame. FloorBlob reads them in its own Update (at most one frame behind).
    private void Update()
    {
        // New tracked people: anything tagged Player that isn't one of our markers.
        foreach (GameObject tagged in GameObject.FindGameObjectsWithTag("Player"))
        {
            if (ownMarkers.Contains(tagged)) continue;
            Transform raw = tagged.transform;
            if (markers.ContainsKey(raw)) continue;

            tagged.tag = "Untagged"; // from now on only the corrected marker counts as the visitor
            markers.Add(raw, MakeMarker(raw.name));
        }

        // Move markers to the corrected positions; remove markers of people who left.
        gone.Clear();
        foreach (var pair in markers)
        {
            Transform raw = pair.Key, marker = pair.Value;
            if (raw == null) { gone.Add(raw); continue; }
            bool present = raw.gameObject.activeInHierarchy;
            marker.gameObject.SetActive(present);
            if (present) MoveSmoothly(marker, calibration.Map(TrackedBody.FloorPoint(raw))); // corrected, then smoothed
        }
        foreach (Transform raw in gone)
        {
            if (markers[raw] != null)
            {
                ownMarkers.Remove(markers[raw].gameObject);
                Destroy(markers[raw].gameObject);
            }
            jumpTimers.Remove(markers[raw]); placedOnce.Remove(markers[raw]);
            markers.Remove(raw);
        }
        visitorsTracked = markers.Count;
        if (gone.Count > 0) TrackedBody.Tidy();
    }

    // Glides toward the new position (hides jitter) and ignores short glitches where the
    // tracking jumps far away for a moment.
    private void MoveSmoothly(Transform marker, Vector3 target)
    {
        if (!placedOnce.Contains(marker)) { marker.position = target; placedOnce.Add(marker); return; }

        Vector3 current = marker.position;
        float jump = Vector2.Distance(new Vector2(current.x, current.z), new Vector2(target.x, target.z));
        if (jump > maxJump)
        {
            jumpTimers.TryGetValue(marker, out float t);
            t += Time.deltaTime;
            jumpTimers[marker] = t;
            if (t < jumpConfirmTime) return;            // probably a glitch: stay put for now
            marker.position = target;                    // it lasted: the person really is there
            jumpTimers[marker] = 0f;
            return;
        }
        jumpTimers[marker] = 0f;

        float k = smoothTime <= 0f ? 1f : 1f - Mathf.Exp(-Time.deltaTime / smoothTime);
        marker.position = Vector3.Lerp(current, target, k);
    }

    private Transform MakeMarker(string rawName)
    {
        GameObject g;
        if (showMarkers)
        {
            g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(g.GetComponent<Collider>());
            g.transform.localScale = new Vector3(markerSize, 0.01f, markerSize);
            Renderer r = g.GetComponent<Renderer>();
            Shader unlit = Shader.Find("Unlit/Color");
            if (unlit != null) r.material = new Material(unlit);
            r.material.color = new Color(0.4f, 0.9f, 1f);
        }
        else
        {
            g = new GameObject();
        }
        g.name = "Visitor (calibrated) - " + rawName;
        ownMarkers.Add(g);
        g.tag = "Player";
        g.transform.SetParent(transform, true);
        return g.transform;
    }

    private void OnDestroy()
    {
        foreach (var pair in markers)
            if (pair.Value != null) Destroy(pair.Value.gameObject);
    }
}