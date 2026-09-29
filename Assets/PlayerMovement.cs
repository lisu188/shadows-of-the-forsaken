using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public float speed = 5f;
    public float rotationSpeed = 20f;
    public float gravity = 9.81f;
    public float jumpForce = 5f;

    private CharacterController characterController;
    private float verticalVelocity;

    void Start()
    {
        characterController = GetComponent<CharacterController>();
    }

    void Update()
    {
        // Check if the player is grounded
        if (characterController.isGrounded)
        {
            // Reset vertical velocity when grounded
            verticalVelocity = -gravity * Time.deltaTime;

            // Check for jump input
            if (Input.GetButtonDown("Jump"))
            {
                verticalVelocity = jumpForce;
            }
        }
        else
        {
            // Apply gravity when not grounded
            verticalVelocity -= gravity * Time.deltaTime;
        }

        // Get input for movement and rotation
        float rotationInput = Input.GetAxis("Horizontal");
        float moveDirectionZ = Input.GetAxis("Vertical");

        // Rotate the character
        if (rotationInput != 0)
        {
            transform.Rotate(0, rotationInput * rotationSpeed * Time.deltaTime, 0);
        }

        // Calculate movement direction
        Vector3 move = transform.forward * moveDirectionZ * speed;

        // Include vertical velocity in movement
        move.y = verticalVelocity;

        // Move the character
        characterController.Move(move * Time.deltaTime);
    }
}
