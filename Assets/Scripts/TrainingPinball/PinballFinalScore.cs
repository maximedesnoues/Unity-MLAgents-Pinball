using UnityEngine;

public class PinballFinalScore : MonoBehaviour
{
    [SerializeField] private TrainingPinballArea trainingPinball;
    [SerializeField] private bool longPlay = false;
    [SerializeField, Min(1)] private int evaluationEpisodeCount = 100;

    private int episodeCount;
    private int targetCount;
    private int drainCount;
    private int resetZoneCount;
    private int otherSideCount;

    private void Start()
    {
        if (trainingPinball == null)
        {
            Debug.LogError("Attribue le plateau TrainingPinballArea au compteur.", this);
            enabled = false;
            return;
        }

        trainingPinball.onBallHitTarget += HandleTargetHit;
        trainingPinball.onBallEnterBound += HandleBound;
        trainingPinball.onEpisodeEnd += HandleEpisodeEnd;
    }

    private void OnDestroy()
    {
        if (trainingPinball == null)
        {
            return;
        }

        trainingPinball.onBallHitTarget -= HandleTargetHit;
        trainingPinball.onBallEnterBound -= HandleBound;
        trainingPinball.onEpisodeEnd -= HandleEpisodeEnd;
    }

    private void HandleTargetHit()
    {
        targetCount++;
    }

    private void HandleBound(BoundTrigger.BoundType boundType)
    {
        switch (boundType)
        {
            case BoundTrigger.BoundType.Drain:
                drainCount++;
                break;

            case BoundTrigger.BoundType.ResetZone:
                resetZoneCount++;
                break;

            case BoundTrigger.BoundType.OtherSide:
                otherSideCount++;
                break;
        }
    }

    private void HandleEpisodeEnd()
    {
        episodeCount++;

        if (longPlay)
        {
            Debug.Log($"DUAL — Partie {episodeCount} : {targetCount} cibles en 30 secondes, {drainCount} drains.");
            ResetCounters();
            return;
        }

        if (episodeCount < evaluationEpisodeCount)
        {
            return;
        }

        float successRate = 100f * targetCount / Mathf.Max(episodeCount, 1);

        Debug.Log($"SINGLE — {targetCount}/{episodeCount} cibles ; réussite = {successRate:F1} % ; drains = {drainCount} ; ResetZone = {resetZoneCount} ; OtherSide = {otherSideCount}.");

        ResetCounters();
    }

    private void ResetCounters()
    {
        episodeCount = 0;
        targetCount = 0;
        drainCount = 0;
        resetZoneCount = 0;
        otherSideCount = 0;
    }
}