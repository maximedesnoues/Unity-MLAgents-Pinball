using System.Collections.Generic;
using NaughtyAttributes;
using Unity.MLAgents.Policies;
using UnityEngine;

public class TimeScaleModifier : MonoBehaviour
{
    [InfoBox("Time Scale modifier will only be active if no agent is in Default (learning) Behaviour Mode")]
    [SerializeField] float timeScale = 20f;
    [SerializeField] bool toggledOn = false;

    List<BehaviorParameters> behaviours = new List<BehaviorParameters>();

    bool HasLearningAgent => behaviours.Exists(behavior => behavior != null && behavior.BehaviorType == BehaviorType.Default);

    void Awake()
    {
        behaviours.AddRange(UnityEngine.Object.FindObjectsByType<BehaviorParameters>(FindObjectsSortMode.None));

        if (toggledOn && !HasLearningAgent)
        {
            Time.timeScale = timeScale;
        }
    }

    private void OnDisable()
    {
        if (Application.isPlaying)
        {
            Time.timeScale = 1f;
        }
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        behaviours.Clear();
        behaviours.AddRange(UnityEngine.Object.FindObjectsByType<BehaviorParameters>(FindObjectsSortMode.None));

        Time.timeScale = toggledOn && !HasLearningAgent ? timeScale : 1f;
    }
}