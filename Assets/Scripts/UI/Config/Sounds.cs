using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public class Sounds : SearcherByName
{
    [System.Serializable]
    public class NamedVoiceProfile
    {
        public string profileId;
        public List<string> characterNames = new();
        public List<AudioClip> selectClips = new();
    }

    [System.Serializable]
    public class SharedVoiceProfile
    {
        public string profileId;
        public RacesEnum race;
        public SexEnum sex;
        public List<AudioClip> selectClips = new();
    }

    private class VoiceSet
    {
        public AudioClip attack;
        public AudioClip effort;
        public AudioClip pain;
        public int voiceIndex;
    }

    public static Sounds Instance { get; private set; }

    [Header("Audio")]
    public AudioSource soundAudioSource;

    [Header("UI SFX")]
    public List<AudioClip> uiHoverClips = new();
    public List<AudioClip> uiClickClips = new();
    public List<AudioClip> uiDeniedClips = new();
    public List<AudioClip> uiPositiveClips = new();
    public List<AudioClip> uiNegativeClips = new();
    public List<AudioClip> uiExitClips = new();

    [Header("Notifications")]
    public List<AudioClip> messageClips = new();
    public List<AudioClip> artifactFoundClips = new();

    [Header("Voice - Named Characters")]
    public List<NamedVoiceProfile> namedVoiceProfiles = new();

    [Header("Voice - Shared Race and Sex")]
    public List<SharedVoiceProfile> sharedVoiceProfiles = new();

    [Header("Voice - Combat")]
    public List<AudioClip> voiceAttackMaleClips = new();
    public List<AudioClip> voiceAttackFemaleClips = new();
    public List<AudioClip> voiceEffortMaleClips = new();
    public List<AudioClip> voiceEffortFemaleClips = new();
    public List<AudioClip> voicePainMaleClips = new();
    public List<AudioClip> voicePainFemaleClips = new();

    [Header("Action SFX")]
    public List<AudioClip> actionExecuteClips = new();
    public List<AudioClip> actionSuccessDefaultClips = new();
    public List<AudioClip> speedUpClips = new();
    public List<AudioClip> actionSuccessIceClips = new();
    public List<AudioClip> actionSuccessFireClips = new();
    public List<AudioClip> actionSuccessLightningClips = new();
    public List<AudioClip> actionSuccessHealClips = new();
    public List<AudioClip> actionSuccessCurseClips = new();
    public List<AudioClip> actionSuccessTeleportClips = new();
    public List<AudioClip> actionSuccessScryClips = new();
    public List<AudioClip> actionSuccessArtifactClips = new();
    public List<AudioClip> actionSuccessCoinClips = new();
    public List<AudioClip> actionSuccessBuildClips = new();
    public List<AudioClip> actionSuccessAttackClips = new();
    public List<AudioClip> actionSuccessDestroyClips = new();
    public List<AudioClip> actionFailClips = new();

    [Header("Combat/Magic SFX")]
    public List<AudioClip> combatImpactClips = new();
    public List<AudioClip> magicClips = new();
    public List<AudioClip> buffClips = new();
    public List<AudioClip> coinClips = new();
    public List<AudioClip> keyClips = new();

    [Header("Movement SFX")]
    public float footstepMinInterval = 0.12f;
    public float footstepVolume = 1.6f;
    public float voiceVolume = 2.0f;
    public List<AudioClip> footstepForestClips = new();
    public List<AudioClip> footstepGrasslandsClips = new();
    public List<AudioClip> footstepPlainsClips = new();
    public List<AudioClip> footstepHillsClips = new();
    public List<AudioClip> footstepMountainsClips = new();
    public List<AudioClip> footstepDesertClips = new();
    public List<AudioClip> footstepSwampClips = new();
    public List<AudioClip> footstepWastelandsClips = new();
    public List<AudioClip> footstepShoreClips = new();
    public List<AudioClip> footstepDefaultClips = new();
    public List<AudioClip> movementWaterClips = new();

    [Header("Queue")]
    public int maxQueueSize = 8;
    public float globalMinInterval = 0.05f;

    [Header("Diagnostics")]
    [Tooltip("Logs every clip that actually reaches AudioSource.PlayOneShot, including its external caller and game state.")]
    public bool logSoundPlayback = true;

    private readonly Queue<SfxRequest> queue = new();
    private readonly Dictionary<string, float> lastPlayByKey = new();
    private readonly Dictionary<string, AudioClip> stablePickByKey = new();
    private readonly Dictionary<string, int> nextVoiceClipIndexByKey = new();
    private float lastPlayTime = -999f;
    private Coroutine queueRoutine;
    private float lastFootstepTime = -999f;
    private readonly Dictionary<EntityId, VoiceSet> voiceSetByCharacterId = new();
    private Game cachedGame;

    private struct SfxRequest
    {
        public AudioClip clip;
        public float volume;
        public float minInterval;
        public string key;
        public string origin;
    }

    private void Awake()
    {
        EnsureAudioClipLists();
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (soundAudioSource != null && soundAudioSource.volume < 1f)
        {
            soundAudioSource.volume = 1f;
            soundAudioSource.spatialBlend = 0f;
        }

        cachedGame = Game.Instance;
        PrewarmVoiceSets();
    }

    private void OnValidate()
    {
        EnsureAudioClipLists();
    }

    private void EnsureAudioClipLists()
    {
        namedVoiceProfiles ??= new List<NamedVoiceProfile>();
        sharedVoiceProfiles ??= new List<SharedVoiceProfile>();
        var fields = GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var field in fields)
        {
            if (field.FieldType == typeof(List<AudioClip>) && field.GetValue(this) == null)
            {
                field.SetValue(this, new List<AudioClip>());
            }
        }
    }

    public void StopAllSounds()
    {
        if (soundAudioSource != null) soundAudioSource.Stop();
        queue.Clear();
    }

    public void PlayUiHover()
    {
        EnqueueFromList(uiHoverClips, "ui_hover", 0.08f, 0.7f);
    }

    public void PlayUiClick()
    {
        EnqueueFromList(uiClickClips, "ui_click", 0.06f, 0.8f);
    }

    public void PlayUiDenied()
    {
        EnqueueFromList(uiDeniedClips, "ui_denied", 0.2f, 0.9f);
    }

    public void PlayPositive()
    {
        EnqueueFromList(uiPositiveClips, "ui_positive", 0.2f, 0.85f);
    }

    public void PlayNegative()
    {
        EnqueueFromList(uiNegativeClips, "ui_negative", 0.25f, 0.9f);
    }

    public void PlayVoiceExpression(Character character)
    {
        if (character == null) return;
        if (soundAudioSource == null) return;
        if (!PlayerCanSeeHex(character.hex)) return;
        if (character.race == RacesEnum.Machine) return;

        if (TryPlayNamedVoiceExpression(character.characterName, character.GetEntityId().ToString())) return;
        TryPlaySharedVoiceExpression(character);
    }

    public void PlayVoiceAttack(Character character)
    {
        if (character == null) return;
        if (soundAudioSource == null) return;
        if (!PlayerCanSeeHex(character.hex)) return;
        if (!IsHumanoidRace(character.race)) return;

        var voiceSet = GetOrCreateVoiceSet(character);
        if (voiceSet == null || !voiceSet.attack) return;
        Enqueue(voiceSet.attack, voiceVolume, 0.4f);
    }

    public void PlayVoiceEffort(Character character)
    {
        if (character == null) return;
        if (soundAudioSource == null) return;
        if (!PlayerCanSeeHex(character.hex)) return;
        if (!IsHumanoidRace(character.race)) return;

        var voiceSet = GetOrCreateVoiceSet(character);
        if (voiceSet == null || !voiceSet.effort) return;
        Enqueue(voiceSet.effort, voiceVolume, 0.4f);
    }

    public void PlayVoicePain(Character character)
    {
        if (character == null) return;
        if (soundAudioSource == null) return;
        if (!PlayerCanSeeHex(character.hex)) return;
        if (!IsHumanoidRace(character.race)) return;

        var voiceSet = GetOrCreateVoiceSet(character);
        if (voiceSet == null || !voiceSet.pain) return;
        Enqueue(voiceSet.pain, voiceVolume, 0.4f);
    }

    public void PlayVoiceForAction(Character character, string actionName)
    {
        if (character == null || string.IsNullOrWhiteSpace(actionName)) return;
        if (!IsHumanoidRace(character.race)) return;

        string lower = actionName.ToLowerInvariant();
        if (lower.Contains("attack") || lower.Contains("assassinate") || lower.Contains("wound") || lower.Contains("siege"))
        {
            PlayVoiceAttack(character);
            return;
        }

        if (lower.Contains("train") || lower.Contains("fortify") || lower.Contains("fortification") || lower.Contains("destroy")
            || lower.Contains("sabotage") || lower.Contains("block") || lower.Contains("create camp")
            || lower.Contains("post camp") || lower.Contains("build") || lower.Contains("remove"))
        {
            PlayVoiceEffort(character);
        }
    }

    public void PlayUiExit()
    {
        EnqueueFromList(uiExitClips, "ui_exit", 0.12f, 0.6f);
    }

    public void PlayActionExecute()
    {
        EnqueueFromList(actionExecuteClips, "action_execute", 0.1f, 0.7f);
    }

    public void PlayActionSuccess()
    {
        EnqueueFromList(actionSuccessDefaultClips, "action_success_default", 0.3f, 0.9f);
    }

    public void PlaySpeedUp()
    {
        EnqueueFromList(speedUpClips, "speed_up", 0.12f, 0.85f);
    }

    public void PlayActionSuccess(string actionName)
    {
        if (string.IsNullOrWhiteSpace(actionName))
        {
            PlayActionSuccess();
            return;
        }

        string lower = actionName.ToLowerInvariant();

        if (lower.Contains("ice") || lower.Contains("frost"))
        {
            EnqueueFromList(actionSuccessIceClips, "action_success_ice", 0.18f, 0.9f);
            return;
        }
        if (lower.Contains("fire") || lower.Contains("flame") || lower.Contains("burn"))
        {
            EnqueueFromList(actionSuccessFireClips, "action_success_fire", 0.18f, 0.9f);
            return;
        }
        if (lower.Contains("lightning") || lower.Contains("storm"))
        {
            EnqueueFromList(actionSuccessLightningClips, "action_success_lightning", 0.18f, 0.9f);
            return;
        }
        if (lower.Contains("heal") || lower.Contains("cure") || lower.Contains("restore") || lower.Contains("buff") || lower.Contains("courage"))
        {
            EnqueueFromList(actionSuccessHealClips, "action_success_heal", 0.2f, 0.85f);
            return;
        }
        if (lower.Contains("curse") || lower.Contains("halt") || lower.Contains("darkness"))
        {
            EnqueueFromList(actionSuccessCurseClips, "action_success_curse", 0.2f, 0.85f);
            return;
        }
        if (lower.Contains("teleport") || lower.Contains("return"))
        {
            EnqueueFromList(actionSuccessTeleportClips, "action_success_teleport", 0.2f, 0.85f);
            return;
        }
        if (lower.Contains("scry") || lower.Contains("reveal") || lower.Contains("perceive"))
        {
            EnqueueFromList(actionSuccessScryClips, "action_success_scry", 0.2f, 0.7f);
            return;
        }
        if (lower.Contains("find artifact") || lower.Contains("artifact"))
        {
            EnqueueFromList(actionSuccessArtifactClips, "action_success_artifact", 0.2f, 0.8f);
            return;
        }
        if (lower.Contains("buy") || lower.Contains("sell") || lower.Contains("steal") || lower.Contains("gold"))
        {
            EnqueueFromList(actionSuccessCoinClips, "action_success_coin", 0.12f, 0.8f);
            return;
        }
        if (lower.Contains("train") || lower.Contains("fortify") || lower.Contains("create camp") || lower.Contains("post camp"))
        {
            EnqueueFromList(actionSuccessBuildClips, "action_success_build", 0.2f, 0.75f);
            return;
        }
        if (lower.Contains("attack") || lower.Contains("siege") || lower.Contains("assassinate") || lower.Contains("wound"))
        {
            EnqueueFromList(actionSuccessAttackClips, "action_success_attack", 0.12f, 0.85f);
            return;
        }
        if (lower.Contains("destroy") || lower.Contains("sabotage"))
        {
            EnqueueFromList(actionSuccessDestroyClips, "action_success_destroy", 0.12f, 0.85f);
            return;
        }

        PlayActionSuccess();
    }

    public void PlayActionFail()
    {
        EnqueueFromList(actionFailClips, "action_fail", 0.3f, 0.9f);
    }

    public void PlayMessage()
    {
        EnqueueFromList(messageClips, "message", 0.15f, 0.6f);
    }

    public void PlayArtifactFound()
    {
        EnqueueFromList(artifactFoundClips, "artifact_found", 0.2f, 0.9f);
    }

    public void PlayMovement(Hex from, Hex to)
    {
        if (ShouldSuppressBackgroundNplAudio()) return;
        if (soundAudioSource == null) return;

        List<AudioClip> clips = footstepDefaultClips;

        if (to != null)
        {
            if (to.IsWaterTerrain() || to.terrainType == TerrainEnum.shore)
            {
                clips = movementWaterClips;
            }
            else
            {
                clips = GetFootstepClipsForTerrain(to.terrainType);
            }
        }

        if (Time.time - lastFootstepTime < footstepMinInterval) return;
        var clip = PickRandomClip(clips);
        if (!clip) return;

        string origin = CaptureExternalCaller();
        LogPlayback("footstep", clip, footstepVolume, origin);
        soundAudioSource.PlayOneShot(clip, footstepVolume);
        lastFootstepTime = Time.time;
        lastPlayTime = Time.time;
    }

    public void PlayCombatImpact()
    {
        EnqueueRandomFromList(combatImpactClips, 0.08f, 0.85f);
    }

    public void PlayMagic()
    {
        EnqueueRandomFromList(magicClips, 0.12f, 0.85f);
    }

    public void PlayBuff()
    {
        EnqueueRandomFromList(buffClips, 0.12f, 0.8f);
    }

    public void PlayCoin()
    {
        EnqueueFromList(coinClips, "coin", 0.08f, 0.75f);
    }

    public void PlayKey()
    {
        EnqueueFromList(keyClips, "key", 0.12f, 0.75f);
    }

    private void EnqueueFromList(List<AudioClip> clips, string key, float minInterval, float volume)
    {
        var clip = PickStableClip(clips, key);
        if (!clip) return;
        Enqueue(clip, volume, minInterval);
    }

    private void EnqueueRandomFromList(List<AudioClip> clips, float minInterval, float volume)
    {
        var clip = PickRandomClip(clips);
        if (!clip) return;
        Enqueue(clip, volume, minInterval);
    }

    private void Enqueue(AudioClip clip, float volume, float minInterval)
    {
        if (ShouldSuppressBackgroundNplAudio()) return;
        if (clip == null || soundAudioSource == null) return;
        float now = Time.time;
        if (now - lastPlayTime < globalMinInterval) return;

        string key = Normalize(clip.name);
        if (lastPlayByKey.TryGetValue(key, out float lastTime) && now - lastTime < minInterval) return;

        if (queue.Count >= maxQueueSize) queue.Dequeue();
        queue.Enqueue(new SfxRequest
        {
            clip = clip,
            volume = volume,
            minInterval = minInterval,
            key = key,
            origin = CaptureExternalCaller()
        });

        if (queueRoutine == null) queueRoutine = StartCoroutine(ProcessQueue());
    }

    private static bool ShouldSuppressBackgroundNplAudio()
    {
        return AITurnController.CurrentExecutingLeader is NonPlayableLeader;
    }

    private IEnumerator ProcessQueue()
    {
        while (queue.Count > 0)
        {
            var req = queue.Dequeue();
            LogPlayback(req.key, req.clip, req.volume, req.origin);
            soundAudioSource.PlayOneShot(req.clip, req.volume);
            lastPlayByKey[req.key] = Time.time;
            lastPlayTime = Time.time;
            float spacing = Mathf.Clamp(req.minInterval, 0.02f, 0.35f);
            yield return new WaitForSeconds(spacing);
        }
        queueRoutine = null;
    }

    private static string CaptureExternalCaller()
    {
        var trace = new System.Diagnostics.StackTrace(1, false);
        for (int i = 0; i < trace.FrameCount; i++)
        {
            System.Reflection.MethodBase method = trace.GetFrame(i)?.GetMethod();
            if (method == null || method.DeclaringType == typeof(Sounds)) continue;
            return $"{method.DeclaringType?.Name}.{method.Name}";
        }
        return "unknown";
    }

    private void LogPlayback(string key, AudioClip clip, float volume, string origin)
    {
        if (!logSoundPlayback) return;
        Game game = Game.Instance;
        string current = game?.currentlyPlaying?.characterName ?? "none";
        string aiActor = AITurnController.CurrentExecutingLeader?.characterName ?? "none";
        Debug.Log($"[SoundTrace] PLAY frame={Time.frameCount} time={Time.time:F3} clip='{clip?.name ?? "null"}' key='{key}' volume={volume:F2} origin={origin ?? "unknown"} turn={game?.turn ?? -1} current='{current}' aiActor='{aiActor}'");
    }

    private AudioClip PickStableClip(List<AudioClip> clips, string key)
    {
        if (clips == null || clips.Count == 0) return null;
        if (stablePickByKey.TryGetValue(key, out var cached) && cached != null) return cached;

        AudioClip chosen = null;
        for (int i = 0; i < clips.Count; i++)
        {
            var candidate = clips[UnityEngine.Random.Range(0, clips.Count)];
            if (candidate != null)
            {
                chosen = candidate;
                break;
            }
        }

        if (chosen == null)
        {
            foreach (var candidate in clips)
            {
                if (candidate != null)
                {
                    chosen = candidate;
                    break;
                }
            }
        }

        if (chosen != null)
        {
            stablePickByKey[key] = chosen;
        }
        return chosen;
    }

    private AudioClip PickRotatingVoiceClip(List<AudioClip> clips, string key)
    {
        if (clips == null || clips.Count == 0) return null;

        if (!nextVoiceClipIndexByKey.TryGetValue(key, out int startIndex))
        {
            startIndex = UnityEngine.Random.Range(0, clips.Count);
        }

        for (int offset = 0; offset < clips.Count; offset++)
        {
            int index = (startIndex + offset) % clips.Count;
            AudioClip candidate = clips[index];
            if (candidate == null) continue;
            nextVoiceClipIndexByKey[key] = (index + 1) % clips.Count;
            return candidate;
        }

        return null;
    }

    private static AudioClip PickRandomClip(List<AudioClip> clips)
    {
        if (clips == null || clips.Count == 0) return null;
        for (int i = 0; i < clips.Count; i++)
        {
            var candidate = clips[UnityEngine.Random.Range(0, clips.Count)];
            if (candidate != null) return candidate;
        }

        foreach (var candidate in clips)
        {
            if (candidate != null) return candidate;
        }

        return null;
    }

    private List<AudioClip> GetFootstepClipsForTerrain(TerrainEnum terrain)
    {
        return terrain switch
        {
            TerrainEnum.forest => footstepForestClips,
            TerrainEnum.grasslands => footstepGrasslandsClips,
            TerrainEnum.plains => footstepPlainsClips,
            TerrainEnum.hills => footstepHillsClips,
            TerrainEnum.mountains => footstepMountainsClips,
            TerrainEnum.desert => footstepDesertClips,
            TerrainEnum.swamp => footstepSwampClips,
            TerrainEnum.wastelands => footstepWastelandsClips,
            TerrainEnum.shore => footstepShoreClips,
            _ => footstepDefaultClips
        };
    }

    private AudioClip PickVoiceClip(VoiceType type, SexEnum sex, int voiceIndex)
    {
        var clips = GetVoiceClips(type, sex);
        if (clips == null || clips.Count == 0) return null;
        int index = Mathf.Abs(voiceIndex) % clips.Count;
        for (int i = 0; i < clips.Count; i++)
        {
            var candidate = clips[(index + i) % clips.Count];
            if (candidate != null) return candidate;
        }
        return null;
    }

    private List<AudioClip> GetVoiceClips(VoiceType type, SexEnum sex)
    {
        bool isFemale = sex == SexEnum.Female;
        return type switch
        {
            VoiceType.Attack => isFemale ? voiceAttackFemaleClips : voiceAttackMaleClips,
            VoiceType.Effort => isFemale ? voiceEffortFemaleClips : voiceEffortMaleClips,
            VoiceType.Pain => isFemale ? voicePainFemaleClips : voicePainMaleClips,
            _ => null
        };
    }

    private static bool IsHumanoidRace(RacesEnum race)
    {
        return race is RacesEnum.Common or RacesEnum.Elf or RacesEnum.Dwarf or RacesEnum.Hobbit
            or RacesEnum.Maia or RacesEnum.Dunedain or RacesEnum.Beorning or RacesEnum.Wildman or RacesEnum.Ent
            or RacesEnum.Southron or RacesEnum.Easterling;
    }

    private bool PlayerCanSeeHex(Hex hex)
    {
        if (hex == null) return false;
        Game g = GetGame();
        if (g == null || g.player == null) return false;
        return g.player.visibleHexes.Contains(hex) && hex.IsHexSeen();
    }

    public bool PlayNamedVoiceExpression(string characterName, bool interruptQueuedAudio = false)
    {
        if (string.IsNullOrWhiteSpace(characterName) || soundAudioSource == null) return false;
        return TryPlayNamedVoiceExpression(characterName, "ui", interruptQueuedAudio);
    }

    private bool TryPlayNamedVoiceExpression(string characterName, string rotationKey, bool interruptQueuedAudio = false)
    {
        if (string.IsNullOrWhiteSpace(characterName)) return false;
        if (namedVoiceProfiles == null || namedVoiceProfiles.Count == 0) return false;

        foreach (NamedVoiceProfile profile in namedVoiceProfiles)
        {
            if (profile == null || profile.characterNames == null || profile.selectClips == null) continue;
            bool matches = false;
            foreach (string configuredName in profile.characterNames)
            {
                if (!string.Equals(configuredName, characterName, System.StringComparison.OrdinalIgnoreCase)) continue;
                matches = true;
                break;
            }
            if (!matches) continue;

            string profileKey = string.IsNullOrWhiteSpace(profile.profileId) ? characterName : profile.profileId;
            AudioClip clip = PickRotatingVoiceClip(profile.selectClips, $"voice_named_{profileKey}_{rotationKey}");
            if (!clip) return false;
            if (interruptQueuedAudio)
            {
                soundAudioSource.Stop();
                queue.Clear();
            }
            Enqueue(clip, voiceVolume, 0.5f);
            return true;
        }

        return false;
    }

    private bool TryPlaySharedVoiceExpression(Character character)
    {
        if (character == null || sharedVoiceProfiles == null || sharedVoiceProfiles.Count == 0) return false;

        foreach (SharedVoiceProfile profile in sharedVoiceProfiles)
        {
            if (profile == null || profile.race != character.race || profile.sex != character.sex) continue;
            string profileKey = string.IsNullOrWhiteSpace(profile.profileId)
                ? $"{character.race}_{character.sex}"
                : profile.profileId;
            AudioClip clip = PickRotatingVoiceClip(profile.selectClips, $"voice_shared_{profileKey}_{character.GetEntityId()}");
            if (!clip) return false;
            Enqueue(clip, voiceVolume, 0.5f);
            return true;
        }

        return false;
    }

    private VoiceSet GetOrCreateVoiceSet(Character character)
    {
        EntityId key = character.GetEntityId();
        if (voiceSetByCharacterId.TryGetValue(key, out var set)) return set;

        int voiceIndex = GetVoiceIndex(character.sex);
        if (voiceIndex < 0) return null;

        var created = new VoiceSet
        {
            voiceIndex = voiceIndex,
            attack = PickVoiceClip(VoiceType.Attack, character.sex, voiceIndex),
            effort = PickVoiceClip(VoiceType.Effort, character.sex, voiceIndex),
            pain = PickVoiceClip(VoiceType.Pain, character.sex, voiceIndex)
        };
        voiceSetByCharacterId[key] = created;
        return created;
    }

    private Game GetGame()
    {
        if (cachedGame == null) cachedGame = Game.Instance;
        return cachedGame;
    }

    private void PrewarmVoiceSets()
    {
        Character[] characters = FindObjectsByType<Character>();
        if (characters == null || characters.Length == 0) return;

        for (int i = 0; i < characters.Length; i++)
        {
            Character character = characters[i];
            if (character == null) continue;
            if (!IsHumanoidRace(character.race)) continue;
            GetOrCreateVoiceSet(character);
        }
    }

    private int GetVoiceIndex(SexEnum sex)
    {
        int maxCount = 0;
        maxCount = Mathf.Max(maxCount, GetVoiceClips(VoiceType.Attack, sex).Count);
        maxCount = Mathf.Max(maxCount, GetVoiceClips(VoiceType.Effort, sex).Count);
        maxCount = Mathf.Max(maxCount, GetVoiceClips(VoiceType.Pain, sex).Count);
        if (maxCount == 0) return -1;
        return UnityEngine.Random.Range(0, maxCount);
    }

    private enum VoiceType
    {
        Attack,
        Effort,
        Pain
    }
}
