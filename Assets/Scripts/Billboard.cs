using UnityEngine;

public class Billboard : MonoBehaviour
{
    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null) return;

        if (ComfortSettings.ReduceMotion)
        {
            Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (fwd.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
                return;
            }
        }
        transform.rotation = cam.transform.rotation;
    }
}
