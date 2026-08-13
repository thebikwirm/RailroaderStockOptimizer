using HarmonyLib;
using Model;
using RollingStock;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityModManagerNet;

namespace RailroaderStockOptimizer
{
    public static class Main
    {
        public static UnityModManager.ModEntry ModEntry;
        public static Settings Settings;
        public static bool Enabled;

        private static float _refreshTimer;
        private static float _playerScanTimer;
        private static float _lastDebugLogTime;
        private static GameObject _overlayObject;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            ModEntry = modEntry;
            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry) ?? new Settings();
            RepairBubbleTestSettings();

            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnUpdate = OnUpdate;

            Log("Loaded.");
            return true;
        }

        private static void RepairBubbleTestSettings()
        {
            if (Settings == null) return;
            Settings.EnablePrecisionWatchdog = true;
            Settings.PrecisionDryRunOnly = false;
            Settings.ShowPrecisionDetailsInOverlay = true;
            Settings.EnableConsistDryRun = true;
            Settings.EnableRigidbodySnapshotDryRun = true;
            Settings.EnableHandoffEligibilityDryRun = true;
            Settings.EnablePendingHandoffDryRun = true;
            Settings.EnableAutomaticHandoff = true;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;
            if (value)
            {
                PerfManager.Reset();
                EnsureOverlay();
                Log("Enabled.");
            }
            else
            {
                PerfManager.RestoreAll();
                DestroyOverlay();
                Log("Disabled.");
            }
            return true;
        }

        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            GUILayout.Label("<b>Railroader Stock Optimizer</b>");
            Settings.EnableOverlay = GUILayout.Toggle(Settings.EnableOverlay, "Enable overlay");
            Settings.EnableSleep = GUILayout.Toggle(Settings.EnableSleep, "Allow sleeping distant rigidbodies");
            Settings.RequireStationaryForSleep = GUILayout.Toggle(Settings.RequireStationaryForSleep, "Require stationary for sleep");
            Settings.UseVisibilityCheck = GUILayout.Toggle(Settings.UseVisibilityCheck, "Use renderer visibility");

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            Settings.EnableDebugLogging = GUILayout.Toggle(Settings.EnableDebugLogging, string.Empty, GUILayout.Width(24f));
            GUILayout.Label(Settings.EnableDebugLogging ? "Debug logging: ON" : "Debug logging: OFF");
            GUILayout.EndHorizontal();
            if (Settings.EnableDebugLogging)
            {
                GUILayout.Label($"Debug Log Interval: {Settings.DebugLogInterval:F1} sec");
                Settings.DebugLogInterval = GUILayout.HorizontalSlider(Settings.DebugLogInterval, 1f, 30f);
            }

            GUILayout.Space(8f);
            GUILayout.Label($"Hot Radius: {Settings.HotRadius:F0} m");
            Settings.HotRadius = GUILayout.HorizontalSlider(Settings.HotRadius, 50f, 1000f);
            GUILayout.Label($"Warm Radius: {Settings.WarmRadius:F0} m");
            Settings.WarmRadius = GUILayout.HorizontalSlider(Settings.WarmRadius, 100f, 2000f);
            GUILayout.Label($"Cold Radius: {Settings.ColdRadius:F0} m");
            Settings.ColdRadius = GUILayout.HorizontalSlider(Settings.ColdRadius, 300f, 5000f);
            GUILayout.Label($"Stationary Speed Threshold: {Settings.StationarySpeedThreshold:F3}");
            Settings.StationarySpeedThreshold = GUILayout.HorizontalSlider(Settings.StationarySpeedThreshold, 0.001f, 1.0f);
            GUILayout.Label($"Freeze Delay: {Settings.FreezeDelaySeconds:F1} sec");
            Settings.FreezeDelaySeconds = GUILayout.HorizontalSlider(Settings.FreezeDelaySeconds, 1f, 60f);
            GUILayout.Label($"Batch Fraction: 1/{Settings.BatchDivider}");
            Settings.BatchDivider = Mathf.RoundToInt(GUILayout.HorizontalSlider(Settings.BatchDivider, 1f, 30f));
            GUILayout.Label($"Full Refresh Interval: {Settings.FullRefreshInterval:F1} sec");
            Settings.FullRefreshInterval = GUILayout.HorizontalSlider(Settings.FullRefreshInterval, 0.2f, 10f);
            GUILayout.Label($"Player Refresh Interval: {Settings.PlayerRefreshInterval:F1} sec");
            Settings.PlayerRefreshInterval = GUILayout.HorizontalSlider(Settings.PlayerRefreshInterval, 0.1f, 5f);

            GUILayout.Space(8f);
            GUILayout.Label("<b>Precision / Physics Bubble Tests</b>");
            Settings.EnablePrecisionWatchdog = GUILayout.Toggle(Settings.EnablePrecisionWatchdog, "Enable precision watchdog");
            Settings.PrecisionDryRunOnly = GUILayout.Toggle(Settings.PrecisionDryRunOnly, "Dry-run only: do not move trains");
            Settings.ShowPrecisionDetailsInOverlay = GUILayout.Toggle(Settings.ShowPrecisionDetailsInOverlay, "Show precision details in overlay");
            Settings.EnableConsistDryRun = GUILayout.Toggle(Settings.EnableConsistDryRun, "Enable coupled-consist planning");
            Settings.EnableRigidbodySnapshotDryRun = GUILayout.Toggle(Settings.EnableRigidbodySnapshotDryRun, "Enable rigidbody snapshot");
            Settings.EnableHandoffEligibilityDryRun = GUILayout.Toggle(Settings.EnableHandoffEligibilityDryRun, "Enable handoff eligibility gate");
            Settings.EnablePendingHandoffDryRun = GUILayout.Toggle(Settings.EnablePendingHandoffDryRun, "Enable pending handoff queue");
            Settings.EnableAutomaticHandoff = GUILayout.Toggle(Settings.EnableAutomaticHandoff, "ENABLE AUTOMATIC STOPPED-CONSIST HANDOFF");

            GUILayout.Label($"Bubble Grid Size: {Settings.BubbleGridSize:F0} m");
            Settings.BubbleGridSize = GUILayout.HorizontalSlider(Settings.BubbleGridSize, 5000f, 50000f);
            GUILayout.Label($"Transfer Recommendation Distance: {Settings.PrecisionTransferDistance:F0} m");
            Settings.PrecisionTransferDistance = GUILayout.HorizontalSlider(Settings.PrecisionTransferDistance, 2000f, 75000f);
            GUILayout.Label($"Better Bubble Margin: {Settings.PrecisionBetterBubbleMargin:F0} m");
            Settings.PrecisionBetterBubbleMargin = GUILayout.HorizontalSlider(Settings.PrecisionBetterBubbleMargin, 500f, 20000f);
            GUILayout.Label($"Consist rebuild interval: {Settings.ConsistDryRunInterval:F1} sec");
            Settings.ConsistDryRunInterval = GUILayout.HorizontalSlider(Settings.ConsistDryRunInterval, 0.5f, 10f);
            GUILayout.Label($"Transfer plan hold time: {Settings.TransferPlanHoldSeconds:F0} sec");
            Settings.TransferPlanHoldSeconds = GUILayout.HorizontalSlider(Settings.TransferPlanHoldSeconds, 5f, 120f);
            GUILayout.Label($"Snapshot moving threshold: {Settings.SnapshotMovingSpeedThreshold:F3} m/s");
            Settings.SnapshotMovingSpeedThreshold = GUILayout.HorizontalSlider(Settings.SnapshotMovingSpeedThreshold, 0.001f, 2f);
            GUILayout.Label($"Handoff max moving cars: {Settings.HandoffMaxMovingCars}");
            Settings.HandoffMaxMovingCars = Mathf.RoundToInt(GUILayout.HorizontalSlider(Settings.HandoffMaxMovingCars, 0f, 10f));
            GUILayout.Label($"Handoff max ref delta: {Settings.HandoffMaxReferenceDelta:F1} m");
            Settings.HandoffMaxReferenceDelta = GUILayout.HorizontalSlider(Settings.HandoffMaxReferenceDelta, 1f, 100f);
            GUILayout.Label($"Handoff max target local distance: {Settings.HandoffMaxTargetLocalDistance:F0} m");
            Settings.HandoffMaxTargetLocalDistance = GUILayout.HorizontalSlider(Settings.HandoffMaxTargetLocalDistance, 1000f, 50000f);
            GUILayout.Label($"Auto handoff cooldown: {Settings.AutoHandoffCooldownSeconds:F1} sec");
            Settings.AutoHandoffCooldownSeconds = GUILayout.HorizontalSlider(Settings.AutoHandoffCooldownSeconds, 1f, 60f);
            GUILayout.Label($"Pending handoff retain time: {Settings.PendingHandoffRetainSeconds:F0} sec");
            Settings.PendingHandoffRetainSeconds = GUILayout.HorizontalSlider(Settings.PendingHandoffRetainSeconds, 30f, 600f);
            GUILayout.Label($"Pending handoff max shown: {Settings.PendingHandoffMaxShown}");
            Settings.PendingHandoffMaxShown = Mathf.RoundToInt(GUILayout.HorizontalSlider(Settings.PendingHandoffMaxShown, 1f, 12f));

            GUILayout.Space(8f);
            GUILayout.Label($"Tracked cars: {PerfManager.TrackedCount}");
            GUILayout.Label($"Hot: {PerfManager.HotCount}  Warm: {PerfManager.WarmCount}  Cold: {PerfManager.ColdCount}  Frozen: {PerfManager.FrozenCount}");
            GUILayout.Label($"Manager cost last pass: {PerfManager.LastPassMs:F3} ms");
            GUILayout.Label($"Auto handoff: {ConsistDryRun.AutoHandoffSummary}");
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            Settings.Save(modEntry);
        }

        private static void OnUpdate(UnityModManager.ModEntry modEntry, float deltaTime)
        {
            if (!Enabled) return;
            if (!Application.isPlaying) return;
            EnsureOverlay();
            _playerScanTimer += deltaTime;
            _refreshTimer += deltaTime;

            if (_playerScanTimer >= Settings.PlayerRefreshInterval)
            {
                _playerScanTimer = 0f;
                PerfManager.RefreshPlayerAnchor();
            }
            if (_refreshTimer >= Settings.FullRefreshInterval)
            {
                _refreshTimer = 0f;
                PerfManager.RefreshCars();
            }
            PerfManager.Tick(deltaTime);
        }

        private static void EnsureOverlay()
        {
            if (_overlayObject != null) return;
            try
            {
                _overlayObject = new GameObject("RailroaderStockOptimizerOverlay");
                UnityEngine.Object.DontDestroyOnLoad(_overlayObject);
                _overlayObject.hideFlags = HideFlags.HideAndDontSave;
                _overlayObject.AddComponent<OverlayBehaviour>();
            }
            catch (Exception ex)
            {
                Log($"Failed to create overlay: {ex}");
            }
        }

        private static void DestroyOverlay()
        {
            try
            {
                if (_overlayObject != null)
                {
                    UnityEngine.Object.Destroy(_overlayObject);
                    _overlayObject = null;
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to destroy overlay: {ex}");
            }
        }

        public static void Log(string msg)
        {
            ModEntry?.Logger.Log($"[RailroaderStockOptimizer] {msg}");
        }

        public static void DebugLog(string msg)
        {
            if (Settings == null || !Settings.EnableDebugLogging) return;
            Log("[Debug] " + msg);
        }

        public static void DebugLogThrottled(string msg)
        {
            if (Settings == null || !Settings.EnableDebugLogging) return;
            if (Time.realtimeSinceStartup - _lastDebugLogTime < Settings.DebugLogInterval) return;
            _lastDebugLogTime = Time.realtimeSinceStartup;
            Log("[Debug] " + msg);
        }
    }

    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        public bool EnableOverlay = true;
        public bool EnableSleep = false;
        public bool RequireStationaryForSleep = true;
        public bool UseVisibilityCheck = true;
        public bool EnableDebugLogging = false;
        public float DebugLogInterval = 5f;
        public float HotRadius = 300f;
        public float WarmRadius = 800f;
        public float ColdRadius = 1500f;
        public float StationarySpeedThreshold = 0.03f;
        public float FreezeDelaySeconds = 10f;
        public int BatchDivider = 10;
        public float FullRefreshInterval = 1.5f;
        public float PlayerRefreshInterval = 0.5f;

        public bool EnablePrecisionWatchdog = false;
        public bool PrecisionDryRunOnly = true;
        public bool ShowPrecisionDetailsInOverlay = true;
        public bool EnableConsistDryRun = true;
        public bool EnableRigidbodySnapshotDryRun = true;
        public bool EnableHandoffEligibilityDryRun = true;
        public bool EnablePendingHandoffDryRun = true;
        public bool EnableAutomaticHandoff = false;
        public float BubbleGridSize = 20000f;
        public float PrecisionWarningDistance = 10000f;
        public float PrecisionTransferDistance = 20000f;
        public float PrecisionEmergencyDistance = 30000f;
        public float PrecisionBetterBubbleMargin = 5000f;
        public float PrecisionOverlaySampleInterval = 2f;
        public float PrecisionOverlayWorstHoldSeconds = 10f;
        public float ConsistDryRunInterval = 2f;
        public float ConsistCachedPositionMaxAge = 60f;
        public float TransferPlanHoldSeconds = 30f;
        public float SnapshotMovingSpeedThreshold = 0.03f;
        public int HandoffMaxMovingCars = 0;
        public float HandoffMaxReferenceDelta = 25f;
        public float HandoffMaxTargetLocalDistance = 15000f;
        public float AutoHandoffCooldownSeconds = 5f;
        public float PendingHandoffRetainSeconds = 240f;
        public int PendingHandoffMaxShown = 6;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }

        public void OnChange() { }
    }

    public enum ActivityTier { Hot, Warm, Cold, Frozen }

    public struct Vector3d
    {
        public double X;
        public double Y;
        public double Z;
        public static readonly Vector3d Zero = new Vector3d(0.0, 0.0, 0.0);

        public Vector3d(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static Vector3d FromVector3(Vector3 value) { return new Vector3d(value.x, value.y, value.z); }
        public Vector3 ToVector3() { return new Vector3((float)X, (float)Y, (float)Z); }
        public static Vector3d operator +(Vector3d a, Vector3d b) { return new Vector3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static Vector3d operator -(Vector3d a, Vector3d b) { return new Vector3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public double Magnitude() { return Math.Sqrt(X * X + Y * Y + Z * Z); }
        public static double Distance(Vector3d a, Vector3d b) { return (a - b).Magnitude(); }
        public override string ToString() { return $"{X:F1}, {Y:F1}, {Z:F1}"; }
    }

    public sealed class PhysicsBubble
    {
        public string Id;
        public Vector3d GlobalOrigin;
        public PhysicsBubble(string id, Vector3d globalOrigin) { Id = id; GlobalOrigin = globalOrigin; }
        public Vector3 GlobalToLocal(Vector3d globalPosition) { return (globalPosition - GlobalOrigin).ToVector3(); }
    }

    public sealed class CarState
    {
        public string CarId;
        public Car Car;
        public GameObject GameObject;
        public Transform Transform;
        public Transform PhysicsTransform;
        public string PhysicsTransformSource;
        public Rigidbody Rigidbody;
        public Rigidbody PhysicsRigidbody;
        public string RigidbodySource;
        public Renderer[] Renderers;
        public ActivityTier Tier;
        public float LastDistance;
        public bool IsVisible;
        public bool IsMoving;
        public float LastMovingTime;
        public int LastProcessedFrame;
        public bool WasSleepingForced;
        public string BubbleId;
        public Vector3d BubbleOrigin;
        public Vector3d GlobalPosition;
        public bool HasPlanPosition;
        public Vector3 PlanPosition;
        public string PlanPositionSource;
        public float PlanPositionTime;
        public bool HasCachedPosition;
        public Vector3 CachedPosition;
        public string CachedPositionSource;
        public float CachedPositionTime;
        public double CachedLocalDistance;
        public double CachedFloatStepMeters;
        public string Name => GameObject != null ? GameObject.name : "<null>";
    }

    public sealed class RigidbodySnapshot
    {
        public string CarName;
        public bool HasTransform;
        public string TransformSource;
        public Vector3 TransformPosition;
        public Quaternion TransformRotation;
        public bool HasRigidbody;
        public string RigidbodySource;
        public Vector3 RigidbodyPosition;
        public Quaternion RigidbodyRotation;
        public Vector3 Velocity;
        public Vector3 AngularVelocity;
        public bool IsSleeping;
        public bool WasForcedSleeping;
        public bool IsMoving;
        public float Speed;
        public float AngularSpeed;
        public float DistanceToReferencePosition;
        public string ReferenceSource;
    }

    public sealed class HandoffEligibilityReport
    {
        public bool Ready;
        public string Summary = "none";
        public string Details = "none";
        public string BlockerText = "none";
        public int CachedCars;
        public int CoupledCars;
        public int RigidbodyCount;
        public int MissingRigidbodies;
        public int MovingCars;
        public int ForcedSleeping;
        public int FarPhysicsRefs;
        public double TargetLocalDistance;
    }

    public sealed class PendingHandoffRecord
    {
        public string Key;
        public string DisplayName;
        public string CurrentBubble;
        public string TargetBubble;
        public int CachedCars;
        public int CoupledCars;
        public double LocalDistance;
        public double TargetLocalDistance;
        public float FirstSeenTime;
        public float LastSeenTime;
        public bool Ready;
        public bool Applied;
        public string Status;
        public string Blockers;
    }

    public static class PrecisionWatchdog
    {
        public const string MainBubbleId = "MainBubble";
        public static int EvaluatedCount { get; private set; }
        public static int NonZeroSampleCount { get; private set; }
        public static int ZeroSampleCount { get; private set; }
        public static int WarningCount { get; private set; }
        public static int TransferRecommendedCount { get; private set; }
        public static int EmergencyCount { get; private set; }
        public static double WorstLocalDistance { get; private set; }
        public static double WorstFloatStepMeters { get; private set; }
        public static string LastRecommendation { get; private set; } = "none";
        public static string LastRecommendationStatus { get; private set; } = "none";
        public static string LastRecommendationOriginalText { get; private set; } = "none";
        public static string LastRecommendationCurrentText { get; private set; } = "none";
        public static string LastRecommendedCarName { get; private set; } = "none";
        public static float LastRecommendationAgeSeconds => _lastRecommendationSetTime > 0f ? Time.realtimeSinceStartup - _lastRecommendationSetTime : 0f;
        public static string DisplayCarName { get; private set; } = "waiting";
        public static string DisplayPositionSource { get; private set; } = "none";
        public static string DisplayChosenPositionText { get; private set; } = "none";
        public static float DisplaySampleAgeSeconds => _displaySampleSetTime > 0f ? Time.realtimeSinceStartup - _displaySampleSetTime : 0f;
        public static string LastNonZeroCarName { get; private set; } = "none";
        public static string LastNonZeroChosenPositionText { get; private set; } = "none";
        public static double LastNonZeroLocalDistance { get; private set; }
        public static double LastNonZeroFloatStepMeters { get; private set; }
        public static string HeldWorstCarName { get; private set; } = "none";
        public static string HeldWorstChosenPositionText { get; private set; } = "none";
        public static double HeldWorstLocalDistance { get; private set; }
        public static double HeldWorstFloatStepMeters { get; private set; }

        private static float _lastRecommendationSetTime;
        private static float _displaySampleSetTime;
        private static float _nextDisplaySampleTime;
        private static float _heldWorstExpireTime;

        public static void Reset()
        {
            EvaluatedCount = NonZeroSampleCount = ZeroSampleCount = WarningCount = TransferRecommendedCount = EmergencyCount = 0;
            WorstLocalDistance = WorstFloatStepMeters = 0.0;
            LastRecommendation = LastRecommendationStatus = LastRecommendationOriginalText = LastRecommendationCurrentText = "none";
            LastRecommendedCarName = "none";
            DisplayCarName = "waiting";
            DisplayPositionSource = DisplayChosenPositionText = "none";
            LastNonZeroCarName = HeldWorstCarName = "none";
            LastNonZeroChosenPositionText = HeldWorstChosenPositionText = "none";
            LastNonZeroLocalDistance = LastNonZeroFloatStepMeters = HeldWorstLocalDistance = HeldWorstFloatStepMeters = 0.0;
            _lastRecommendationSetTime = _displaySampleSetTime = _nextDisplaySampleTime = _heldWorstExpireTime = 0f;
        }

        public static void BeginBatch()
        {
            EvaluatedCount = NonZeroSampleCount = ZeroSampleCount = WarningCount = TransferRecommendedCount = EmergencyCount = 0;
            WorstLocalDistance = WorstFloatStepMeters = 0.0;
        }

        public static void Evaluate(CarState state)
        {
            if (state == null || Main.Settings == null) return;
            if (string.IsNullOrEmpty(state.BubbleId)) { state.BubbleId = MainBubbleId; state.BubbleOrigin = Vector3d.Zero; }

            Vector3 local;
            string source;
            if (!ResolveBestLocalPosition(state, out local, out source))
            {
                EvaluatedCount++;
                ZeroSampleCount++;
                return;
            }

            Vector3d global = Vector3d.FromVector3(local) + state.BubbleOrigin;
            state.GlobalPosition = global;
            state.HasCachedPosition = true;
            state.CachedPosition = local;
            state.CachedPositionSource = source;
            state.CachedPositionTime = Time.realtimeSinceStartup;

            double localDistance = Vector3d.Distance(global, state.BubbleOrigin);
            double localMagnitude = Math.Max(Math.Abs(global.X - state.BubbleOrigin.X), Math.Abs(global.Z - state.BubbleOrigin.Z));
            double floatStep = EstimateFloatStepMeters(localMagnitude);
            state.CachedLocalDistance = localDistance;
            state.CachedFloatStepMeters = floatStep;

            EvaluatedCount++;
            NonZeroSampleCount++;
            if (localDistance >= Main.Settings.PrecisionWarningDistance) WarningCount++;
            if (localDistance >= Main.Settings.PrecisionEmergencyDistance) EmergencyCount++;
            if (localDistance > WorstLocalDistance || EvaluatedCount == 1) { WorstLocalDistance = localDistance; WorstFloatStepMeters = floatStep; }

            if (Time.realtimeSinceStartup >= _nextDisplaySampleTime)
            {
                DisplayCarName = state.Name;
                DisplayPositionSource = source;
                DisplayChosenPositionText = FormatVector(local);
                _displaySampleSetTime = Time.realtimeSinceStartup;
                _nextDisplaySampleTime = Time.realtimeSinceStartup + Mathf.Max(0.25f, Main.Settings.PrecisionOverlaySampleInterval);
            }

            LastNonZeroCarName = state.Name;
            LastNonZeroChosenPositionText = FormatVector(local);
            LastNonZeroLocalDistance = localDistance;
            LastNonZeroFloatStepMeters = floatStep;
            UpdateHeldWorst(state.Name, local, localDistance, floatStep);

            PhysicsBubble best = FindBestBubble(global);
            double bestDistance = Vector3d.Distance(global, best.GlobalOrigin);
            bool recommend = localDistance >= Main.Settings.PrecisionTransferDistance && bestDistance <= localDistance - Main.Settings.PrecisionBetterBubbleMargin && !string.Equals(best.Id, state.BubbleId, StringComparison.OrdinalIgnoreCase);
            if (recommend)
            {
                TransferRecommendedCount++;
                LastRecommendedCarName = state.Name;
                _lastRecommendationSetTime = Time.realtimeSinceStartup;
                LastRecommendationStatus = "active: still recommends transfer";
                LastRecommendationOriginalText = $"{state.BubbleId} -> {best.Id}, global {global}, local {localDistance:F0}m -> {bestDistance:F0}m";
                LastRecommendationCurrentText = LastRecommendationOriginalText;
                LastRecommendation = $"{state.Name}: {state.BubbleId} -> {best.Id}, local {localDistance:F0}m -> {bestDistance:F0}m, source {source}, float step {floatStep * 1000.0:F3}mm";
                ConsistDryRun.RequestImmediateRebuild("new active individual recommendation: " + state.Name);
            }
        }

        private static void UpdateHeldWorst(string carName, Vector3 pos, double localDistance, double floatStep)
        {
            bool expired = Time.realtimeSinceStartup >= _heldWorstExpireTime;
            bool worse = localDistance > HeldWorstLocalDistance;
            if (!expired && !worse) return;
            HeldWorstCarName = carName;
            HeldWorstChosenPositionText = FormatVector(pos);
            HeldWorstLocalDistance = localDistance;
            HeldWorstFloatStepMeters = floatStep;
            _heldWorstExpireTime = Time.realtimeSinceStartup + Mathf.Max(1f, Main.Settings != null ? Main.Settings.PrecisionOverlayWorstHoldSeconds : 10f);
        }

        public static void UpdateLastRecommendationStatus(IReadOnlyList<CarState> states) { }

        public static bool ResolveBestLocalPosition(CarState state, out Vector3 local, out string source)
        {
            local = Vector3.zero;
            source = "none";
            if (state == null) return false;
            Transform t;
            string ts;
            Rigidbody rb;
            string rs;
            CarPhysicsResolver.Resolve(state, state.HasCachedPosition ? state.CachedPosition : Vector3.zero, out t, out ts, out rb, out rs);
            state.PhysicsTransform = t;
            state.PhysicsTransformSource = ts;
            state.PhysicsRigidbody = rb;
            state.Rigidbody = rb;
            state.RigidbodySource = rs;
            if (rb != null && IsMeaningfullyNonZero(rb.position)) { local = rb.position; source = "Rigidbody:" + rs; return true; }
            if (t != null && IsMeaningfullyNonZero(t.position)) { local = t.position; source = "Transform:" + ts; return true; }
            Vector3 center;
            if (TryGetRendererBoundsCenter(state, out center) && IsMeaningfullyNonZero(center)) { local = center; source = "RendererBounds"; return true; }
            return false;
        }

        public static bool TryGetRendererBoundsCenter(CarState state, out Vector3 center)
        {
            center = Vector3.zero;
            if (state == null || state.Renderers == null || state.Renderers.Length == 0) return false;
            bool hasBounds = false;
            Bounds combined = new Bounds();
            for (int i = 0; i < state.Renderers.Length; i++)
            {
                Renderer renderer = state.Renderers[i];
                if (renderer == null) continue;
                try { if (!hasBounds) { combined = renderer.bounds; hasBounds = true; } else combined.Encapsulate(renderer.bounds); } catch { }
            }
            if (!hasBounds) return false;
            center = combined.center;
            return true;
        }

        public static bool IsMeaningfullyNonZero(Vector3 value) { return value.sqrMagnitude > 0.25f; }
        public static string FormatVector(Vector3 value) { return $"{value.x:F1}, {value.y:F1}, {value.z:F1}"; }
        public static PhysicsBubble FindBestBubble(Vector3d globalPosition)
        {
            float rawGrid = Main.Settings != null ? Main.Settings.BubbleGridSize : 20000f;
            double grid = Math.Max(1000.0, rawGrid);
            double originX = Math.Round(globalPosition.X / grid) * grid;
            double originZ = Math.Round(globalPosition.Z / grid) * grid;
            return new PhysicsBubble($"GridBubble[{originX:F0},{originZ:F0}]", new Vector3d(originX, 0.0, originZ));
        }
        public static double EstimateFloatStepMeters(double magnitude)
        {
            magnitude = Math.Abs(magnitude);
            if (magnitude <= 0.0) return 0.0;
            double exponent = Math.Floor(Math.Log(magnitude, 2.0));
            return Math.Pow(2.0, exponent - 23.0);
        }
    }

    public static class CarPhysicsResolver
    {
        public static void Resolve(CarState state, Vector3 referencePosition, out Transform transform, out string transformSource, out Rigidbody rb, out string rbSource)
        {
            transform = null;
            transformSource = "none";
            rb = null;
            rbSource = "none";
            if (state == null) return;
            List<RigidCandidate> rigidCandidates = new List<RigidCandidate>();
            AddRigid(rigidCandidates, state.Rigidbody, "state/root");
            if (state.GameObject != null)
            {
                AddRigid(rigidCandidates, state.GameObject.GetComponent<Rigidbody>(), "root");
                Rigidbody[] childBodies = state.GameObject.GetComponentsInChildren<Rigidbody>(true);
                for (int i = 0; i < childBodies.Length; i++) AddRigid(rigidCandidates, childBodies[i], "child");
                AddRigid(rigidCandidates, state.GameObject.GetComponentInParent<Rigidbody>(), "parent");
            }
            AddReflectionRigidbodies(rigidCandidates, state.Car);
            rb = PickBestRigidbody(rigidCandidates, referencePosition, out rbSource);
            transform = ResolveTransform(state, referencePosition, out transformSource);
            if (transform == null && rb != null) { transform = rb.transform; transformSource = "rigidbody"; }
        }

        private static void AddRigid(List<RigidCandidate> candidates, Rigidbody rb, string source)
        {
            if (rb == null) return;
            for (int i = 0; i < candidates.Count; i++) if (candidates[i].Body == rb) return;
            candidates.Add(new RigidCandidate { Body = rb, Source = source });
        }

        private static void AddReflectionRigidbodies(List<RigidCandidate> candidates, object obj)
        {
            if (obj == null || candidates == null) return;
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            Type type = obj.GetType();
            FieldInfo[] fields = type.GetFields(flags);
            for (int i = 0; i < fields.Length; i++) { try { AddObjectRigidbodies(candidates, fields[i].GetValue(obj), "field:" + fields[i].Name); } catch { } }
            PropertyInfo[] props = type.GetProperties(flags);
            for (int i = 0; i < props.Length; i++)
            {
                if (props[i].GetIndexParameters().Length != 0) continue;
                try { AddObjectRigidbodies(candidates, props[i].GetValue(obj, null), "prop:" + props[i].Name); } catch { }
            }
        }

        private static void AddObjectRigidbodies(List<RigidCandidate> candidates, object value, string source)
        {
            Rigidbody body = value as Rigidbody;
            if (body != null) { AddRigid(candidates, body, source); return; }
            Component component = value as Component;
            if (component != null)
            {
                AddRigid(candidates, component.GetComponent<Rigidbody>(), source + ".component");
                AddRigid(candidates, component.GetComponentInChildren<Rigidbody>(true), source + ".child");
                AddRigid(candidates, component.GetComponentInParent<Rigidbody>(), source + ".parent");
                return;
            }
            GameObject go = value as GameObject;
            if (go != null)
            {
                AddRigid(candidates, go.GetComponent<Rigidbody>(), source + ".gameObject");
                AddRigid(candidates, go.GetComponentInChildren<Rigidbody>(true), source + ".child");
                AddRigid(candidates, go.GetComponentInParent<Rigidbody>(), source + ".parent");
            }
        }

        private static Rigidbody PickBestRigidbody(List<RigidCandidate> candidates, Vector3 referencePosition, out string source)
        {
            source = "none";
            bool hasReference = PrecisionWatchdog.IsMeaningfullyNonZero(referencePosition);
            Rigidbody best = null;
            float bestScore = float.MaxValue;
            string bestSource = "none";
            for (int i = 0; i < candidates.Count; i++)
            {
                RigidCandidate c = candidates[i];
                if (c == null || c.Body == null) continue;
                float score = hasReference ? (c.Body.position - referencePosition).sqrMagnitude : i;
                if (best == null || score < bestScore) { best = c.Body; bestScore = score; bestSource = c.Source; }
            }
            source = bestSource;
            return best;
        }

        private static Transform ResolveTransform(CarState state, Vector3 referencePosition, out string source)
        {
            source = "none";
            List<TransformCandidate> candidates = new List<TransformCandidate>();
            if (state != null)
            {
                AddTransform(candidates, state.Transform, "root");
                if (state.Renderers != null) for (int i = 0; i < state.Renderers.Length; i++) if (state.Renderers[i] != null) AddTransform(candidates, state.Renderers[i].transform, "renderer");
                AddReflectionTransforms(candidates, state.Car);
            }
            return PickBestTransform(candidates, referencePosition, out source);
        }

        private static void AddReflectionTransforms(List<TransformCandidate> candidates, object obj)
        {
            if (obj == null || candidates == null) return;
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            Type type = obj.GetType();
            FieldInfo[] fields = type.GetFields(flags);
            for (int i = 0; i < fields.Length; i++) { try { AddObjectTransforms(candidates, fields[i].GetValue(obj), "field:" + fields[i].Name); } catch { } }
            PropertyInfo[] props = type.GetProperties(flags);
            for (int i = 0; i < props.Length; i++)
            {
                if (props[i].GetIndexParameters().Length != 0) continue;
                try { AddObjectTransforms(candidates, props[i].GetValue(obj, null), "prop:" + props[i].Name); } catch { }
            }
        }

        private static void AddObjectTransforms(List<TransformCandidate> candidates, object value, string source)
        {
            Transform t = value as Transform;
            if (t != null) { AddTransform(candidates, t, source); return; }
            Component c = value as Component;
            if (c != null) { AddTransform(candidates, c.transform, source + ".component"); return; }
            GameObject go = value as GameObject;
            if (go != null) AddTransform(candidates, go.transform, source + ".gameObject");
        }

        private static void AddTransform(List<TransformCandidate> candidates, Transform transform, string source)
        {
            if (transform == null) return;
            for (int i = 0; i < candidates.Count; i++) if (candidates[i].Transform == transform) return;
            candidates.Add(new TransformCandidate { Transform = transform, Source = source });
        }

        private static Transform PickBestTransform(List<TransformCandidate> candidates, Vector3 referencePosition, out string source)
        {
            source = "none";
            bool hasReference = PrecisionWatchdog.IsMeaningfullyNonZero(referencePosition);
            Transform best = null;
            float bestScore = float.MaxValue;
            string bestSource = "none";
            for (int i = 0; i < candidates.Count; i++)
            {
                TransformCandidate c = candidates[i];
                if (c == null || c.Transform == null) continue;
                float score = hasReference ? (c.Transform.position - referencePosition).sqrMagnitude : i;
                if (best == null || score < bestScore) { best = c.Transform; bestScore = score; bestSource = c.Source; }
            }
            source = bestSource;
            return best;
        }

        private sealed class RigidCandidate { public Rigidbody Body; public string Source; }
        private sealed class TransformCandidate { public Transform Transform; public string Source; }
    }

    public static class HandoffPositionResolver
    {
        public static bool TryResolvePlanPosition(CarState state, float now, out Vector3 localPosition, out string source)
        {
            localPosition = Vector3.zero;
            source = "none";
            if (!PrecisionWatchdog.ResolveBestLocalPosition(state, out localPosition, out source)) return false;
            state.HasPlanPosition = true;
            state.PlanPosition = localPosition;
            state.PlanPositionSource = source;
            state.PlanPositionTime = now;
            state.HasCachedPosition = true;
            state.CachedPosition = localPosition;
            state.CachedPositionSource = source;
            state.CachedPositionTime = now;
            return true;
        }
    }

    public static class PendingHandoffQueue
    {
        private static readonly List<PendingHandoffRecord> _items = new List<PendingHandoffRecord>();
        public static string Summary { get; private set; } = "none";
        public static string Details { get; private set; } = "none";

        public static void Reset()
        {
            _items.Clear();
            Summary = "none";
            Details = "none";
        }

        public static void UpdateFromPlan(string key, string displayName, int cachedCars, int coupledCars, string currentBubble, string targetBubble, double localDistance, double targetLocalDistance, HandoffEligibilityReport eligibility)
        {
            if (Main.Settings == null || !Main.Settings.EnablePendingHandoffDryRun || string.IsNullOrEmpty(key)) return;
            float now = Time.realtimeSinceStartup;
            PendingHandoffRecord record = Find(key);
            if (record == null) { record = new PendingHandoffRecord { Key = key, FirstSeenTime = now }; _items.Add(record); }
            record.DisplayName = string.IsNullOrEmpty(displayName) ? key : displayName;
            record.CachedCars = cachedCars;
            record.CoupledCars = coupledCars;
            record.CurrentBubble = currentBubble;
            record.TargetBubble = targetBubble;
            record.LocalDistance = localDistance;
            record.TargetLocalDistance = targetLocalDistance;
            record.LastSeenTime = now;
            record.Ready = eligibility != null && eligibility.Ready;
            record.Blockers = eligibility != null ? eligibility.BlockerText : "unknown";
            if (!record.Applied) record.Status = record.Ready ? "READY - automatic handoff eligible" : "waiting: " + record.Blockers;
            RefreshSummary();
        }

        public static void MarkApplied(string key, string status)
        {
            PendingHandoffRecord record = Find(key);
            if (record == null) return;
            record.Applied = true;
            record.Ready = false;
            record.Status = status;
            record.LastSeenTime = Time.realtimeSinceStartup;
            RefreshSummary();
        }

        public static void RefreshSummary()
        {
            if (Main.Settings == null || !Main.Settings.EnablePendingHandoffDryRun) { Summary = "disabled"; Details = "none"; return; }
            float now = Time.realtimeSinceStartup;
            float retain = Mathf.Max(10f, Main.Settings.PendingHandoffRetainSeconds);
            for (int i = _items.Count - 1; i >= 0; i--) if (now - _items[i].LastSeenTime > retain) _items.RemoveAt(i);
            if (_items.Count == 0) { Summary = "none"; Details = "none"; return; }
            int ready = 0, waiting = 0, applied = 0;
            for (int i = 0; i < _items.Count; i++) { if (_items[i].Applied) applied++; else if (_items[i].Ready) ready++; else waiting++; }
            Summary = $"pending {_items.Count}, ready {ready}, waiting {waiting}, applied {applied}";
            List<string> lines = new List<string>();
            lines.Add("Pending handoff queue");
            lines.Add("Running trains are queued. Stopped eligible trains are moved automatically when auto handoff is enabled.");
            int max = Mathf.Max(1, Main.Settings.PendingHandoffMaxShown);
            int count = Mathf.Min(max, _items.Count);
            for (int i = 0; i < count; i++)
            {
                PendingHandoffRecord r = _items[i];
                lines.Add($"{i + 1}. {r.DisplayName}");
                lines.Add($"   {r.CurrentBubble} -> {r.TargetBubble}, cars {r.CachedCars}/{r.CoupledCars}, {r.LocalDistance:F0}m -> {r.TargetLocalDistance:F0}m");
                lines.Add($"   status: {r.Status}");
                lines.Add($"   age {now - r.FirstSeenTime:F1}s, seen {now - r.LastSeenTime:F1}s ago");
            }
            if (_items.Count > count) lines.Add($"... plus {_items.Count - count} more pending handoff(s)");
            Details = string.Join("\n", lines.ToArray());
        }

        private static PendingHandoffRecord Find(string key)
        {
            for (int i = 0; i < _items.Count; i++) if (string.Equals(_items[i].Key, key, StringComparison.OrdinalIgnoreCase)) return _items[i];
            return null;
        }
    }

    public static class ConsistDryRun
    {
        public static int SourceCarCount { get; private set; }
        public static int LiveNowCarCount { get; private set; }
        public static int CachedUsableCarCount { get; private set; }
        public static int ExpiredCacheCount { get; private set; }
        public static int GroupCount { get; private set; }
        public static int LargestGroupSize { get; private set; }
        public static int RecommendedGroupCount { get; private set; }
        public static double WorstGroupLocalDistance { get; private set; }
        public static double WorstGroupFloatStepMeters { get; private set; }
        public static string LastGroupSummary { get; private set; } = "none";
        public static string LastRecommendation { get; private set; } = "none";
        public static string WorstGroupSummary { get; private set; } = "none";
        public static string LastRebuildReason { get; private set; } = "none";
        public static string TransferPlanSummary { get; private set; } = "none";
        public static string TransferPlanDetails { get; private set; } = "none";
        public static string SnapshotSummary { get; private set; } = "none";
        public static string SnapshotDetails { get; private set; } = "none";
        public static string HandoffEligibilitySummary { get; private set; } = "none";
        public static string HandoffEligibilityDetails { get; private set; } = "none";
        public static string PendingHandoffSummary => PendingHandoffQueue.Summary;
        public static string PendingHandoffDetails => PendingHandoffQueue.Details;
        public static string AutoHandoffSummary { get; private set; } = "none";
        public static float TransferPlanAgeSeconds => _transferPlanSetTime > 0f ? Time.realtimeSinceStartup - _transferPlanSetTime : 0f;
        public static float LastRebuildAgeSeconds => _lastRebuildTime > 0f ? Time.realtimeSinceStartup - _lastRebuildTime : 0f;

        private static float _nextRebuildTime;
        private static float _lastRebuildTime;
        private static float _transferPlanSetTime;
        private static float _transferPlanExpireTime;
        private static float _lastAutoHandoffTime;
        private static bool _forceRebuild;
        private static string _forceReason = "none";

        public static void Reset()
        {
            SourceCarCount = LiveNowCarCount = CachedUsableCarCount = ExpiredCacheCount = GroupCount = LargestGroupSize = RecommendedGroupCount = 0;
            WorstGroupLocalDistance = WorstGroupFloatStepMeters = 0.0;
            LastGroupSummary = LastRecommendation = WorstGroupSummary = LastRebuildReason = "none";
            TransferPlanSummary = TransferPlanDetails = SnapshotSummary = SnapshotDetails = HandoffEligibilitySummary = HandoffEligibilityDetails = "none";
            AutoHandoffSummary = "none";
            _nextRebuildTime = _lastRebuildTime = _transferPlanSetTime = _transferPlanExpireTime = _lastAutoHandoffTime = 0f;
            _forceRebuild = false;
            _forceReason = "none";
            PendingHandoffQueue.Reset();
        }

        public static void RequestImmediateRebuild(string reason)
        {
            _forceRebuild = true;
            _forceReason = string.IsNullOrEmpty(reason) ? "forced" : reason;
            _nextRebuildTime = 0f;
        }

        public static void Tick(IReadOnlyList<CarState> carStates)
        {
            if (Main.Settings == null || !Main.Settings.EnableConsistDryRun) return;
            PendingHandoffQueue.RefreshSummary();
            float now = Time.realtimeSinceStartup;
            if (_transferPlanSetTime > 0f && now >= _transferPlanExpireTime)
            {
                TransferPlanSummary = "expired";
                TransferPlanDetails = "none";
                SnapshotSummary = "expired";
                SnapshotDetails = "none";
                HandoffEligibilitySummary = "expired";
                HandoffEligibilityDetails = "none";
                _transferPlanSetTime = _transferPlanExpireTime = 0f;
            }
            if (!_forceRebuild && now < _nextRebuildTime) return;
            string reason = _forceRebuild ? _forceReason : "interval";
            _forceRebuild = false;
            _forceReason = "none";
            _nextRebuildTime = now + Mathf.Max(0.25f, Main.Settings.ConsistDryRunInterval);
            _lastRebuildTime = now;
            LastRebuildReason = reason;
            Rebuild(carStates);
            PendingHandoffQueue.RefreshSummary();
        }

        private static void Rebuild(IReadOnlyList<CarState> carStates)
        {
            SourceCarCount = carStates != null ? carStates.Count : 0;
            LiveNowCarCount = CachedUsableCarCount = ExpiredCacheCount = GroupCount = LargestGroupSize = RecommendedGroupCount = 0;
            WorstGroupLocalDistance = WorstGroupFloatStepMeters = 0.0;
            LastGroupSummary = LastRecommendation = WorstGroupSummary = "none";
            if (carStates == null || carStates.Count == 0) return;

            float now = Time.realtimeSinceStartup;
            float maxAge = Main.Settings != null ? Main.Settings.ConsistCachedPositionMaxAge : 60f;
            for (int i = 0; i < carStates.Count; i++)
            {
                CarState state = carStates[i];
                if (state == null) continue;
                if (state.HasCachedPosition && now - state.CachedPositionTime <= maxAge) CachedUsableCarCount++; else if (state.HasCachedPosition) ExpiredCacheCount++;
                if (state.HasCachedPosition) LiveNowCarCount++;
            }

            HashSet<string> processed = new HashSet<string>();
            List<Car> coupledCars = new List<Car>(32);
            List<CarState> plannedStates = new List<CarState>(32);
            List<string> missingCars = new List<string>(32);
            for (int i = 0; i < carStates.Count; i++)
            {
                CarState seedState = carStates[i];
                if (seedState == null || seedState.Car == null || string.IsNullOrEmpty(seedState.CarId)) continue;
                if (processed.Contains(seedState.CarId)) continue;
                coupledCars.Clear();
                plannedStates.Clear();
                missingCars.Clear();
                CollectCoupledCars(seedState.Car, coupledCars);
                if (coupledCars.Count == 0) coupledCars.Add(seedState.Car);
                Vector3d groupOrigin = seedState.BubbleOrigin;
                string groupBubbleId = string.IsNullOrEmpty(seedState.BubbleId) ? PrecisionWatchdog.MainBubbleId : seedState.BubbleId;
                for (int c = 0; c < coupledCars.Count; c++)
                {
                    Car car = coupledCars[c];
                    if (car == null || string.IsNullOrEmpty(car.id)) continue;
                    processed.Add(car.id);
                    CarState state = PerfManager.GetOrCreateState(car);
                    if (state == null) { missingCars.Add(car.id); continue; }
                    if (string.IsNullOrEmpty(state.BubbleId)) { state.BubbleId = groupBubbleId; state.BubbleOrigin = groupOrigin; }
                    Vector3 planPos;
                    string planSource;
                    if (HandoffPositionResolver.TryResolvePlanPosition(state, now, out planPos, out planSource)) plannedStates.Add(state); else missingCars.Add(state.Name);
                }
                if (plannedStates.Count == 0) continue;
                EvaluateGroup(plannedStates, coupledCars, missingCars);
            }
        }

        private static void CollectCoupledCars(Car seed, List<Car> output)
        {
            output.Clear();
            if (seed == null) return;
            try
            {
                foreach (Car car in seed.EnumerateCoupled(Car.LogicalEnd.A)) if (car != null && !string.IsNullOrEmpty(car.id)) output.Add(car);
            }
            catch (Exception ex)
            {
                Main.DebugLogThrottled("Consist handoff: EnumerateCoupled failed, using seed car only: " + ex.Message);
                output.Clear();
                output.Add(seed);
            }
        }

        private static void EvaluateGroup(List<CarState> plannedStates, List<Car> coupledCars, List<string> missingCars)
        {
            int coupledCount = coupledCars != null ? coupledCars.Count : plannedStates.Count;
            int missingCount = missingCars != null ? missingCars.Count : 0;
            GroupCount++;
            if (coupledCount > LargestGroupSize) LargestGroupSize = coupledCount;
            float now = Time.realtimeSinceStartup;
            CarState first = plannedStates[0];
            Vector3d currentOrigin = first.BubbleOrigin;
            string currentBubbleId = string.IsNullOrEmpty(first.BubbleId) ? PrecisionWatchdog.MainBubbleId : first.BubbleId;
            Vector3d sum = Vector3d.Zero;
            double worstCarDistance = 0.0;
            string worstCarName = "none";
            float oldestAge = 0f;
            for (int i = 0; i < plannedStates.Count; i++)
            {
                CarState state = plannedStates[i];
                Vector3d global = Vector3d.FromVector3(state.PlanPosition) + state.BubbleOrigin;
                sum += global;
                double d = Vector3d.Distance(global, state.BubbleOrigin);
                if (d > worstCarDistance) { worstCarDistance = d; worstCarName = state.Name; }
                float age = now - state.PlanPositionTime;
                if (age > oldestAge) oldestAge = age;
            }
            Vector3d center = new Vector3d(sum.X / plannedStates.Count, sum.Y / plannedStates.Count, sum.Z / plannedStates.Count);
            double localDistance = Vector3d.Distance(center, currentOrigin);
            double localMagnitude = Math.Max(Math.Abs(center.X - currentOrigin.X), Math.Abs(center.Z - currentOrigin.Z));
            double floatStep = PrecisionWatchdog.EstimateFloatStepMeters(localMagnitude);
            PhysicsBubble best = PrecisionWatchdog.FindBestBubble(center);
            double bestDistance = Vector3d.Distance(center, best.GlobalOrigin);
            string summary = $"{first.Name}: coupled {coupledCount}, plan {plannedStates.Count}, missing {missingCount}, center {FormatVector(center)}, local {localDistance:F0}m, worst car {worstCarDistance:F0}m ({worstCarName}), oldest {oldestAge:F1}s";
            LastGroupSummary = summary;
            if (localDistance > WorstGroupLocalDistance) { WorstGroupLocalDistance = localDistance; WorstGroupFloatStepMeters = floatStep; WorstGroupSummary = summary; }
            bool farEnough = localDistance >= Main.Settings.PrecisionTransferDistance;
            bool betterEnough = bestDistance <= localDistance - Main.Settings.PrecisionBetterBubbleMargin;
            bool different = !string.Equals(best.Id, currentBubbleId, StringComparison.OrdinalIgnoreCase);
            if (farEnough && betterEnough && different)
            {
                RecommendedGroupCount++;
                LastRecommendation = $"{plannedStates.Count}/{coupledCount} planned cars: {currentBubbleId} -> {best.Id}, center local {localDistance:F0}m -> {bestDistance:F0}m, worst car {worstCarDistance:F0}m, float step {floatStep * 1000.0:F3}mm";
                string key = BuildConsistKey(coupledCars, plannedStates);
                BuildTransferPlan(key, plannedStates, coupledCount, missingCars, currentBubbleId, currentOrigin, best, center, localDistance, bestDistance, worstCarName, worstCarDistance, floatStep, oldestAge);
            }
        }

        private static string BuildConsistKey(List<Car> coupledCars, List<CarState> plannedStates)
        {
            List<string> ids = new List<string>();
            if (coupledCars != null) for (int i = 0; i < coupledCars.Count; i++) if (coupledCars[i] != null && !string.IsNullOrEmpty(coupledCars[i].id)) ids.Add(coupledCars[i].id);
            if (ids.Count == 0 && plannedStates != null) for (int i = 0; i < plannedStates.Count; i++) if (plannedStates[i] != null && !string.IsNullOrEmpty(plannedStates[i].CarId)) ids.Add(plannedStates[i].CarId);
            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join("|", ids.ToArray());
        }

        private static void BuildTransferPlan(string consistKey, List<CarState> plannedStates, int coupledCount, List<string> missingCars, string currentBubbleId, Vector3d currentOrigin, PhysicsBubble targetBubble, Vector3d center, double localDistance, double targetDistance, string worstCarName, double worstCarDistance, double floatStep, float oldestAge)
        {
            int missingCount = missingCars != null ? missingCars.Count : 0;
            bool incomplete = plannedStates.Count < coupledCount || missingCount > 0;
            List<RigidbodySnapshot> snapshots = CaptureRigidbodySnapshots(plannedStates);
            HandoffEligibilityReport eligibility = BuildEligibility(plannedStates, coupledCount, snapshots, incomplete, targetDistance, oldestAge);
            string displayName = plannedStates.Count > 0 ? plannedStates[0].Name + (coupledCount > 1 ? " +" + (coupledCount - 1).ToString() : string.Empty) : consistKey;
            PendingHandoffQueue.UpdateFromPlan(consistKey, displayName, plannedStates.Count, coupledCount, currentBubbleId, targetBubble.Id, localDistance, targetDistance, eligibility);

            bool applied = false;
            string applyStatus = "not attempted";
            if (eligibility.Ready && Main.Settings.EnableAutomaticHandoff && !Main.Settings.PrecisionDryRunOnly)
            {
                float now = Time.realtimeSinceStartup;
                if (now - _lastAutoHandoffTime >= Mathf.Max(1f, Main.Settings.AutoHandoffCooldownSeconds))
                {
                    applied = ApplyAutomaticHandoff(consistKey, plannedStates, currentBubbleId, currentOrigin, targetBubble, center, localDistance, targetDistance, out applyStatus);
                    if (applied) _lastAutoHandoffTime = now;
                }
                else
                {
                    applyStatus = "cooldown";
                }
            }
            else if (Main.Settings.PrecisionDryRunOnly) applyStatus = "dry-run only";
            else if (!Main.Settings.EnableAutomaticHandoff) applyStatus = "auto disabled";
            else applyStatus = eligibility.BlockerText;

            TransferPlanSummary = $"{(incomplete ? "INCOMPLETE - " : string.Empty)}{plannedStates.Count}/{coupledCount} planned cars: {currentBubbleId} -> {targetBubble.Id}, center {localDistance:F0}m -> {targetDistance:F0}m";
            List<string> lines = new List<string>();
            lines.Add(applied ? "AUTO HANDOFF APPLIED" : "AUTO HANDOFF NOT APPLIED");
            lines.Add("Status: " + applyStatus);
            lines.Add("Plan positions are bubble-local; global = local + current bubble origin.");
            if (incomplete) lines.Add($"NOT SAFE TO MOVE YET - missing plan positions for {coupledCount - plannedStates.Count} coupled car(s)");
            lines.Add($"Current bubble: {currentBubbleId} origin {FormatVector(currentOrigin)}");
            lines.Add($"Target bubble: {targetBubble.Id} origin {FormatVector(targetBubble.GlobalOrigin)}");
            lines.Add($"Center global: {FormatVector(center)}");
            lines.Add($"Worst car: {worstCarDistance:F0}m ({worstCarName})");
            lines.Add($"Float step now: {floatStep * 1000.0:F3}mm, oldest plan sample {oldestAge:F1}s");
            lines.Add("Cars, first 8 shown:");
            int limit = Mathf.Min(8, plannedStates.Count);
            for (int i = 0; i < limit; i++)
            {
                CarState state = plannedStates[i];
                Vector3d global = Vector3d.FromVector3(state.PlanPosition) + currentOrigin;
                Vector3 oldLocal = state.PlanPosition;
                Vector3 newLocal = targetBubble.GlobalToLocal(global);
                float age = Time.realtimeSinceStartup - state.PlanPositionTime;
                lines.Add($"{i + 1}. {state.Name}");
                lines.Add($"   global {FormatVector(global)} via {state.PlanPositionSource}");
                lines.Add($"   old local {PrecisionWatchdog.FormatVector(oldLocal)}");
                lines.Add($"   new local {PrecisionWatchdog.FormatVector(newLocal)}  age {age:F1}s");
            }
            if (plannedStates.Count > limit) lines.Add($"... plus {plannedStates.Count - limit} more planned car(s)");
            TransferPlanDetails = string.Join("\n", lines.ToArray());
            BuildSnapshotSummaryAndDetails(snapshots, plannedStates.Count, incomplete);
            HandoffEligibilitySummary = eligibility.Summary;
            HandoffEligibilityDetails = eligibility.Details;
            _transferPlanSetTime = Time.realtimeSinceStartup;
            _transferPlanExpireTime = _transferPlanSetTime + Mathf.Max(1f, Main.Settings.TransferPlanHoldSeconds);
        }

        private static bool ApplyAutomaticHandoff(string consistKey, List<CarState> states, string currentBubbleId, Vector3d currentOrigin, PhysicsBubble targetBubble, Vector3d center, double oldDistance, double newDistance, out string status)
        {
            status = "unknown";
            try
            {
                Vector3 delta = (currentOrigin - targetBubble.GlobalOrigin).ToVector3();
                for (int i = 0; i < states.Count; i++)
                {
                    CarState state = states[i];
                    if (state == null) continue;
                    Transform transform;
                    string transformSource;
                    Rigidbody rb;
                    string rbSource;
                    CarPhysicsResolver.Resolve(state, state.PlanPosition, out transform, out transformSource, out rb, out rbSource);
                    Vector3 sourcePos = rb != null ? rb.position : (transform != null ? transform.position : state.PlanPosition);
                    Vector3 newPos = sourcePos + delta;
                    Quaternion rot = rb != null ? rb.rotation : (transform != null ? transform.rotation : Quaternion.identity);
                    if (rb != null)
                    {
                        rb.WakeUp();
                        rb.velocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                        rb.position = newPos;
                        rb.rotation = rot;
                    }
                    if (transform != null)
                    {
                        transform.position = newPos;
                        transform.rotation = rot;
                    }
                    if (rb != null)
                    {
                        rb.velocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                        rb.Sleep();
                    }
                    state.BubbleId = targetBubble.Id;
                    state.BubbleOrigin = targetBubble.GlobalOrigin;
                    state.HasCachedPosition = true;
                    state.CachedPosition = newPos;
                    state.CachedPositionSource = "AutoHandoff";
                    state.CachedPositionTime = Time.realtimeSinceStartup;
                    state.HasPlanPosition = true;
                    state.PlanPosition = newPos;
                    state.PlanPositionSource = "AutoHandoff";
                    state.PlanPositionTime = Time.realtimeSinceStartup;
                    PerfManager.MarkBubble(state.CarId, targetBubble.Id, targetBubble.GlobalOrigin);
                }
                AutoHandoffSummary = $"APPLIED {states.Count} cars: {currentBubbleId} -> {targetBubble.Id}, {oldDistance:F0}m -> {newDistance:F0}m";
                PendingHandoffQueue.MarkApplied(consistKey, AutoHandoffSummary);
                Main.Log("Automatic bubble handoff applied: " + AutoHandoffSummary);
                return true;
            }
            catch (Exception ex)
            {
                status = "failed: " + ex.Message;
                AutoHandoffSummary = status;
                Main.Log("Automatic bubble handoff failed: " + ex);
                return false;
            }
        }

        private static List<RigidbodySnapshot> CaptureRigidbodySnapshots(List<CarState> states)
        {
            List<RigidbodySnapshot> snapshots = new List<RigidbodySnapshot>(states != null ? states.Count : 0);
            if (states == null) return snapshots;
            float movingThreshold = Main.Settings != null ? Main.Settings.SnapshotMovingSpeedThreshold : 0.03f;
            for (int i = 0; i < states.Count; i++)
            {
                CarState state = states[i];
                Vector3 reference = state != null && state.HasPlanPosition ? state.PlanPosition : Vector3.zero;
                string referenceSource = state != null && state.HasPlanPosition ? state.PlanPositionSource : "none";
                Transform transform;
                string transformSource;
                Rigidbody rb;
                string rbSource;
                CarPhysicsResolver.Resolve(state, reference, out transform, out transformSource, out rb, out rbSource);
                if (state != null) { state.Rigidbody = rb; state.PhysicsRigidbody = rb; state.RigidbodySource = rbSource; state.PhysicsTransform = transform; state.PhysicsTransformSource = transformSource; }
                RigidbodySnapshot snap = new RigidbodySnapshot { CarName = state != null ? state.Name : "<null>", HasTransform = transform != null, TransformSource = transformSource, HasRigidbody = rb != null, RigidbodySource = rbSource, WasForcedSleeping = state != null && state.WasSleepingForced, ReferenceSource = referenceSource, DistanceToReferencePosition = -1f };
                if (snap.HasTransform) { snap.TransformPosition = transform.position; snap.TransformRotation = transform.rotation; }
                if (snap.HasRigidbody)
                {
                    try
                    {
                        snap.RigidbodyPosition = rb.position;
                        snap.RigidbodyRotation = rb.rotation;
                        snap.Velocity = rb.velocity;
                        snap.AngularVelocity = rb.angularVelocity;
                        snap.IsSleeping = rb.IsSleeping();
                        snap.Speed = snap.Velocity.magnitude;
                        snap.AngularSpeed = snap.AngularVelocity.magnitude;
                        snap.IsMoving = snap.Speed > movingThreshold || snap.AngularSpeed > movingThreshold;
                        if (PrecisionWatchdog.IsMeaningfullyNonZero(reference)) snap.DistanceToReferencePosition = Vector3.Distance(rb.position, reference);
                    }
                    catch { snap.HasRigidbody = false; snap.RigidbodySource = "read failed"; }
                }
                else if (snap.HasTransform && PrecisionWatchdog.IsMeaningfullyNonZero(reference)) snap.DistanceToReferencePosition = Vector3.Distance(transform.position, reference);
                snapshots.Add(snap);
            }
            return snapshots;
        }

        private static HandoffEligibilityReport BuildEligibility(List<CarState> plannedStates, int coupledCount, List<RigidbodySnapshot> snapshots, bool incompletePlan, double targetDistance, float oldestAge)
        {
            HandoffEligibilityReport report = new HandoffEligibilityReport();
            if (Main.Settings == null || !Main.Settings.EnableHandoffEligibilityDryRun) { report.Summary = "disabled"; report.BlockerText = "disabled"; return report; }
            int planned = plannedStates != null ? plannedStates.Count : 0;
            int missingRb = 0, rbCount = 0, moving = 0, forced = 0, farRefs = 0;
            float maxRef = Mathf.Max(0.1f, Main.Settings.HandoffMaxReferenceDelta);
            if (snapshots != null)
            {
                for (int i = 0; i < snapshots.Count; i++)
                {
                    RigidbodySnapshot s = snapshots[i];
                    if (s == null) continue;
                    if (s.HasRigidbody) rbCount++; else missingRb++;
                    if (s.IsMoving) moving++;
                    if (s.WasForcedSleeping) forced++;
                    if (s.DistanceToReferencePosition > maxRef) farRefs++;
                }
            }
            else missingRb = planned;
            report.CachedCars = planned;
            report.CoupledCars = coupledCount;
            report.RigidbodyCount = rbCount;
            report.MissingRigidbodies = missingRb;
            report.MovingCars = moving;
            report.ForcedSleeping = forced;
            report.FarPhysicsRefs = farRefs;
            report.TargetLocalDistance = targetDistance;
            List<string> blockers = new List<string>();
            if (incompletePlan || planned < coupledCount) blockers.Add($"plan {planned}/{coupledCount}");
            if (missingRb > 0) blockers.Add($"missing rb {missingRb}");
            if (moving > Main.Settings.HandoffMaxMovingCars) blockers.Add($"moving {moving}>{Main.Settings.HandoffMaxMovingCars}");
            if (farRefs > 0) blockers.Add($"physics ref delta {farRefs}");
            if (targetDistance > Main.Settings.HandoffMaxTargetLocalDistance) blockers.Add($"target local {targetDistance:F0}>{Main.Settings.HandoffMaxTargetLocalDistance:F0}");
            if (oldestAge > Main.Settings.ConsistCachedPositionMaxAge) blockers.Add($"old plan {oldestAge:F1}s");
            report.Ready = blockers.Count == 0;
            report.BlockerText = report.Ready ? "ready" : string.Join(", ", blockers.ToArray());
            report.Summary = report.Ready ? $"READY: {planned}/{coupledCount} cars, rb {rbCount}, moving {moving}, target {targetDistance:F0}m" : $"NOT READY: {report.BlockerText}";
            List<string> details = new List<string>();
            details.Add("Handoff eligibility gate");
            details.Add(report.Ready ? "PASS: plan is eligible for automatic stopped-consist handoff" : "BLOCKED: do not move yet");
            details.Add($"Full consist planned: {planned}/{coupledCount}");
            details.Add($"Rigidbodies: {rbCount}, missing {missingRb}");
            details.Add($"Moving cars: {moving}, allowed {Main.Settings.HandoffMaxMovingCars}");
            details.Add($"Physics ref delta failures: {farRefs}, max {maxRef:F1}m");
            details.Add($"Target local distance: {targetDistance:F0}m, max {Main.Settings.HandoffMaxTargetLocalDistance:F0}m");
            details.Add($"Forced-sleeping cars: {forced}");
            report.Details = string.Join("\n", details.ToArray());
            return report;
        }

        private static void BuildSnapshotSummaryAndDetails(List<RigidbodySnapshot> snapshots, int expectedCars, bool incompletePlan)
        {
            if (snapshots == null || snapshots.Count == 0) { SnapshotSummary = "none"; SnapshotDetails = "none"; return; }
            int rbCount = 0, missingRb = 0, sleeping = 0, awake = 0, moving = 0, forced = 0, far = 0;
            float maxRef = Main.Settings != null ? Main.Settings.HandoffMaxReferenceDelta : 25f;
            for (int i = 0; i < snapshots.Count; i++)
            {
                RigidbodySnapshot s = snapshots[i];
                if (s == null) continue;
                if (s.HasRigidbody) { rbCount++; if (s.IsSleeping) sleeping++; else awake++; if (s.IsMoving) moving++; } else missingRb++;
                if (s.DistanceToReferencePosition > maxRef) far++;
                if (s.WasForcedSleeping) forced++;
            }
            SnapshotSummary = $"snap {snapshots.Count}/{expectedCars}, rb {rbCount}, missing {missingRb}, sleep/awake {sleeping}/{awake}, moving {moving}, forced {forced}, farRef {far}";
            List<string> lines = new List<string>();
            lines.Add("Snapshot - captured before possible automatic handoff");
            if (incompletePlan) lines.Add("WARNING: transfer plan is incomplete until every coupled car has a plan position");
            lines.Add(SnapshotSummary);
            lines.Add("First 8 snapshots:");
            int limit = Mathf.Min(8, snapshots.Count);
            for (int i = 0; i < limit; i++)
            {
                RigidbodySnapshot s = snapshots[i];
                if (s == null) continue;
                string dist = s.DistanceToReferencePosition >= 0f ? $", refΔ {s.DistanceToReferencePosition:F1}m" : string.Empty;
                if (!s.HasRigidbody)
                {
                    lines.Add($"{i + 1}. {s.CarName}: no Rigidbody via {s.RigidbodySource}");
                    lines.Add($"   tSrc {s.TransformSource}, tPos {PrecisionWatchdog.FormatVector(s.TransformPosition)}{dist}");
                }
                else
                {
                    lines.Add($"{i + 1}. {s.CarName}: rbSrc {s.RigidbodySource}, sleep {s.IsSleeping}{dist}");
                    lines.Add($"   rbPos {PrecisionWatchdog.FormatVector(s.RigidbodyPosition)} | ref {s.ReferenceSource}");
                    lines.Add($"   vel {PrecisionWatchdog.FormatVector(s.Velocity)} ({s.Speed:F2}m/s), ang {s.AngularSpeed:F2}");
                }
            }
            if (snapshots.Count > limit) lines.Add($"... plus {snapshots.Count - limit} more snapshot(s)");
            SnapshotDetails = string.Join("\n", lines.ToArray());
        }

        private static string FormatVector(Vector3d value) { return $"{value.X:F1}, {value.Y:F1}, {value.Z:F1}"; }
    }

    public static class PerfManager
    {
        private static readonly List<CarState> _cars = new List<CarState>(1024);
        private static readonly Dictionary<string, CarState> _carById = new Dictionary<string, CarState>(1024);
        private static readonly Dictionary<string, PhysicsBubble> _knownBubbles = new Dictionary<string, PhysicsBubble>(1024);
        private static int _cursor;
        private static Transform _playerAnchor;
        public static int TrackedCount => _cars.Count;
        public static IReadOnlyList<CarState> Cars => _cars;
        public static int HotCount { get; private set; }
        public static int WarmCount { get; private set; }
        public static int ColdCount { get; private set; }
        public static int FrozenCount { get; private set; }
        public static double LastPassMs { get; private set; }

        public static void Reset()
        {
            RestoreAll();
            _cars.Clear();
            _carById.Clear();
            _knownBubbles.Clear();
            _cursor = 0;
            _playerAnchor = null;
            HotCount = WarmCount = ColdCount = FrozenCount = 0;
            LastPassMs = 0;
            PrecisionWatchdog.Reset();
            ConsistDryRun.Reset();
        }

        public static void RestoreAll()
        {
            for (int i = 0; i < _cars.Count; i++)
            {
                CarState car = _cars[i];
                if (car?.Rigidbody == null) continue;
                try { if (car.WasSleepingForced) car.Rigidbody.WakeUp(); car.WasSleepingForced = false; } catch { }
            }
        }

        public static void MarkBubble(string carId, string bubbleId, Vector3d origin)
        {
            if (string.IsNullOrEmpty(carId)) return;
            _knownBubbles[carId] = new PhysicsBubble(bubbleId, origin);
            CarState state;
            if (_carById.TryGetValue(carId, out state) && state != null)
            {
                state.BubbleId = bubbleId;
                state.BubbleOrigin = origin;
            }
        }

        public static CarState GetOrCreateState(Car car)
        {
            if (car == null || string.IsNullOrEmpty(car.id)) return null;
            CarState state;
            if (_carById.TryGetValue(car.id, out state)) return state;
            GameObject go = car.gameObject;
            if (go == null) return null;
            PhysicsBubble bubble;
            if (!_knownBubbles.TryGetValue(car.id, out bubble)) bubble = new PhysicsBubble(PrecisionWatchdog.MainBubbleId, Vector3d.Zero);
            state = new CarState
            {
                CarId = car.id,
                Car = car,
                GameObject = go,
                Transform = go.transform,
                Rigidbody = go.GetComponent<Rigidbody>(),
                Renderers = go.GetComponentsInChildren<Renderer>(true),
                Tier = ActivityTier.Hot,
                LastDistance = 0f,
                IsVisible = car.IsVisible,
                LastMovingTime = Time.time,
                LastProcessedFrame = -1,
                BubbleId = bubble.Id,
                BubbleOrigin = bubble.GlobalOrigin,
                GlobalPosition = Vector3d.FromVector3(go.transform.position) + bubble.GlobalOrigin,
                CachedPositionSource = "none"
            };
            _cars.Add(state);
            _carById[car.id] = state;
            return state;
        }

        public static void RefreshPlayerAnchor() { _playerAnchor = FindPlayerAnchor(); }

        public static void RefreshCars()
        {
            try
            {
                CarCuller culler = UnityEngine.Object.FindObjectOfType<CarCuller>();
                if (culler == null) { Main.DebugLogThrottled("RefreshCars: CarCuller not found."); return; }
                FieldInfo recordsField = typeof(CarCuller).GetField("_records", BindingFlags.NonPublic | BindingFlags.Instance);
                if (recordsField == null) { Main.Log("RefreshCars: _records field not found on CarCuller."); return; }
                var records = recordsField.GetValue(culler) as System.Collections.IList;
                if (records == null) { Main.Log("RefreshCars: _records is null."); return; }
                HashSet<string> seen = new HashSet<string>();
                foreach (object record in records)
                {
                    if (record == null) continue;
                    FieldInfo carField = record.GetType().GetField("Car", BindingFlags.Public | BindingFlags.Instance);
                    if (carField == null) continue;
                    Car car = carField.GetValue(record) as Car;
                    if (car == null || string.IsNullOrEmpty(car.id)) continue;
                    seen.Add(car.id);
                    GetOrCreateState(car);
                }
                for (int i = _cars.Count - 1; i >= 0; i--)
                {
                    CarState state = _cars[i];
                    if (state == null || string.IsNullOrEmpty(state.CarId) || !seen.Contains(state.CarId))
                    {
                        if (state != null && state.Rigidbody != null && state.WasSleepingForced) { try { state.Rigidbody.WakeUp(); } catch { } }
                        if (state != null && !string.IsNullOrEmpty(state.CarId)) _carById.Remove(state.CarId);
                        _cars.RemoveAt(i);
                    }
                }
                Main.DebugLogThrottled($"RefreshCars: found {records.Count} culler records, tracking {_cars.Count} cars.");
            }
            catch (Exception ex) { Main.Log("RefreshCars failed: " + ex); }
        }

        public static void Tick(float deltaTime)
        {
            if (_cars.Count == 0) return;
            double start = Time.realtimeSinceStartup;
            int batchDivider = Mathf.Max(1, Main.Settings.BatchDivider);
            int batchSize = Mathf.Max(1, _cars.Count / batchDivider);
            if (Main.Settings.EnablePrecisionWatchdog) PrecisionWatchdog.BeginBatch();
            for (int i = 0; i < batchSize; i++)
            {
                if (_cursor >= _cars.Count) _cursor = 0;
                CarState state = _cars[_cursor++];
                if (state == null || state.GameObject == null || state.Transform == null) continue;
                UpdateTier(state);
                ApplyOptimizations(state);
                if (Main.Settings.EnablePrecisionWatchdog) PrecisionWatchdog.Evaluate(state);
            }
            HotCount = WarmCount = ColdCount = FrozenCount = 0;
            for (int i = 0; i < _cars.Count; i++)
            {
                switch (_cars[i].Tier)
                {
                    case ActivityTier.Hot: HotCount++; break;
                    case ActivityTier.Warm: WarmCount++; break;
                    case ActivityTier.Cold: ColdCount++; break;
                    case ActivityTier.Frozen: FrozenCount++; break;
                }
            }
            if (Main.Settings.EnablePrecisionWatchdog && Main.Settings.EnableConsistDryRun) ConsistDryRun.Tick(_cars);
            LastPassMs = (Time.realtimeSinceStartup - start) * 1000.0;
        }

        private static void UpdateTier(CarState state)
        {
            float dist = GetDistanceToPlayer(state);
            bool visible = state.Car != null && state.Car.IsVisible;
            bool moving = state.Car != null && Mathf.Abs(state.Car.velocity) > Main.Settings.StationarySpeedThreshold;
            state.LastDistance = dist;
            state.IsVisible = visible;
            state.IsMoving = moving;
            if (moving) state.LastMovingTime = Time.time;
            float hot = Mathf.Min(Main.Settings.HotRadius, Main.Settings.WarmRadius);
            float warm = Mathf.Max(Main.Settings.HotRadius, Main.Settings.WarmRadius);
            float cold = Mathf.Max(warm, Main.Settings.ColdRadius);
            float idle = Time.time - state.LastMovingTime;
            if (moving || dist <= hot) { state.Tier = ActivityTier.Hot; return; }
            if (dist <= warm || (visible && dist <= cold)) { state.Tier = ActivityTier.Warm; return; }
            if (dist > warm && idle >= Main.Settings.FreezeDelaySeconds) { state.Tier = ActivityTier.Frozen; return; }
            state.Tier = ActivityTier.Cold;
        }

        private static void ApplyOptimizations(CarState state)
        {
            int frame = Time.frameCount;
            if (state.LastProcessedFrame == frame) return;
            switch (state.Tier)
            {
                case ActivityTier.Hot: state.LastProcessedFrame = frame; RestoreIfNeeded(state); break;
                case ActivityTier.Warm: if ((frame % 3) != 0) return; state.LastProcessedFrame = frame; RestoreIfNeeded(state); break;
                case ActivityTier.Cold: if ((frame % 10) != 0) return; state.LastProcessedFrame = frame; TrySleep(state); break;
                case ActivityTier.Frozen: if ((frame % 30) != 0) return; state.LastProcessedFrame = frame; TrySleep(state); break;
            }
        }

        private static void TrySleep(CarState state)
        {
            if (!Main.Settings.EnableSleep || state.Rigidbody == null) return;
            if (Main.Settings.RequireStationaryForSleep && state.IsMoving) return;
            try { if (!state.Rigidbody.IsSleeping()) { state.Rigidbody.Sleep(); state.WasSleepingForced = true; } } catch { }
        }

        private static void RestoreIfNeeded(CarState state)
        {
            if (state.Rigidbody == null || !state.WasSleepingForced) return;
            try { state.Rigidbody.WakeUp(); } catch { }
            state.WasSleepingForced = false;
        }

        private static float GetDistanceToPlayer(CarState state)
        {
            Transform anchor = _playerAnchor;
            if (anchor == null) anchor = Camera.main != null ? Camera.main.transform : null;
            if (anchor == null || state.Transform == null) return float.MaxValue;
            return Vector3.Distance(anchor.position, state.Transform.position);
        }

        private static Transform FindPlayerAnchor()
        {
            try
            {
                if (Camera.main != null) return Camera.main.transform;
                Camera[] cams = UnityEngine.Object.FindObjectsOfType<Camera>(true);
                for (int i = 0; i < cams.Length; i++) if (cams[i] != null && cams[i].enabled) return cams[i].transform;
            }
            catch { }
            return null;
        }
    }

    public class OverlayBehaviour : MonoBehaviour
    {
        private Rect _windowRect = new Rect(8f, 20f, 900f, 640f);
        private Vector2 _scroll;
        private GUIStyle _label;
        private GUIStyle _header;
        private GUIStyle _box;

        private void OnGUI()
        {
            if (!Main.Enabled || !Main.Settings.EnableOverlay) return;
            float maxWidth = Mathf.Max(560f, Screen.width - 20f);
            float maxHeight = Mathf.Max(300f, Screen.height - 40f);
            if (_windowRect.width > maxWidth) _windowRect.width = maxWidth;
            if (_windowRect.height > maxHeight) _windowRect.height = maxHeight;
            _windowRect = GUI.Window(444123, _windowRect, DrawWindow, "Stock Optimizer");
        }

        private void DrawWindow(int id)
        {
            EnsureStyles();
            float scrollWidth = Mathf.Max(540f, _windowRect.width - 24f);
            float scrollHeight = Mathf.Max(160f, _windowRect.height - 34f);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Width(scrollWidth), GUILayout.Height(scrollHeight));
            DrawTopColumns(scrollWidth);
            DrawRecommendationAndSamples(scrollWidth);
            DrawTransferPlanStacked(scrollWidth);
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }

        private void EnsureStyles()
        {
            if (_label != null) return;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = false, margin = new RectOffset(1, 1, -1, -1), padding = new RectOffset(0, 0, 0, 0), fixedHeight = 14f, clipping = TextClipping.Clip };
            _header = new GUIStyle(_label) { fontStyle = FontStyle.Bold, fixedHeight = 15f };
            _box = new GUIStyle(GUI.skin.box) { margin = new RectOffset(1, 1, 1, 1), padding = new RectOffset(3, 3, 2, 2) };
        }

        private void DrawTopColumns(float width)
        {
            float col = Mathf.Max(180f, (width - 18f) / 3f);
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(_box, GUILayout.Width(col));
            Header("Stock"); Row("Tracked", PerfManager.TrackedCount.ToString()); Row("Hot/Warm", $"{PerfManager.HotCount}/{PerfManager.WarmCount}"); Row("Cold/Frozen", $"{PerfManager.ColdCount}/{PerfManager.FrozenCount}"); Row("Pass", $"{PerfManager.LastPassMs:F3} ms");
            GUILayout.EndVertical();
            GUILayout.BeginVertical(_box, GUILayout.Width(col));
            Header("Precision"); Row("Eval L/Z", $"{PrecisionWatchdog.EvaluatedCount}  {PrecisionWatchdog.NonZeroSampleCount}/{PrecisionWatchdog.ZeroSampleCount}"); Row("W/M/E", $"{PrecisionWatchdog.WarningCount}/{PrecisionWatchdog.TransferRecommendedCount}/{PrecisionWatchdog.EmergencyCount}"); Row("Batch worst", $"{PrecisionWatchdog.WorstLocalDistance:F0} m"); Row("Float step", $"{PrecisionWatchdog.WorstFloatStepMeters * 1000.0:F3} mm");
            GUILayout.EndVertical();
            GUILayout.BeginVertical(_box, GUILayout.Width(col));
            Header("Consists"); Row("Source/cache", $"{ConsistDryRun.SourceCarCount}/{ConsistDryRun.CachedUsableCarCount}"); Row("Live/expired", $"{ConsistDryRun.LiveNowCarCount}/{ConsistDryRun.ExpiredCacheCount}"); Row("Groups/largest", $"{ConsistDryRun.GroupCount}/{ConsistDryRun.LargestGroupSize}"); Row("Move groups", ConsistDryRun.RecommendedGroupCount.ToString());
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void DrawRecommendationAndSamples(float width)
        {
            float col = Mathf.Max(260f, (width - 14f) * 0.5f);
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(_box, GUILayout.Width(col));
            Header("Recommendation");
            Row("Status", PrecisionWatchdog.LastRecommendationStatus);
            Row("Age", $"{PrecisionWatchdog.LastRecommendationAgeSeconds:F1}s");
            ClipLabel("Original: " + PrecisionWatchdog.LastRecommendationOriginalText);
            ClipLabel("Current: " + PrecisionWatchdog.LastRecommendationCurrentText);
            ClipLabel("Indiv: " + PrecisionWatchdog.LastRecommendation);
            Header("Consist move");
            Row("Worst group", $"{ConsistDryRun.WorstGroupLocalDistance:F0} m / {ConsistDryRun.WorstGroupFloatStepMeters * 1000.0:F3} mm");
            ClipLabel("Group: " + ConsistDryRun.LastGroupSummary);
            ClipLabel("Move: " + ConsistDryRun.LastRecommendation);
            ClipLabel("Auto: " + ConsistDryRun.AutoHandoffSummary);
            Row("Rebuild", $"{ConsistDryRun.LastRebuildAgeSeconds:F1}s, {ConsistDryRun.LastRebuildReason}");
            GUILayout.EndVertical();
            GUILayout.BeginVertical(_box, GUILayout.Width(col));
            Header("Samples");
            Row("Live car", PrecisionWatchdog.DisplayCarName);
            Row("Source", PrecisionWatchdog.DisplayPositionSource);
            Row("Pos", PrecisionWatchdog.DisplayChosenPositionText);
            Row("Age", $"{PrecisionWatchdog.DisplaySampleAgeSeconds:F1}s");
            Row("Last nonzero", PrecisionWatchdog.LastNonZeroCarName);
            Row("NZ pos", PrecisionWatchdog.LastNonZeroChosenPositionText);
            Row("NZ dist", $"{PrecisionWatchdog.LastNonZeroLocalDistance:F0} m / {PrecisionWatchdog.LastNonZeroFloatStepMeters * 1000.0:F3} mm");
            Row("Held worst", PrecisionWatchdog.HeldWorstCarName);
            Row("Worst pos", PrecisionWatchdog.HeldWorstChosenPositionText);
            Row("Worst dist", $"{PrecisionWatchdog.HeldWorstLocalDistance:F0} m / {PrecisionWatchdog.HeldWorstFloatStepMeters * 1000.0:F3} mm");
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void DrawTransferPlanStacked(float width)
        {
            GUILayout.BeginVertical(_box, GUILayout.Width(width - 8f));
            Header("Consist transfer plan + rigidbody snapshot + automatic handoff queue");
            Row("Plan age", $"{ConsistDryRun.TransferPlanAgeSeconds:F1}s");
            ClipLabel("Summary: " + ConsistDryRun.TransferPlanSummary);
            ClipLabel("Snapshot: " + ConsistDryRun.SnapshotSummary);
            ClipLabel("Eligibility: " + ConsistDryRun.HandoffEligibilitySummary);
            ClipLabel("Pending: " + ConsistDryRun.PendingHandoffSummary);
            ClipLabel("Auto: " + ConsistDryRun.AutoHandoffSummary);
            DrawSection("Transfer plan", ConsistDryRun.TransferPlanDetails, 18);
            DrawSection("Rigidbody snapshot", ConsistDryRun.SnapshotDetails, 18);
            DrawSection("Handoff eligibility", ConsistDryRun.HandoffEligibilityDetails, 12);
            DrawSection("Pending handoffs", ConsistDryRun.PendingHandoffDetails, 18);
            GUILayout.EndVertical();
        }

        private void DrawSection(string title, string text, int maxLines)
        {
            GUILayout.BeginVertical(_box);
            Header(title);
            DrawMultilineCompact(text, maxLines);
            GUILayout.EndVertical();
        }

        private void Header(string text) { GUILayout.Label(text, _header); }
        private void Row(string label, string value)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(14f));
            GUILayout.Label(label + ":", _label, GUILayout.Width(78f));
            GUILayout.Label(value ?? "none", _label);
            GUILayout.EndHorizontal();
        }
        private void ClipLabel(string text) { GUILayout.Label(text ?? "none", _label, GUILayout.Height(14f)); }
        private void DrawMultilineCompact(string text, int maxLines)
        {
            if (string.IsNullOrEmpty(text) || text == "none") { ClipLabel("none"); return; }
            string[] lines = text.Split(new[] { '\n' }, StringSplitOptions.None);
            int count = Mathf.Min(maxLines, lines.Length);
            for (int i = 0; i < count; i++) ClipLabel(lines[i]);
            if (lines.Length > count) ClipLabel($"... plus {lines.Length - count} more line(s)");
        }
    }

    [HarmonyPatch(typeof(Car), "SetCullerDistanceBand")]
    class Patch_CarDistance
    {
        static void Postfix(Car __instance, int currentDistance)
        {
            if (currentDistance >= 3)
            {
                // Placeholder patch retained from the stock optimizer scaffold.
            }
        }
    }
}
