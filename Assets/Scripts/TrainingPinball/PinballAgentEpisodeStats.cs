using System.Collections.Generic;
using UnityEngine;

public class PinballAgentEpisodeStats
{
    private readonly Dictionary<PinballRewardType, int> rewardCounts = new Dictionary<PinballRewardType, int>();
    private readonly Dictionary<PinballRewardType, float> rewardTotals = new Dictionary<PinballRewardType, float>();

    private bool wasPressed;
    private bool activationHadContact;
    private bool shotContactRecorded;

    private float continuousActiveSeconds;

    public int AgentId { get; }
    public string AreaId { get; }
    public string Side { get; }
    public int EpisodeId { get; }
    public float StartedAt { get; }

    public int Activations { get; private set; }
    public int ActivationsWithContact { get; private set; }
    public int ActivationsWithoutContact { get; private set; }
    public int OwnContacts { get; private set; }
    public int OtherContacts { get; private set; }
    public int TargetHits { get; private set; }
    public int RejectedRequests { get; private set; }

    public float ActiveSeconds { get; private set; }
    public float MaxContinuousActiveSeconds { get; private set; }
    public float RewardTotal { get; private set; }

    public float FirstDecisionDelaySeconds { get; private set; } = -1f;
    public float FirstShotContactDelaySeconds { get; private set; } = -1f;
    public float TargetDistanceAtContact { get; private set; } = -1f;
    public float ClosestTargetDistance { get; private set; } = -1f;

    public PinballAgentEpisodeStats(int agentId, string areaId, string side, int episodeId)
    {
        AgentId = agentId;
        AreaId = areaId;
        Side = side;
        EpisodeId = episodeId;
        StartedAt = Time.fixedTime;
    }

    public void RecordFirstDecision(float delaySeconds)
    {
        if (FirstDecisionDelaySeconds < 0f)
        {
            FirstDecisionDelaySeconds = Mathf.Max(0f, delaySeconds);
        }
    }

    public void RecordFixedStep(float deltaTime, bool isPressed)
    {
        if (isPressed && !wasPressed)
        {
            Activations++;
            activationHadContact = false;
            continuousActiveSeconds = 0f;
        }

        if (isPressed)
        {
            ActiveSeconds += deltaTime;
            continuousActiveSeconds += deltaTime;
            MaxContinuousActiveSeconds = Mathf.Max(MaxContinuousActiveSeconds, continuousActiveSeconds);
        }

        if (!isPressed && wasPressed)
        {
            FinishActivation();
        }

        wasPressed = isPressed;
    }

    public void RecordOwnContact()
    {
        OwnContacts++;

        if (wasPressed)
        {
            activationHadContact = true;
        }
    }

    public void RecordOtherContact()
    {
        OtherContacts++;
    }

    public void RecordTargetHit()
    {
        TargetHits++;
    }

    public void RecordRejectedRequest()
    {
        RejectedRequests++;
    }

    public void RecordShotContact(float delaySeconds, float distanceToTarget)
    {
        if (shotContactRecorded || delaySeconds < 0f)
        {
            return;
        }

        shotContactRecorded = true;
        FirstShotContactDelaySeconds = delaySeconds;
        TargetDistanceAtContact = distanceToTarget;
        ClosestTargetDistance = distanceToTarget;
    }

    public void TrackTargetDistance(float distanceToTarget)
    {
        if (!shotContactRecorded)
        {
            return;
        }

        ClosestTargetDistance = Mathf.Min(ClosestTargetDistance, distanceToTarget);
    }

    public void RecordReward(PinballRewardType rewardType, float rewardValue)
    {
        rewardCounts[rewardType] = GetRewardCount(rewardType) + 1;
        rewardTotals[rewardType] = GetRewardTotal(rewardType) + rewardValue;
        RewardTotal += rewardValue;
    }

    public int GetRewardCount(PinballRewardType rewardType)
    {
        return rewardCounts.TryGetValue(rewardType, out int count) ? count : 0;
    }

    public float GetRewardTotal(PinballRewardType rewardType)
    {
        return rewardTotals.TryGetValue(rewardType, out float total) ? total : 0f;
    }

    public void FinishPendingActivation()
    {
        if (wasPressed)
        {
            FinishActivation();
            wasPressed = false;
        }
    }

    private void FinishActivation()
    {
        if (activationHadContact)
        {
            ActivationsWithContact++;
        }
        else
        {
            ActivationsWithoutContact++;
        }

        activationHadContact = false;
        continuousActiveSeconds = 0f;
    }
}