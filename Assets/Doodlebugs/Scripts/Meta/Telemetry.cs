using System.Text;
using UnityEngine;

/// <summary>
/// The one place events are sent from (ROADMAP Phase 0, `Scripts/Meta/Telemetry.cs`).
/// Until UGS Analytics is wired in this only logs - Phase 0 replaces the
/// body, the call sites stay where they are. Event names and fields follow
/// the Phase 0 taxonomy in ROADMAP.md.
/// </summary>
public static class Telemetry
{
    public static void Log(string name, params (string key, object value)[] fields)
    {
        var sb = new StringBuilder("[Telemetry] ").Append(name);
        foreach (var (key, value) in fields)
        {
            sb.Append(' ').Append(key).Append('=').Append(value);
        }
        Debug.Log(sb.ToString());
    }
}
