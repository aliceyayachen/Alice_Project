using UnityEngine;

public class ChangeOtherObjectColor : MonoBehaviour
{
    [Header("Target Object")]
    public Renderer targetObject;

    [Header("Color Settings")]
    public Color touchedColor = Color.blue;

    // The target's material and original color.
    private Material targetMaterial;
    private Color originalColor;

    // How long the color transition takes.
    private float transitionDuration = 0.5f;

    // Tracks how far through the color transition we are.
    private float transitionProgress = 0f;

    void Awake()
    {
        // Get the target object's material.
        if (targetObject != null)
        {
            targetMaterial = targetObject.material;

            // Remember its original color.
            originalColor = targetMaterial.color;
        }
        else
        {
            Debug.LogWarning("Please assign a Target Object!", this);
        }
    }

    void Update()
    {
        if (targetObject == null || targetMaterial == null)
            return;

        // Find all GameObjects tagged Player.
        GameObject[] players =
            GameObject.FindGameObjectsWithTag("Player");

        bool playerInside = false;

        // Check each player's position against this object's Collider.
        foreach (GameObject player in players)
        {
            if (player == null)
                continue;

            Vector3 closestPoint =
                GetComponent<Collider>().ClosestPoint(
                    player.transform.position);

            if (Vector3.Distance(
                closestPoint, player.transform.position) < 0.001f)
            {
                playerInside = true;
                break;
            }
        }

        // Choose the transition direction.
        float targetProgress = playerInside ? 1f : 0f;

        // Move smoothly toward the desired state.
        transitionProgress = Mathf.MoveTowards(
            transitionProgress,
            targetProgress,
            Time.deltaTime / transitionDuration
        );

        // Blend between the original color and the touched color.
        targetMaterial.color = Color.Lerp(
            originalColor,
            touchedColor,
            transitionProgress
        );
    }

    void OnDestroy()
    {
        // Clean up the material instance.
        if (targetMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(targetMaterial);
            else
                DestroyImmediate(targetMaterial);
        }
    }
}
