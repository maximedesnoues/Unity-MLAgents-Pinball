using UnityEngine;

public enum PinballRewardType
{
    Drain,
    TargetHit,
    OwnFlipperContact,
    OtherFlipperContact,
    ActivationWithoutContact,
    ResetZone,
    OtherSide,
    UnnecessaryActivation,
    Timeout,
    UnnecessaryHold,
    RejectedRequest,
    TimedContact,
    ShotProximity
}

[CreateAssetMenu(fileName = "PinballRewardConfiguration", menuName = "Pinball/Reward Configuration")]
public class PinballRewardConfiguration : ScriptableObject
{
    [Header("Récompenses de résultat")]
    [SerializeField] private float drain = -1f;
    [SerializeField] private float targetHit = 2f;
    [SerializeField] private float resetZone = -0.15f;
    [SerializeField] private float otherSide = -0.15f;
    [SerializeField] private float timeout = -0.15f;

    public float GetReward(PinballRewardType rewardType)
    {
        switch (rewardType)
        {
            case PinballRewardType.Drain:
                return drain;

            case PinballRewardType.TargetHit:
                return targetHit;

            case PinballRewardType.ResetZone:
                return resetZone;

            case PinballRewardType.OtherSide:
                return otherSide;

            case PinballRewardType.Timeout:
                return timeout;

            default:
                return 0f;
        }
    }
}