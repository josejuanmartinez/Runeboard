using UnityEngine;

public static class ComponentExtensions
{
    // `GetComponent<T>() ?? AddComponent<T>()` is wrong for Unity objects: a missing component comes
    // back as a fake-null that C#'s `??` treats as non-null, so AddComponent never runs.
    public static T GetOrAddComponent<T>(this GameObject go) where T : Component
    {
        return go.TryGetComponent(out T component) ? component : go.AddComponent<T>();
    }

    public static T GetOrAddComponent<T>(this Component owner) where T : Component
    {
        return owner.gameObject.GetOrAddComponent<T>();
    }
}
