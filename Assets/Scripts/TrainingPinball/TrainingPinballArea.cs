using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public class TrainingPinballArea : MonoBehaviour
{
    public enum EpisodeType
    {
        OneShot = 0,
        LongPlay = 1
    }

    public enum EpisodeEndReason
    {
        None,
        TargetHit,
        Drain,
        ResetZone,
        OtherSide,
        Timeout
    }

    [field: SerializeField] public PinballConfiguration Configuration { get; private set; }

    [Header("Episode")]
    [SerializeField] EpisodeType episodeType = EpisodeType.OneShot;
    [SerializeField] float episodeDuration = 10f;

    [Header("Prefabs")]
    [SerializeField] Rigidbody2D ballPrefab;
    [SerializeField] Target targetPrefab;

    public Rigidbody2D Ball { get; private set; }
    public Target Target { get; private set; }

    List<BallSpawner> ballSpawners;
    List<TargetSpawner> targetSpawners;

    Flipper[] flippers = new Flipper[2];
    public Flipper LeftFlipper => flippers[0];
    public Flipper RightFlipper => flippers[1];
    public Flipper Flipper(Flipper.FlipperSide side) => flippers[(int)side];

    bool[] isButtonsPressed = new bool[2];
    public bool IsLeftButtonPressed => isButtonsPressed[0];
    public bool IsRightButtonPressed => isButtonsPressed[1];
    public bool IsButtonPressed(Flipper.FlipperSide side) => isButtonsPressed[(int)side];

    List<BoundTrigger> boundTriggers;

    public Action<BoundTrigger.BoundType> onBallEnterBound;
    public Action onEpisodeEnd;
    public Action onBallHitTarget;

    float episodeBeginTimestamp = 0f;
    float lastEpisodeEndTimestamp = float.NegativeInfinity;

    [SerializeField] private bool logFirstEpisode = false;

    public float EpisodeBeginTimestamp => episodeBeginTimestamp;
    public bool HasBegunEpisode { get; private set; }
    public int EpisodeNumber { get; private set; } = -1;
    public EpisodeEndReason LastEpisodeEndReason { get; private set; } = EpisodeEndReason.None;

    public void BeginEpisode()
    {
        episodeBeginTimestamp = Time.fixedTime;

        RelocateBall();
        RelocateTarget();

        foreach (Flipper flipper in flippers)
        {
            if (flipper == null)
            {
                continue;
            }

            flipper.Clear();
        }

        isButtonsPressed[0] = false;
        isButtonsPressed[1] = false;

        LastEpisodeEndReason = EpisodeEndReason.None;
        EpisodeNumber++;
        HasBegunEpisode = true;

        if (logFirstEpisode && EpisodeNumber == 0)
        {
            Debug.Log($"[Plateau] Première balle : temps = {Time.fixedTime:F3}, position = {Ball.position}, vitesse = {Ball.linearVelocity}, flipper gauche = {IsLeftButtonPressed}.");
        }
    }

    public void PlayNextEpisode()
    {
        if (!HasBegunEpisode || Time.fixedTime == lastEpisodeEndTimestamp)
        {
            return;
        }

        lastEpisodeEndTimestamp = Time.fixedTime;
        onEpisodeEnd?.Invoke();
        BeginEpisode();
    }

    public void RelocateBall()
    {
        if (Ball == null || ballSpawners == null || ballSpawners.Count == 0)
        {
            return;
        }

        int spawnerIndex = Random.Range(0, ballSpawners.Count);
        BallSpawner selectedSpawner = ballSpawners[spawnerIndex];

        Ball.linearVelocity = Vector2.zero;
        Ball.angularVelocity = 0f;
        Ball.position = selectedSpawner.RandomPosition;
        Ball.rotation = 0f;
        Ball.linearVelocity = selectedSpawner.RandomVelocity;
    }

    public void RelocateTarget()
    {
        if (Target == null || targetSpawners == null || targetSpawners.Count == 0)
        {
            return;
        }

        int spawnerIndex = Random.Range(0, targetSpawners.Count);
        Target.transform.position = targetSpawners[spawnerIndex].RandomPosition;
    }

    public void SetButtonState(bool isPressed, Flipper.FlipperSide side)
    {
        isButtonsPressed[(int)side] = isPressed;
        flippers[(int)side]?.SetState(isPressed);
    }

    void Awake()
    {
        boundTriggers = new List<BoundTrigger>(GetComponentsInChildren<BoundTrigger>());
        ballSpawners = new List<BallSpawner>(GetComponentsInChildren<BallSpawner>());
        targetSpawners = new List<TargetSpawner>(GetComponentsInChildren<TargetSpawner>());

        Flipper[] foundFlippers = GetComponentsInChildren<Flipper>();

        foreach (Flipper flipper in foundFlippers)
        {
            flippers[(int)flipper.Side] = flipper;
            flipper.Initialize(this);
        }

        foreach (BoundTrigger boundTrigger in boundTriggers)
        {
            boundTrigger.onBallEnter += HandleOnBallEnterBound;
        }

        Ball = Instantiate(ballPrefab);
        Ball.gravityScale = Configuration.GetGravityScale();
        Ball.transform.localScale = Vector3.one * Configuration.GetBallScale();

        Target = Instantiate(targetPrefab);
        Target.onBallHit += HandleOnBallHitTarget;
    }

    void OnDestroy()
    {
        if (boundTriggers != null)
        {
            foreach (BoundTrigger boundTrigger in boundTriggers)
            {
                if (boundTrigger != null)
                {
                    boundTrigger.onBallEnter -= HandleOnBallEnterBound;
                }
            }
        }

        if (Target != null)
        {
            Target.onBallHit -= HandleOnBallHitTarget;
        }
    }

    void Start()
    {
        BeginEpisode();
    }

    void FixedUpdate()
    {
        flippers[0]?.SetState(isButtonsPressed[0]);
        flippers[1]?.SetState(isButtonsPressed[1]);

        if (!HasBegunEpisode)
        {
            return;
        }

        if (episodeDuration > 0f && Time.fixedTime - episodeBeginTimestamp >= episodeDuration)
        {
            LastEpisodeEndReason = EpisodeEndReason.Timeout;
            PlayNextEpisode();
        }
    }

    void HandleOnBallEnterBound(BoundTrigger.BoundType boundType)
    {
        if (!HasBegunEpisode)
        {
            return;
        }

        if (episodeType == EpisodeType.OneShot && Time.fixedTime == lastEpisodeEndTimestamp)
        {
            return;
        }

        if (episodeType == EpisodeType.OneShot)
        {
            switch (boundType)
            {
                case BoundTrigger.BoundType.Drain:
                    LastEpisodeEndReason = EpisodeEndReason.Drain;
                    break;

                case BoundTrigger.BoundType.ResetZone:
                    LastEpisodeEndReason = EpisodeEndReason.ResetZone;
                    break;

                case BoundTrigger.BoundType.OtherSide:
                    LastEpisodeEndReason = EpisodeEndReason.OtherSide;
                    break;
            }
        }

        onBallEnterBound?.Invoke(boundType);

        if (episodeType == EpisodeType.OneShot)
        {
            PlayNextEpisode();
        }
        else
        {
            RelocateBall();
        }
    }

    void HandleOnBallHitTarget()
    {
        if (!HasBegunEpisode)
        {
            return;
        }

        if (episodeType == EpisodeType.OneShot && Time.fixedTime == lastEpisodeEndTimestamp)
        {
            return;
        }

        if (episodeType == EpisodeType.OneShot)
        {
            LastEpisodeEndReason = EpisodeEndReason.TargetHit;
        }

        onBallHitTarget?.Invoke();

        if (episodeType == EpisodeType.OneShot)
        {
            PlayNextEpisode();
        }
        else
        {
            RelocateTarget();
        }
    }
}