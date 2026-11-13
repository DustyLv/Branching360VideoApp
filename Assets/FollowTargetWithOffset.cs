using UnityEngine;
using UnityEngine.InputSystem.Processors;

public class FollowTargetWithOffset : MonoBehaviour
{
    public Transform followTarget;
    public Vector3 positionOffset;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        transform.position = followTarget.position + positionOffset;
    }
}
