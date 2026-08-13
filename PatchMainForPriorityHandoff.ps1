param(
    [Parameter(Mandatory=$true)][string]$InputPath,
    [Parameter(Mandatory=$true)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$text = Get-Content -Raw -Path $InputPath

# Keep this generated patch deliberately small and reversible.  The test branch still has the
# single-file Main.cs scaffold, so this script patches a generated compile copy without
# rewriting the checked-in Main.cs file.

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
'            if (eligibility.Ready && Main.Settings.EnableAutomaticHandoff && !Main.Settings.PrecisionDryRunOnly)',
'            if (eligibility.Ready && Main.Settings.EnableAutomaticHandoff && !Main.Settings.PrecisionDryRunOnly && PendingHandoffQueue.ShouldAttemptAutoHandoff(consistKey))')

$text = $text.Replace(
'            else if (!Main.Settings.EnableAutomaticHandoff) applyStatus = "auto disabled";
            else applyStatus = eligibility.BlockerText;',
'            else if (!Main.Settings.EnableAutomaticHandoff) applyStatus = "auto disabled";
            else if (eligibility.Ready) applyStatus = "waiting: lower priority ready handoff";
            else applyStatus = eligibility.BlockerText;')

$text = $text.Replace(
'        private static string FormatVector(Vector3d value) { return $"{value.X:F1}, {value.Y:F1}, {value.Z:F1}"; }',
'        private static double HorizontalDistance(Vector3d a, Vector3d b)
        {
            double dx = a.X - b.X;
            double dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        private static string FormatVector(Vector3d value) { return $"{value.X:F1}, {value.Y:F1}, {value.Z:F1}"; }')

$dir = Split-Path -Parent $OutputPath
if ($dir -and !(Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
Set-Content -Path $OutputPath -Value $text -Encoding UTF8
