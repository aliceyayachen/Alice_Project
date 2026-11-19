using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Gives the floor blob its fluid, glowing look (matches the "Floor Blob Look" browser preview).
/// Put it on the blob object (e.g. Blob (1)), next to FloorBlob.
///
///   - Creates a flat glowing quad ("BlobVisual") under the blob and hides the old sphere.
///   - Wobbles the outline all the time, like liquid.
///   - Stretches a little in the direction of motion.
///   - When the blob bounces off a floor edge: squashes flat against it, then jiggles like jelly.
///
/// It only WATCHES how the blob moves. FloorBlob, BlobAbsorber and the zones are not changed.
/// (BlobAbsorber shrinks the blob object, so the glow shrinks with it when absorbed.)
/// </summary>
public class BlobLook : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("A material that uses the Exhibit/FluidBlob shader.")]
    [SerializeField] private Material blobMaterial;
    [Tooltip("The floor, so only direction changes near its edges count as bounces.")]
    [SerializeField] private BoxCollider floor;
    [Tooltip("Hide the blob's original shape (the sphere) and show only the glow.")]
    [SerializeField] private bool hideOriginalShape = true;

    [Header("Look (from the browser preview)")]
    [Tooltip("Blob radius in floor units. Changes apply the next time you press Play.")]
    [SerializeField, Min(0.05f)] private float size = 0.42f;
    [SerializeField, Range(0f, 0.5f)] private float wobble = 0.25f;
    [SerializeField, Min(0f)] private float wobbleSpeed = 0.9f;
    [SerializeField, Min(0.01f)] private float edgeSoftness = 0.17f;
    [SerializeField, Min(0.01f)] private float haloSize = 0.75f;
    [SerializeField, Min(0f)] private float haloStrength = 0.43f;
    [SerializeField] private Color color = new Color(1f, 0.949f, 0.541f); // #FFF28A

    [Header("Jelly bounce")]
    [SerializeField, Min(1f)] private float jellyStiffness = 23f;
    [SerializeField, Min(0f)] private float jellyDamping = 3.3f;
    [SerializeField, Range(0f, 1f)] private float bounceSquash = 0.61f;
    [SerializeField, Range(0f, 0.3f)] private float speedStretch = 0.05f;
    [Tooltip("How close to a floor edge a change of direction counts as a bounce (floor units).")]
    [SerializeField, Min(0f)] private float bounceEdgeZone = 0.7f;

    private const float Extent = 3.2f; // quad size, in blob radii (room for the halo)

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int ExtentId = Shader.PropertyToID("_Extent");
    private static readonly int WobbleId = Shader.PropertyToID("_Wobble");
    private static readonly int WobbleTimeId = Shader.PropertyToID("_WobbleTime");
    private static readonly int SoftId = Shader.PropertyToID("_Soft");
    private static readonly int HaloId = Shader.PropertyToID("_Halo");
    private static readonly int GlowId = Shader.PropertyToID("_Glow");
    private static readonly int DeformId = Shader.PropertyToID("_Deform");

    private Transform visual;
    private Material material;
    private Renderer originalRenderer;

    private Vector3 lastPosition;
    private bool hasLastPosition;
    private Vector2 velocity;
    private Vector2 lastVelocity;
    private Vector2 stretch;
    private float squash, squashVelocity, squashAngle;
    private float jiggle;
    private float wobbleTime;

    private void Awake()
    {
        originalRenderer = GetComponent<Renderer>();

        if (blobMaterial == null)
        {
            Shader shader = Shader.Find("Exhibit/FluidBlob");
            if (shader == null)
            {
                Debug.LogError("BlobLook: assign Blob Material (a material using Exhibit/FluidBlob).", this);
                enabled = false;
                return;
            }
            blobMaterial = new Material(shader);
            Debug.LogWarning("BlobLook: no Blob Material assigned, using a temporary one. Assign one so it works in builds.", this);
        }

        // The flat glowing shape, created under the blob.
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "BlobVisual";
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(transform, false);
        visual = quad.transform;

        Renderer quadRenderer = quad.GetComponent<Renderer>();
        material = new Material(blobMaterial);
        quadRenderer.sharedMaterial = material;
        quadRenderer.shadowCastingMode = ShadowCastingMode.Off;
        quadRenderer.receiveShadows = false;

        // Make the quad the right size on the floor, whatever the blob's own scale is.
        float worldSize = 2f * size * Extent;
        Vector3 parentScale = transform.lossyScale;
        visual.localScale = new Vector3(
            worldSize / Mathf.Max(Mathf.Abs(parentScale.x), 0.0001f),
            worldSize / Mathf.Max(Mathf.Abs(parentScale.z), 0.0001f),
            1f);
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || visual == null) return;

        // Always lie flat on the floor, facing up.
        visual.rotation = Quaternion.Euler(90f, 0f, 0f);
        if (hideOriginalShape && originalRenderer != null) originalRenderer.enabled = false;

        // Measure how the blob moved this frame (on the floor plane).
        Vector3 position = transform.position;
        if (!hasLastPosition) { lastPosition = position; hasLastPosition = true; }
        Vector3 step = position - lastPosition;
        lastPosition = position;

        if (step.sqrMagnitude > 1f)
        {
            // A big jump means it was moved (e.g. returned to the floor), not a real motion.
            velocity = Vector2.zero;
            lastVelocity = Vector2.zero;
            stretch = Vector2.zero;
        }
        else
        {
            Vector2 v = new Vector2(step.x, step.z) / dt;
            DetectBounce(v, position);
            lastVelocity = v;
            velocity = v;
        }

        // Jelly spring: squashes, overshoots and settles with a few gentle wobbles.
        squashVelocity += (-jellyStiffness * squash - jellyDamping * squashVelocity) * dt;
        squash = Mathf.Clamp(squash + squashVelocity * dt, -0.6f, 0.6f);
        jiggle *= Mathf.Exp(-1.4f * dt);

        // Stretch along the direction of motion, smoothed.
        stretch += (velocity * speedStretch - stretch) * (1f - Mathf.Exp(-6f * dt));
        wobbleTime += dt * wobbleSpeed;

        // Send everything to the shader.
        material.SetColor(ColorId, color);
        material.SetFloat(ExtentId, Extent);
        material.SetFloat(WobbleId, wobble * (1f + jiggle * 3f));
        material.SetFloat(WobbleTimeId, wobbleTime);
        material.SetFloat(SoftId, edgeSoftness);
        material.SetFloat(HaloId, haloSize);
        material.SetFloat(GlowId, haloStrength);
        material.SetVector(DeformId, InverseDeform());
    }

    // A bounce = the blob was heading toward a floor edge, is now heading away, and is near that edge.
    private void DetectBounce(Vector2 v, Vector3 position)
    {
        const float minSpeed = 0.2f;
        bool hasFloor = floor != null;
        Bounds b = hasFloor ? floor.bounds : default;

        if (lastVelocity.x > minSpeed && v.x <= 0f && (!hasFloor || b.max.x - position.x < bounceEdgeZone))
            Bounce(new Vector2(-1f, 0f), lastVelocity.x);
        if (lastVelocity.x < -minSpeed && v.x >= 0f && (!hasFloor || position.x - b.min.x < bounceEdgeZone))
            Bounce(new Vector2(1f, 0f), -lastVelocity.x);
        if (lastVelocity.y > minSpeed && v.y <= 0f && (!hasFloor || b.max.z - position.z < bounceEdgeZone))
            Bounce(new Vector2(0f, -1f), lastVelocity.y);
        if (lastVelocity.y < -minSpeed && v.y >= 0f && (!hasFloor || position.z - b.min.z < bounceEdgeZone))
            Bounce(new Vector2(0f, 1f), -lastVelocity.y);
    }

    private void Bounce(Vector2 normal, float impact)
    {
        squashAngle = Mathf.Atan2(normal.y, normal.x);
        squashVelocity += impact * bounceSquash * 1.6f;
        jiggle = Mathf.Min(1f, jiggle + impact * 0.18f);
    }

    // Inverse of (stretch along motion) x (squash along the bounce direction), as a 2x2 matrix.
    private Vector4 InverseDeform()
    {
        float sq = squash;
        Vector4 squashMatrix = Scale(squashAngle, 1f - sq, 1f / (1f - sq) * 0.98f + 0.02f * (1f + sq));
        float st = Mathf.Min(0.45f, stretch.magnitude);
        float stretchAngle = Mathf.Atan2(stretch.y, stretch.x);
        Vector4 stretchMatrix = Scale(stretchAngle, 1f + st, 1f / (1f + st));

        Vector4 m = Multiply(stretchMatrix, squashMatrix);
        float det = m.x * m.w - m.y * m.z;
        if (Mathf.Abs(det) < 1e-5f) return new Vector4(1f, 0f, 0f, 1f);
        return new Vector4(m.w / det, -m.y / det, -m.z / det, m.x / det);
    }

    // Rotation(angle) * diag(a, b) * Rotation(-angle), stored as (row0.x, row0.y, row1.x, row1.y).
    private static Vector4 Scale(float angle, float a, float b)
    {
        float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
        return new Vector4(c * c * a + s * s * b, c * s * (a - b), c * s * (a - b), s * s * a + c * c * b);
    }

    private static Vector4 Multiply(Vector4 A, Vector4 B)
    {
        return new Vector4(
            A.x * B.x + A.y * B.z, A.x * B.y + A.y * B.w,
            A.z * B.x + A.w * B.z, A.z * B.y + A.w * B.w);
    }

    private void OnDestroy()
    {
        if (material != null) Destroy(material);
    }
}
