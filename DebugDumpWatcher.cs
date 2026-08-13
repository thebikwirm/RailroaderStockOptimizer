using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace RailroaderStockOptimizer
{
    /// <summary>
    /// Writes the current bubble-test debug state to a plain text file so it can be uploaded
    /// instead of posting screenshots. This is deliberately passive: it never changes trains,
    /// rigidbodies, bubbles, or optimiser state.
    /// </summary>
    public sealed class DebugDumpWatcher : MonoBehaviour
    {
        private const string StableDumpFileName = "StockOptimizerDebugDump.txt";
        private const string ManualDumpPrefix = "StockOptimizerDebugDump_";
        private static DebugDumpWatcher _instance;

        private float _nextAutoDumpTime;
        private float _lastManualDumpTime;
        private Rect _buttonRect = new Rect(12f, 12f, 260f, 76f);

        public static string LastDumpPath { get; private set; } = "not written yet";
        public static string LastDumpStatus { get; private set; } = "not written yet";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RuntimeInstallAfterSceneLoad()
        {
            Install();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RuntimeInstallBeforeSceneLoad()
        {
            Install();
        }

        public static void Install()
        {
            try
            {
                if (_instance != null)
                    return;

                GameObject go = new GameObject("RailroaderStockOptimizerDebugDumpWatcher");
                DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                _instance = go.AddComponent<DebugDumpWatcher>();
                LastDumpStatus = "dump watcher installed";
            }
            catch (Exception ex)
            {
                LastDumpStatus = "dump watcher install failed: " + ex.Message;
            }
        }

        private void Update()
        {
            try
            {
                if (Main.ModEntry == null)
                    return;

                if (Time.realtimeSinceStartup >= _nextAutoDumpTime)
                {
                    _nextAutoDumpTime = Time.realtimeSinceStartup + 10f;
                    WriteDump(false);
                }
            }
            catch (Exception ex)
            {
                LastDumpStatus = "auto dump failed: " + ex.Message;
            }
        }

        private void OnGUI()
        {
            try
            {
                if (Main.ModEntry == null || Main.Settings == null || !Main.Settings.EnableOverlay)
                    return;

                _buttonRect = GUI.Window(444124, _buttonRect, DrawDumpWindow, "Debug dump");
            }
            catch
            {
            }
        }

        private void DrawDumpWindow(int id)
        {
            GUILayout.BeginVertical();
            if (GUILayout.Button("Write uploadable dump now", GUILayout.Height(24f)))
            {
                ManualDump();
            }
            GUILayout.Label(LastDumpStatus ?? "not written yet");
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }

        public static string ManualDump()
        {
            try
            {
                Install();
                string path = WriteDump(true);
                LastDumpStatus = "manual dump written: " + path;
                return path;
            }
            catch (Exception ex)
            {
                LastDumpStatus = "manual dump failed: " + ex.Message;
                try { Main.Log(LastDumpStatus); } catch { }
                return null;
            }
        }

        public static string GetDumpDirectory()
        {
            try
            {
                if (Main.ModEntry != null && !string.IsNullOrEmpty(Main.ModEntry.Path))
                    return Main.ModEntry.Path;
            }
            catch
            {
            }

            return Application.persistentDataPath;
        }

        public static string WriteDump(bool timestampedCopy)
        {
            string dir = GetDumpDirectory();
            Directory.CreateDirectory(dir);

            string stablePath = Path.Combine(dir, StableDumpFileName);
            string text = BuildDumpText();
            File.WriteAllText(stablePath, text, Encoding.UTF8);
            LastDumpPath = stablePath;
            LastDumpStatus = "stable dump written: " + stablePath;

            if (timestampedCopy)
            {
                string stampedName = ManualDumpPrefix + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
                string stampedPath = Path.Combine(dir, stampedName);
                File.WriteAllText(stampedPath, text, Encoding.UTF8);
                LastDumpPath = stampedPath;
                LastDumpStatus = "manual dump written: " + stampedPath;
                try { Main.Log("Debug dump written: " + stampedPath); } catch { }
                return stampedPath;
            }

            return stablePath;
        }

        private static string BuildDumpText()
        {
            StringBuilder sb = new StringBuilder(8192);
            sb.AppendLine("Railroader Stock Optimizer debug dump");
            sb.AppendLine("Generated local: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Generated UTC:   " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + "Z");
            sb.AppendLine("Dump directory:  " + GetDumpDirectory());
            sb.AppendLine("Mod enabled:     " + Main.Enabled);
            sb.AppendLine();

            AppendSettings(sb);
            AppendStockSummary(sb);
            AppendPrecisionSummary(sb);
            AppendConsistSummary(sb);
            AppendLongSection(sb, "Transfer plan", ConsistDryRun.TransferPlanDetails);
            AppendLongSection(sb, "Rigidbody snapshot", ConsistDryRun.SnapshotDetails);
            AppendLongSection(sb, "Handoff eligibility", ConsistDryRun.HandoffEligibilityDetails);
            AppendLongSection(sb, "Pending handoffs", ConsistDryRun.PendingHandoffDetails);

            return sb.ToString();
        }

        private static void AppendSettings(StringBuilder sb)
        {
            Settings s = Main.Settings;
            sb.AppendLine("== Settings ==");
            if (s == null)
            {
                sb.AppendLine("Settings: null");
                sb.AppendLine();
                return;
            }

            sb.AppendLine("EnableOverlay: " + s.EnableOverlay);
            sb.AppendLine("EnableSleep: " + s.EnableSleep);
            sb.AppendLine("RequireStationaryForSleep: " + s.RequireStationaryForSleep);
            sb.AppendLine("UseVisibilityCheck: " + s.UseVisibilityCheck);
            sb.AppendLine("BatchDivider: " + s.BatchDivider);
            sb.AppendLine("FullRefreshInterval: " + s.FullRefreshInterval.ToString("F2"));
            sb.AppendLine("PlayerRefreshInterval: " + s.PlayerRefreshInterval.ToString("F2"));
            sb.AppendLine("EnablePrecisionWatchdog: " + s.EnablePrecisionWatchdog);
            sb.AppendLine("PrecisionDryRunOnly: " + s.PrecisionDryRunOnly);
            sb.AppendLine("ShowPrecisionDetailsInOverlay: " + s.ShowPrecisionDetailsInOverlay);
            sb.AppendLine("EnableConsistDryRun: " + s.EnableConsistDryRun);
            sb.AppendLine("EnableRigidbodySnapshotDryRun: " + s.EnableRigidbodySnapshotDryRun);
            sb.AppendLine("EnableHandoffEligibilityDryRun: " + s.EnableHandoffEligibilityDryRun);
            sb.AppendLine("EnablePendingHandoffDryRun: " + s.EnablePendingHandoffDryRun);
            sb.AppendLine("BubbleGridSize: " + s.BubbleGridSize.ToString("F0"));
            sb.AppendLine("PrecisionWarningDistance: " + s.PrecisionWarningDistance.ToString("F0"));
            sb.AppendLine("PrecisionTransferDistance: " + s.PrecisionTransferDistance.ToString("F0"));
            sb.AppendLine("PrecisionEmergencyDistance: " + s.PrecisionEmergencyDistance.ToString("F0"));
            sb.AppendLine("PrecisionBetterBubbleMargin: " + s.PrecisionBetterBubbleMargin.ToString("F0"));
            sb.AppendLine("ConsistDryRunInterval: " + s.ConsistDryRunInterval.ToString("F2"));
            sb.AppendLine("ConsistCachedPositionMaxAge: " + s.ConsistCachedPositionMaxAge.ToString("F0"));
            sb.AppendLine("TransferPlanHoldSeconds: " + s.TransferPlanHoldSeconds.ToString("F0"));
            sb.AppendLine("SnapshotMovingSpeedThreshold: " + s.SnapshotMovingSpeedThreshold.ToString("F3"));
            sb.AppendLine("HandoffMaxMovingCars: " + s.HandoffMaxMovingCars);
            sb.AppendLine("HandoffMaxReferenceDelta: " + s.HandoffMaxReferenceDelta.ToString("F1"));
            sb.AppendLine("HandoffMaxTargetLocalDistance: " + s.HandoffMaxTargetLocalDistance.ToString("F0"));
            sb.AppendLine("PendingHandoffRetainSeconds: " + s.PendingHandoffRetainSeconds.ToString("F0"));
            sb.AppendLine("PendingHandoffMaxShown: " + s.PendingHandoffMaxShown);
            sb.AppendLine();
        }

        private static void AppendStockSummary(StringBuilder sb)
        {
            sb.AppendLine("== Stock optimiser ==");
            sb.AppendLine("Tracked: " + PerfManager.TrackedCount);
            sb.AppendLine("Hot/Warm/Cold/Frozen: " + PerfManager.HotCount + "/" + PerfManager.WarmCount + "/" + PerfManager.ColdCount + "/" + PerfManager.FrozenCount);
            sb.AppendLine("Last pass ms: " + PerfManager.LastPassMs.ToString("F3"));
            sb.AppendLine();
        }

        private static void AppendPrecisionSummary(StringBuilder sb)
        {
            sb.AppendLine("== Precision watchdog ==");
            sb.AppendLine("Eval live/zero: " + PrecisionWatchdog.EvaluatedCount + " " + PrecisionWatchdog.NonZeroSampleCount + "/" + PrecisionWatchdog.ZeroSampleCount);
            sb.AppendLine("Warn/move/emergency: " + PrecisionWatchdog.WarningCount + "/" + PrecisionWatchdog.TransferRecommendedCount + "/" + PrecisionWatchdog.EmergencyCount);
            sb.AppendLine("Batch worst: " + PrecisionWatchdog.WorstLocalDistance.ToString("F0") + " m, float step " + (PrecisionWatchdog.WorstFloatStepMeters * 1000.0).ToString("F3") + " mm");
            sb.AppendLine("Last recommendation status: " + PrecisionWatchdog.LastRecommendationStatus);
            sb.AppendLine("Last recommendation age: " + PrecisionWatchdog.LastRecommendationAgeSeconds.ToString("F1") + "s");
            sb.AppendLine("Last recommendation original: " + PrecisionWatchdog.LastRecommendationOriginalText);
            sb.AppendLine("Last recommendation current: " + PrecisionWatchdog.LastRecommendationCurrentText);
            sb.AppendLine("Last recommendation: " + PrecisionWatchdog.LastRecommendation);
            sb.AppendLine("Display live car: " + PrecisionWatchdog.DisplayCarName + " source " + PrecisionWatchdog.DisplayPositionSource + " pos " + PrecisionWatchdog.DisplayChosenPositionText + " age " + PrecisionWatchdog.DisplaySampleAgeSeconds.ToString("F1") + "s");
            sb.AppendLine("Last nonzero: " + PrecisionWatchdog.LastNonZeroCarName + " pos " + PrecisionWatchdog.LastNonZeroChosenPositionText + " dist " + PrecisionWatchdog.LastNonZeroLocalDistance.ToString("F0") + "m");
            sb.AppendLine("Held worst: " + PrecisionWatchdog.HeldWorstCarName + " pos " + PrecisionWatchdog.HeldWorstChosenPositionText + " dist " + PrecisionWatchdog.HeldWorstLocalDistance.ToString("F0") + "m");
            sb.AppendLine();
        }

        private static void AppendConsistSummary(StringBuilder sb)
        {
            sb.AppendLine("== Consist dry-run ==");
            sb.AppendLine("Source/cache: " + ConsistDryRun.SourceCarCount + "/" + ConsistDryRun.CachedUsableCarCount);
            sb.AppendLine("Live/expired: " + ConsistDryRun.LiveNowCarCount + "/" + ConsistDryRun.ExpiredCacheCount);
            sb.AppendLine("Groups/largest: " + ConsistDryRun.GroupCount + "/" + ConsistDryRun.LargestGroupSize);
            sb.AppendLine("Move groups: " + ConsistDryRun.RecommendedGroupCount);
            sb.AppendLine("Worst group: " + ConsistDryRun.WorstGroupLocalDistance.ToString("F0") + " m, float step " + (ConsistDryRun.WorstGroupFloatStepMeters * 1000.0).ToString("F3") + " mm");
            sb.AppendLine("Last group: " + ConsistDryRun.LastGroupSummary);
            sb.AppendLine("Last group move: " + ConsistDryRun.LastRecommendation);
            sb.AppendLine("Last rebuild: " + ConsistDryRun.LastRebuildAgeSeconds.ToString("F1") + "s, " + ConsistDryRun.LastRebuildReason);
            sb.AppendLine("Transfer plan age: " + ConsistDryRun.TransferPlanAgeSeconds.ToString("F1") + "s");
            sb.AppendLine("Transfer plan summary: " + ConsistDryRun.TransferPlanSummary);
            sb.AppendLine("Snapshot summary: " + ConsistDryRun.SnapshotSummary);
            sb.AppendLine("Handoff eligibility summary: " + ConsistDryRun.HandoffEligibilitySummary);
            sb.AppendLine("Pending handoff summary: " + ConsistDryRun.PendingHandoffSummary);
            sb.AppendLine();
        }

        private static void AppendLongSection(StringBuilder sb, string title, string text)
        {
            sb.AppendLine("== " + title + " ==");
            if (string.IsNullOrEmpty(text))
                sb.AppendLine("none");
            else
                sb.AppendLine(text);
            sb.AppendLine();
        }
    }
}
