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

        transform.position = new Vector3(
            center.x + correctedX * 1.354f - leftTravel * 0.32f,
            center.y + 0.03f,
            center.z + correctedZ * 1.282f
        );
    }
}