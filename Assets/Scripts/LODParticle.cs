using UnityEngine;

public class LODParticle : MonoBehaviour
{
    [Tooltip("Disable particles when the camera zoom is below this value.")]
    public int zoom = 5;

    [Tooltip("Optional camera override; defaults to Camera.main.")]
    public Camera targetCamera;

    private ParticleSystem particleSystemRef;
    private bool lastEnabled = true;

    private void Awake()
    {
        particleSystemRef = GetComponent<ParticleSystem>();
        if (targetCamera == null) targetCamera = Camera.main;
    }

    private void OnEnable()
    {
        if (particleSystemRef == null) particleSystemRef = GetComponent<ParticleSystem>();
        if (targetCamera == null) targetCamera = Camera.main;
        ApplyLOD();
    }

    private void Update()
    {
        ApplyLOD();
    }

    private void ApplyLOD()
    {
        if (particleSystemRef == null || targetCamera == null) return;
        // orthographicSize is the board zoom even while the camera is tilted into perspective (BoardNavigator).
        float currentZoom = targetCamera.orthographicSize;
        bool shouldEnable = currentZoom <= zoom;
        if (shouldEnable == lastEnabled) return;

        lastEnabled = shouldEnable;
        if (shouldEnable)
        {
            particleSystemRef.Play(true);
        }
        else
        {
            particleSystemRef.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        // particleSystemRef.gameObject.SetActive(shouldEnable);
    }
}
