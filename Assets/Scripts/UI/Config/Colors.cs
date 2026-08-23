using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Serialization;

public class Colors : SearcherByName
{
    public Color mountains;
    public Color hills;
    public Color forest;
    public Color grasslands;
    public Color plains;
    public Color shore;
    public Color deepWater;
    public Color shallowWater;
    public Color swamp;
    public Color desert;
    public Color wastelands;
    public Color snow;
    public Color freePeople;
    public Color neutral;
    public Color darkServants;
    [FormerlySerializedAs("pcCard")] public Color pc;
    [FormerlySerializedAs("landCard")] public Color land;
    [FormerlySerializedAs("characterCard")] public Color character;
    [FormerlySerializedAs("armyCard")] public Color army;
    [FormerlySerializedAs("eventCard")] public Color @event;
    [FormerlySerializedAs("actionCard")] public Color action;
    [FormerlySerializedAs("spellCard")] public Color spell;
    [FormerlySerializedAs("objectCard")] public Color @object = new Color(0.62f, 0.36f, 0.14f, 1f);
    public Color encounter;
    public Color environmental;
    public Color logNotification = new Color(0.55f, 0.85f, 1f);
    public Color logRumour = new Color(0.85f, 0.55f, 1f);
    public Color logEvent = new Color(1f, 0.75f, 0.3f);
    public Color movementStart = new Color(0.72f, 0.16f, 0.2f);
    public Color movementEnd = new Color(0.08f, 0.58f, 0.52f);

    [Header("Hex Hover Tooltip")]
    public Color hoverLinkDefault = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
    public Color hoverLinkHover = new Color32(0xFF, 0xD7, 0x00, 0xFF);
    public Color hoverHeader = new Color32(0xD8, 0xC9, 0xA3, 0xFF);
    public Color hoverTerrain = new Color32(0x8F, 0xBF, 0x6F, 0xFF);
    public Color hoverFeatures = new Color32(0x6F, 0xA8, 0xDC, 0xFF);

    public Color MAX;

    [Header("Nation Colors")]
    [Tooltip("Assigned to leaders in order (player, then competitors, then npcs) by AssignNationColors. Add more entries here as needed.")]
    public Color[] nationColors =
    {
        new(0.55f, 0.10f, 0.10f, 1f),
        new(0.10f, 0.25f, 0.50f, 1f),
        new(0.55f, 0.40f, 0.05f, 1f),
        new(0.10f, 0.40f, 0.20f, 1f),
        new(0.35f, 0.15f, 0.45f, 1f),
        new(0.60f, 0.30f, 0.05f, 1f),
        new(0.55f, 0.20f, 0.35f, 1f),
        new(0.05f, 0.40f, 0.40f, 1f),
        new(0.35f, 0.40f, 0.08f, 1f),
        new(0.35f, 0.20f, 0.12f, 1f),
        new(0.20f, 0.25f, 0.55f, 1f),
        new(0.22f, 0.28f, 0.35f, 1f),
        new(0.30f, 0.32f, 0.10f, 1f),
        new(0.35f, 0.08f, 0.12f, 1f),
        new(0.15f, 0.40f, 0.42f, 1f),
        new(0.50f, 0.38f, 0.10f, 1f),
        new(0.28f, 0.10f, 0.35f, 1f),
        new(0.15f, 0.45f, 0.30f, 1f),
        new(0.55f, 0.28f, 0.20f, 1f),
        new(0.08f, 0.12f, 0.30f, 1f),
        new(0.45f, 0.38f, 0.08f, 1f),
        new(0.45f, 0.18f, 0.28f, 1f),
        new(0.08f, 0.30f, 0.22f, 1f),
        new(0.55f, 0.25f, 0.15f, 1f),
        new(0.22f, 0.22f, 0.45f, 1f)
    };

    private Dictionary<string, FieldInfo> normalizedLookup;

    private void Awake()
    {
        BuildLookup();
    }

    private void BuildLookup()
    {
        normalizedLookup = new Dictionary<string, FieldInfo>();

        var fields = typeof(Colors).GetFields(BindingFlags.Public | BindingFlags.Instance);

        foreach (var field in fields)
        {
            if (field.FieldType == typeof(Color))
            {
                string normalized = Normalize(field.Name);
                normalizedLookup[normalized] = field;
            }
        }
    }

    public Color GetColorByName(string colorName)
    {
        if (normalizedLookup == null)
        {
            BuildLookup();
        }

        string normalized = Normalize(colorName);

        if (normalizedLookup.TryGetValue(normalized, out var field))
        {
            return (Color)field.GetValue(this);
        }

        throw new System.ArgumentException($"No color found for name '{colorName}' (normalized: '{normalized}').");
    }

    public string GetHexColorByName(string colorName)
    {
        return "#" + ColorUtility.ToHtmlStringRGB(GetColorByName(colorName));
    }
}
