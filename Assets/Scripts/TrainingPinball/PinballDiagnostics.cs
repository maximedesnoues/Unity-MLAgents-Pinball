using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

public class PinballDiagnostics : MonoBehaviour
{
    private class AreaEntry
    {
        public TrainingPinballArea Area;
        public string Id;
        public int Episode;
        public int Targets;
        public int Drains;
        public int ResetZones;
        public int OtherSides;

        public Action TargetHandler;
        public Action<BoundTrigger.BoundType> BoundHandler;
        public Action EpisodeHandler;
    }

    private class AgentAggregate
    {
        public int Episodes;
        public float TotalReward;
    }

    public static PinballDiagnostics Current { get; private set; }

    [SerializeField] private string runId = "SingleFlipper_2_8_0";
    [SerializeField] private bool exportCsv = true;
    [SerializeField] private bool logEachReward = false;
    [SerializeField] private bool logEveryEpisode = false;
    [SerializeField, Min(1)] private int summaryEveryAgentEpisodes = 100;

    private readonly Dictionary<TrainingPinballArea, AreaEntry> areas = new Dictionary<TrainingPinballArea, AreaEntry>();
    private readonly Dictionary<int, AgentAggregate> agentAggregates = new Dictionary<int, AgentAggregate>();

    private string agentsPath;
    private string areasPath;

    private int globalEpisodes;
    private float globalRewardTotal;

    private void Awake()
    {
        if (Current != null && Current != this)
        {
            Debug.LogError("Plusieurs composants PinballDiagnostics sont présents.", this);
            enabled = false;
            return;
        }

        Current = this;

        if (!exportCsv)
        {
            return;
        }

        string directory = Path.Combine(Application.persistentDataPath, "PinballDiagnostics");
        Directory.CreateDirectory(directory);

        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string filePrefix = runId + "_" + timestamp;

        agentsPath = Path.Combine(directory, filePrefix + "_agents.csv");
        areasPath = Path.Combine(directory, filePrefix + "_areas.csv");

        List<string> agentHeaders = new List<string>
        {
            "run_id", "utc", "agent_id", "area_id", "side", "episode",
            "duration_s", "end_reason", "activations",
            "activations_with_contact", "activations_without_contact",
            "active_seconds", "max_continuous_active_seconds",
            "own_contacts", "other_contacts", "target_hits",
            "rejected_requests", "first_decision_delay_s",
            "first_shot_contact_delay_s", "target_distance_at_contact",
            "closest_target_distance", "reward_total",
            "ml_cumulative_reward", "agent_average_reward",
            "global_average_reward"
        };

        foreach (PinballRewardType rewardType in Enum.GetValues(typeof(PinballRewardType)))
        {
            agentHeaders.Add(rewardType + "_count");
            agentHeaders.Add(rewardType + "_total");
        }

        File.WriteAllText(agentsPath, string.Join(";", agentHeaders) + Environment.NewLine);
        File.WriteAllText(areasPath, "run_id;utc;area_id;episode;duration_s;end_reason;target_hits;drains;reset_zones;other_sides" + Environment.NewLine);

        Debug.Log($"Diagnostics Unity : {directory}");
    }

    private void OnDestroy()
    {
        foreach (AreaEntry entry in areas.Values)
        {
            if (entry.Area == null)
            {
                continue;
            }

            entry.Area.onBallHitTarget -= entry.TargetHandler;
            entry.Area.onBallEnterBound -= entry.BoundHandler;
            entry.Area.onEpisodeEnd -= entry.EpisodeHandler;
        }

        areas.Clear();

        if (Current == this)
        {
            Current = null;
        }
    }

    public string RegisterArea(TrainingPinballArea trainingPinball)
    {
        if (trainingPinball == null)
        {
            return "None";
        }

        if (areas.TryGetValue(trainingPinball, out AreaEntry existingEntry))
        {
            return existingEntry.Id;
        }

        AreaEntry entry = new AreaEntry
        {
            Area = trainingPinball,
            Id = "Area" + (areas.Count + 1)
        };

        entry.TargetHandler = () => entry.Targets++;

        entry.BoundHandler = boundType =>
        {
            switch (boundType)
            {
                case BoundTrigger.BoundType.Drain:
                    entry.Drains++;
                    break;

                case BoundTrigger.BoundType.ResetZone:
                    entry.ResetZones++;
                    break;

                case BoundTrigger.BoundType.OtherSide:
                    entry.OtherSides++;
                    break;
            }
        };

        entry.EpisodeHandler = () => RecordAreaEpisode(entry);

        trainingPinball.onBallHitTarget += entry.TargetHandler;
        trainingPinball.onBallEnterBound += entry.BoundHandler;
        trainingPinball.onEpisodeEnd += entry.EpisodeHandler;

        areas.Add(trainingPinball, entry);

        return entry.Id;
    }

    public void LogReward(int agentId, int episodeId, PinballRewardType rewardType, float rewardValue, float cumulativeReward)
    {
        if (!logEachReward)
        {
            return;
        }

        Debug.Log($"[Agent {agentId}] Épisode {episodeId} : {rewardType} = {Format(rewardValue)} ; cumul = {Format(cumulativeReward)}");
    }

    public void RecordAgentEpisode(PinballAgentEpisodeStats stats, float duration, float cumulativeReward, string endReason)
    {
        globalEpisodes++;
        globalRewardTotal += cumulativeReward;

        if (!agentAggregates.TryGetValue(stats.AgentId, out AgentAggregate aggregate))
        {
            aggregate = new AgentAggregate();
            agentAggregates.Add(stats.AgentId, aggregate);
        }

        aggregate.Episodes++;
        aggregate.TotalReward += cumulativeReward;

        float agentAverage = aggregate.TotalReward / aggregate.Episodes;
        float globalAverage = globalRewardTotal / globalEpisodes;

        if (logEveryEpisode || globalEpisodes % summaryEveryAgentEpisodes == 0)
        {
            Debug.Log($"[Agent {stats.AgentId}] Épisode {stats.EpisodeId} : résultat = {endReason}, récompense = {Format(cumulativeReward)}, moyenne agent = {Format(agentAverage)}, moyenne globale = {Format(globalAverage)}, activations = {stats.Activations}, cibles = {stats.TargetHits}.");
        }

        if (!exportCsv || string.IsNullOrEmpty(agentsPath))
        {
            return;
        }

        List<string> fields = new List<string>
        {
            runId,
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            stats.AgentId.ToString(CultureInfo.InvariantCulture),
            stats.AreaId,
            stats.Side,
            stats.EpisodeId.ToString(CultureInfo.InvariantCulture),
            Format(duration),
            endReason,
            stats.Activations.ToString(CultureInfo.InvariantCulture),
            stats.ActivationsWithContact.ToString(CultureInfo.InvariantCulture),
            stats.ActivationsWithoutContact.ToString(CultureInfo.InvariantCulture),
            Format(stats.ActiveSeconds),
            Format(stats.MaxContinuousActiveSeconds),
            stats.OwnContacts.ToString(CultureInfo.InvariantCulture),
            stats.OtherContacts.ToString(CultureInfo.InvariantCulture),
            stats.TargetHits.ToString(CultureInfo.InvariantCulture),
            stats.RejectedRequests.ToString(CultureInfo.InvariantCulture),
            Format(stats.FirstDecisionDelaySeconds),
            Format(stats.FirstShotContactDelaySeconds),
            Format(stats.TargetDistanceAtContact),
            Format(stats.ClosestTargetDistance),
            Format(stats.RewardTotal),
            Format(cumulativeReward),
            Format(agentAverage),
            Format(globalAverage)
        };

        foreach (PinballRewardType rewardType in Enum.GetValues(typeof(PinballRewardType)))
        {
            fields.Add(stats.GetRewardCount(rewardType).ToString(CultureInfo.InvariantCulture));
            fields.Add(Format(stats.GetRewardTotal(rewardType)));
        }

        File.AppendAllText(agentsPath, string.Join(";", fields) + Environment.NewLine);
    }

    private void RecordAreaEpisode(AreaEntry entry)
    {
        float duration = Mathf.Max(0f, Time.fixedTime - entry.Area.EpisodeBeginTimestamp);
        string endReason = entry.Area.LastEpisodeEndReason.ToString();

        if (exportCsv && !string.IsNullOrEmpty(areasPath))
        {
            string[] fields =
            {
                runId,
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                entry.Id,
                entry.Episode.ToString(CultureInfo.InvariantCulture),
                Format(duration),
                endReason,
                entry.Targets.ToString(CultureInfo.InvariantCulture),
                entry.Drains.ToString(CultureInfo.InvariantCulture),
                entry.ResetZones.ToString(CultureInfo.InvariantCulture),
                entry.OtherSides.ToString(CultureInfo.InvariantCulture)
            };

            File.AppendAllText(areasPath, string.Join(";", fields) + Environment.NewLine);
        }

        entry.Episode++;
        entry.Targets = 0;
        entry.Drains = 0;
        entry.ResetZones = 0;
        entry.OtherSides = 0;
    }

    private static string Format(float value)
    {
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }
}