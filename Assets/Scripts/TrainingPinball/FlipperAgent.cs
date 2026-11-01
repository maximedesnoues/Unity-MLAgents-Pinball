using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;
using static Flipper;

public class FlipperAgent : Agent
{
    static int NextAgentId = 0;

    [SerializeField] TrainingPinballArea trainingPinball;
    [SerializeField] Flipper.FlipperSide side = Flipper.FlipperSide.Left;

    int id = 0;
    int episodeId = 0;

    PlayerController playerController;

    float ballInContactTimestamp = float.NegativeInfinity;
    float ballEnterOtherFlipperTimestamp = float.NegativeInfinity;

    bool BallInContact => Time.fixedTime <= ballInContactTimestamp + Time.fixedDeltaTime * 3f;

    [Header("Récompenses")]
    [SerializeField] private PinballRewardConfiguration rewardConfiguration;

    [Header("Observations")]
    [SerializeField, Min(1f)] private float velocityNormalization = 50f;

    [Header("Contrôle du flipper")]
    [SerializeField, Min(0.02f)] private float pulseDuration = 0.08f;
    [SerializeField, Min(0f)] private float maximumShotDistance = 12f;
    [SerializeField, Min(0f)] private float rearmDistance = 16f;
    [SerializeField, Min(0f)] private float minimumRearmSpeed = 0.5f;
    [SerializeField, Min(0f)] private float rearmCooldown = 0.12f;

    [Header("Diagnostic")]
    [SerializeField, Min(0f)] private float postPulseContactWindow = 0.15f;
    [SerializeField] private bool logFirstEpisode = false;
    [SerializeField] private bool logShotEvents = false;

    private BehaviorParameters behaviorParameters;
    private PinballAgentEpisodeStats episodeStats;

    private Collider2D ballCollider;
    private Collider2D flipperCollider;

    private bool isPulseActive;
    private bool shotArmed;
    private bool previousFireRequest;
    private bool releasedSinceLastShot;
    private bool firstDecisionLogged;
    private bool timedContactRecorded;

    private float pulseEndTime;
    private float lastPulseStartTime = float.NegativeInfinity;

    private bool ShouldLog => behaviorParameters != null && behaviorParameters.BehaviorType != BehaviorType.Default;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetAgentIdCounter()
    {
        NextAgentId = 0;
    }

    void Awake()
    {
        playerController = new PlayerController();
        playerController.Enable();

        behaviorParameters = GetComponent<BehaviorParameters>();
        id = NextAgentId++;

        ResetShotState();
        CreateEpisodeStats();
    }

    void Start()
    {
        if (trainingPinball == null || rewardConfiguration == null)
        {
            Debug.LogError($"[Agent {id}] Vérifie les références du plateau et des récompenses.", this);
            enabled = false;
            return;
        }

        Flipper currentFlipper = trainingPinball.Flipper(side);

        if (currentFlipper == null || trainingPinball.Ball == null || trainingPinball.Target == null)
        {
            Debug.LogError($"[Agent {id}] Vérifie le flipper, la balle et la cible.", this);
            enabled = false;
            return;
        }

        ballCollider = trainingPinball.Ball.GetComponentInChildren<Collider2D>();
        flipperCollider = currentFlipper.GetComponentInChildren<Collider2D>();

        if (ballCollider == null || flipperCollider == null || !ballCollider.enabled || !flipperCollider.enabled)
        {
            Debug.LogError($"[Agent {id}] Vérifie les colliders de la balle et du flipper.", this);
            enabled = false;
            return;
        }

        if (rearmDistance <= maximumShotDistance)
        {
            Debug.LogError($"[Agent {id}] Rearm Distance doit être supérieur à Maximum Shot Distance.", this);
            enabled = false;
            return;
        }

        currentFlipper.onBallEnter += HandleOnBallEnterFlipper;
        currentFlipper.onBallStay += HandleOnBallInContactWithFlipper;
        currentFlipper.onBallExit += HandleOnBallExitFlipper;

        if (trainingPinball.LeftFlipper != null)
        {
            trainingPinball.LeftFlipper.onBallEnter += HandleOnBallEnterAnyFlipper;
        }

        if (trainingPinball.RightFlipper != null)
        {
            trainingPinball.RightFlipper.onBallEnter += HandleOnBallEnterAnyFlipper;
        }

        trainingPinball.onBallEnterBound += HandleOnBallEnterBound;
        trainingPinball.onBallHitTarget += HandleOnBallHitTarget;
        trainingPinball.onEpisodeEnd += HandleOnEpisodeEnd;

        PinballDiagnostics.Current?.RegisterArea(trainingPinball);
    }

    private void OnDestroy()
    {
        if (playerController != null)
        {
            playerController.Disable();
            playerController.Dispose();
        }

        if (trainingPinball == null)
        {
            return;
        }

        Flipper currentFlipper = trainingPinball.Flipper(side);

        if (currentFlipper != null)
        {
            currentFlipper.onBallEnter -= HandleOnBallEnterFlipper;
            currentFlipper.onBallStay -= HandleOnBallInContactWithFlipper;
            currentFlipper.onBallExit -= HandleOnBallExitFlipper;
        }

        if (trainingPinball.LeftFlipper != null)
        {
            trainingPinball.LeftFlipper.onBallEnter -= HandleOnBallEnterAnyFlipper;
        }

        if (trainingPinball.RightFlipper != null)
        {
            trainingPinball.RightFlipper.onBallEnter -= HandleOnBallEnterAnyFlipper;
        }

        trainingPinball.onBallEnterBound -= HandleOnBallEnterBound;
        trainingPinball.onBallHitTarget -= HandleOnBallHitTarget;
        trainingPinball.onEpisodeEnd -= HandleOnEpisodeEnd;
    }

    private void FixedUpdate()
    {
        if (trainingPinball == null || !trainingPinball.HasBegunEpisode || episodeStats == null)
        {
            return;
        }

        bool isPressed = trainingPinball.IsButtonPressed(side);
        int previousWithoutContact = episodeStats.ActivationsWithoutContact;

        episodeStats.RecordFixedStep(Time.fixedDeltaTime, isPressed);

        if (episodeStats.ActivationsWithoutContact > previousWithoutContact)
        {
            Award(PinballRewardType.ActivationWithoutContact);
        }

        if (episodeStats.TargetDistanceAtContact >= 0f)
        {
            episodeStats.TrackTargetDistance(GetTargetDistance());
        }

        UpdatePulse();
        UpdateRearming();
    }

    public override void OnEpisodeBegin()
    {
        ResetShotState();

        ballInContactTimestamp = float.NegativeInfinity;
        ballEnterOtherFlipperTimestamp = float.NegativeInfinity;

        if (trainingPinball != null)
        {
            trainingPinball.SetButtonState(false, side);
        }

        CreateEpisodeStats();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        Flipper currentFlipper = trainingPinball.Flipper(side);

        Vector2 flipperPosition = currentFlipper.transform.position;
        Vector2 ballPosition = trainingPinball.Ball.position;
        Vector2 targetPosition = trainingPinball.Target.transform.position;

        Vector2 ballRelativePosition = ballPosition - flipperPosition;
        Vector2 targetRelativePosition = targetPosition - flipperPosition;

        Vector2 dimensions = trainingPinball.Configuration.AveragePinballDimension;
        float dimensionX = Mathf.Max(Mathf.Abs(dimensions.x), 0.0001f);
        float dimensionY = Mathf.Max(Mathf.Abs(dimensions.y), 0.0001f);

        Vector2 normalizedBallPosition = new Vector2(ballRelativePosition.x / dimensionX, ballRelativePosition.y / dimensionY);
        Vector2 normalizedTargetPosition = new Vector2(targetRelativePosition.x / dimensionX, targetRelativePosition.y / dimensionY);

        Vector2 localBallPosition = currentFlipper.WorldToLocalVector(normalizedBallPosition);
        Vector2 localTargetPosition = currentFlipper.WorldToLocalVector(normalizedTargetPosition);

        float velocityScale = Mathf.Max(velocityNormalization, 1f);
        Vector2 normalizedBallVelocity = trainingPinball.Ball.linearVelocity / velocityScale;
        Vector2 localBallVelocity = currentFlipper.WorldToLocalVector(normalizedBallVelocity);

        sensor.AddObservation(localBallPosition);
        sensor.AddObservation(localBallVelocity);
        sensor.AddObservation(localTargetPosition);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (trainingPinball == null || !trainingPinball.HasBegunEpisode || ballCollider == null || flipperCollider == null)
        {
            return;
        }

        episodeStats.RecordFirstDecision(Time.fixedTime - trainingPinball.EpisodeBeginTimestamp);

        bool fireRequest = actions.DiscreteActions[0] > 0;
        bool isNewRequest = fireRequest && !previousFireRequest;

        previousFireRequest = fireRequest;

        if (logFirstEpisode && episodeId == 0 && !firstDecisionLogged)
        {
            firstDecisionLogged = true;

            Debug.Log($"[Agent {id}] Première décision : délai = {episodeStats.FirstDecisionDelaySeconds:F3} s, balle = {trainingPinball.Ball.position}, vitesse = {trainingPinball.Ball.linearVelocity}, demande = {fireRequest}.");
        }

        if (!fireRequest && !isPulseActive)
        {
            releasedSinceLastShot = true;
        }

        if (!shotArmed || isPulseActive)
        {
            return;
        }

        if (!TryGetBallDistance(out float distance, out float outwardSpeed))
        {
            return;
        }

        if (fireRequest && distance <= maximumShotDistance)
        {
            StartPulse(distance);
            return;
        }

        if (isNewRequest && distance > maximumShotDistance)
        {
            episodeStats.RecordRejectedRequest();
            Award(PinballRewardType.RejectedRequest);
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discreteActionsOut = actionsOut.DiscreteActions;

        bool isPressed = side == Flipper.FlipperSide.Left
            ? playerController.Player.LeftFlipper.IsPressed()
            : playerController.Player.RightFlipper.IsPressed();

        discreteActionsOut[0] = isPressed ? 1 : 0;
    }

    void HandleOnEpisodeEnd()
    {
        episodeStats.TrackTargetDistance(GetTargetDistance());

        if (trainingPinball.LastEpisodeEndReason != TrainingPinballArea.EpisodeEndReason.TargetHit && episodeStats.TargetDistanceAtContact > 0f && episodeStats.ClosestTargetDistance < episodeStats.TargetDistanceAtContact)
        {
            Award(PinballRewardType.ShotProximity);
        }

        if (trainingPinball.LastEpisodeEndReason == TrainingPinballArea.EpisodeEndReason.Timeout)
        {
            Award(PinballRewardType.Timeout);
        }

        int previousWithoutContact = episodeStats.ActivationsWithoutContact;
        episodeStats.FinishPendingActivation();

        if (episodeStats.ActivationsWithoutContact > previousWithoutContact)
        {
            Award(PinballRewardType.ActivationWithoutContact);
        }

        float duration = Mathf.Max(0f, Time.fixedTime - trainingPinball.EpisodeBeginTimestamp);
        float cumulativeReward = GetCumulativeReward();
        string endReason = trainingPinball.LastEpisodeEndReason.ToString();

        PinballDiagnostics.Current?.RecordAgentEpisode(episodeStats, duration, cumulativeReward, endReason);

        if (ShouldLog && PinballDiagnostics.Current == null)
        {
            Debug.Log($"[Agent {id}] Épisode {episodeId} : résultat = {endReason}, récompense = {cumulativeReward:F3}.");
        }

        episodeId++;
        EndEpisode();
    }

    void HandleOnBallEnterFlipper(FlipperSide flipperSide)
    {
        if (!BallInContact)
        {
            episodeStats.RecordOwnContact();
            Award(PinballRewardType.OwnFlipperContact);

            if (lastPulseStartTime > float.NegativeInfinity)
            {
                float timeSincePulse = Time.fixedTime - lastPulseStartTime;

                episodeStats.RecordShotContact(timeSincePulse, GetTargetDistance());

                if (timeSincePulse >= 0f && timeSincePulse <= pulseDuration + postPulseContactWindow && !timedContactRecorded)
                {
                    timedContactRecorded = true;
                    Award(PinballRewardType.TimedContact);
                }
            }
        }

        HandleOnBallInContactWithFlipper(flipperSide);
    }

    void HandleOnBallInContactWithFlipper(FlipperSide flipperSide)
    {
        ballInContactTimestamp = Time.fixedTime;
    }

    void HandleOnBallExitFlipper(FlipperSide flipperSide)
    {
    }

    void HandleOnBallHitTarget()
    {
        episodeStats.RecordTargetHit();
        Award(PinballRewardType.TargetHit);
    }

    void HandleOnBallEnterAnyFlipper(FlipperSide flipperSide)
    {
        if (flipperSide == side)
        {
            return;
        }

        if (Time.fixedTime <= ballEnterOtherFlipperTimestamp + Time.fixedDeltaTime * 3f)
        {
            return;
        }

        ballEnterOtherFlipperTimestamp = Time.fixedTime;

        episodeStats.RecordOtherContact();
        Award(PinballRewardType.OtherFlipperContact);
    }

    void HandleOnBallEnterBound(BoundTrigger.BoundType type)
    {
        switch (type)
        {
            case BoundTrigger.BoundType.Drain:
                Award(PinballRewardType.Drain);
                break;

            case BoundTrigger.BoundType.ResetZone:
                Award(PinballRewardType.ResetZone);
                break;

            case BoundTrigger.BoundType.OtherSide:
                Award(PinballRewardType.OtherSide);
                break;
        }
    }

    private bool TryGetBallDistance(out float distance, out float outwardSpeed)
    {
        distance = float.PositiveInfinity;
        outwardSpeed = 0f;

        ColliderDistance2D separation = Physics2D.Distance(ballCollider, flipperCollider);

        if (!separation.isValid)
        {
            return false;
        }

        distance = Mathf.Max(0f, separation.distance);
        outwardSpeed = Vector2.Dot(trainingPinball.Ball.linearVelocity, separation.normal);

        return true;
    }

    private void StartPulse(float distance)
    {
        shotArmed = false;
        isPulseActive = true;
        releasedSinceLastShot = false;

        lastPulseStartTime = Time.fixedTime;
        pulseEndTime = Time.fixedTime + pulseDuration;

        trainingPinball.SetButtonState(true, side);

        if (logShotEvents && ShouldLog)
        {
            Debug.Log($"[Agent {id}] Épisode {episodeId} : impulsion à distance {distance:F2}, durée = {pulseDuration:F3} s.");
        }
    }

    private void UpdatePulse()
    {
        if (!isPulseActive || Time.fixedTime < pulseEndTime)
        {
            return;
        }

        isPulseActive = false;
        trainingPinball.SetButtonState(false, side);

        if (logShotEvents && ShouldLog)
        {
            Debug.Log($"[Agent {id}] Épisode {episodeId} : impulsion terminée.");
        }
    }

    private void UpdateRearming()
    {
        if (shotArmed || isPulseActive || !releasedSinceLastShot)
        {
            return;
        }

        if (Time.fixedTime - lastPulseStartTime < pulseDuration + rearmCooldown)
        {
            return;
        }

        if (!TryGetBallDistance(out float distance, out float outwardSpeed))
        {
            return;
        }

        if (distance >= rearmDistance && outwardSpeed >= minimumRearmSpeed)
        {
            shotArmed = true;

            if (logShotEvents && ShouldLog)
            {
                Debug.Log($"[Agent {id}] Épisode {episodeId} : flipper réarmé.");
            }
        }
    }

    private float GetTargetDistance()
    {
        Vector2 ballPosition = trainingPinball.Ball.position;
        Vector2 targetPosition = trainingPinball.Target.transform.position;

        return Vector2.Distance(ballPosition, targetPosition);
    }

    private void Award(PinballRewardType rewardType)
    {
        if (rewardConfiguration == null || episodeStats == null)
        {
            return;
        }

        float rewardValue = rewardConfiguration.GetReward(rewardType);
        episodeStats.RecordReward(rewardType, rewardValue);

        if (!Mathf.Approximately(rewardValue, 0f))
        {
            AddReward(rewardValue);
        }

        PinballDiagnostics.Current?.LogReward(id, episodeId, rewardType, rewardValue, GetCumulativeReward());
    }

    private void ResetShotState()
    {
        isPulseActive = false;
        shotArmed = true;
        previousFireRequest = false;
        releasedSinceLastShot = true;
        firstDecisionLogged = false;
        timedContactRecorded = false;

        pulseEndTime = 0f;
        lastPulseStartTime = float.NegativeInfinity;
    }

    private void CreateEpisodeStats()
    {
        string areaId = PinballDiagnostics.Current != null ? PinballDiagnostics.Current.RegisterArea(trainingPinball) : "NoDiagnostics";

        episodeStats = new PinballAgentEpisodeStats(id, areaId, side.ToString(), episodeId);
    }
}