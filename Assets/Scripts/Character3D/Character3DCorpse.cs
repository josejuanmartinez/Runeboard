using UnityEngine;

// A live 3D unit's body after Character.Killed (prototype, see UnitAnimations.Killed): detached
// from the hex it stood on, it plays a death fall, holds the last pose a moment, then dissolves
// (the shader's screen-door fade, shadow and outline included) and removes itself.
[RequireComponent(typeof(Character3DModel))]
public class Character3DCorpse : MonoBehaviour
{
    private const float LieStill = 2.4f;
    private const float FadeTime = 1.2f;

    private Character3DModel model;
    private float age;
    private float startAlpha = 1f;

    public void Begin(string deathClip)
    {
        model = GetComponent<Character3DModel>();
        startAlpha = model.Tint.a;
        model.RimColor = Color.clear; // no alignment outline on the dead
        model.Focus = 0f;             // nor a selection glow on its base
        model.Animation.PlayOnce(deathClip, 0.1f, 1f, hold: true);
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (age < LieStill) return;
        float alpha = startAlpha * Mathf.Clamp01(1f - (age - LieStill) / FadeTime);
        Color tint = model.Tint;
        tint.a = alpha;
        model.Tint = tint;
        if (alpha <= 0f) Destroy(gameObject);
    }
}
