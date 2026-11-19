using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Floor calibration, for the calibration scene (e.g. Lab_Calibration).
/// Put it on an empty object called "FloorCalibrator".
///
/// How it works:
///   1. Dots appear on the projected floor, one at a time (a grid, e.g. 5 x 4 = 20).
///   2. Stand on the dot and hold still. A ring fills up (about 2 seconds) and the next dot appears.
///      Walk to it, hold still, and so on.
///   3. After the last dot it works out the calibration, saves it, and shows how accurate it is.
///   4. Then it goes into CHECK mode: a small dot follows you. If it sits under your feet
///      everywhere on the floor, the calibration is good.
///
/// It reads the tracked person the same way the exhibit does: any object tagged Player
/// (the ZED avatars). The ZED plugin is not changed. Have only ONE person in view while calibrating.
///
/// Keys (in Play Mode): Backspace = redo the previous dot, R = start again, Space = take this dot now.
/// The same actions are in the ⋮ menu of this component.
/// </summary>
public class FloorCalibrator : MonoBehaviour
{
    [Header("Floor and dot grid")]
    [SerializeField] private BoxCollider floor;
    [SerializeField, Min(2)] private int columns = 5;
    [SerializeField, Min(2)] private int rows = 4;
    [Tooltip("How far in from the floor's edges the outer dots are (floor units).")]
    [SerializeField, Min(0f)] private float inset = 0.6f;

    [Header("Holding still")]
    [Tooltip("Seconds to stand still on each dot.")]
    [SerializeField, Min(0.3f)] private float holdTime = 2f;
    [Tooltip("Counts as still while the tracked position stays within this distance (floor units).")]
    [SerializeField, Min(0.01f)] private float stillRadius = 0.08f;
    [Tooltip("After a dot is taken, you must move at least this far before the next one can be taken.")]
    [SerializeField, Min(0f)] private float moveOnDistance = 0.4f;

    [Header("Look")]
    [SerializeField, Min(0.05f)] private float dotSize = 0.35f;
    [SerializeField] private Color dotColor = new Color(1f, 0.95f, 0.54f);
    [SerializeField] private Color doneColor = new Color(0.35f, 0.33f, 0.2f);

    [Header("Saving")]
    [SerializeField] private string fileName = FloorCalibration.DefaultFileName;

    [Header("Live readout (Play Mode)")]
    [SerializeField] private string status = "";
    [SerializeField] private int currentDot;
    [SerializeField] private float averageError;
    [SerializeField] private float worstError;

    private readonly List<Vector2> targets = new List<Vector2>();
    private readonly List<Vector2> captured = new List<Vector2>();
    private readonly List<(float time, Vector2 pos)> recent = new List<(float, Vector2)>();
    private readonly List<GameObject> dots = new List<GameObject>();
    private GameObject ring;
    private GameObject checkDot;
    private Vector2 holdSum;
    private int holdCount;
    private float holdTimer;
    private Vector2? lastCapture;
    private FloorCalibration result;
    private float floorY;

    private void Start()
    {
        if (floor == null)
        {
            status = "Assign the Floor on FloorCalibrator.";
            Debug.LogError("FloorCalibrator: assign the Floor (BoxCollider).", this);
            enabled = false;
            return;
        }
        Bounds b = floor.bounds;
        floorY = b.max.y + 0.02f;

        // Grid of dots, in a snake order so you walk the shortest way.
        for (int r = 0; r < rows; r++)
        {
            float z = Mathf.Lerp(b.min.z + inset, b.max.z - inset, r / (float)(rows - 1));
            for (int c = 0; c < columns; c++)
            {
                int cc = r % 2 == 0 ? c : columns - 1 - c;
                float x = Mathf.Lerp(b.min.x + inset, b.max.x - inset, cc / (float)(columns - 1));
                targets.Add(new Vector2(x, z));
            }
        }

        foreach (Vector2 t in targets) dots.Add(MakeDisc("CalibrationDot", t, dotSize, dotColor));
        ring = MakeDisc("HoldRing", targets[0], dotSize * 1.6f, dotColor * 0.55f);
        checkDot = MakeDisc("CheckDot", Vector2.zero, dotSize * 0.5f, new Color(0.4f, 0.9f, 1f));
        checkDot.SetActive(false);

        Restart();
    }

    private void Update()
    {
        if (KeyDown(KeyCode.R)) Restart();
        if (KeyDown(KeyCode.Backspace)) RedoPrevious();

        Vector2? tracked = TrackedPosition(out int peopleSeen);

        // CHECK mode: show where the calibrated position lands.
        if (result != null)
        {
            checkDot.SetActive(tracked.HasValue);
            if (tracked.HasValue) Place(checkDot, result.Map(tracked.Value), 0.006f);
            status = "Done. Average error " + Cm(averageError) + ", worst " + Cm(worstError) +
                     ". Walk around: the small blue dot should stay under your feet. (R = calibrate again)";
            return;
        }

        if (!tracked.HasValue) { status = "Dot " + (currentDot + 1) + "/" + targets.Count + ": no person seen yet."; ResetHold(); return; }

        // Remember the last moment or so, to tell whether the person is standing still.
        float now = Time.time;
        recent.Add((now, tracked.Value));
        while (recent.Count > 0 && now - recent[0].time > 0.5f) recent.RemoveAt(0);

        bool movedOn = !lastCapture.HasValue || Vector2.Distance(tracked.Value, lastCapture.Value) > moveOnDistance;
        bool still = IsStill();
        string extra = peopleSeen > 1 ? " (" + peopleSeen + " people seen: only one person should be in view!)" : "";

        if (KeyDown(KeyCode.Space)) { Capture(tracked.Value); return; }

        if (!movedOn)
        {
            status = "Dot " + (currentDot + 1) + "/" + targets.Count + ": walk to the new dot." + extra;
            ResetHold();
        }
        else if (!still)
        {
            status = "Dot " + (currentDot + 1) + "/" + targets.Count + ": stand on the dot and hold still." + extra;
            ResetHold();
        }
        else
        {
            holdTimer += Time.deltaTime;
            holdSum += tracked.Value;
            holdCount++;
            status = "Dot " + (currentDot + 1) + "/" + targets.Count + ": hold still..." + extra;
            if (holdTimer >= holdTime) Capture(holdSum / holdCount);
        }

        // The ring grows as you hold still.
        float k = Mathf.Clamp01(holdTimer / holdTime);
        ring.transform.localScale = new Vector3(dotSize * (0.6f + k), 0.004f, dotSize * (0.6f + k));
    }

    private void Capture(Vector2 trackedPosition)
    {
        captured.Add(trackedPosition);
        lastCapture = trackedPosition;
        SetColor(dots[currentDot], doneColor);
        Debug.Log("FloorCalibrator: dot " + (currentDot + 1) + " taken (tracked " + trackedPosition.ToString("F2") + ")", this);
        currentDot++;
        ResetHold();
        recent.Clear();

        if (currentDot >= targets.Count) Finish();
        else ShowCurrent();
    }

    private void Finish()
    {
        ring.SetActive(false);
        FloorCalibration c = FloorCalibration.Solve(captured, targets);
        if (c == null)
        {
            status = "Couldn't work out a calibration from these points. Press R to try again.";
            Debug.LogError("FloorCalibrator: " + status, this);
            return;
        }

        // If one dot is far off (e.g. someone walked through the view), leave it out and try again.
        List<float> errors = Errors(c, captured, targets);
        List<float> sorted = new List<float>(errors);
        sorted.Sort();
        float limit = Mathf.Max(0.15f, sorted[sorted.Count / 2] * 3f);
        var keepTracked = new List<Vector2>();
        var keepTargets = new List<Vector2>();
        var dropped = new List<int>();
        for (int i = 0; i < errors.Count; i++)
        {
            if (errors[i] <= limit) { keepTracked.Add(captured[i]); keepTargets.Add(targets[i]); }
            else dropped.Add(i + 1);
        }
        if (dropped.Count > 0 && keepTracked.Count >= 6)
        {
            FloorCalibration again = FloorCalibration.Solve(keepTracked, keepTargets);
            if (again != null)
            {
                c = again;
                Debug.LogWarning("FloorCalibrator: left out dot(s) " + string.Join(", ", dropped) +
                                 " because they were far off. If that's many dots, calibrate again.", this);
            }
        }

        errors = Errors(c, captured, targets);
        float sum = 0f, worst = 0f;
        for (int i = 0; i < errors.Count; i++)
        {
            sum += errors[i];
            worst = Mathf.Max(worst, errors[i]);
            // Each dot's colour shows its error: bright = good, red = off by 10 cm or more.
            SetColor(dots[i], Color.Lerp(new Color(0.5f, 1f, 0.6f), new Color(1f, 0.3f, 0.25f), Mathf.InverseLerp(0.03f, 0.1f, errors[i])));
        }
        c.averageError = averageError = sum / errors.Count;
        c.worstError = worstError = worst;

        c.Save(fileName);
        result = c;
        Debug.Log("FloorCalibrator: saved " + FloorCalibration.PathFor(fileName) + "  (average error " + Cm(averageError) + ", worst " + Cm(worstError) + ")", this);
    }

    private static List<float> Errors(FloorCalibration c, List<Vector2> tracked, List<Vector2> floorPoints)
    {
        var e = new List<float>();
        for (int i = 0; i < tracked.Count; i++) e.Add(Vector2.Distance(c.Map(tracked[i]), floorPoints[i]));
        return e;
    }

    [ContextMenu("Take this dot now")]
    private void TakeNow()
    {
        if (result != null || currentDot >= targets.Count) return;
        Vector2? tracked = TrackedPosition(out _);
        if (tracked.HasValue) Capture(tracked.Value);
    }

    [ContextMenu("Start again")]
    private void Restart()
    {
        captured.Clear();
        currentDot = 0;
        lastCapture = null;
        result = null;
        averageError = worstError = 0f;
        foreach (GameObject d in dots) SetColor(d, dotColor);
        if (checkDot != null) checkDot.SetActive(false);
        if (ring != null) ring.SetActive(true);
        ResetHold();
        recent.Clear();
        if (dots.Count > 0) ShowCurrent();
    }

    [ContextMenu("Redo the previous dot")]
    private void RedoPrevious()
    {
        if (result != null || currentDot == 0) return;
        currentDot--;
        captured.RemoveAt(captured.Count - 1);
        lastCapture = null; // allow taking it again straight away
        ResetHold();
        ShowCurrent();
    }

    private void ShowCurrent()
    {
        for (int i = 0; i < dots.Count; i++)
        {
            // Current dot bright and big, done ones dim, upcoming ones hidden.
            dots[i].SetActive(i <= currentDot);
            if (i == currentDot) SetColor(dots[i], dotColor);
        }
        Place(ring, targets[currentDot], -0.008f); // just under the dot, so it shows as a growing halo
    }

    private void ResetHold()
    {
        holdTimer = 0f;
        holdSum = Vector2.zero;
        holdCount = 0;
    }

    private bool IsStill()
    {
        if (recent.Count < 5 || recent[recent.Count - 1].time - recent[0].time < 0.4f) return false;
        Vector2 mean = Vector2.zero;
        foreach (var r in recent) mean += r.pos;
        mean /= recent.Count;
        foreach (var r in recent)
            if (Vector2.Distance(r.pos, mean) > stillRadius) return false;
        return true;
    }

    // The tracked person: an object tagged Player (a ZED avatar, or the test stand-in).
    private Vector2? TrackedPosition(out int count)
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        count = players.Length;
        if (count == 0) return null;
        // With several, use the one nearest the last tracked spot.
        Transform best = players[0].transform;
        if (count > 1 && recent.Count > 0)
        {
            Vector2 last = recent[recent.Count - 1].pos;
            float bestD = float.MaxValue;
            foreach (GameObject p in players)
            {
                float d = Vector2.Distance(new Vector2(p.transform.position.x, p.transform.position.z), last);
                if (d < bestD) { bestD = d; best = p.transform; }
            }
        }
        return new Vector2(best.position.x, best.position.z);
    }

    private GameObject MakeDisc(string objectName, Vector2 at, float size, Color color)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        g.name = objectName;
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(transform, true);
        g.transform.localScale = new Vector3(size, 0.004f, size);
        Renderer r = g.GetComponent<Renderer>();
        Shader unlit = Shader.Find("Unlit/Color");
        if (unlit != null) r.material = new Material(unlit);
        r.material.color = color;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Place(g, at);
        return g;
    }

    private void Place(GameObject g, Vector2 at, float yOffset = 0f) => g.transform.position = new Vector3(at.x, floorY + yOffset, at.y);

    private static void SetColor(GameObject g, Color c) => g.GetComponent<Renderer>().material.color = c;

    // Keys are optional: if the project only uses the new Input System, reading old-style keys
    // would throw an error every frame and stop the calibration, so then they're simply ignored
    // (use the ⋮ menu on this component instead).
    private static bool keysUnavailable;
    private static bool KeyDown(KeyCode key)
    {
        if (keysUnavailable) return false;
        try { return Input.GetKeyDown(key); }
        catch (System.InvalidOperationException) { keysUnavailable = true; return false; }
    }

    private static string Cm(float metres) => Mathf.RoundToInt(metres * 100f) + " cm";

    // Big instructions on the main display, so you can read them from the floor.
    private void OnGUI()
    {
        GUIStyle style = new GUIStyle(GUI.skin.label) { fontSize = 28, wordWrap = true };
        style.normal.textColor = Color.white;
        GUI.Label(new Rect(30, 20, Screen.width - 60, 200), status, style);
    }
}