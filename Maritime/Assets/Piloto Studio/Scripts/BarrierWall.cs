using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.ParticleSystem;

[ExecuteAlways]
public class BarrierWall : MonoBehaviour
{
    public Transform pointA; // Extremo izquierdo de la pared
    public Transform pointB; // Extremo derecho de la pared
    [SerializeField] private ParticleSystem[] particleSystem;
    [SerializeField] private ParticleSystem particlesEmitter;
    [SerializeField] private ParticleSystem SecondparticlesEmitter;

    private ParticleSystem.ShapeModule shape;
    private ParticleSystem.ShapeModule Secondshape;

    private Vector3 center;
    private Vector3 direction;

    private Material originalMaterial;
    private Material runtimeMaterial;
    private Vector2 originalMainTexTiling;
    private Vector2 originalDetailNoiseTiling;

    void OnEnable()
    {
        if (particleSystem != null)
            UpdateParticle();
    }

    void UpdateParticle()
    {
        shape = particlesEmitter.shape;
        if (SecondparticlesEmitter)
            Secondshape = SecondparticlesEmitter.shape;

        foreach (ParticleSystem ps in particleSystem)
        {
            if (pointA == null || pointB == null || ps == null) return;

            // Save the original material and tiling values
            if (originalMaterial == null)
            {
                originalMaterial = ps.GetComponent<Renderer>().sharedMaterial;
                if (originalMaterial != null)
                {
                    originalMainTexTiling = originalMaterial.GetTextureScale("_MainTex");
                    originalDetailNoiseTiling = originalMaterial.GetTextureScale("_DetailNoise");
                }
            }

            // Create a runtime material instance for play mode
            if (Application.isPlaying && runtimeMaterial == null)
            {
                runtimeMaterial = new Material(originalMaterial);
                ps.GetComponent<Renderer>().material = runtimeMaterial;
            }

            // Calculate the distance and apply the formula to tiling
            float distance = Vector3.Distance(pointA.position, pointB.position);

            if (runtimeMaterial != null)
            {
                Vector2 adjustedMainTexTiling = originalMainTexTiling;
                adjustedMainTexTiling.x = (distance * originalMainTexTiling.x) / 10;

                Vector2 adjustedDetailNoiseTiling = originalDetailNoiseTiling;
                adjustedDetailNoiseTiling.x = (distance * originalDetailNoiseTiling.x) / 10;

                runtimeMaterial.SetTextureScale("_MainTex", adjustedMainTexTiling);
                runtimeMaterial.SetTextureScale("_DetailNoise", adjustedDetailNoiseTiling);
            }

            // Update particle system properties
            center = (pointA.localPosition + pointB.localPosition) / 2 + new Vector3(0, 0.02f, 0);
            float width = Vector3.Distance(pointA.position, pointB.position);

            var main = ps.main;
            main.startSizeX = width; // Set width

            ps.transform.localPosition = center;
            direction = (pointB.localPosition - pointA.localPosition);
            ps.transform.localRotation = Quaternion.LookRotation(direction, Vector3.up);
        }

        // Update particle emitter
        particlesEmitter.transform.localPosition = center;
        direction = (pointB.localPosition - pointA.localPosition);
        shape.rotation = Quaternion.LookRotation(Vector3.up, direction).eulerAngles + new Vector3(0, 90f, 0);
        shape.radius = direction.magnitude / 2.2f;

        if (SecondparticlesEmitter != null)
        {
            SecondparticlesEmitter.transform.localPosition = center;
            Secondshape.rotation = Quaternion.LookRotation(Vector3.up, direction).eulerAngles + new Vector3(0, 90f, 0);
            Secondshape.radius = direction.magnitude / 2.2f;
        }
    }
}