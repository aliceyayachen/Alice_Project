using UnityEngine;

public class CalibratedFloorDot : MonoBehaviour
{
    [SerializeField] private Transform centerMarker;
    [SerializeField] private float travelMultiplier = 2.4f;
    [SerializeField] private Renderer dotRenderer;

    private void LateUpdate()
    {
        if (centerMarker == null || dotRenderer == null) return;

        GameObject player = GameObject.FindWithTag("Player");
        dotRenderer.enabled = player != null;
        if (player == null) return;

        Vector3 center = centerMarker.position;
        Vector3 offset = player.transform.position - center;

        float correctedX = offset.x * travelMultiplier + 0.52685f;
        float correctedZ = offset.z * travelMultiplier - 0.04f;

        float leftTravel = Mathf.Max(0f, correctedZ * 1.282f);

        // The dot position with your current calibration.
        Vector2 dot = new Vector2(
                center.x + correctedX * 1.354f - leftTravel * 0.32f,
                center.z + correctedZ * 1.282f
        );

        // Keep the center, back, and right test spots close to where they are now.
        // At the four measured spots, shift the dot toward your feet.
        Vector2 correction = BlendCorrections(dot, center);

        // Use the dot's position after the existing calibration.
        Vector2 currentDot = dot + correction;
        Vector2 halfwayLeft = new Vector2(0.33f, 3.60f);

        // Apply the full correction here, fading it out farther away.
        float weight = Mathf.Clamp01(
                1f - Vector2.Distance(currentDot, halfwayLeft) / 2f
        );

        correction += new Vector2(0.03f, 1.40f) * weight;

        transform.position = new Vector3(
            dot.x + correction.x,
            center.y + 0.03f,
            dot.y + correction.y
        );
    }

    private static Vector2 BlendCorrections(Vector2 dot, Vector3 center)
    {
        Vector2 middle = new Vector2(center.x, center.z);
        Vector2 weightedCorrection = Vector2.zero;
        float totalWeight = 0f;

        AddPoint(middle, Vector2.zero);
        AddPoint(middle + new Vector2(-2f, 0f), Vector2.zero);
        AddPoint(middle + new Vector2(0f, -2f), Vector2.zero);

        AddPoint(new Vector2(4.21f, 5.01f), new Vector2(-0.845f, 0.42f));
        AddPoint(new Vector2(-1.12f, 5.63f), new Vector2(-0.51f, -1.07f));
        AddPoint(new Vector2(-3.16f, -3.86f), new Vector2(1.22f, -0.66f));
        AddPoint(new Vector2(4.46f, -3.34f), new Vector2(-0.23f, 0.695f));

        return weightedCorrection / totalWeight;

        void AddPoint(Vector2 measuredDot, Vector2 adjustment)
        {
            float distanceSquared = (dot - measuredDot).sqrMagnitude;
            float weight = 1f / Mathf.Max(distanceSquared, 0.001f);
            weightedCorrection += adjustment * weight;
            totalWeight += weight;
        }
    }
}