using System.Collections;
using UnityEngine;
using UnityEngine.Video;
using RetroLOTR.Scenarios;

public class IntroVideoManager : MonoBehaviour
{
    private BoardGenerator boardGenerator;
    private VideoPlayer vp;

    void Start()
    {
        boardGenerator = GameObject.Find("Board").GetComponent<BoardGenerator>();

        // Scenario selection reloads the scene; don't replay the intro on those rebuilds.
        // The generation frame budget must still be released — it is only throttled to
        // keep video playback smooth, and there is no video here.
        if (GameConfig.SkipIntro)
        {
            boardGenerator.SetVideoPlaying(false);
            gameObject.SetActive(false);
            return;
        }

        vp = GetComponent<VideoPlayer>();
        vp.playOnAwake = false;
        vp.Stop();
        vp.loopPointReached += OnVideoFinished;     // fires at end (and on each loop)
        vp.errorReceived += OnVideoError;
        vp.started += OnVideoStarted;

        // The intro waits for the scenario-selection screen: nothing plays until
        // the player has chosen what to play.
        StartCoroutine(PlayAfterScenarioChoice());
    }

    private IEnumerator PlayAfterScenarioChoice()
    {
        yield return new WaitUntil(() => GameConfig.ScenarioChosen);
        SkinManager skinManager = FindAnyObjectByType<SkinManager>();
        vp.clip = skinManager != null ? skinManager.GetIntroVideo() : null;
        if (vp.clip == null)
        {
            // No intro to protect — release the generation throttle immediately.
            boardGenerator.SetVideoPlaying(false);
            gameObject.SetActive(false);
            yield break;
        }

        // Intro clips carry their own soundtrack. Route the first embedded audio
        // track directly and keep game music silent until Game.StartGame().
        vp.audioOutputMode = VideoAudioOutputMode.Direct;
        if (vp.clip.audioTrackCount > 0)
        {
            vp.controlledAudioTrackCount = 1;
            vp.EnableAudioTrack(0, true);
            vp.SetDirectAudioMute(0, false);
            vp.SetDirectAudioVolume(0, 1f);
        }
        Music.Instance?.StopMusicForVideo();
        vp.Play();
    }

    void Update()
    {
        // Any key or click skips the intro — and releases the generation
        // throttle so the board finishes loading at full speed.
        if (vp != null && vp.isPlaying && Input.anyKeyDown)
        {
            vp.Stop();
            boardGenerator.SetVideoPlaying(false);
            gameObject.SetActive(false);
        }
    }

    void OnVideoStarted(VideoPlayer p)
    {
        Music.Instance?.StopMusicForVideo();
        boardGenerator.SetVideoPlaying(true);
    }
    void OnVideoFinished(VideoPlayer p)
    {
        boardGenerator.SetVideoPlaying(false);
    }

    void OnVideoError(VideoPlayer p, string msg)
    {
        boardGenerator.SetVideoPlaying(false);
    }

    private void OnDisable()
    {
        if (vp != null && vp.isPlaying) vp.Stop();
    }
}
