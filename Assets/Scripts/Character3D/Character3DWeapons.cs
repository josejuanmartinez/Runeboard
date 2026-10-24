using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

// What a live 3D unit holds (see Character3DModel.Arm): a weapon in its right hand and, for some,
// a shield on its left forearm, picked from who the character is. The meshes are the Addressable
// Assets/Art/3D/fbx/Weapons/[Name]/[Name].fbx models, drawn with the unit's own materials pipeline.
public static class Character3DWeapons
{
    public enum Weapon { None, Sword, Axe, Scimitar, MorningStar, Spear, Staff, Shield }

    public readonly struct Loadout : System.IEquatable<Loadout>
    {
        public readonly Weapon Right;
        public readonly bool Shield;
        public readonly float Scale; // on the weapon's usual length (hobbits small, trolls big)

        public Loadout(Weapon right, bool shield = false, float scale = 1f)
        {
            Right = right;
            Shield = shield;
            Scale = scale;
        }

        public bool Equals(Loadout other) => Right == other.Right && Shield == other.Shield && Scale == other.Scale;
        public override bool Equals(object obj) => obj is Loadout other && Equals(other);
        public override int GetHashCode() => ((int)Right * 397) ^ (Shield ? 1 : 0) ^ Scale.GetHashCode();
    }

    public static readonly Loadout Unarmed = new(Weapon.None);

    // ── Who carries what ────────────────────────────────────────────────────────────────────

    public static Loadout For(Character character)
    {
        if (character == null) return Unarmed;
        string name = (character.characterName ?? "").Replace(" ", "");
        switch (name)
        {
            case "Gollum":
            case "TomBombadil": return Unarmed;
            case "Sauron": return new Loadout(Weapon.MorningStar, scale: 1.2f); // his mace
        }

        bool mage = character.GetBaseMage() > 0 && character.GetBaseMage() >= character.GetBaseCommander();
        bool commander = character.GetBaseCommander() > 0
            && character.GetBaseCommander() >= Mathf.Max(character.GetBaseAgent(), character.GetBaseEmmissary(), character.GetBaseMage());
        bool pick = Variety(name); // a stable coin flip per character, for races with two weapons

        switch (character.race)
        {
            // Creatures fight with what they are.
            case RacesEnum.Spider:
            case RacesEnum.Dragon:
            case RacesEnum.Eagle:
            case RacesEnum.Ent:
            case RacesEnum.Beast:
            case RacesEnum.Machine: return Unarmed;
            case RacesEnum.Troll: return new Loadout(Weapon.MorningStar, scale: 1.25f);
            case RacesEnum.Balrog: return new Loadout(Weapon.Sword, scale: 1.2f);
            case RacesEnum.Nazgul:
            case RacesEnum.Undead: return new Loadout(Weapon.Sword);
            case RacesEnum.Maia: return new Loadout(Weapon.Staff);
        }
        if (mage) return new Loadout(Weapon.Staff);

        switch (character.race)
        {
            case RacesEnum.Hobbit: return new Loadout(Weapon.Sword, scale: 0.75f);
            case RacesEnum.Dwarf: return new Loadout(Weapon.Axe, shield: commander);
            case RacesEnum.Beorning: return new Loadout(Weapon.Axe);
            case RacesEnum.Orc:
            case RacesEnum.Goblin: return new Loadout(pick ? Weapon.Scimitar : Weapon.MorningStar);
            case RacesEnum.Southron:
            case RacesEnum.Easterling: return new Loadout(pick ? Weapon.Scimitar : Weapon.Spear);
            case RacesEnum.Wildman: return new Loadout(Weapon.Spear);
            default: return new Loadout(Weapon.Sword, shield: commander); // elves and men
        }
    }

    // Rank and file of a regiment (Character3DRegiment), by the model folder they're drawn with
    // and the army's main troop type. No bows or horses exist, so archers keep their sidearm and
    // cavalry carry lances; light infantry carry spears, heavy infantry add a shield.
    public static Loadout ForTroop(string folder, TroopsTypeEnum type, int index)
    {
        bool shield = type is TroopsTypeEnum.hi or TroopsTypeEnum.hc;
        if (type is TroopsTypeEnum.li or TroopsTypeEnum.lc or TroopsTypeEnum.hc) return new Loadout(Weapon.Spear, shield);
        switch (folder)
        {
            case "Dwarf":
            case "Beorning": return new Loadout(Weapon.Axe, shield);
            case "Hobbit": return new Loadout(Weapon.Sword, shield, 0.75f);
            case "Orc": return new Loadout(index % 2 == 0 ? Weapon.Scimitar : Weapon.MorningStar, shield);
            case "Southron":
            case "Easterling": return new Loadout(Weapon.Scimitar, shield);
            case "Wildman": return new Loadout(Weapon.Spear);
            default: return new Loadout(Weapon.Sword, shield);
        }
    }

    private static bool Variety(string name)
    {
        int hash = 0;
        foreach (char c in name) hash = hash * 31 + c;
        return (hash & 1) == 0;
    }

    // ── How each mesh is held ───────────────────────────────────────────────────────────────

    // Points are fractions of the mesh's own bounds (Unity mesh space: the FBX's X is mirrored on
    // import). All the weapon FBXs stand along their mesh +Y with the handle at the bottom, a
    // metre tall. Measured from the vertices:
    //   Grip   — the middle of the handle, where the fist closes.
    //   Tip    — a point further along the weapon; grip → tip is the axis the fist holds.
    //   Edge   — mesh direction the cutting edge faces; turned toward the forearm, so blows land
    //            edge first (the axe is modelled with its handle leaning and its bit to one side,
    //            the scimitar curving back from its edge).
    //   Length — the mesh's height as a fraction of the figure's height.
    //   Tilt   — degrees the weapon is raised from square-to-the-fist toward the elbow: a sword
    //            juts forward from a hanging hand, a staff or spear stands nearly upright.
    // The shield's Grip is the back of its boss, Tip the direction its face looks, Edge its top.
    public readonly struct Spec
    {
        public readonly Vector3 Grip, Tip, Edge;
        public readonly float Length, Tilt;

        public Spec(Vector3 grip, Vector3 tip, Vector3 edge, float length, float tilt)
        {
            Grip = grip;
            Tip = tip;
            Edge = edge;
            Length = length;
            Tilt = tilt;
        }
    }

    public static readonly Dictionary<Weapon, Spec> Specs = new()
    {
        { Weapon.Sword, new Spec(new(0.5f, 0.15f, 0.5f), new(0.5f, 1f, 0.5f), Vector3.right, 0.5f, 20f) },
        { Weapon.Axe, new Spec(new(0.8f, 0.1f, 0.24f), new(0.19f, 0.8f, 0.47f), Vector3.right, 0.5f, 15f) },
        { Weapon.Scimitar, new Spec(new(0.46f, 0.06f, 0.89f), new(0.26f, 1f, 0.06f), Vector3.forward, 0.5f, 20f) },
        { Weapon.MorningStar, new Spec(new(0.5f, 0.12f, 0.5f), new(0.5f, 1f, 0.5f), Vector3.right, 0.5f, 15f) },
        { Weapon.Spear, new Spec(new(0.5f, 0.4f, 0.5f), new(0.5f, 1f, 0.5f), Vector3.right, 1.15f, 65f) },
        { Weapon.Staff, new Spec(new(0.38f, 0.42f, 0.61f), new(0.38f, 1f, 0.61f), Vector3.right, 1f, 72f) },
        { Weapon.Shield, new Spec(new(0.5f, 0.55f, 0f), new(0.5f, 0.55f, 1f), Vector3.up, 0.42f, 0f) },
    };

    // ── Loading ─────────────────────────────────────────────────────────────────────────────

    public class Parts
    {
        public Mesh Mesh;
        public Material Material;
        public bool Missing;
        public bool Done => Mesh != null || Missing;
    }

    private static readonly Dictionary<Weapon, Parts> partsByWeapon = new();

    // Starts loading on first request; check Done.
    public static Parts Request(Weapon weapon)
    {
        if (partsByWeapon.TryGetValue(weapon, out Parts parts)) return parts;
        parts = new Parts();
        partsByWeapon[weapon] = parts;
        string address = $"{Character3DModel.AddressRoot}Weapons/{weapon}/{weapon}.fbx";
        // Locations first: LoadAssetAsync on an unknown key throws an InvalidKeyException error.
        Addressables.LoadResourceLocationsAsync(address, typeof(GameObject)).Completed += locations =>
        {
            IResourceLocation location = locations.Status == AsyncOperationStatus.Succeeded && locations.Result.Count > 0
                ? locations.Result[0] : null;
            Addressables.Release(locations);
            if (location == null)
            {
                parts.Missing = true;
                Debug.LogWarning($"[Character3D] Could not locate Addressable weapon '{address}'.");
                return;
            }
            Addressables.LoadAssetAsync<GameObject>(location).Completed += load =>
            {
                MeshFilter filter = load.Status == AsyncOperationStatus.Succeeded ? load.Result.GetComponentInChildren<MeshFilter>(true) : null;
                MeshRenderer renderer = filter != null ? filter.GetComponent<MeshRenderer>() : null;
                if (filter == null || filter.sharedMesh == null || renderer == null)
                {
                    parts.Missing = true;
                    return;
                }
                parts.Material = renderer.sharedMaterial;
                parts.Mesh = filter.sharedMesh;
            };
        };
        return parts;
    }
}
