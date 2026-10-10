using System;
using TMPro;
using UnityEngine;

[Serializable]
public class PC
{
    [SerializeField] public Leader owner;
    [SerializeField] public Hex hex;
    [SerializeField] public string pcName;
    [SerializeField] public Leader initialOwner;
    [SerializeField] public PCOriginType originType = PCOriginType.Unknown;
    [SerializeField] public PCAcquisitionType acquisitionType = PCAcquisitionType.StartingOwned;

    [SerializeField] public int loyalty = 100;

    [SerializeField] public bool isHidden;
    // Marks the hex this PC sits on as an entrance to the Underground (see Hex.IsUnderground).
    [SerializeField] public bool isUnderground;
    [SerializeField] public PCSizeEnum citySize = PCSizeEnum.NONE;
    [SerializeField] public FortSizeEnum fortSize = FortSizeEnum.NONE;
    [SerializeField] public bool hasPort;
    [SerializeField] public bool hiddenButRevealed;
    [SerializeField] public bool isCapital;
    [SerializeField] public bool artifactOccupancyHidden;
    [SerializeField] public int temporaryHiddenTurns;
    [SerializeField] public int temporaryRevealTurns;
    [SerializeField] public int guardLevel;
    [SerializeField] public int guardTurns;


    public PC(Leader owner, string pcName, PCSizeEnum citySize, FortSizeEnum fortSize, bool hasPort, bool isHidden, Hex hex, bool isCapital = false, int loyalty = 75)
    {
        this.owner = owner;
        initialOwner = owner;
        this.hex = hex;
        this.pcName = pcName;
        this.citySize = citySize;
        this.fortSize = fortSize;
        this.hasPort = hasPort;
        this.isHidden = isHidden;
        this.loyalty = loyalty;
        this.isCapital = isCapital;
        hiddenButRevealed = false;
        originType = owner != null ? GetOriginType(owner) : PCOriginType.Unknown;
        acquisitionType = PCAcquisitionType.StartingOwned;

        if (owner != null)
        {
            owner.controlledPcs.Add(this);
            owner.visibleHexes.Add(hex);
        }

        hex.SetPC(this);
    }

    private static PCOriginType GetOriginType(Leader leader)
    {
        if (leader is PlayableLeader) return PCOriginType.PlayableLeader;
        if (leader is NonPlayableLeader) return PCOriginType.NonPlayableLeader;
        return PCOriginType.Unknown;
    }

    /**
     * This constructor is used to create a new PC in the starting city of a leader
     */
    public PC(Leader leader, Hex hex): this(leader, leader.GetBiome().startingCityName, leader.GetBiome().startingCitySize, leader.GetBiome().startingCityFortSize, leader.GetBiome().noScenarioStart.startsWithPort, leader.GetBiome().noScenarioStart.startingCityIsHidden, hex, true)
    {
        
    }

    public bool IsRevealed(PlayableLeader overrideLeader = null)
    {
        var l = overrideLeader ? overrideLeader : Game.Instance.player;
        if (l == null) return false;

        if (IsArtifactOccupancyHidden(l)) return false;
        if (IsTemporarilyRevealed()) return true;
        if (IsTemporarilyHidden(l)) return false;
        if (!isHidden || hiddenButRevealed) return true;

        var pcOwner = owner;
        if (pcOwner == null) return false;
        if (pcOwner == l) return true;

        var pcAlign = pcOwner.GetAlignment();
        var lAlign = l.GetAlignment();
        return pcAlign != AlignmentEnum.neutral && pcAlign == lAlign;
    }

    public bool IsTemporarilyRevealed() => temporaryRevealTurns > 0;

    public bool IsTemporarilyHidden(Leader viewer = null)
    {
        if (temporaryHiddenTurns <= 0) return false;
        if (viewer != null && owner != null && viewer == owner) return false;
        return true;
    }

    public bool IsArtifactOccupancyHidden(Leader viewer = null)
    {
        if (!artifactOccupancyHidden) return false;
        if (viewer != null && owner != null && viewer == owner) return false;
        return true;
    }

    public void SetArtifactOccupancyHidden(bool hidden)
    {
        artifactOccupancyHidden = hidden;
    }

    public void SetTemporaryHidden(int turns)
    {
        if (turns <= 0) return;
        temporaryHiddenTurns = Mathf.Max(temporaryHiddenTurns, turns);
        temporaryRevealTurns = 0;
    }

    public void SetTemporaryReveal(int turns)
    {
        if (turns <= 0) return;
        temporaryRevealTurns = Mathf.Max(temporaryRevealTurns, turns);
        temporaryHiddenTurns = 0;
    }

    public void TickTemporaryVisibility()
    {
        if (temporaryHiddenTurns > 0) temporaryHiddenTurns--;
        if (temporaryRevealTurns > 0) temporaryRevealTurns--;
        if (guardTurns > 0)
        {
            guardTurns--;
            if (guardTurns == 0) guardLevel = 0;
        }
    }

    public void SetGuard(int level)
    {
        guardLevel = Mathf.Max(guardLevel, level);
        guardTurns = Mathf.Max(guardTurns, 1);
    }

    public string GetLoyaltyText()
    {
        // Brighter palette for readability in hover
        string color = loyalty <= 25 ? "#ff4d4d" : loyalty <= 50 ? "#ffb347" : loyalty <= 65 ? "#8fd14f" : "#00c853";
        return $"Loyalty [<color={color}>{Math.Max(0, loyalty)}</color>]";
    }


    /// <summary>Sets the owner when restoring a saved game: no capture side effects (eliminations, messages, loyalty roll).</summary>
    public void RestoreOwnerForLoad(Leader leader)
    {
        if (owner == leader) return;
        if (owner != null)
        {
            owner.controlledPcs.Remove(this);
            owner.visibleHexes.Remove(hex);
        }
        owner = leader;
        if (owner != null)
        {
            if (!owner.controlledPcs.Contains(this)) owner.controlledPcs.Add(this);
            owner.visibleHexes.Add(hex);
        }
    }

    public void CapturePC(Leader leader)
    {
        Leader previousOwner = owner;
        if (previousOwner != null)
        {
            previousOwner.controlledPcs.Remove(this);
            previousOwner.visibleHexes.Remove(hex);
        }

        owner = leader;
        acquisitionType = PCAcquisitionType.CapturedByForce;
        owner.controlledPcs.Add(this);
        owner.visibleHexes.Add(hex);
        loyalty = UnityEngine.Random.Range(50, 75);
        owner.hex.RedrawPC();
        if (owner is NonPlayableLeader && hex != null)
        {
            hex.EnsurePersistentScouting(owner);
        }

        if (previousOwner != null && previousOwner.controlledPcs.Count < 1) previousOwner.Killed(leader);

        MessageDisplayNoUI.ShowMessage(hex, owner,  $"{pcName} was captured!", Color.red);
    }

    public bool ClaimUnowned(Leader leader)
    {
        if (leader == null || owner != null) return false;
        return ClaimByAllegiance(leader);
    }

    public bool ClaimByAllegiance(Leader leader)
    {
        if (leader == null) return false;
        if (owner == leader) return false;

        Leader previousOwner = owner;
        if (previousOwner != null)
        {
            previousOwner.controlledPcs.Remove(this);
            previousOwner.visibleHexes.Remove(hex);
        }

        owner = leader;
        acquisitionType = PCAcquisitionType.Joined;
        if (!leader.controlledPcs.Contains(this))
        {
            leader.controlledPcs.Add(this);
        }
        if (!leader.visibleHexes.Contains(hex))
        {
            leader.visibleHexes.Add(hex);
        }
        loyalty = UnityEngine.Random.Range(50, 75);
        hex.RedrawPC();
        if (owner is NonPlayableLeader && hex != null)
        {
            hex.EnsurePersistentScouting(owner);
        }
        MessageDisplayNoUI.ShowMessage(hex, owner, $"{pcName} swore allegiance!", Color.green);
        return true;
    }

    public void CheckHighLoyalty()
    {
        if (loyalty < 100 || citySize == PCSizeEnum.city) return;

        if (UnityEngine.Random.Range(1, 5) >= ((int)citySize) + 1) IncreaseSize();
    }

    public void IncreaseSize()
    {
        if (citySize >= PCSizeEnum.city) return;

        citySize++;

        loyalty = 70;

        MessageDisplayNoUI.ShowMessage(hex, owner,  $"Population in {pcName} grow!", Color.green);

        owner.hex.RedrawPC();
    }

    public void IncreaseFort()
    {
        if (fortSize >= FortSizeEnum.citadel) return;

        // This will also increase gold per turn
        fortSize++;

        MessageDisplayNoUI.ShowMessage(hex, owner,  $"{pcName} fort was upgraded", Color.green);

        owner.hex.RedrawPC();
    }


    public void DecreaseFort()
    {
        if (fortSize <= FortSizeEnum.NONE) return;

        // This will also increase gold per turn
        fortSize--;

        MessageDisplayNoUI.ShowMessage(hex, owner,  $"{pcName} fort was downgraded", Color.red);

        owner.hex.RedrawPC();
    }

    public int GetFortSize()
    {
        return (int) fortSize;
    }

    public void CheckLowLoyalty(Leader leader)
    {
        if (loyalty >= 50) return;

        MessageDisplayNoUI.ShowMessage(hex, owner,  $"{pcName} suffers the effects of an unhappy population!", Color.red);

        if ( UnityEngine.Random.Range(0, 50) > loyalty)
        {
            if(owner is NonPlayableLeader || citySize == PCSizeEnum.camp)
            {
                CapturePC(leader);
            }
            else
            {
                DecreaseSize();
            }

            owner.hex.RedrawPC();
        } 
        
        if (owner.controlledPcs.Count < 1) owner.Killed(leader);
    }


    public void DecreaseSize()
    {
        if (citySize > PCSizeEnum.camp) citySize--;

        loyalty = 60;

        owner.hex.RedrawPC();
        
        MessageDisplayNoUI.ShowMessage(hex, owner,  $"{pcName} population flee!", Color.red);

    }

    public void IncreaseLoyalty(int loyalty, Character c)
    {
        this.loyalty = Math.Min(100, this.loyalty + loyalty);
        MessageDisplayNoUI.ShowMessage(hex, c,  $"{pcName} loyalty increased by {loyalty}", Color.green);
        hex.RedrawPC();
    }

    public void DecreaseLoyalty(int loyalty, Character decreasedBy)
    {
        this.loyalty = Math.Max(0, this.loyalty - loyalty);
        MessageDisplayNoUI.ShowMessage(hex, decreasedBy,  $"{pcName} loyalty decreased by {loyalty}", Color.red);
        hex.RedrawPC();
        CheckLowLoyalty(decreasedBy.GetOwner());
    }

    public int GetDefense()
    {
        // Core PC defense is scaled by loyalty: low loyalty = weaker defensive contribution.
        int staticDefense = (int)citySize
            + (int)fortSize * FortSizeData.defensePerFortSizeLevel
            + (hasPort ? 1 : 0)
            + (isCapital ? 1 : 0);

        float loyaltyFactor = Mathf.Clamp01(loyalty / 100f);
        int defense = Mathf.RoundToInt(staticDefense * loyaltyFactor);

        // Armies stationed in the PC defend at full strength if aligned.
        hex.armies.ForEach(army =>
        {
            bool aligned = owner != null &&
                army.commander != null &&
                army.commander.GetAlignment() == owner.GetAlignment() &&
                owner.GetAlignment() != AlignmentEnum.neutral;
            if (aligned) defense += army.GetDefence();
        });

        return defense;
    }

    public void Reveal()
    {
        if(isHidden) hiddenButRevealed = true;
    }
}
