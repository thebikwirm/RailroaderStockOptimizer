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
        private static OverlayBehaviour _overlayBehaviour;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            ModEntry = modEntry;
            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);

            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnUpdate = OnUpdate;

            Log("Loaded.");
            return true;
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
            Settings.EnablePrecisionWatchdog = GUILayout.Toggle(Settings.EnablePrecisionWatchdog, "Enable precision watchdog dry-run");
            Settings.PrecisionDryRunOnly = GUILayout.Toggle(Settings.PrecisionDryRunOnly, "Dry-run only: log recommended bubble moves, do not move trains");
            Settings.ShowPrecisionDetailsInOverlay = GUILayout.Toggle(Settings.ShowPrecisionDetailsInOverlay, "Show precision details in overlay");

            GUILayout.Label($"Bubble Grid Size: {Settings.BubbleGridSize:F0} m");
            Settings.BubbleGridSize = GUILayout.HorizontalSlider(Settings.BubbleGridSize, 5000f, 50000f);

            GUILayout.Label($"Precision Warning Distance: {Settings.PrecisionWarningDistance:F0} m");
            Settings.PrecisionWarningDistance = GUILayout.HorizontalSlider(Settings.PrecisionWarningDistance, 1000f, 50000f);

            GUILayout.Label($"Transfer Recommendation Distance: {Settings.PrecisionTransferDistance:F0} m");
            Settings.PrecisionTransferDistance = GUILayout.HorizontalSlider(Settings.PrecisionTransferDistance, 2000f, 75000f);

            GUILayout.Label($"Emergency Distance: {Settings.PrecisionEmergencyDistance:F0} m");
            Settings.PrecisionEmergencyDistance = GUILayout.HorizontalSlider(Settings.PrecisionEmergencyDistance, 5000f, 100000f);

            GUILayout.Label($"Better Bubble Margin: {Settings.PrecisionBetterBubbleMargin:F0} m");
            Settings.PrecisionBetterBubbleMargin = GUILayout.HorizontalSlider(Settings.PrecisionBetterBubbleMargin, 500f, 20000f);

            GUILayout.Space(8f);
            GUILayout.Label($"Tracked cars: {PerfManager.TrackedCount}");
            GUILayout.Label($"Hot: {PerfManager.HotCount}  Warm: {PerfManager.WarmCount}  Cold: {PerfManager.ColdCount}  Frozen: {PerfManager.FrozenCount}");
            GUILayout.Label($"Manager cost last pass: {PerfManager.LastPassMs:F3} ms");

            if (Settings.EnablePrecisionWatchdog)
            {
                GUILayout.Label($"Precision batch: warn {PrecisionWatchdog.WarningCount}, recommend {PrecisionWatchdog.TransferRecommendedCount}, emergency {PrecisionWatchdog.EmergencyCount}");
                GUILayout.Label($"Worst local distance: {PrecisionWatchdog.WorstLocalDistance:F0} m, float step {PrecisionWatchdog.WorstFloatStepMeters * 1000.0:F3} mm");
                GUILayout.Label($"Last recommendation: {PrecisionWatchdog.LastRecommendation}");
            }
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
                _overlayBehaviour = _overlayObject.AddComponent<OverlayBehaviour>();
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
                    _overlayBehaviour = null;
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
            if (Settings == null || !Settings.EnableDebugLogging)
                return;

            Log("[Debug] " + msg);
        }

        public static void DebugLogThrottled(string msg)
        {
            if (Settings == null || !Settings.EnableDebugLogging)
                return;

            if (Time.realtimeSinceStartup - _lastDebugLogTime < Settings.DebugLogInterval)
                return;

            _lastDebugLogTime = Time.realtimeSinceStartup;
            Log("[Debug] " + msg);
        }
    }

    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        public bool EnableOverlay = true;

        // Start with sleep disabled until detection is proven correct.
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

        // Physics bubble / precision test settings.
        // These are dry-run diagnostics only for now. They do not move trains yet.
        public bool EnablePrecisionWatchdog = false;
        public bool PrecisionDryRunOnly = true;
        public bool ShowPrecisionDetailsInOverlay = true;
        public float BubbleGridSize = 20000f;
        public float PrecisionWarningDistance = 10000f;
        public float PrecisionTransferDistance = 20000f;
        public float PrecisionEmergencyDistance = 30000f;
        public float PrecisionBetterBubbleMargin = 5000f;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }

        public void OnChange()
        {
        }
    }

    public enum ActivityTier
    {
        Hot,
        Warm,
        Cold,
        Frozen
    }

    public struct Vector3d
    {
        public double X;
        public double Y;
        public double Z;

        public static readonly Vector3d Zero = new Vector3d(0.0, 0.0, 0.0);

        public Vector3d(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vector3d FromVector3(Vector3 value)
        {
            return new Vector3d(value.x, value.y, value.z);
        }

        public Vector3 ToVector3()
        {
            return new Vector3((float)X, (float)Y, (float)Z);
        }

        public static Vector3d operator +(Vector3d a, Vector3d b)
        {
            return new Vector3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }

        public static Vector3d operator -(Vector3d a, Vector3d b)
        {
            return new Vector3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }

        public double Magnitude()
        {
            return Math.Sqrt(X * X + Y * Y + Z * Z);
        }

        public static double Distance(Vector3d a, Vector3d b)
        {
            return (a - b).Magnitude();
        }

        public override string ToString()
        {
            return $"{X:F1}, {Y:F1}, {Z:F1}";
        }
    }

    public sealed class PhysicsBubble
    {
        public string Id;
        public Vector3d GlobalOrigin;

        public PhysicsBubble(string id, Vector3d globalOrigin)
        {
            Id = id;
            GlobalOrigin = globalOrigin;
        }

        public Vector3 GlobalToLocal(Vector3d globalPosition)
        {
            return (globalPosition - GlobalOrigin).ToVector3();
        }

        public Vector3d LocalToGlobal(Vector3 localPosition)
        {
            return GlobalOrigin + Vector3d.FromVector3(localPosition);
        }
    }

    public sealed class CarState
    {
        public string CarId;
        public Model.Car Car;

        public GameObject GameObject;
        public Transform Transform;
        public Rigidbody Rigidbody;
        public Renderer[] Renderers;

        public ActivityTier Tier;
        public float LastDistance;
        public bool IsVisible;
        public bool IsMoving;
        public float LastMovingTime;
        public int LastProcessedFrame;
        public bool WasSleepingForced;

        // Dry-run precision/bubble state. Current implementation treats scene position as global position.
        public string BubbleId;
        public Vector3d BubbleOrigin;
        public Vector3d GlobalPosition;
        public double PrecisionLocalDistance;
        public double PrecisionFloatStepMeters;
        public bool PrecisionWarning;
        public bool PrecisionTransferRecommended;
        public bool PrecisionEmergency;
        public string PrecisionTargetBubbleId;

        public string Name => GameObject != null ? GameObject.name : "<null>";
    }

    public static class PrecisionWatchdog
    {
        public const string MainBubbleId = "MainBubble";

        public static int WarningCount { get; private set; }
        public static int TransferRecommendedCount { get; private set; }
        public static int EmergencyCount { get; private set; }
        public static double WorstLocalDistance { get; private set; }
        public static double WorstFloatStepMeters { get; private set; }
        public static string LastRecommendation { get; private set; } = "none";

        public static void Reset()
        {
            WarningCount = 0;
            TransferRecommendedCount = 0;
            EmergencyCount = 0;
            WorstLocalDistance = 0.0;
            WorstFloatStepMeters = 0.0;
            LastRecommendation = "none";
        }

        public static void BeginBatch()
        {
            WarningCount = 0;
            TransferRecommendedCount = 0;
            EmergencyCount = 0;
            WorstLocalDistance = 0.0;
            WorstFloatStepMeters = 0.0;
        }

        public static void Evaluate(CarState state)
        {
            if (state == null || state.Transform == null || Main.Settings == null)
                return;

            Settings settings = Main.Settings;

            if (string.IsNullOrEmpty(state.BubbleId))
            {
                state.BubbleId = MainBubbleId;
                state.BubbleOrigin = Vector3d.Zero;
            }

            // First test stage: use the current Unity scene position as the global position.
            // Later this should become a real double/global track coordinate that survives bubble moves.
            Vector3d global = Vector3d.FromVector3(state.Transform.position);
            state.GlobalPosition = global;

            double localDistance = Vector3d.Distance(global, state.BubbleOrigin);
            double localMagnitude = Math.Max(Math.Abs(global.X - state.BubbleOrigin.X), Math.Abs(global.Z - state.BubbleOrigin.Z));
            double floatStep = EstimateFloatStepMeters(localMagnitude);

            state.PrecisionLocalDistance = localDistance;
            state.PrecisionFloatStepMeters = floatStep;
            state.PrecisionWarning = localDistance >= settings.PrecisionWarningDistance;
            state.PrecisionEmergency = localDistance >= settings.PrecisionEmergencyDistance;
            state.PrecisionTransferRecommended = false;
            state.PrecisionTargetBubbleId = null;

            if (state.PrecisionWarning)
                WarningCount++;

            if (state.PrecisionEmergency)
                EmergencyCount++;

            if (localDistance > WorstLocalDistance)
            {
                WorstLocalDistance = localDistance;
                WorstFloatStepMeters = floatStep;
            }

            PhysicsBubble best = FindBestBubble(global);
            double bestDistance = Vector3d.Distance(global, best.GlobalOrigin);
            bool farEnough = localDistance >= settings.PrecisionTransferDistance;
            bool betterEnough = bestDistance <= localDistance - settings.PrecisionBetterBubbleMargin;
            bool differentBubble = !string.Equals(best.Id, state.BubbleId, StringComparison.OrdinalIgnoreCase);

            if (farEnough && betterEnough && differentBubble)
            {
                state.PrecisionTransferRecommended = true;
                state.PrecisionTargetBubbleId = best.Id;
                TransferRecommendedCount++;

                LastRecommendation = $"{state.Name}: {state.BubbleId} -> {best.Id}, local {localDistance:F0}m -> {bestDistance:F0}m, float step {floatStep * 1000.0:F3}mm";
                Main.DebugLogThrottled("Precision watchdog dry-run recommends bubble transfer: " + LastRecommendation);

                if (!settings.PrecisionDryRunOnly)
                {
                    Main.DebugLogThrottled("Precision watchdog real transfer is not implemented yet. Staying in dry-run behavior.");
                }
            }
        }

        public static PhysicsBubble FindBestBubble(Vector3d globalPosition)
        {
            float rawGrid = Main.Settings != null ? Main.Settings.BubbleGridSize : 20000f;
            double grid = Math.Max(1000.0, rawGrid);

            double originX = Math.Round(globalPosition.X / grid) * grid;
            double originZ = Math.Round(globalPosition.Z / grid) * grid;

            string id = $"GridBubble[{originX:F0},{originZ:F0}]";
            return new PhysicsBubble(id, new Vector3d(originX, 0.0, originZ));
        }

        public static double EstimateFloatStepMeters(double magnitude)
        {
            magnitude = Math.Abs(magnitude);
            if (magnitude <= 0.0)
                return 0.0;

            // Single-precision float spacing around this magnitude is roughly 2^(floor(log2(magnitude)) - 23).
            double exponent = Math.Floor(Math.Log(magnitude, 2.0));
            return Math.Pow(2.0, exponent - 23.0);
        }
    }

    public static class PerfManager
    {
        private static readonly List<CarState> _cars = new List<CarState>(1024);
        private static readonly Dictionary<string, CarState> _carById = new Dictionary<string, CarState>(1024);

        private static int _cursor;
        private static Transform _playerAnchor;

        public static int TrackedCount => _cars.Count;
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
            _cursor = 0;
            _playerAnchor = null;
            HotCount = 0;
            WarmCount = 0;
            ColdCount = 0;
            FrozenCount = 0;
            LastPassMs = 0;
            PrecisionWatchdog.Reset();
        }

        public static void RestoreAll()
        {
            for (int i = 0; i < _cars.Count; i++)
            {
                CarState car = _cars[i];
                if (car?.Rigidbody == null) continue;

                try
                {
                    if (car.WasSleepingForced)
                        car.Rigidbody.WakeUp();

                    car.WasSleepingForced = false;
                }
                catch
                {
                }
            }
        }

        public static void RefreshPlayerAnchor()
        {
            _playerAnchor = FindPlayerAnchor();
        }

        public static void RefreshCars()
        {
            try
            {
                CarCuller culler = UnityEngine.Object.FindObjectOfType<CarCuller>();
                if (culler == null)
                {
                    Main.DebugLogThrottled("RefreshCars: CarCuller not found.");
                    return;
                }

                var recordsField = typeof(CarCuller).GetField(
                    "_records",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (recordsField == null)
                {
                    Main.Log("RefreshCars: _records field not found on CarCuller.");
                    return;
                }

                var records = recordsField.GetValue(culler) as System.Collections.IList;
                if (records == null)
                {
                    Main.Log("RefreshCars: _records is null.");
                    return;
                }

                HashSet<string> seen = new HashSet<string>();

                foreach (var record in records)
                {
                    if (record == null) continue;

                    var carField = record.GetType().GetField(
                        "Car",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                    if (carField == null) continue;

                    Car car = carField.GetValue(record) as Car;
                    if (car == null) continue;
                    if (string.IsNullOrEmpty(car.id)) continue;

                    string id = car.id;
                    seen.Add(id);

                    if (_carById.ContainsKey(id))
                        continue;

                    GameObject go = car.gameObject;
                    Rigidbody rb = go != null ? go.GetComponent<Rigidbody>() : null;

                    CarState state = new CarState
                    {
                        CarId = id,
                        Car = car,
                        GameObject = go,
                        Transform = go != null ? go.transform : null,
                        Rigidbody = rb,
                        Renderers = go != null ? go.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>(),
                        Tier = ActivityTier.Hot,
                        LastDistance = 0f,
                        IsVisible = car.IsVisible,
                        IsMoving = false,
                        LastMovingTime = Time.time,
                        LastProcessedFrame = -1,
                        WasSleepingForced = false,
                        BubbleId = PrecisionWatchdog.MainBubbleId,
                        BubbleOrigin = Vector3d.Zero,
                        GlobalPosition = go != null ? Vector3d.FromVector3(go.transform.position) : Vector3d.Zero,
                        PrecisionLocalDistance = 0.0,
                        PrecisionFloatStepMeters = 0.0,
                        PrecisionWarning = false,
                        PrecisionTransferRecommended = false,
                        PrecisionEmergency = false,
                        PrecisionTargetBubbleId = null
                    };

                    _cars.Add(state);
                    _carById[id] = state;
                }

                for (int i = _cars.Count - 1; i >= 0; i--)
                {
                    CarState state = _cars[i];
                    if (state == null)
                    {
                        _cars.RemoveAt(i);
                        continue;
                    }

                    if (string.IsNullOrEmpty(state.CarId) || !seen.Contains(state.CarId))
                    {
                        if (state.Rigidbody != null && state.WasSleepingForced)
                        {
                            try { state.Rigidbody.WakeUp(); } catch { }
                        }

                        if (!string.IsNullOrEmpty(state.CarId))
                            _carById.Remove(state.CarId);

                        _cars.RemoveAt(i);
                    }
                }

                Main.DebugLogThrottled($"RefreshCars: found {records.Count} culler records, tracking {_cars.Count} cars.");
            }
            catch (Exception ex)
            {
                Main.Log("RefreshCars failed: " + ex);
            }
        }

        public static void Tick(float deltaTime)
        {
            if (_cars.Count == 0) return;

            double start = Time.realtimeSinceStartupAsDouble;

            int batchDivider = Mathf.Max(1, Main.Settings.BatchDivider);
            int batchSize = Mathf.Max(1, _cars.Count / batchDivider);

            if (Main.Settings.EnablePrecisionWatchdog)
                PrecisionWatchdog.BeginBatch();

            for (int i = 0; i < batchSize; i++)
            {
                if (_cursor >= _cars.Count)
                    _cursor = 0;

                CarState state = _cars[_cursor++];
                if (state == null || state.GameObject == null || state.Transform == null)
                    continue;

                UpdateTier(state);
                ApplyOptimizations(state);

                if (Main.Settings.EnablePrecisionWatchdog)
                    PrecisionWatchdog.Evaluate(state);
            }

            HotCount = 0;
            WarmCount = 0;
            ColdCount = 0;
            FrozenCount = 0;

            for (int i = 0; i < _cars.Count; i++)
            {
                switch (_cars[i].Tier)
                {
                    case ActivityTier.Hot:
                        HotCount++;
                        break;
                    case ActivityTier.Warm:
                        WarmCount++;
                        break;
                    case ActivityTier.Cold:
                        ColdCount++;
                        break;
                    case ActivityTier.Frozen:
                        FrozenCount++;
                        break;
                }
            }

            LastPassMs = (Time.realtimeSinceStartupAsDouble - start) * 1000.0;
        }

        private static void UpdateTier(CarState state)
        {
            float dist = GetDistanceToPlayer(state);
            bool visible = IsVisible(state);
            bool moving = IsMoving(state);

            state.LastDistance = dist;
            state.IsVisible = visible;
            state.IsMoving = moving;

            if (moving)
                state.LastMovingTime = Time.time;

            float hot = Mathf.Min(Main.Settings.HotRadius, Main.Settings.WarmRadius);
            float warm = Mathf.Max(Main.Settings.HotRadius, Main.Settings.WarmRadius);
            float cold = Mathf.Max(warm, Main.Settings.ColdRadius);

            float idleTime = Time.time - state.LastMovingTime;

            // Only truly nearby or moving cars should be Hot.
            if (moving || dist <= hot)
            {
                state.Tier = ActivityTier.Hot;
                return;
            }

            // Visible cars should usually be Warm, not Hot.
            if (dist <= warm || (visible && dist <= cold))
            {
                state.Tier = ActivityTier.Warm;
                return;
            }

            // Freeze more aggressively once far enough and idle briefly.
            if (dist > warm && idleTime >= Main.Settings.FreezeDelaySeconds)
            {
                state.Tier = ActivityTier.Frozen;
                return;
            }

            state.Tier = ActivityTier.Cold;
        }

        private static void ApplyOptimizations(CarState state)
        {
            int frame = Time.frameCount;
            if (state.LastProcessedFrame == frame)
                return;

            switch (state.Tier)
            {
                case ActivityTier.Hot:
                    state.LastProcessedFrame = frame;
                    RestoreIfNeeded(state);
                    break;

                case ActivityTier.Warm:
                    if ((frame % 3) != 0) return;
                    state.LastProcessedFrame = frame;
                    RestoreIfNeeded(state);
                    break;

                case ActivityTier.Cold:
                    if ((frame % 10) != 0) return;
                    state.LastProcessedFrame = frame;
                    TrySleep(state);
                    break;

                case ActivityTier.Frozen:
                    if ((frame % 30) != 0) return;
                    state.LastProcessedFrame = frame;
                    TrySleep(state);
                    break;
            }
        }

        private static void TrySleep(CarState state)
        {
            if (!Main.Settings.EnableSleep) return;
            if (state.Rigidbody == null) return;
            if (Main.Settings.RequireStationaryForSleep && state.IsMoving) return;

            try
            {
                if (!state.Rigidbody.IsSleeping())
                {
                    state.Rigidbody.Sleep();
                    state.WasSleepingForced = true;
                }
            }
            catch
            {
            }
        }

        private static void RestoreIfNeeded(CarState state)
        {
            if (state.Rigidbody == null) return;

            if (state.WasSleepingForced)
            {
                try
                {
                    state.Rigidbody.WakeUp();
                }
                catch
                {
                }

                state.WasSleepingForced = false;
            }
        }

        private static float GetDistanceToPlayer(CarState state)
        {
            if (_playerAnchor == null)
            {
                Transform fallback = Camera.main != null ? Camera.main.transform : null;
                if (fallback == null || state.Transform == null)
                    return float.MaxValue;

                return Vector3.Distance(fallback.position, state.Transform.position);
            }

            return Vector3.Distance(_playerAnchor.position, state.Transform.position);
        }

        private static bool IsVisible(CarState state)
        {
            if (state == null || state.Car == null)
                return false;

            return state.Car.IsVisible;
        }

        private static bool IsMoving(CarState state)
        {
            if (state == null || state.Car == null)
                return false;

            return Mathf.Abs(state.Car.velocity) > Main.Settings.StationarySpeedThreshold;
        }

        private static Transform FindPlayerAnchor()
        {
            try
            {
                if (Camera.main != null)
                    return Camera.main.transform;

                Camera[] cams = UnityEngine.Object.FindObjectsOfType<Camera>(true);
                for (int i = 0; i < cams.Length; i++)
                {
                    if (cams[i] != null && cams[i].enabled)
                        return cams[i].transform;
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool LooksLikeRailCar(GameObject go)
        {
            if (go == null) return false;
            if (!go.activeInHierarchy) return false;

            string n = go.name.ToLowerInvariant();

            if (n.Contains("asset")) return false;
            if (n.Contains("scassetpack")) return false;
            if (n.Contains("building")) return false;
            if (n.Contains("terrain")) return false;
            if (n.Contains("tree")) return false;
            if (n.Contains("rock")) return false;
            if (n.Contains("crossing")) return false;
            if (n.Contains("signal")) return false;
            if (n.Contains("switchstand")) return false;
            if (n.Contains("decal")) return false;
            if (n.Contains("effect")) return false;
            if (n.Contains("particle")) return false;
            if (n.Contains("smoke")) return false;
            if (n.Contains("ui")) return false;
            if (n.Contains("canvas")) return false;

            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb == null) return false;

            bool nameLooksRight =
                n.Contains("car") ||
                n.Contains("wagon") ||
                n.Contains("freight") ||
                n.Contains("rollingstock") ||
                n.Contains("locomotive") ||
                n.Contains("engine") ||
                n.Contains("tender") ||
                n.Contains("caboose") ||
                n.Contains("boxcar") ||
                n.Contains("flatcar") ||
                n.Contains("hopper") ||
                n.Contains("tankcar") ||
                n.Contains("loco");

            if (!nameLooksRight) return false;

            return true;
        }
    }

    public class OverlayBehaviour : MonoBehaviour
    {
        private Rect _windowRect = new Rect(20f, 20f, 360f, 230f);

        private void OnGUI()
        {
            if (!Main.Enabled) return;
            if (!Main.Settings.EnableOverlay) return;

            _windowRect = GUI.Window(444123, _windowRect, DrawWindow, "Stock Optimizer");
        }

        private void DrawWindow(int id)
        {
            GUILayout.Label($"Tracked: {PerfManager.TrackedCount}");
            GUILayout.Label($"Hot: {PerfManager.HotCount}");
            GUILayout.Label($"Warm: {PerfManager.WarmCount}");
            GUILayout.Label($"Cold: {PerfManager.ColdCount}");
            GUILayout.Label($"Frozen: {PerfManager.FrozenCount}");
            GUILayout.Label($"Last pass: {PerfManager.LastPassMs:F3} ms");

            if (Main.Settings.EnablePrecisionWatchdog && Main.Settings.ShowPrecisionDetailsInOverlay)
            {
                GUILayout.Space(4f);
                GUILayout.Label("--- Precision watchdog ---");
                GUILayout.Label($"Warn: {PrecisionWatchdog.WarningCount}  Move: {PrecisionWatchdog.TransferRecommendedCount}  Emergency: {PrecisionWatchdog.EmergencyCount}");
                GUILayout.Label($"Worst local: {PrecisionWatchdog.WorstLocalDistance:F0} m");
                GUILayout.Label($"Float step: {PrecisionWatchdog.WorstFloatStepMeters * 1000.0:F3} mm");
                GUILayout.Label($"Last: {PrecisionWatchdog.LastRecommendation}");
            }

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }
    }

    [HarmonyPatch(typeof(Car), "SetCullerDistanceBand")]
    class Patch_CarDistance
    {
        static void Postfix(Car __instance, int currentDistance)
        {
            if (currentDistance >= 3)
            {
                // far away car
                // reduce update rate
            }
        }
    }
}
