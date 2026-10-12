using UnityEngine;

// How board characters are drawn (see Character3DView): Character3DSettings.startMode — live 3D
// models instead of the baked sprites (Replace), next to them (Side By Side) or not at all (Off).
// "-char3d-mode off|replace|sidebyside" on the command line overrides it, for unattended AutoTour
// comparisons.
public static class Character3DPrototype
{
    public static Character3DSettings.Mode Mode { get; private set; } = Character3DSettings.Mode.Off;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        Character3DSettings settings = Character3DSettings.Instance;
        if (settings == null || settings.shader == null || Character3DAnimationLibrary.Instance == null)
        {
            Debug.LogWarning("[Character3D] Resources/Character3DSettings or Character3DAnimations is missing or incomplete — 3D characters disabled.");
            Mode = Character3DSettings.Mode.Off;
            return;
        }
        Mode = settings.startMode;
        string[] args = System.Environment.GetCommandLineArgs();
        int modeArg = System.Array.IndexOf(args, "-char3d-mode");
        if (modeArg >= 0 && modeArg + 1 < args.Length
            && System.Enum.TryParse(args[modeArg + 1], true, out Character3DSettings.Mode parsed)) Mode = parsed;
    }
}
