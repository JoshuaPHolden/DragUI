using Stunlock.Core;

namespace DragUI.Data;

// V Blood bosses the hunt list offers. GUIDs come from the decaprime/vrising-modding prefab dump
// (game 1.1). DefaultRespawnSeconds is the community-dataset value for each boss and is only a
// starting estimate: exact timers from the V Blood ghost, and respawns we observe in-game, override it.
// 0 = no known default (Settings.FallbackRespawnSeconds is used).
internal sealed record VBloodInfo(string Name, PrefabGUID Guid, string PrefabName, int Act, string Region, int DefaultRespawnSeconds)
{
    public string ActLabel => Act == 5 ? "Shard" : $"Act {Act}";
}

internal static class VBloodCatalog
{
    public static readonly IReadOnlyList<VBloodInfo> All = new VBloodInfo[]
    {
        new("Alpha the White Wolf", new PrefabGUID(-1905691330), "CHAR_Forest_Wolf_VBlood", 1, "Farbane", 180),
        new("Clive the Firestarter", new PrefabGUID(1896428751), "CHAR_Bandit_Bomber_VBlood", 1, "Farbane", 300),
        new("Errol the Stonebreaker", new PrefabGUID(-2025101517), "CHAR_Bandit_StoneBreaker_VBlood", 1, "Farbane", 240),
        new("Finn the Fisherman", new PrefabGUID(-2122682556), "CHAR_Bandit_Fisherman_VBlood", 1, "Farbane", 300),
        new("Goreswine the Ravager", new PrefabGUID(577478542), "CHAR_Undead_BishopOfDeath_VBlood", 1, "Farbane", 300),
        new("Grayson the Armourer", new PrefabGUID(1106149033), "CHAR_Bandit_Stalker_VBlood", 1, "Farbane", 300),
        new("Keely the Frost Archer", new PrefabGUID(1124739990), "CHAR_Bandit_Frostarrow_VBlood", 1, "Farbane", 240),
        new("Kodia the Ferocious Bear", new PrefabGUID(-1391546313), "CHAR_Forest_Bear_Dire_Vblood", 1, "Farbane", 300),
        new("Lidia the Chaos Archer", new PrefabGUID(763273073), "CHAR_Bandit_Chaosarrow_VBlood", 1, "Farbane", 300),
        new("Nibbles the Putrid Rat", new PrefabGUID(-2039908510), "CHAR_Vermin_DireRat_VBlood", 1, "Farbane", 0),
        new("Nicholaus the Fallen", new PrefabGUID(153390636), "CHAR_Undead_Priest_VBlood", 1, "Farbane", 480),
        new("Polora the Feywalker", new PrefabGUID(-484556888), "CHAR_Poloma_VBlood", 1, "Farbane", 300),
        new("Quincey the Bandit King", new PrefabGUID(-1659822956), "CHAR_Bandit_Tourok_VBlood", 1, "Farbane", 300),
        new("Rufus the Foreman", new PrefabGUID(2122229952), "CHAR_Bandit_Foreman_VBlood", 1, "Farbane", 240),
        new("Bane the Shadowblade", new PrefabGUID(613251918), "CHAR_Undead_Infiltrator_VBlood", 2, "Dunley", 480),
        new("Beatrice the Tailor", new PrefabGUID(-1942352521), "CHAR_Villager_Tailor_VBlood", 2, "Dunley", 480),
        new("Christina the Sun Priestess", new PrefabGUID(-99012450), "CHAR_Militia_Nun_VBlood", 2, "Dunley", 480),
        new("Frostmaw the Mountain Terror", new PrefabGUID(24378719), "CHAR_Wendigo_VBlood", 2, "Hallowed", 480),
        new("Gaius the Cursed Champion", new PrefabGUID(-753453016), "CHAR_Undead_ArenaChampion_VBlood", 2, "Dunley", 480),
        new("General Cassius the Betrayer", new PrefabGUID(-496360395), "CHAR_Vampire_HighLord_VBlood", 2, "Mortium", 480),
        new("General Elena the Hollow", new PrefabGUID(795262842), "CHAR_Vampire_IceRanger_VBlood", 2, "Mortium", 480),
        new("Grethel the Glassblower", new PrefabGUID(910988233), "CHAR_Militia_Glassblower_VBlood", 2, "Dunley", 480),
        new("Jade the Vampire Hunter", new PrefabGUID(-1968372384), "CHAR_VHunter_Jade_VBlood", 2, "Dunley", 480),
        new("Kriig the Undead General", new PrefabGUID(-1365931036), "CHAR_Undead_Leader_Vblood", 2, "Dunley", 480),
        new("Leandra the Shadow Priestess", new PrefabGUID(939467639), "CHAR_Undead_BishopOfShadows_VBlood", 2, "Dunley", 300),
        new("Maja the Dark Savant", new PrefabGUID(1945956671), "CHAR_Militia_Scribe_VBlood", 2, "Dunley", 480),
        new("Meredith the Bright Archer", new PrefabGUID(850622034), "CHAR_Militia_Longbowman_LightArrow_Vblood", 2, "Dunley", 480),
        new("Octavian the Militia Captain", new PrefabGUID(1688478381), "CHAR_Militia_Leader_VBlood", 2, "Dunley", 480),
        new("Raziel the Shepherd", new PrefabGUID(-680831417), "CHAR_Militia_BishopOfDunley_VBlood", 2, "Dunley", 480),
        new("Sir Erwin the Gallant Cavalier", new PrefabGUID(619948378), "CHAR_Militia_Fabian_VBlood", 2, "Dunley", 480),
        new("Terah the Geomancer", new PrefabGUID(-1065970933), "CHAR_Geomancer_Human_VBlood", 2, "Dunley", 480),
        new("Tristan the Vampire Hunter", new PrefabGUID(-1449631170), "CHAR_VHunter_Leader_VBlood", 2, "Farbane", 480),
        new("Vincent the Frostbringer", new PrefabGUID(-29797003), "CHAR_Militia_Guard_VBlood", 2, "Dunley", 480),
        new("Albert the Duke of Balaton", new PrefabGUID(-203043163), "CHAR_Cursed_ToadKing_VBlood", 3, "CursedForest", 480),
        new("Angram the Purifier", new PrefabGUID(106480588), "CHAR_Gloomrot_Purifier_VBlood", 3, "GloomrotSouth", 480),
        new("Ben the Old Wanderer", new PrefabGUID(109969450), "CHAR_Villager_CursedWanderer_VBlood", 3, "CursedForest", 480),
        new("Cyril the Cursed Smith", new PrefabGUID(326378955), "CHAR_Undead_CursedSmith_VBlood", 3, "CursedForest", 480),
        new("Domina the Blade Dancer", new PrefabGUID(-1101874342), "CHAR_Gloomrot_Voltage_VBlood", 3, "GloomrotSouth", 480),
        new("Foulrot the Soultaker", new PrefabGUID(-1208888966), "CHAR_Undead_ZealousCultist_VBlood", 3, "CursedForest", 480),
        new("Ungora the Spider Queen", new PrefabGUID(-548489519), "CHAR_Spider_Queen_VBlood", 3, "CursedForest", 480),
        new("Willfred the Village Elder", new PrefabGUID(-1505705712), "CHAR_WerewolfChieftain_Human", 3, "Dunley", 480),
        new("Ziva the Engineer", new PrefabGUID(172235178), "CHAR_Gloomrot_Iva_VBlood", 3, "GloomrotSouth", 480),
        new("Azariel the Sunbringer", new PrefabGUID(114912615), "CHAR_ChurchOfLight_Cardinal_VBlood", 4, "Silverlight", 600),
        new("Baron du Bouchon the Sommelier", new PrefabGUID(192051202), "CHAR_ChurchOfLight_Sommelier_VBlood", 4, "Silverlight", 480),
        new("Dantos the Forgebinder", new PrefabGUID(173259239), "CHAR_Blackfang_Valyr_VBlood", 4, "Oakveil", 600),
        new("General Valencia the Depraved", new PrefabGUID(495971434), "CHAR_Vampire_BloodKnight_VBlood", 4, "Mortium", 600),
        new("Gorecrusher the Behemoth", new PrefabGUID(-1936575244), "CHAR_Cursed_MountainBeast_VBlood", 4, "CursedForest", 900),
        new("Henry Blackbrew the Doctor", new PrefabGUID(814083983), "CHAR_Gloomrot_TheProfessor_VBlood", 4, "GloomrotNorth", 600),
        new("Jakira the Shadow Huntress", new PrefabGUID(-1383529374), "CHAR_Blackfang_Livith_VBlood", 4, "Oakveil", 480),
        new("Lord Styx the Night Champion", new PrefabGUID(1112948824), "CHAR_BatVampire_VBlood", 4, "GloomrotSouth", 600),
        new("Lucile the Venom Alchemist", new PrefabGUID(1295855316), "CHAR_Blackfang_Lucie_VBlood", 4, "Oakveil", 480),
        new("Mairwyn the Elementalist", new PrefabGUID(-2013903325), "CHAR_ArchMage_VBlood", 4, "Silverlight", 480),
        new("Matka the Curse Weaver", new PrefabGUID(-910296704), "CHAR_Cursed_Witch_VBlood", 4, "CursedForest", 600),
        new("Morian the Stormwing Matriarch", new PrefabGUID(685266977), "CHAR_Harpy_Matriarch_VBlood", 4, "Silverlight", 600),
        new("Simon Belmont the Vampire Hunter", new PrefabGUID(336560131), "CHAR_VHunter_CastleMan", 4, "Farbane/Dunley/Silverlight", 600),
        new("Sir Magnus the Overseer", new PrefabGUID(-26105228), "CHAR_ChurchOfLight_Overseer_VBlood", 4, "Silverlight", 480),
        new("Stavros the Carver", new PrefabGUID(-1669199769), "CHAR_Blackfang_CarverBoss_VBlood", 4, "Oakveil", 480),
        new("Terrorclaw the Ogre", new PrefabGUID(-1347412392), "CHAR_Winter_Yeti_VBlood", 4, "Hallowed", 480),
        new("Voltatia the Power Master", new PrefabGUID(2054432370), "CHAR_Gloomrot_RailgunSergeant_VBlood", 4, "GloomrotNorth", 600),
        new("Adam the Firstborn", new PrefabGUID(1233988687), "CHAR_Gloomrot_Monster_VBlood", 5, "GloomrotNorth", 900),
        new("Dracula the Immortal King", new PrefabGUID(-327335305), "CHAR_Vampire_Dracula_VBlood", 5, "Mortium", 0),
        new("Megara the Serpent Queen", new PrefabGUID(591725925), "CHAR_Blackfang_Morgana_VBlood", 5, "Oakveil", 900),
        new("Solarus the Immaculate", new PrefabGUID(-740796338), "CHAR_ChurchOfLight_Paladin_VBlood", 5, "Silverlight", 600),
        new("Talzur the Winged Horror", new PrefabGUID(-393555055), "CHAR_Manticore_VBlood", 5, "Farbane", 900),
    };

    static readonly Dictionary<int, VBloodInfo> _byGuid = All.ToDictionary(v => v.Guid.GuidHash);

    public static bool TryGet(PrefabGUID guid, out VBloodInfo info) => _byGuid.TryGetValue(guid.GuidHash, out info);
    public static bool TryGet(int guidHash, out VBloodInfo info) => _byGuid.TryGetValue(guidHash, out info);
    public static bool IsKnown(int guidHash) => _byGuid.ContainsKey(guidHash);
}
