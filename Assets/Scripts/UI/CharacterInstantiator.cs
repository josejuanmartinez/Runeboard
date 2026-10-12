using UnityEngine;

public class CharacterInstantiator : MonoBehaviour
{
    public Transform leadersParent;
    public Transform nonPlayableLeadersParent;
    public Transform otherCharactersParent;

    private GameObject characterTemplate;
    private GameObject playableLeaderTemplate;
    private GameObject nonPlayableLeaderTemplate;

    private void Awake()
    {
        // Pre-build tiny hidden templates so runtime instantiation never has to call AddComponent/constructor reflection.
        characterTemplate = CreateTemplate<Character>("CharacterTemplate");
        playableLeaderTemplate = CreateTemplate<PlayableLeader>("PlayableLeaderTemplate");
        nonPlayableLeaderTemplate = CreateTemplate<NonPlayableLeader>("NonPlayableLeaderTemplate");
    }

    public Character InstantiateCharacter(Leader leader, Hex hex, BiomeConfig biomeConfig)
    {
        Character character = InstantiateFromTemplate<Character>(characterTemplate, biomeConfig.characterName, otherCharactersParent);
        character.InitializeFromBiome(leader, hex, biomeConfig, showSpawnMessage: false);
        CharacterIcons.RefreshForHumanPlayerOf(leader);
        UnitAnimations.Recruited(character); // no-op during setup and save loading
        return character;
    }
    // applyNoScenarioStart: pass true only from procedural (non-scenario) leader placement —
    // it's what tells InitializeFromBiome to create the leader's default army/uses its biome's
    // noScenarioStart data. An authored scenario provides its own army/PC data per leader and
    // must never also get this procedural default (see Character.InitializeFromBiome).
    public PlayableLeader InstantiatePlayableLeader(Hex hex, LeaderBiomeConfig leaderBiomeConfig, bool applyNoScenarioStart = false)
    {
        PlayableLeader playableLeader = InstantiateFromTemplate<PlayableLeader>(playableLeaderTemplate, leaderBiomeConfig.characterName, leadersParent);
        playableLeader.Initialize(hex, leaderBiomeConfig, showSpawnMessage: false, applyNoScenarioStart: applyNoScenarioStart);
        return playableLeader;
    }
    public NonPlayableLeader InstantiateNonPlayableLeader(Hex hex, NonPlayableLeaderBiomeConfig nonPlayableLeaderBiomeConfig, bool applyNoScenarioStart = false)
    {
        NonPlayableLeader nonPlayableLeader = InstantiateFromTemplate<NonPlayableLeader>(nonPlayableLeaderTemplate, nonPlayableLeaderBiomeConfig.characterName, nonPlayableLeadersParent);
        nonPlayableLeader.Initialize(hex, nonPlayableLeaderBiomeConfig, showSpawnMessage: false, applyNoScenarioStart: applyNoScenarioStart);
        return nonPlayableLeader;
    }

    private GameObject CreateTemplate<T>(string templateName) where T : Component
    {
        var template = new GameObject(templateName);
        template.transform.SetParent(transform, false);
        template.hideFlags = HideFlags.HideAndDontSave;
        template.SetActive(false);
        template.AddComponent<T>();
        return template;
    }

    private T InstantiateFromTemplate<T>(GameObject template, string instanceName, Transform parent) where T : Component
    {
        var instance = Instantiate(template, parent, false);
        instance.hideFlags = HideFlags.None;
        instance.name = instanceName;
        instance.SetActive(true);
        return instance.GetComponent<T>();
    }
}
