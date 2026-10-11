using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public class TutorialInstructionsManager : MonoBehaviour
{
    [Serializable]
    private class SaveData
    {
        public List<string> closedInstructionIds = new();
    }

    private const string SaveFileName = "tutorial_instructions.json";
    private static TutorialInstructionsManager instance;

    private readonly List<TutorialInstructionPopup> instructions = new();
    private readonly HashSet<string> closedInstructionIds = new(StringComparer.Ordinal);
    private TutorialInstructionPopup current;

    public static TutorialInstructionsManager Instance
    {
        get
        {
            EnsureInstance();
            return instance;
        }
    }

    // True while an instruction popup is open. Game.StartGame() polls this to hold the
    // turn-0 start (and every hover/click/camera gate keyed off BoardNavigator's input
    // lock and TurnBanner) until the player has closed every instruction.
    public bool IsShowing => current != null;

    private string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
        instance.StartCoroutine(instance.InitializeSceneNextFrame());
    }

    private static void EnsureInstance()
    {
        if (instance != null) return;

        instance = FindAnyObjectByType<TutorialInstructionsManager>();
        if (instance != null) return;

        GameObject managerObject = new(nameof(TutorialInstructionsManager));
        instance = managerObject.AddComponent<TutorialInstructionsManager>();
        // Keep fallback instances scoped to the scene that created them. If this object
        // survives into InGame, its Awake runs before the scene-authored manager and makes
        // that manager destroy itself as a duplicate. The tutorial popups are children of
        // the scene-authored manager, and the Tutorial button also targets it, so destroying
        // it leaves no instructions to show and a dead UnityEvent target.
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        Load();
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        instance = null;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        StartCoroutine(InitializeSceneNextFrame());
    }

    // Only populates and sorts the queue — does not open anything. Instructions are meant
    // to appear as part of the game-start sequence (see Game.StartGame), not the instant a
    // scene loads (which would fire them over the leader-select screen, before a turn even
    // exists). Callers that want the queue shown immediately call OpenNext() themselves.
    private IEnumerator InitializeSceneNextFrame()
    {
        yield return null;
        RefreshInstructions();
    }

    private void RefreshInstructions()
    {
        instructions.Clear();
        instructions.AddRange(FindObjectsByType<TutorialInstructionPopup>(
            FindObjectsInactive.Include));

        instructions.RemoveAll(instruction => instruction == null ||
            instruction.gameObject.scene != SceneManager.GetActiveScene());
        instructions.Sort(CompareInstructions);

        foreach (TutorialInstructionPopup instruction in instructions)
        {
            instruction.gameObject.SetActive(false);
        }

        current = null;
    }

    private static int CompareInstructions(TutorialInstructionPopup left, TutorialInstructionPopup right)
    {
        int order = left.SequenceOrder.CompareTo(right.SequenceOrder);
        return order != 0
            ? order
            : EditorLikeNaturalName(left.name).CompareTo(EditorLikeNaturalName(right.name));
    }

    private static string EditorLikeNaturalName(string objectName)
    {
        int openParen = objectName.LastIndexOf('(');
        if (openParen >= 0 && objectName.EndsWith(")", StringComparison.Ordinal) &&
            int.TryParse(objectName.Substring(openParen + 1, objectName.Length - openParen - 2), out int suffix))
        {
            return objectName.Substring(0, openParen) + suffix.ToString("D8");
        }

        return objectName + "00000000";
    }

    public void Close(TutorialInstructionPopup instruction, GameObject activateAfterClose = null)
    {
        if (instruction == null) return;

        closedInstructionIds.Add(instruction.TutorialId);
        Save();
        instruction.gameObject.SetActive(false);
        if (current == instruction) current = null;

        if (activateAfterClose != null)
        {
            activateAfterClose.SetActive(true);
            current = activateAfterClose.GetComponent<TutorialInstructionPopup>();
            return;
        }

        OpenNext();
    }

    public void OpenNext()
    {
        if (current != null && current.gameObject.activeSelf) return;

        current = instructions.FirstOrDefault(instruction =>
            instruction != null && !closedInstructionIds.Contains(instruction.TutorialId));
        if (current != null)
        {
            current.gameObject.SetActive(true);
        }
    }

    public void ResetProgress()
    {
        Debug.Log($"[DIAG] ResetProgress ENTERED on instance id={GetEntityId()}, gameObject={name}");
        closedInstructionIds.Clear();
        Save();
        RefreshInstructions();
        Debug.Log($"[DIAG] after RefreshInstructions: instructions.Count={instructions.Count}, closedInstructionIds.Count={closedInstructionIds.Count}");
        OpenNext();
        Debug.Log($"[DIAG] after OpenNext: current={(current != null ? current.name : "NULL")}");
    }

    public void ResetFlagsAndShowAll()
    {
        Debug.Log($"[DIAG] ResetFlagsAndShowAll ENTERED on instance id={GetEntityId()}, gameObject={name}");
        ResetProgress();
    }

    public static string BuildId(GameObject instruction)
    {
        string hierarchyPath = instruction.name;
        Transform parent = instruction.transform.parent;
        while (parent != null)
        {
            hierarchyPath = parent.name + "/" + hierarchyPath;
            parent = parent.parent;
        }

        return instruction.scene.path + ":" + hierarchyPath;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(SavePath)) return;
            SaveData data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
            if (data?.closedInstructionIds == null) return;
            closedInstructionIds.UnionWith(data.closedInstructionIds);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Could not load tutorial instruction progress: {exception.Message}");
        }
    }

    private void Save()
    {
        try
        {
            SaveData data = new() { closedInstructionIds = closedInstructionIds.ToList() };
            File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Could not save tutorial instruction progress: {exception.Message}");
        }
    }
}
