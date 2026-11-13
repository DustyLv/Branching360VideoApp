using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

public class MainGameController : MonoBehaviour
{
    [Tooltip("The XROrigin component (not the GameObject).")]
    public XROrigin xrOrigin; 

    [Tooltip("The Transform representing the desired spawn position and rotation for the PLAYER'S HEAD.")]
    public Transform spawnPoint;

    
    public Transform head;
    public Transform origin;
    public Transform target;

    private Vector3 offset;
    private Vector3 targetForward;
    private Vector3 cameraForward;
    private float angle;
    
    
    void Start()
    {
        if (xrOrigin == null)
        {
            Debug.LogError("PlayerSpawner: XROrigin component is not assigned!");
            return;
        }

        if (spawnPoint == null)
        {
            Debug.LogError("PlayerSpawner: Spawn Point Transform is not assigned!");
            return;
        }

        // StartCoroutine(SpawnPlayer());
        Recenter();
    }
    
    private IEnumerator SpawnPlayer()
    {
        // Wait for the end of the frame to ensure all XR initialization is complete
        yield return new WaitForEndOfFrame();
        
        // This method moves the origin rig so that the camera
        // lands exactly at the spawnPoint's position.
        xrOrigin.MoveCameraToWorldLocation(spawnPoint.position);
        
        // These methods rotate the rig so its "up" and "forward"
        // directions match the spawnPoint's orientation.
        // xrOrigin.MatchOriginUp(spawnPoint.up);
        xrOrigin.MatchOriginUpCameraForward(spawnPoint.up, spawnPoint.forward);
        
        Debug.Log($"PlayerSpawner: Moved XR Origin so camera is at {spawnPoint.position}");
    }
    
    public void Recenter()
    {
        offset = head.position - origin.position;
        offset.y = 0;
        origin.position = target.position + offset;
      
        targetForward = target.forward;
        targetForward.y = 0;
        cameraForward = head.forward;
        cameraForward.y = 0;

        angle = Vector3.SignedAngle(cameraForward, targetForward, Vector3.up);
      
        origin.RotateAround(head.position, Vector3.up, angle);
        Vector3 headPos = head.localPosition;
        headPos.x = 0;
        headPos.z = 0;
        head.localPosition = headPos;
    }
}
