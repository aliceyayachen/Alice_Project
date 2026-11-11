
using UnityEngine;

public class TurnBlueOnTouch : MonoBehaviour
{
    // The tag used to identify the player.
    public string playerTag = "Player";

    // The color the object changes to.
    public Color touchedColor = Color.blue;

    // The object components and its original color.
    private Collider objectCollider;
    private Renderer objectRenderer;
    private Material objectMaterial;
    private Color originalColor;

    void Awake()
    {
        // Get the Collider and Renderer on this object.
        objectCollider = GetComponent<Collider>();
        objectRenderer = GetComponent<Renderer>();

        // Make a separate material instance for this object.
        if (objectRenderer != null)
        {
            objectMaterial = objectRenderer.material;
            originalColor = objectMaterial.color;
        }

        // Warn us if the object is missing required components.
        if (objectCollider == null)
            Debug.LogWarning(name + " needs a Collider.", this);

        if (objectRenderer == null)
            Debug.LogWarning(name + " needs a Renderer.", this);
    }

    void Update()
    {
        if (objectCollider == null || objectMaterial == null)
            return;

        // Find all GameObjects tagged as Player.
        GameObject[] players =
            GameObject.FindGameObjectsWithTag(playerTag);

        bool playerInside = false;

        // Check whether any player's position is inside this object.
        foreach (GameObject player in players)
        {
            if (player == null)
                continue;

            Vector3 closestPoint =
                objectCollider.ClosestPoint(player.transform.position);

            // If the closest point is the player's position,
            // that position is inside the Collider.
            if (Vector3.Distance(
                closestPoint, player.transform.position) < 0.001f)
            {
                playerInside = true;
                break;
            }
        }

        // Turn blue while a player is inside; otherwise restore the color.
        objectMaterial.color =
            playerInside ? touchedColor : originalColor;
    }

    void OnDestroy()
    {
        // Clean up the material instance created by this script.
        if (objectMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(objectMaterial);
            else
                DestroyImmediate(objectMaterial);
        }
    }
}
