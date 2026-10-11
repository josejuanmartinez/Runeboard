using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Bridge for Layout.prefab's Minimap skin dropdown: SkinManager lives in the scene, not inside
// this prefab, so it's reached via FindAnyObjectByType instead of a serialized reference (same
// pattern as HexGridToggle/RegionsViewToggle). Options are populated from the Skins enum at
// runtime so a newly added skin shows up here without another prefab edit.
[RequireComponent(typeof(TMP_Dropdown))]
public class SkinDropdown : MonoBehaviour
{
    private TMP_Dropdown dropdown;
    private Skins[] values;

    private void OnEnable()
    {
        dropdown = GetComponent<TMP_Dropdown>();
        values = (Skins[])Enum.GetValues(typeof(Skins));

        List<string> options = new();
        foreach (Skins skin in values) options.Add(skin.ToString());
        dropdown.ClearOptions();
        dropdown.AddOptions(options);

        // Matches StartScreenController's own default resolution (CurrentSkin, itself defaulting
        // to Skins.Default) so the dropdown never disagrees with what's actually active.
        SkinManager manager = FindAnyObjectByType<SkinManager>();
        Skins current = manager != null ? manager.CurrentSkin : Skins.Default;
        dropdown.SetValueWithoutNotify(Mathf.Max(0, Array.IndexOf(values, current)));

        dropdown.onValueChanged.AddListener(OnDropdownValueChanged);
    }

    private void OnDisable()
    {
        dropdown.onValueChanged.RemoveListener(OnDropdownValueChanged);
    }

    private void OnDropdownValueChanged(int index)
    {
        if (values == null || index < 0 || index >= values.Length) return;
        FindAnyObjectByType<SkinManager>()?.ChangeSkin(values[index]);
        Sounds.Instance?.PlayUiClick();
    }
}
