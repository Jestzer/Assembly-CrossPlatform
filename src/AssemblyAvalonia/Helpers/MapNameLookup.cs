using System.Collections.Generic;

namespace AssemblyAvalonia.Helpers;

/// <summary>
/// Maps internal .map file names to their in-game display names across all Halo titles.
/// </summary>
public static class MapNameLookup
{
	/// <summary>
	/// Returns the in-game display name for a map, or null if not found.
	/// Uses the engine name to disambiguate maps that share internal names across games.
	/// </summary>
	public static string GetMapName(string internalName, string engineName)
	{
		if (string.IsNullOrEmpty(internalName))
			return null;

		string key = internalName.Trim().ToLowerInvariant();
		string eng = engineName?.ToLowerInvariant() ?? "";

		// Try game-specific lookup first (order matters for disambiguation)
		Dictionary<string, string> gameNames = null;
		if (eng.Contains("odst"))
			gameNames = OdstMaps;
		else if (eng.Contains("reach"))
			gameNames = ReachMaps;
		else if (eng.Contains("halo 4"))
			gameNames = Halo4Maps;
		else if (eng.Contains("halo 3"))
			gameNames = Halo3Maps;
		else if (eng.Contains("halo 2 anniversary"))
			gameNames = Halo2AMaps;
		else if (eng.Contains("halo 2"))
			gameNames = Halo2Maps;
		else if (eng.Contains("halo 1") || eng.Contains("halo ce"))
			gameNames = Halo1Maps;
		else if (eng.Contains("stubbs"))
			gameNames = StubbsMaps;

		if (gameNames != null && gameNames.TryGetValue(key, out string name))
			return name;

		// Fall back to searching all games
		foreach (var dict in AllMaps)
		{
			if (dict.TryGetValue(key, out name))
				return name;
		}

		return null;
	}

	private static readonly Dictionary<string, string>[] AllMaps =
	{
		Halo1Maps, Halo2Maps, Halo2AMaps, Halo3Maps,
		OdstMaps, ReachMaps, Halo4Maps, StubbsMaps
	};

	private static readonly Dictionary<string, string> Halo1Maps = new()
	{
		// Campaign
		["a10"] = "The Pillar of Autumn",
		["a30"] = "Halo",
		["a50"] = "The Truth and Reconciliation",
		["b30"] = "The Silent Cartographer",
		["b40"] = "Assault on the Control Room",
		["c10"] = "343 Guilty Spark",
		["c20"] = "The Library",
		["c40"] = "Two Betrayals",
		["d20"] = "Keyes",
		["d40"] = "The Maw",
		// Multiplayer
		["beavercreek"] = "Battle Creek",
		["bloodgulch"] = "Blood Gulch",
		["boardingaction"] = "Boarding Action",
		["chillout"] = "Chill Out",
		["putput"] = "Chiron TL-34",
		["damnation"] = "Damnation",
		["dangercanyon"] = "Danger Canyon",
		["deathisland"] = "Death Island",
		["carousel"] = "Derelict",
		["gephyrophobia"] = "Gephyrophobia",
		["hangemhigh"] = "Hang 'Em High",
		["icefields"] = "Ice Fields",
		["infinity"] = "Infinity",
		["longest"] = "Longest",
		["prisoner"] = "Prisoner",
		["ratrace"] = "Rat Race",
		["sidewinder"] = "Sidewinder",
		["timberland"] = "Timberland",
		["wizard"] = "Wizard",
	};

	private static readonly Dictionary<string, string> Halo2Maps = new()
	{
		// Campaign
		["00a_introduction"] = "The Heretic",
		["01a_tutorial"] = "Armory",
		["01b_spacestation"] = "Cairo Station",
		["03a_oldmombasa"] = "Outskirts",
		["03b_newmombasa"] = "Metropolis",
		["04a_gasgiant"] = "The Arbiter",
		["04b_floodlab"] = "Oracle",
		["05a_deltaapproach"] = "Delta Halo",
		["05b_deltatowers"] = "Regret",
		["06a_sentinelwalls"] = "Sacred Icon",
		["06b_floodzone"] = "Quarantine Zone",
		["07a_highcharity"] = "Gravemind",
		["07b_forerunnership"] = "High Charity",
		["08a_deltacliffs"] = "Uprising",
		["08b_deltacontrol"] = "The Great Journey",
		// Multiplayer
		["ascension"] = "Ascension",
		["backwash"] = "Backwash",
		["beavercreek"] = "Beaver Creek",
		["burial_mounds"] = "Burial Mounds",
		["coagulation"] = "Coagulation",
		["colossus"] = "Colossus",
		["cyclotron"] = "Ivory Tower",
		["foundation"] = "Foundation",
		["headlong"] = "Headlong",
		["lockout"] = "Lockout",
		["midship"] = "Midship",
		["waterworks"] = "Waterworks",
		["zanzibar"] = "Zanzibar",
		["containment"] = "Containment",
		["deltatap"] = "Sanctuary",
		["dune"] = "Relic",
		["elongation"] = "Elongation",
		["gemini"] = "Gemini",
		["triplicate"] = "Terminal",
		["turf"] = "Turf",
		["warlock"] = "Warlock",
		["needle"] = "Uplift",
		["street_sweeper"] = "District",
		["derelict"] = "Desolation",
		["highplains"] = "Tombstone",
	};

	private static readonly Dictionary<string, string> Halo2AMaps = new()
	{
		["ca_ascension"] = "Zenith",
		["ca_coagulation"] = "Bloodline",
		["ca_forge_skybox01"] = "Skyward",
		["ca_forge_skybox02"] = "Nebula",
		["ca_forge_skybox03"] = "Awash",
		["ca_lockout"] = "Lockdown",
		["ca_relic"] = "Remnant",
		["ca_sanctuary"] = "Shrine",
		["ca_warlock"] = "Warlord",
		["ca_zanzibar"] = "Stonetown",
	};

	private static readonly Dictionary<string, string> Halo3Maps = new()
	{
		// Campaign
		["005_intro"] = "Arrival",
		["010_jungle"] = "Sierra 117",
		["020_base"] = "Crow's Nest",
		["030_outskirts"] = "Tsavo Highway",
		["040_voi"] = "The Storm",
		["050_floodvoi"] = "Floodgate",
		["070_waste"] = "The Ark",
		["100_citadel"] = "The Covenant",
		["110_hc"] = "Cortana",
		["120_halo"] = "Halo",
		["130_epilogue"] = "Epilogue",
		// Multiplayer
		["chill"] = "Narrows",
		["construct"] = "Construct",
		["cyberdyne"] = "The Pit",
		["deadlock"] = "High Ground",
		["guardian"] = "Guardian",
		["isolation"] = "Isolation",
		["riverworld"] = "Valhalla",
		["salvation"] = "Epitaph",
		["shrine"] = "Sandtrap",
		["snowbound"] = "Snowbound",
		["zanzibar"] = "Last Resort",
		["armory"] = "Rat's Nest",
		["bunkerworld"] = "Standoff",
		["chillout"] = "Cold Storage",
		["descent"] = "Assembly",
		["docks"] = "Longshore",
		["fortress"] = "Citadel",
		["ghosttown"] = "Ghost Town",
		["lockout"] = "Blackout",
		["midship"] = "Heretic",
		["sandbox"] = "Sandbox",
		["sidewinder"] = "Avalanche",
		["spacecamp"] = "Orbital",
		["warehouse"] = "Foundry",
		["s3d_waterfall"] = "Waterfall",
		["s3d_edge"] = "Edge",
		["s3d_turf"] = "Icebox",
	};

	private static readonly Dictionary<string, string> OdstMaps = new()
	{
		// Campaign
		["c100"] = "Prepare To Drop",
		["sc100"] = "Tayari Plaza",
		["sc110"] = "Uplift Reserve",
		["sc120"] = "Kizingo Blvd.",
		["sc130"] = "ONI Alpha Site",
		["sc140"] = "NMPD HQ",
		["sc150"] = "Kikowani Stn.",
		["l200"] = "Data Hive",
		["l300"] = "Coastal Highway",
		["h100"] = "Mombasa Streets",
		["c200"] = "Epilogue",
	};

	private static readonly Dictionary<string, string> ReachMaps = new()
	{
		// Campaign
		["m05"] = "Noble Actual",
		["m10"] = "Winter Contingency",
		["m20"] = "ONI Sword Base",
		["m30"] = "Nightfall",
		["m35"] = "Tip of the Spear",
		["m45"] = "Long Night of Solace",
		["m50"] = "Exodus",
		["m52"] = "New Alexandria",
		["m60"] = "The Package",
		["m70"] = "The Pillar of Autumn",
		["m70_a"] = "Credits",
		["m70_bonus"] = "Lone Wolf",
		// Multiplayer
		["20_sword_slayer"] = "Sword Base",
		["30_settlement"] = "Powerhouse",
		["35_island"] = "Spire",
		["45_aftship"] = "Zealot",
		["45_launch_station"] = "Countdown",
		["50_panopticon"] = "Boardwalk",
		["52_ivory_tower"] = "Reflection",
		["70_boneyard"] = "Boneyard",
		["forge_halo"] = "Forge World",
		["cex_beavercreek"] = "Battle Canyon",
		["cex_damnation"] = "Penance",
		["cex_hangemhigh"] = "High Noon",
		["cex_headlong"] = "Breakneck",
		["cex_prisoner"] = "Solitary",
		["cex_timberland"] = "Ridgeline",
		["condemned"] = "Condemned",
		["dlc_invasion"] = "Breakpoint",
		["dlc_medium"] = "Tempest",
		["dlc_slayer"] = "Anchor 9",
		["trainingpreserve"] = "Highlands",
		// Firefight
		["ff10_prototype"] = "Overlook",
		["ff20_courtyard"] = "Courtyard",
		["ff30_waterfront"] = "Waterfront",
		["ff45_corvette"] = "Corvette",
		["ff50_park"] = "Beachhead",
		["ff60_airview"] = "Outpost",
		["ff60_icecave"] = "Glacier",
		["ff70_holdout"] = "Holdout",
		["cex_ff_halo"] = "Installation 04",
		["ff_unearthed"] = "Unearthed",
	};

	private static readonly Dictionary<string, string> Halo4Maps = new()
	{
		// Campaign
		["m05_prologue"] = "Prologue",
		["m10_crash"] = "Dawn",
		["m020"] = "Requiem",
		["m30_cryptum"] = "Forerunner",
		["m40_invasion"] = "Reclaimer",
		["m60_rescue"] = "Infinity",
		["m70_liftoff"] = "Shutdown",
		["m80_delta"] = "Composer",
		["m90_sacrifice"] = "Midnight",
		["m95_epilogue"] = "Epilogue",
		// Multiplayer
		["ca_blood_cavern"] = "Abandon",
		["ca_blood_crash"] = "Exile",
		["ca_canyon"] = "Meltdown",
		["ca_forge_bonanza"] = "Impact",
		["ca_forge_erosion"] = "Erosion",
		["ca_forge_ravine"] = "Ravine",
		["ca_gore_valley"] = "Longbow",
		["ca_redoubt"] = "Vortex",
		["ca_tower"] = "Solace",
		["ca_warhouse"] = "Adrift",
		["wraparound"] = "Haven",
		["z05_cliffside"] = "Complex",
		["z11_valhalla"] = "Ragnarok",
		["ca_basin"] = "Outcast",
		["ca_creeper"] = "Pitfall",
		["ca_deadlycrossing"] = "Monolith",
		["ca_dropoff"] = "Vertigo",
		["ca_highrise"] = "Perdition",
		["ca_port"] = "Landfall",
		["ca_rattler"] = "Skyline",
		["ca_spiderweb"] = "Daybreak",
		["dlc_dejewel"] = "Shatter",
		["dlc_dejunkyard"] = "Wreckage",
		["dlc_forge_island"] = "Forge Island",
		["zd_02_grind"] = "Harvest",
		// Spartan Ops
		["dlc01_engine"] = "Infinity",
		["dlc01_factory"] = "Lockup",
		["ff151_mezzanine"] = "Control",
		["ff152_vortex"] = "Cyclone",
		["ff153_caverns"] = "Warrens",
		["ff154_hillside"] = "Apex",
		["ff155_breach"] = "Harvester",
		["ff81_courtyard"] = "The Gate",
		["ff82_scurve"] = "The Cauldron",
		["ff84_temple"] = "The Refuge",
		["ff86_sniperalley"] = "Sniper Alley",
		["ff87_chopperbowl"] = "Quarry",
		["ff90_fortsw"] = "Fortress",
		["ff91_complex"] = "Galileo Base",
		["ff92_valhalla"] = "Two Giants",
	};

	private static readonly Dictionary<string, string> StubbsMaps = new()
	{
		["a10_plaza"] = "Welcome to Punchbowl",
		["a30_greenhouse"] = "Bleeding Ground",
		["a40_police_station"] = "The Slammer",
		["a45_dance"] = "Cop Rock",
		["a50_maul"] = "Painting the Town Red",
		["a60_maulfight"] = "Punchbowl Maul",
		["b10_farm_house"] = "Fall of the House of Otis",
		["b30_dam"] = "When the Zombie Breaks",
		["c10_offender"] = "The Sacking of Punchbowl",
		["c30_lab"] = "The Doctor Will See You Now",
		["c40_cityhall"] = "Paved with Good Intentions",
		["c50_end"] = "The Ghoul of Your Dreams",
	};
}
