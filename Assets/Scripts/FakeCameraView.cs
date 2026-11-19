using UnityEngine;

/// <summary>
/// LAPTOP TEST ONLY: pretends to be a badly placed ZED camera, so you can try the calibration without one.
/// Put it on an empty object called "FakeCameraView".
///
/// You move the Source (your test cylinder) as the "real" person. This script makes a "tracked person"
/// tagged Player that is deliberately in the WRONG place (turned, scaled, shifted and a bit jittery),
/// like raw camera data before calibration. Calibration should then put things right again.
///
/// Important: set the cylinder's own Tag to Untagged while testing, so only the fake tracked person is a Player.
/// </summary>
public class FakeCameraView : MonoBehaviour
{
    [Tooltip("The thing you move around as the 'real' person (your test cylinder).")]
    [SerializeField] private Transform source;
    [Tooltip("The floor, used as the centre of the turn.")]
    [SerializeField] private BoxCollider floor;

    [Header("How wrong the fake camera is")]
    [SerializeField] private float turnDegrees = 12f;
    [SerializeField] private float scale = 0.85f;
    [SerializeField] private Vector2 shift = new Vector2(0.7f, -0.4f);
    [Tooltip("Random wobble, like real tracking (floor units).")]
    [SerializeField, Min(0f)] private float jitter = 0.02f;

    private Transform fake;

    private void Start()
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        g.name = "Fake tracked person (wrong on purpose)";
        Destroy(g.GetComponent<Collider>());
        g.transform.localScale = Vector3.one * 0.25f;
        Shader unlit = Shader.Find("Unlit/Color");
        Renderer r = g.GetComponent<Renderer>();
        if (unlit != null) r.material = new Material(unlit);
        r.material.color = new Color(1f, 0.3f, 0.3f);
        g.tag = "Player";
        fake = g.transform;
    }

    private void Update()
    {
        if (source == null || fake == null) return;
        Vector3 centre = floor != null ? floor.bounds.center : Vector3.zero;
        Vector3 p = source.position - centre;
        p.y = 0f;
        p = Quaternion.Euler(0f, turnDegrees, 0f) * p * scale;
        p += new Vector3(shift.x, 0f, shift.y);
        p += new Vector3(Random.Range(-jitter, jitter), 0f, Random.Range(-jitter, jitter));
        fake.position = new Vector3(centre.x + p.x, source.position.y, centre.z + p.z);
    }
}
