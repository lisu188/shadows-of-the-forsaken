using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform player;       // Reference to the player's transform
    public float distance = 5.0f;  // Distance behind the player
    public float height = 2.0f;    // Height above the player
    public float smoothSpeed = 2f; // Smoothing factor for camera movement

    void LateUpdate()
    {
        // Calculate the desired position
        Vector3 desiredPosition = player.position - player.forward * distance + Vector3.up * height;

        // Smoothly interpolate to the desired position
        Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);

        // Update the camera's position
        transform.position = smoothedPosition;

        // Make the camera look at the player
        transform.LookAt(player);
    }
}
