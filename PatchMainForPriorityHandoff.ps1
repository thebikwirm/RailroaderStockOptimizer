param(
    [Parameter(Mandatory=$true)][string]$InputPath,
    [Parameter(Mandatory=$true)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$text = Get-Content -Raw -Path $InputPath

# Keep this generated patch deliberately small and reversible. The test branch still has the
# single-file Main.cs scaffold, so this script patches a generated compile copy without
# rewriting the checked-in Main.cs file.

# --- Existing priority/cleanup/horizontal-distance patch ---

$text = $text -replace 'public bool Applied;\r?\n        public string Status;', "public bool Applied;`r`n        public float LastAppliedTime;`r`n        public string Status;"

$text = $text.Replace(
'            record.DisplayName = string.IsNullOrEmpty(displayName) ? key : displayName;',
'            if (record.Applied && (!string.Equals(record.CurrentBubble, currentBubble, StringComparison.OrdinalIgnoreCase) || !string.Equals(record.TargetBubble, targetBubble, StringComparison.OrdinalIgnoreCase)))
            {
                record.Applied = false;
                record.Ready = false;
                record.LastAppliedTime = 0f;
            }
            record.DisplayName = string.IsNullOrEmpty(displayName) ? key : displayName;')

$text = $text.Replace(
'            record.Applied = true;
            record.Ready = false;',
'            record.Applied = true;
            record.LastAppliedTime = Time.realtimeSinceStartup;
            record.Ready = false;')

$text = $text.Replace(
'            for (int i = _items.Count - 1; i >= 0; i--) if (now - _items[i].LastSeenTime > retain) _items.RemoveAt(i);',
'            for (int i = _items.Count - 1; i >= 0; i--)
            {
                PendingHandoffRecord item = _items[i];
                if (item == null) { _items.RemoveAt(i); continue; }
                if (item.Applied && item.LastAppliedTime > 0f && now - item.LastAppliedTime > 30f) _items.RemoveAt(i);
                else if (!item.Applied && now - item.LastSeenTime > retain) _items.RemoveAt(i);
            }
            SortItems();')

$text = $text.Replace(
'        private static PendingHandoffRecord Find(string key)
        {',
'        public static bool ShouldAttemptAutoHandoff(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            SortItems();
            for (int i = 0; i < _items.Count; i++)
            {
                PendingHandoffRecord item = _items[i];
                if (item == null || item.Applied || !item.Ready) continue;
                return string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase);
            }
            return true;
        }

        private static void SortItems()
        {
            _items.Sort(CompareRecords);
        }

        private static int CompareRecords(PendingHandoffRecord a, PendingHandoffRecord b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            if (a.Applied != b.Applied) return a.Applied ? 1 : -1;
            if (a.Ready != b.Ready) return a.Ready ? -1 : 1;
            int carCompare = b.CoupledCars.CompareTo(a.CoupledCars);
            if (carCompare != 0) return carCompare;
            int distanceCompare = b.LocalDistance.CompareTo(a.LocalDistance);
            if (distanceCompare != 0) return distanceCompare;
            return a.FirstSeenTime.CompareTo(b.FirstSeenTime);
        }

        private static PendingHandoffRecord Find(string key)
        {')

$text = $text.Replace(
'            double localDistance = Vector3d.Distance(center, currentOrigin);',
'            double localDistance = HorizontalDistance(center, currentOrigin);')

$text = $text.Replace(
'            double bestDistance = Vector3d.Distance(center, best.GlobalOrigin);',
'            double bestDistance = HorizontalDistance(center, best.GlobalOrigin);')

$text = $text.Replace(
'        private static string FormatVector(Vector3d value) { return $"{value.X:F1}, {value.Y:F1}, {value.Z:F1}"; }',
'        private static double HorizontalDistance(Vector3d a, Vector3d b)
        {
            double dx = a.X - b.X;
            double dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        private static string FormatVector(Vector3d value) { return $"{value.X:F1}, {value.Y:F1}, {value.Z:F1}"; }')

# --- Large-save performance/safety patch ---

# Add settings for test safety and plan budget. Session cap and plan budget are now the main
# protections; the hot/warm-only gate stays available but defaults off for active handoff testing.
$text = $text.Replace(
'        public int PendingHandoffMaxShown = 6;',
'        public int PendingHandoffMaxShown = 6;
        public bool AutoHandoffOnlyHotOrWarm = false;
        public int AutoHandoffMaxAppliedPerSession = 2;
        public int MaxHandoffPlansPerRebuild = 12;')

$text = $text.Replace(
'            GUILayout.Label($"Pending handoff max shown: {Settings.PendingHandoffMaxShown}");
            Settings.PendingHandoffMaxShown = Mathf.RoundToInt(GUILayout.HorizontalSlider(Settings.PendingHandoffMaxShown, 1f, 12f));',
'            GUILayout.Label($"Pending handoff max shown: {Settings.PendingHandoffMaxShown}");
            Settings.PendingHandoffMaxShown = Mathf.RoundToInt(GUILayout.HorizontalSlider(Settings.PendingHandoffMaxShown, 1f, 12f));
            Settings.AutoHandoffOnlyHotOrWarm = GUILayout.Toggle(Settings.AutoHandoffOnlyHotOrWarm, "Auto handoff only hot/warm visible consists");
            GUILayout.Label($"Auto handoff session cap: {Settings.AutoHandoffMaxAppliedPerSession}");
            Settings.AutoHandoffMaxAppliedPerSession = Mathf.RoundToInt(GUILayout.HorizontalSlider(Settings.AutoHandoffMaxAppliedPerSession, 0f, 20f));
            GUILayout.Label($"Max handoff plans per rebuild: {Settings.MaxHandoffPlansPerRebuild}");
            Settings.MaxHandoffPlansPerRebuild = Mathf.RoundToInt(GUILayout.HorizontalSlider(Settings.MaxHandoffPlansPerRebuild, 1f, 40f));')

# Add counters/throttles to the consist planner.
$text = $text.Replace(
'        private static bool _forceRebuild;
        private static string _forceReason = "none";',
'        private static bool _forceRebuild;
        private static string _forceReason = "none";
        private static float _nextForcedRebuildAllowedTime;
        private static int _autoHandoffAppliedCount;')

$text = $text.Replace(
'            _forceRebuild = false;
            _forceReason = "none";
            PendingHandoffQueue.Reset();',
'            _forceRebuild = false;
            _forceReason = "none";
            _nextForcedRebuildAllowedTime = 0f;
            _autoHandoffAppliedCount = 0;
            PendingHandoffQueue.Reset();')

$text = $text.Replace(
'        public static void RequestImmediateRebuild(string reason)
        {
            _forceRebuild = true;
            _forceReason = string.IsNullOrEmpty(reason) ? "forced" : reason;
            _nextRebuildTime = 0f;
        }',
'        public static void RequestImmediateRebuild(string reason)
        {
            float now = Time.realtimeSinceStartup;
            float minInterval = Main.Settings != null ? Mathf.Max(1f, Main.Settings.ConsistDryRunInterval * 0.5f) : 1f;
            if (now < _nextForcedRebuildAllowedTime) return;
            _nextForcedRebuildAllowedTime = now + minInterval;
            _forceRebuild = true;
            _forceReason = string.IsNullOrEmpty(reason) ? "forced" : reason;
            _nextRebuildTime = 0f;
        }')

# Keep expensive snapshot/eligibility plans from being built for every far consist in a huge save.
$text = $text.Replace(
'                RecommendedGroupCount++;
                LastRecommendation = $"{plannedStates.Count}/{coupledCount} planned cars: {currentBubbleId} -> {best.Id}, center local {localDistance:F0}m -> {bestDistance:F0}m, worst car {worstCarDistance:F0}m, float step {floatStep * 1000.0:F3}mm";
                string key = BuildConsistKey(coupledCars, plannedStates);
                BuildTransferPlan(key, plannedStates, coupledCount, missingCars, currentBubbleId, currentOrigin, best, center, localDistance, bestDistance, worstCarName, worstCarDistance, floatStep, oldestAge);',
'                RecommendedGroupCount++;
                int maxPlans = Main.Settings != null ? Mathf.Max(1, Main.Settings.MaxHandoffPlansPerRebuild) : 12;
                if (RecommendedGroupCount > maxPlans)
                {
                    LastRecommendation = $"handoff plan budget reached ({maxPlans}); skipped extra group {first.Name}, coupled {coupledCount}, local {localDistance:F0}m";
                    return;
                }
                LastRecommendation = $"{plannedStates.Count}/{coupledCount} planned cars: {currentBubbleId} -> {best.Id}, center local {localDistance:F0}m -> {bestDistance:F0}m, worst car {worstCarDistance:F0}m, float step {floatStep * 1000.0:F3}mm";
                string key = BuildConsistKey(coupledCars, plannedStates);
                BuildTransferPlan(key, plannedStates, coupledCount, missingCars, currentBubbleId, currentOrigin, best, center, localDistance, bestDistance, worstCarName, worstCarDistance, floatStep, oldestAge);')

# Only the highest-priority READY handoff may apply, and only if the safety gate passes.
$text = $text.Replace(
'            if (eligibility.Ready && Main.Settings.EnableAutomaticHandoff && !Main.Settings.PrecisionDryRunOnly)',
'            if (eligibility.Ready && Main.Settings.EnableAutomaticHandoff && !Main.Settings.PrecisionDryRunOnly && PendingHandoffQueue.ShouldAttemptAutoHandoff(consistKey) && AutoHandoffAllowedForGroup(plannedStates, out applyStatus))')

$text = $text.Replace(
'            else if (!Main.Settings.EnableAutomaticHandoff) applyStatus = "auto disabled";
            else applyStatus = eligibility.BlockerText;',
'            else if (!Main.Settings.EnableAutomaticHandoff) applyStatus = "auto disabled";
            else if (eligibility.Ready)
            {
                string gateReason;
                if (!PendingHandoffQueue.ShouldAttemptAutoHandoff(consistKey)) applyStatus = "waiting: lower priority ready handoff";
                else if (!AutoHandoffAllowedForGroup(plannedStates, out gateReason)) applyStatus = gateReason;
                else applyStatus = "ready but not attempted";
            }
            else applyStatus = eligibility.BlockerText;')

$text = $text.Replace(
'                PendingHandoffQueue.MarkApplied(consistKey, AutoHandoffSummary);
                Main.Log("Automatic bubble handoff applied: " + AutoHandoffSummary);',
'                _autoHandoffAppliedCount++;
                PendingHandoffQueue.MarkApplied(consistKey, AutoHandoffSummary);
                Main.Log("Automatic bubble handoff applied: " + AutoHandoffSummary);')

# Insert the capped auto gate before the actual mover.
$text = $text.Replace(
'        private static bool ApplyAutomaticHandoff(string consistKey, List<CarState> states, string currentBubbleId, Vector3d currentOrigin, PhysicsBubble targetBubble, Vector3d center, double oldDistance, double newDistance, out string status)
        {',
'        private static bool AutoHandoffAllowedForGroup(List<CarState> states, out string reason)
        {
            reason = "ready";
            if (Main.Settings == null) { reason = "blocked: no settings"; return false; }
            int cap = Main.Settings.AutoHandoffMaxAppliedPerSession;
            if (cap > 0 && _autoHandoffAppliedCount >= cap)
            {
                reason = "blocked: auto handoff session cap reached";
                return false;
            }
            if (!Main.Settings.AutoHandoffOnlyHotOrWarm) return true;
            if (states != null)
            {
                for (int i = 0; i < states.Count; i++)
                {
                    CarState state = states[i];
                    if (state == null) continue;
                    if (state.Tier == ActivityTier.Hot || state.Tier == ActivityTier.Warm || state.IsVisible)
                        return true;
                }
            }
            reason = "blocked: inactive/cold consist, not pulling remote parked stock into render bubble";
            return false;
        }

        private static bool ApplyAutomaticHandoff(string consistKey, List<CarState> states, string currentBubbleId, Vector3d currentOrigin, PhysicsBubble targetBubble, Vector3d center, double oldDistance, double newDistance, out string status)
        {')

# Avoid repeated reflection/GetComponentsInChildren scans once we have found a physics body.
$text = $text.Replace(
'            if (state == null) return;
            List<RigidCandidate> rigidCandidates = new List<RigidCandidate>();',
'            if (state == null) return;
            if (state.PhysicsRigidbody != null)
            {
                rb = state.PhysicsRigidbody;
                rbSource = string.IsNullOrEmpty(state.RigidbodySource) ? "cached-rigidbody" : state.RigidbodySource;
                transform = rb.transform;
                transformSource = "cached-rigidbody";
                return;
            }
            if (state.Rigidbody != null)
            {
                rb = state.Rigidbody;
                rbSource = string.IsNullOrEmpty(state.RigidbodySource) ? "cached-root" : state.RigidbodySource;
                transform = rb.transform;
                transformSource = "cached-root";
                state.PhysicsRigidbody = rb;
                state.PhysicsTransform = transform;
                return;
            }
            if (state.PhysicsTransform != null)
            {
                transform = state.PhysicsTransform;
                transformSource = string.IsNullOrEmpty(state.PhysicsTransformSource) ? "cached-transform" : state.PhysicsTransformSource;
                return;
            }
            List<RigidCandidate> rigidCandidates = new List<RigidCandidate>();')

# This branch is for actively testing real handoffs. Keep auto enabled, but cap it so a large save
# cannot immediately move every remote stopped consist.
$text = $text.Replace(
'            Settings.EnableAutomaticHandoff = true;',
'            Settings.EnableAutomaticHandoff = true;
            Settings.AutoHandoffOnlyHotOrWarm = false;')

$dir = Split-Path -Parent $OutputPath
if ($dir -and !(Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
Set-Content -Path $OutputPath -Value $text -Encoding UTF8
