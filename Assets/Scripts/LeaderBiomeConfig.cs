using System;
using System.Collections.Generic;

[Serializable]
public class LeaderBiomeConfigCollection
{
	public List<LeaderBiomeConfig> biomes = new ();
}

[Serializable]
public class LeaderVariantConfig
{
    public string variantId;
    public string characterName;
    public string displayName;
    public string description;
    public string deckIdentity;
    public string subdeckId;
    public string banner;
    public string introVideoText;
}

// Fields that only govern the procedural (non-scenario) starting setup: where a leader's
// capital gets placed and what default army it's handed. An authored scenario positions
// leaders and cities explicitly and gives armies via its own ScenarioArmy data, so these are
// read only by NationSpawner.Spawn() — never by SpawnFromScenario(), which must ignore this
// block entirely (see Character.InitializeFromBiome's applyNoScenarioStart parameter and
// NationSpawner's procedural leader-placement path).
[Serializable]
public class LeaderNoScenarioStart
{
    public TerrainEnum terrain;
    public int startingArmySize;
    public string startingArmyCard = "";
    public bool startingCityIsHidden;
    public bool startsWithPort;
    public int startingWarships;
    public string startingCityRegion;
}

[Serializable]
public class LeaderBiomeConfig: BiomeConfig
{
    public string nationName;
    public string nationInitials;
    public string description;
    public string deckIdentity;
    public string subdeckId;
    public string banner;
    public string introVideoText;
    public List<LeaderVariantConfig> variants = new();
    public LeaderNoScenarioStart noScenarioStart = new();
    public FeaturesEnum feature = FeaturesEnum.noFeature;
    public bool isIsland = false;
    public string startingCityName;
    public PCSizeEnum startingCitySize;

    public string pcFeature = "";
    public string fortFeature = "";
    public FortSizeEnum startingCityFortSize;
    public bool isMorgulMaster;

    public List<BiomeConfig> startingCharacters = new();

    public List<string> newCharacters = new();
    public List<string> newPCs = new();

}
