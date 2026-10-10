using UnityEngine;

/// <summary>
/// Gate for high-volume diagnostic logs (per AI character, per PC grant). In player builds
/// every Debug.Log also captured a managed stack trace and hit Player.log on disk, which was a
/// measurable share of AI-turn time; builds now log those lines without stack traces and the
/// chattiest ones not at all.
/// </summary>
public static class DevLog
{
    public static bool Verbose = Application.isEditor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ConfigureStackTraces()
    {
        if (Application.isEditor) return;
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
        Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
    }
}
