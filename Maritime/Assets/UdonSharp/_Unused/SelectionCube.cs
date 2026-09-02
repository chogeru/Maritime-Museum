
using UnityEngine;
using UdonSharp;
using VRC.SDKBase;

/// <summary>
/// Attached to one of the two spawn-facing selection cubes.
/// Interacting with it tells the ExperienceManager which scenario to start.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class SelectionCube : UdonSharpBehaviour
{
    public ExperienceManager manager;

    [Tooltip("If true, this cube starts the planet observation scenario. If false, it starts the constellation observation scenario.")]
    public bool isPlanetChoice;

    private void Start()
    {
        InteractionText = isPlanetChoice ? "惑星観測を始める" : "星座観測を始める";
    }

    public override void Interact()
    {
        if (manager == null) return;

        if (isPlanetChoice)
        {
            manager.StartPlanetMode();
        }
        else
        {
            manager.StartConstellationMode();
        }
    }
}
