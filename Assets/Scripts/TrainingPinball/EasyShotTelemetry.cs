using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using static Flipper;

public class EasyShotTelemetry : MonoBehaviour
{
    [SerializeField] private TrainingPinballArea trainingPinball;
    [SerializeField] private string modelId = "SingleFlipper_2_8_1";

    private string csvPath;

    private int episodeIndex;
    private int pulseCount;
    private int contactCount;

    private bool wasPressed;

    private float firstPulseTime = -1f;
    private float firstContactTime = -1f;
    private float minimumDistanceAfterContact = float.PositiveInfinity;

    private void Start()
    {
        if (trainingPinball == null || trainingPinball.LeftFlipper == null)
        {
            Debug.LogError("Attribue un plateau Easy valide au diagnostic des tirs.", this);
            enabled = false;
            return;
        }

        string directory = Path.Combine(Application.persistentDataPath, "PinballDiagnostics");
        Directory.CreateDirectory(directory);

        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        csvPath = Path.Combine(directory, modelId + "_" + timestamp + "_target_audit.csv");

        File.WriteAllText(csvPath, "episode;end_reason;target_x;target_y;pulses;contacts;first_contact_minus_pulse_s;closest_target_distance" + Environment.NewLine);

        trainingPinball.LeftFlipper.onBallEnter += HandleBallContact;
        trainingPinball.onEpisodeEnd += HandleEpisodeEnd;

        Debug.Log($"Diagnostic des cibles : {csvPath}");
    }

    private void FixedUpdate()
    {
        if (trainingPinball == null || !trainingPinball.HasBegunEpisode || trainingPinball.Ball == null || trainingPinball.Target == null)
        {
            return;
        }

        bool isPressed = trainingPinball.IsButtonPressed(FlipperSide.Left);

        if (isPressed && !wasPressed)
        {
            pulseCount++;

            if (firstPulseTime < 0f)
            {
                firstPulseTime = Time.fixedTime;
            }
        }

        wasPressed = isPressed;

        if (firstContactTime >= 0f)
        {
            float distance = Vector2.Distance(trainingPinball.Ball.position, trainingPinball.Target.transform.position);
            minimumDistanceAfterContact = Mathf.Min(minimumDistanceAfterContact, distance);
        }
    }

    private void HandleBallContact(FlipperSide flipperSide)
    {
        if (flipperSide != FlipperSide.Left)
        {
            return;
        }

        contactCount++;

        if (firstContactTime < 0f)
        {
            firstContactTime = Time.fixedTime;
            minimumDistanceAfterContact = Vector2.Distance(trainingPinball.Ball.position, trainingPinball.Target.transform.position);
        }
    }

    private void HandleEpisodeEnd()
    {
        if (trainingPinball.Target == null)
        {
            return;
        }

        Vector2 targetPosition = trainingPinball.Target.transform.position;

        string contactDelay = firstPulseTime >= 0f && firstContactTime >= 0f ? Format(firstContactTime - firstPulseTime) : "";
        string minimumDistance = float.IsPositiveInfinity(minimumDistanceAfterContact) ? "" : Format(minimumDistanceAfterContact);

        string[] fields =
        {
            episodeIndex.ToString(CultureInfo.InvariantCulture),
            trainingPinball.LastEpisodeEndReason.ToString(),
            Format(targetPosition.x),
            Format(targetPosition.y),
            pulseCount.ToString(CultureInfo.InvariantCulture),
            contactCount.ToString(CultureInfo.InvariantCulture),
            contactDelay,
            minimumDistance
        };

        File.AppendAllText(csvPath, string.Join(";", fields) + Environment.NewLine);

        episodeIndex++;
        pulseCount = 0;
        contactCount = 0;
        wasPressed = false;
        firstPulseTime = -1f;
        firstContactTime = -1f;
        minimumDistanceAfterContact = float.PositiveInfinity;
    }

    private void OnDestroy()
    {
        if (trainingPinball == null)
        {
            return;
        }

        if (trainingPinball.LeftFlipper != null)
        {
            trainingPinball.LeftFlipper.onBallEnter -= HandleBallContact;
        }

        trainingPinball.onEpisodeEnd -= HandleEpisodeEnd;
    }

    private static string Format(float number)
    {
        return number.ToString("0.######", CultureInfo.InvariantCulture);
    }
}