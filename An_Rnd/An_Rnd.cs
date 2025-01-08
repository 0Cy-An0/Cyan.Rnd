using BepInEx;
using RoR2;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AddressableAssets;
using BepInEx.Configuration;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Networking;
using BepInEx.Bootstrap;
using ProperSave;
using RiskOfOptions;

namespace An_Rnd
{
    //RoR2API stuff
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.rune580.riskofoptions", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.KingEnderBrine.ProperSave", BepInDependency.DependencyFlags.SoftDependency)]

    public class An_Rnd : BaseUnityPlugin
    {
        public const string PluginGUID = "Cyan.Rnd";
        public const string PluginAuthor = "Cy/an";
        public const string PluginName = "Cy/an Rnd";
        public const string PluginVersion = "1.0.0";

        //shopPortal is not neccessary anymore but ill leave it to reuse use when testing
        private static GameObject shopPortalPrefab;
        private static GameObject raidPortalPrefab;
        private static GameObject teleporterPrefab;

        public static int arenaCount = -1; //this will count how many times the void fields were entered; just using the name convention of base RoR2 for the stage
        //starts at -1 so that first entry is 0

        //Cap for arenaCounter
        public static int arenaCap = 0;
        //Will make this a riskofOptionsOption, probably, in the future; If this even does anything by then, which it does not currenlty, while i am adding Options
        public static int chunkSize = 50;
        //how many shrines shall activate per entry of the fields; first entry is always base game 0
        public static int numShrines = 5;
        //How many times the void fields roll for items per activiation. Every rolled item stays!
        public static int extraStacks = 1;
        //How many shrines need to be active for the above option to increase by 1. Will set on entry to active / Threshold (int div).
        public static int extraStacksThreshold = 0;
        //How many times the void fields roll for monsters per activition. Every rolled monster stays!
        public static int extraMonsterTypes = 1;
        //How many shrines need to be active for the above option to increase by 1. Will set on entry to active / Threshold (int div). [you may notice some of these comments may be almost to entirely copy-paste]
        public static int extraMonsterTypesThreshold = 0;
        //how many credits are added per active shrine to the arena base credits
        public static float extraMonsterCredits = 0;
        //How many extra items are given to the enemies per active mountain shrine (Rounded down based on the number normally given)
        public static float extraItems = 0f;
        //How many items are spawned after picking per active mountain shrine (Rounded down, because i can't spawn fractions of items)
        public static float extraRewards = 0f;
        //float to increase the Stage after a finished void cell by (scaled with difficulty option) [check the tied option if this explanation is unclear]
        public static float stageIncrease = 0f;
        //will be multiplied to the base radius of the void cells
        public static float voidRadius = 1f;
        //skip unmodifed void fields boolean
        public static bool skipVanilla = false;
        //Super Secret Option
        public static bool KillMeOption = false;
        //multiply by 2 x times instead of just adding x? For the mountain shrines ('numShrines')
        public static bool expScaling = false;
        //for teleporter spawn/counters with mountain shrines
        public static bool useShrine = false;
        //this will store the inventory of the enemies last void Fields; Items are stored as an array of Ints
        public static int[] latestInventoryItems = []; //if i do not set a default, this causes problem with ProperSave
        //Will remove all pre-game(logbook, select, etc.) Hightlights automatically if true
        public static bool noHightlights = false;
        //Option for Bleed-Stacking
        public static bool enableBleed = false;
        //Option for Crit-Stacking
        public static bool enableCrit = false;
        //Option if second crit should multiply the crit damage by 2 (base *4) instead of just base damage *3;will effect third crits, etc. the same way
        public static bool critMults = false;
        //Optionto roll same itemstacks multiple times
        public static bool allowDuplicates = false;
        //Option to add items to inventory directly
        public static bool preventDrops = false;
        //Option to specifically deal with lunar items when 'preventDrops' is enabled
        public static bool preventLunar = false;
        //Need a way to keep track if properSave was used. (relevant in 'ResetRunVars')
        public static bool wasLoaded = false;
        //max charge for all 9 cells (void fields)
        public static float[] maxCharges = [0.11f, 0.22f, 0.33f, 0.44f, 0.55f, 0.66f, 0.77f, 0.88f, 1f];
        //starting charge for all 9 cells (void fields)
        public static float[] startCharges = [0f, 0.11f, 0.22f, 0.33f, 0.44f, 0.55f, 0.66f, 0.77f, 0.88f];
        //charge duration multiplier (void fields)
        public static float chargeDurationMult = 0f;
        //current cell Counter for the void fields; used for 'maxCharges'
        public static int currentCell = -1;
        //current PlayerCounter for item distribution
        public static int currentPlayer = 0;
        //only used if useShrine is false; substitutes/is substituted by mountain shrines
        public static int DifficultyCounter = 0;
        //should be what exactly what the name says. Check method 'RemoveMatchingMonsterCards' for specific use
        public static String monsterBlacklist = "";
        //Will update on Stage start to contain all Printers, Scrappers, Cauldrons and the portal. Used to determin which gameobject spawned a given droplet
        public static List<GameObject> purchaseInteractables = []; //do not ask me why the component thingy, which the list is named after, is named PurchaseInteraction if its used for things that cost something and things without (actually i might be stupid, i think it counts if you use money OR an item; Scrapper needed to be handled seperatly anyway)
        public static short networkId = 4379;

        // The Awake() method is run at the very start when the game is initialized.
        public void Awake()
        {
            // Init our logging class so that we can properly log for debugging
            Log.Init(Logger);
            InitPortalPrefab();
            //loads(creates) config file and tries to load anything marked with SoftDependency
            ConfigandTrySoft();

            On.RoR2.BazaarController.OnStartServer += CheckNullPortal;
            On.RoR2.PickupPickerController.CreatePickup_PickupIndex += MultiplyItemReward;
            On.RoR2.ArenaMissionController.AddItemStack += MultiplyEnemyItem;
            On.RoR2.ArenaMissionController.AddMonsterType += MultiplyEnemyType;
            On.RoR2.ArenaMissionController.BeginRound += ActivateCell;
            On.RoR2.ArenaMissionController.EndRound += FinishCell;
            On.RoR2.HoldoutZoneController.Update += ZoneCharge;
            On.RoR2.GenericPickupController.CreatePickup += AddItemDirectly;
            On.RoR2.PickupDropletController.CreatePickupDroplet_CreatePickupInfo_Vector3_Vector3 += AddDropletDirectly;
            On.RoR2.HealthComponent.TakeDamage += ExtraCrit;
            On.RoR2.DotController.AddDot += ExtraBleed;
            On.RoR2.Stage.Start += CheckTeleporterInstance;
            On.RoR2.Run.Start += ResetRunVars;
            On.RoR2.UserProfile.HasViewedViewable += Viewed;
            On.RoR2.PurchaseInteraction.OnInteractionBegin += Purchase;
            NetworkUser.onPostNetworkUserStart += TryRegisterNetwork;
        }

        private void InitPortalPrefab()
        {
            shopPortalPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/PortalShop/PortalShop.prefab").WaitForCompletion();
            raidPortalPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/PortalArena/PortalArena.prefab").WaitForCompletion();
            //Change flags, such that Null Portal actually connects to the void fields.
            raidPortalPrefab.GetComponent<SceneExitController>().useRunNextStageScene = false;
            teleporterPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Teleporters/Teleporter1.prefab").WaitForCompletion();
        }

        private void ConfigandTrySoft()
        {
            //i could have just done var configEntries, but isn't it fun how long this is? Check comments of CreateLoadConfig if you want to know why (more like what, why is more because use in the end loop(in CreateLoad) and in RiskOfOptions)
            (ConfigEntryBase config, Type StaticType, Action<object> updateStaticVar, object min, object max)[] configEntries = CreateLoadConfig();

            if (Chainloader.PluginInfos.ContainsKey("com.rune580.riskofoptions")) InitRiskOfOptions(configEntries);
            else Log.Warning("RiskOfOptions is not available"); //could probably make this a .Info, but i think i like this better
            
            if (Chainloader.PluginInfos.ContainsKey("com.KingEnderBrine.ProperSave")) InitProperSave();
            else Log.Warning("ProperSave is not available.");
        }
        
        private void InitProperSave()
        {
            SaveFile.OnGatherSaveData += SaveToProperSave;
            Loading.OnLoadingStarted += LoadFromProperSave;

            void SaveToProperSave(Dictionary<String, object> dictionary)
            {
                // Create a combined array. ProperSave readme said to best to only save on thing (/combined thing)
                object[] saveData = new object[]
                {
                    latestInventoryItems,
                    arenaCount,
                    currentPlayer
                };
                //there was some error when i used my GUID, either i made a mistake or it was the '.' so I removed it
                dictionary["CyanRnd"] = saveData;
                Log.Info("Save data successfully added to ProperSave.");
            }

            void LoadFromProperSave(SaveFile file)
            {
                if(!file.ModdedData.TryGetValue("CyanRnd", out ProperSave.Data.ModdedData savedData))
                {
                    Log.Warning("Could not find SaveData");
                    return;
                }
                if (savedData.Value is object[] loadedData && loadedData.Length == 3)
                {
                    //So apperently ProperSaves loads saved int[] as object (which i kinda had to figure out via Consol Logs, because i am stupid) so here is a conversion, that i defintily came up with my self and did not ask chatGpt for
                    try
                    {
                        // Casts loadedData[0](object) to List<object> and converts this to int[](by casting it to List<int> and then converting to array._.) because of course i can't just cast to (int[])
                        latestInventoryItems = ((List<object>)loadedData[0]).Cast<int>().ToArray();
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Failed to Load save Data because: \"Failure to convert object to int[]. Exception: {ex.Message}\"");
                    }

                    arenaCount = Convert.ToInt32(loadedData[1]);
                    currentPlayer = Convert.ToInt32(loadedData[2]);
                    wasLoaded = true; //this is so that the loaded variables are not reset for 'a new run'

                    Log.Info($"Loaded latestInventoryItems, arenaCount({arenaCount}) and currentPlayer({currentPlayer}) with ProperSave.");
                }
                else
                {
                    Log.Error("Saved data format is invalid or unexpected.");
                }
            }

            Log.Info("ProperSave setup successful");
        }

        private void InitRiskOfOptions((ConfigEntryBase config, Type StaticType, Action<object> updateStaticVar, object min, object max)[]  configEntries)
        {

            foreach (var (config, varType, _, min, max) in configEntries)
            {
                AddOption(config, varType, min, max);
            }

            //Hook setting changes; Updates the given Variable if the Option was changed via RiskOfOptions
            foreach (var (config, StaticType, updateStaticVar, _, _) in configEntries)
            {
                // Cast to the specific type of ConfigEntry<T>
                if (StaticType == typeof(int))
                {
                    ConfigEntry<int> castConfig = (ConfigEntry<int>)config;
                    castConfig.SettingChanged += (sender, args) => updateStaticVar(castConfig.Value);
                }
                else if (StaticType == typeof(float))
                {
                    ConfigEntry<float> castConfig = (ConfigEntry<float>)config;
                    castConfig.SettingChanged += (sender, args) => updateStaticVar(castConfig.Value);
                }
                else if (StaticType == typeof(bool))
                {
                    ConfigEntry<bool> castConfig = (ConfigEntry<bool>)config;
                    castConfig.SettingChanged += (sender, args) => updateStaticVar(castConfig.Value);
                }
                else if (StaticType == typeof(String))
                {
                    ConfigEntry<String> castConfig = (ConfigEntry<String>)config;
                    castConfig.SettingChanged += (sender, args) => updateStaticVar(castConfig.Value);
                }
                else
                {
                    Log.Error($"Could not get type {StaticType} to hook SettingChanged for {config.Definition.Key}");
                }
            }

            Log.Info("RiskOfOptions setup successful.");
        }

        //this does what it says creates the config and loads values that may already be there and different from the default ones; returns all options in an array [should only be called once]
        private (ConfigEntryBase config, Type StaticType, Action<object> updateStaticVar, object min, object max)[] CreateLoadConfig()
        {
            // Define the Attray to store ConfigEntry, Typing, the corresponding update action for the static variable and min/max for the slider/field
            //ConfigEntryBase is Configurations for option with Category, Name, Default, and Description
            (ConfigEntryBase config, Type StaticType, Action<object> updateStaticVar, object min, object max)[] configEntries = 
            [
                (
                    Config.Bind("General", "No Hightlights", false, "If enabled, anytime the game asks if you have viewed a 'viewable' it will skip the check and return true.\nThis should effect things like logbook entries and new characters/abilities in the select screen"),
                    typeof(bool),
                    new Action<object>(value => noHightlights = (bool)value),
                    null,
                    null
                ),
                (
                    Config.Bind("General", "Prevent ItemDrops", false, "If enabled, stops normal item creation and adds them to the players inventory directly\nIterates over all players if Multiplayer\nyou can enable this temporary ingame if neccesary\nWith this Option enabled you will not pickup any items(tough they will still be added to your inventory), I had to specifically add PickupNotifications, i may have forgotten something if you notice any issue, please notify me(i am not sure were at the point of writing this but if the mod is public some time in the future you can definitly reach me somehow)\nThis may cause errors if there are other mods that try to do something to items while they spawn as this will set the spawn to 'null'"),
                    typeof(bool),
                    new Action<object>(value => preventDrops = (bool)value),
                    null,
                    null
                ),
                (
                    Config.Bind("General", "Do not prevent lunar", false, "If enabled, 'Prevent ItemDrops' will not effect any item from the tier 'Lunar' at all\nThis Option was added because of a very specific request by someone"),
                    typeof(bool),
                    new Action<object>(value => preventLunar = (bool)value),
                    null,
                    null
                ),
                (
                    Config.Bind("General", "Bleed Stacking", false, "If enabled, anytime a bleed effect is applied and the chance was over 100% there may be a second bleed stack with the remainder, which is added at the same time\nso with 600% crit chance you get 6 bleed stacks per hit"),
                    typeof(bool),
                    new Action<object>(value => enableBleed = (bool)value),
                    null,
                    null
                ),
                (
                    Config.Bind("General", "Crit Stacking", false, "If enabled, anytime a crit is rolled and the chance was over 100% there may be a second crit with the remainder, which amplifies the first\nwith default behaviour a crit(x2) deals x3 the base damage, where a crit(x1) would deal x2, this may be changed with the option below"),
                    typeof(bool),
                    new Action<object>(value => enableCrit = (bool)value),
                    null,
                    null
                ),
                (
                    Config.Bind("General", "Crit Stacking Multiplies", false, "If enabled, any second, third, etc. crits will not up the damage multiplier by just 1, but x2 instead\nfor example with a 200% crit chance the damage will always be x4 base damage as both crits are guranteed and the second amplifies the first x2 to x2 again"),
                    typeof(bool),
                    new Action<object>(value => critMults = (bool)value),
                    null,
                    null
                ),
                (
                    Config.Bind("Void Fields", "Use Shrines", false, "If enabled, will spawn a unusable teleporter in the void fields to activate mountainShrines instead of just an internal counter\nAny use of the difficulty counter is replaced by the current active mountain Shrines\nThis Option will probably do nothing if you do not have other mods that interact with mountain shrines, i reccomend looking for one that makes them persist over the whole run after activiating"),
                    typeof(bool),
                    new Action<object>(value => useShrine = (bool)value),
                    null,
                    null
                ),
                (
                    Config.Bind("Void Fields", "Difficulty Scaling", 5, "Number the Internal void field difficulty counter is set to per additional void field entry\n with otherwise unmodded entrys it goes 0,num,2xnum,3xnum,..."),
                    typeof(int),
                    new Action<object>(value => numShrines = (int)value),
                    0,
                    10000
                ),
                (
                    Config.Bind("Void Fields", "Skip unmodified fields", false, "If enabled, skips the first normal void fields and will directly apply the difficulty modifier"),
                    typeof(bool),
                    new Action<object>(value => skipVanilla = (bool)value),
                    null,
                    null
                ),
                (
                    Config.Bind("Void Fields", "exponential Scaling", false, "If enabled, will use 'Difficulty Scaling' to *2 instead of just adding\nfor example if the difficulty were normally set to 4 this would make it add 1 and then do *2,*2,*2 for a total of 8 (adding 1 first so that its not 0*2)"),
                    typeof(bool),
                    new Action<object>(value => expScaling = (bool)value),
                    null,
                    null
                ),
                (
                    Config.Bind("Void Fields", "Stage Counter Cap", 0, "Sets a cap for the Stage Counter, which is what 'Difficulty Scaling' is multiplied by for each entry\n0 for disabled\nExample with a cap of 2 and a scaling of 3 would be 0*3=0, 1*3=3, 2*3=6, 2*3=6, ...\nThis only effects what is added if you, for example, use shrines and have a persistent shrine mod they will still be uncapped and will just always scale by the current settings *Cap at most"),
                    typeof(int),
                    new Action<object>(value => arenaCap = (int)value),
                    0,
                    1000
                ),
                (
                    Config.Bind("Void Fields", "Enemy ItemStacks", 1, "Sets the number of itemStacks the void fields enemies can obtain.\n1 per activation is vanilla, but with this you can get for example goat's hoof and crit classes at the same time\ndisabled if Kill Me is checked"),
                    typeof(int),
                    new Action<object>(value => extraStacks = (int)value),
                    1,
                    1000
                ),
                (
                    Config.Bind("Void Fields", "Enemy Extra ItemStacks Threshold", 0, "Difficulty Count required to increase 'Enemy Extra ItemStacks' by 1\nExample if Set to 100 and the Counter is at 225, it will use your ItemStacks setting +2\n0 for disabled"),
                    typeof(int),
                    new Action<object>(value => extraStacksThreshold = (int)value),
                    0,
                    10000
                ),
                (
                    Config.Bind("Void Fields", "Allow Duplicates", false, "If enabled this allows the void fields to roll the same item twice\nNormally as long as you god crit glasses once they will not be added to the inventory again, this will make it so you could get crit glasses twice thus possibly having to deal with a higher number(of the same) but less differnt items"),
                    typeof(bool),
                    new Action<object>(value => allowDuplicates = (bool)value),
                    null,
                    null
                ),
                (
                    Config.Bind("Void Fields", "Monsters", 1, "Sets the number of Monsters the void fields add at once.\nReminder that this will be capped by your enemies as in the further you are in stage/lvl you may get harder enemies which you will never get even if you try 100 times (=this set to 100) stage 1\nStill causes an error but seems to work anyway, error is logged, if you are curious, but feel safe to use this for now"),
                    typeof(int),
                    new Action<object>(value => extraMonsterTypes = (int)value),
                    1,
                    1000
                ),
                (
                    Config.Bind("Void Fields", "Monsters Threshold", 0, "Difficulty Count required to increase 'Monsters' by 1\nExample if Set to 20 and the Counter is at 60, it will use your Monster Setting +3\n0 for disabled"),
                    typeof(int),
                    new Action<object>(value => extraMonsterTypesThreshold = (int)value),
                    0,
                    10000
                ),
                (
                    Config.Bind("Void Fields", "Monster Blacklist", "", "Any String written here in the form of '[Name],[Name],...' Will be matched to the potential enemy pool and removed if a match is found\nExample, RoR2 has the Spawn Card 'cscLesserWisp' so having this set to 'cscLesserWisp' will remove only the Wisp from the potential Enemies. Setting it to 'cscLesserWisp,cscGreaterWisp' will remove both lesser and greater Wisp, wereas 'Wisp' will remove any that have the name Wisp in them which might remove other modded entries like Ancient Wisp\nAt this point you just have to know or guess the names of the SpawnCards"),
                    typeof(String),
                    new Action<object>(value => monsterBlacklist = (String)value),
                    null,
                    null
                ),
                (
                    Config.Bind("Void Fields", "Extra Credits", 0f, "How many extra credits are given to the void fields per Difficulty Counter\n0 for disabled[i mean you add 0, so...]\nI am not 100% sure but RoR2 may move unused credits to the next stage combat director, so be aware"),
                    typeof(float),
                    new Action<object>(value => extraMonsterCredits = (float)value),
                    0f,
                    10000f
                ),
                (
                    Config.Bind("Void Fields", "Enemy Extra Items", 1f, "Multiplier for void field enemy items per Difficulty Count.\n0 for disable"),
                    typeof(float),
                    new Action<object>(value => extraItems = (float)value),
                    0f,
                    10000f
                ),
                (
                    Config.Bind("Void Fields", "Reward Item Multiplier", 1f, "Multiplier for void field rewards per Difficulty Count.\n0 for disable"),
                    typeof(float),
                    new Action<object>(value => extraRewards = (float)value),
                    0f,
                    10000f
                ),
                (
                    Config.Bind("Void Fields", "Increase Stage Counter after Cell Finish", 0f, "This value will be multiplied by the difficulty counter and Increase the Stage after each cell by that amount\nAs only whole numbers are allowed it will use the floor so it could be 0 for the first few times if the difficulty and this setting is low enough\n0 for disable"),
                    typeof(float),
                    new Action<object>(value => stageIncrease = (float)value),
                    0f,
                    10f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell Radius", 1f, "Multiplies the base radius with the given number"),
                    typeof(float),
                    new Action<object>(value => voidRadius = (float)value),
                    0f,
                    10000f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell Charge duration", 2f, "Multiplies the base charge duration with the given number\nDefault is 2, which is twice as long, because by default settings all cells only need 11%. All in all you go from 0 to 100% once only for a total speed increase of 4.5(if i mathed(?) correctly)"),
                    typeof(float),
                    new Action<object>(value => chargeDurationMult = (float)value),
                    0.0001f,
                    1000f
                ),
                ( //i wanted to add these via a loop but that caused some wierd error with RiskOfOptions... [I replaced this with an array at a later point; it seemed more fitting because of such things too]
                    Config.Bind("Void Fields", "Void Cell 1 start Charge", 0f, "The void cell starts at the given percentage for Cell 1"),
                    typeof(float),
                    new Action<object>(value => startCharges[0] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 1 max Charge", 0.11f, "At the given percent, the void cell instantly finishes for Cell 1"),
                    typeof(float),
                    new Action<object>(value => maxCharges[0] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 2 start Charge", 0.11f, "The void cell starts at the given percentage for Cell 2"),
                    typeof(float),
                    new Action<object>(value => startCharges[1] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 2 max Charge", 0.22f, "At the given percent, the void cell instantly finishes for Cell 2"),
                    typeof(float),
                    new Action<object>(value => maxCharges[1] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 3 start Charge", 0.22f, "The void cell starts at the given percentage for Cell 3"),
                    typeof(float),
                    new Action<object>(value => startCharges[2] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 3 max Charge", 0.33f, "At the given percent, the void cell instantly finishes for Cell 3"),
                    typeof(float),
                    new Action<object>(value => maxCharges[2] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 4 start Charge", 0.33f, "The void cell starts at the given percentage for Cell 4"),
                    typeof(float),
                    new Action<object>(value => startCharges[3] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 4 max Charge", 0.44f, "At the given percent, the void cell instantly finishes for Cell 4"),
                    typeof(float),
                    new Action<object>(value => maxCharges[3] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 5 start Charge", 0.44f, "The void cell starts at the given percentage for Cell 5"),
                    typeof(float),
                    new Action<object>(value => startCharges[4] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 5 max Charge", 0.55f, "At the given percent, the void cell instantly finishes for Cell 5"),
                    typeof(float),
                    new Action<object>(value => maxCharges[4] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 6 start Charge", 0.55f, "The void cell starts at the given percentage for Cell 6"),
                    typeof(float),
                    new Action<object>(value => startCharges[5] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 6 max Charge", 0.66f, "At the given percent, the void cell instantly finishes for Cell 6"),
                    typeof(float),
                    new Action<object>(value => maxCharges[5] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 7 start Charge", 0.66f, "The void cell starts at the given percentage for Cell 7"),
                    typeof(float),
                    new Action<object>(value => startCharges[6] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 7 max Charge", 0.77f, "At the given percent, the void cell instantly finishes for Cell 7"),
                    typeof(float),
                    new Action<object>(value => maxCharges[6] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 8 start Charge", 0.77f, "The void cell starts at the given percentage for Cell 8"),
                    typeof(float),
                    new Action<object>(value => startCharges[7] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 8 max Charge", 0.88f, "At the given percent, the void cell instantly finishes for Cell 8"),
                    typeof(float),
                    new Action<object>(value => maxCharges[7] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 9 start Charge", 0.88f, "The void cell starts at the given percentage for Cell 9"),
                    typeof(float),
                    new Action<object>(value => startCharges[8] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 9 max Charge", 1f, "At the given percent, the void cell instantly finishes for Cell 9"),
                    typeof(float),
                    new Action<object>(value => maxCharges[8] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Kill Me", false, "If enabled, enemies gain one of every item type instead of a single item type.\nWill also add all Equipment at the same time\nVoid field rewards are now only lunar items(idk why). Items/Equipment may not show up in the listing but are still present\nTHIS OPTION HAS A SPECIFIC NAME FOR A REASON\nTHIS OPTION HAS A SPECIFIC NAME FOR A REASON\nTHIS OPTION HAS A SPECIFIC NAME FOR A REASON"),
                    typeof(bool),
                    new Action<object>(value => KillMeOption = (bool)value),
                    null, //a bool option does not need a min/max [should be somewhat obvious i guess]
                    null
                )

            ];

            //change from default values if config is already present:
            foreach (var (config, type, update, _, _) in configEntries)
            {
                if (type == typeof(int))
                {
                    ConfigEntry<int> castConfig = (ConfigEntry<int>)config;
                    update(castConfig.Value);
                }
                else if (type == typeof(float))
                {
                    ConfigEntry<float> castConfig = (ConfigEntry<float>)config;
                    update(castConfig.Value);
                }
                else if (type == typeof(bool))
                {
                    ConfigEntry<bool> castConfig = (ConfigEntry<bool>)config;
                    update(castConfig.Value);
                }
                else if (type == typeof(String))
                {
                    ConfigEntry<String> castConfig = (ConfigEntry<String>)config;
                    update(castConfig.Value);
                }
                else
                {
                    Log.Error($"{config.Definition.Key} is of invalid type: {type}");
                }
            }

            return configEntries;
        }

        //Adds RiskOfOptions Option
        private void AddOption(ConfigEntryBase config, Type varType, object min, object max)
        {
            //make sure the config that is beeing used here is used with the correct typing
            if (varType == typeof(int))
            {
                RiskOfOptions.OptionConfigs.IntSliderConfig intConfig = new()
                {
                    min = (int)min,
                    max = (int)max,
                };
                RiskOfOptions.Options.IntSliderOption option = new((ConfigEntry<int>)config, intConfig);
                ModSettingsManager.AddOption(option);
                Log.Info($"Added RiskOfOption '{config.Definition.Key}' as IntSlider");
            }
            else if (varType == typeof(float))
            {
                RiskOfOptions.OptionConfigs.FloatFieldConfig floatConfig = new()
                {
                    //why are these capitalized?
                    Min = (float)min,
                    Max = (float)max,
                };
                RiskOfOptions.Options.FloatFieldOption option = new((ConfigEntry<float>)config, floatConfig);
                ModSettingsManager.AddOption(option);
                Log.Info($"Added RiskOfOption '{config.Definition.Key}' as FloatField");
            }
            else if (varType == typeof(bool))
            {
                RiskOfOptions.Options.CheckBoxOption option = new((ConfigEntry<bool>)config);
                ModSettingsManager.AddOption(option);
                Log.Info($"Added RiskOfOption '{config.Definition.Key}' as CheckBox");
            }
            else if (varType == typeof(String))
            {
                RiskOfOptions.Options.StringInputFieldOption option = new((ConfigEntry<string>)config);
                ModSettingsManager.AddOption(option);
                Log.Info($"Added RiskOfOption '{config.Definition.Key}' as StringInputField");
            }
            else
            {
                Log.Error($"Failed to create option for {config.Definition.Key} because type was {varType}");
            }
        }

        //below this comment are hooks and stuff, above should only be awake/Init type stuff. (very good explain: me)
        private bool Viewed(On.RoR2.UserProfile.orig_HasViewedViewable orig, UserProfile self, string viewableName)
        {
            if (noHightlights) return true;
            return orig(self, viewableName);
        }

        private GenericPickupController AddItemDirectly(On.RoR2.GenericPickupController.orig_CreatePickup orig, ref GenericPickupController.CreatePickupInfo createPickupInfo)
        {
            PickupDef item = createPickupInfo.pickupIndex.pickupDef;
            if (preventLunar && item.itemTier == ItemTier.Lunar) return orig(ref createPickupInfo);
            if (preventDrops && item.itemIndex != ItemIndex.None)
            {
                AddToPlayerInventory(item.itemIndex);
                return null;
            }
            return orig(ref createPickupInfo);
        }

        //this creates droplet objects which then turn into items, but thousands of them cause too much lag
        private void AddDropletDirectly(On.RoR2.PickupDropletController.orig_CreatePickupDroplet_CreatePickupInfo_Vector3_Vector3 orig, GenericPickupController.CreatePickupInfo pickupInfo, Vector3 position, Vector3 velocity)
        {
            PickupDef item = pickupInfo.pickupIndex.pickupDef;
            if (preventLunar && item.itemTier == ItemTier.Lunar)
            {
                orig(pickupInfo, position, velocity);
                return;
            }
            if (!preventDrops || item.itemIndex == ItemIndex.None)
            {
                orig(pickupInfo, position, velocity);
                return;
            }

            float smallestDistance;
            GameObject smoll;
            (smallestDistance, smoll) = FindNearest(position);

            //its about close enough so 'Object', is probably the cause
            if (smallestDistance <= 26f) //i tried 8f before but both ChanceShrine(18.+) and triple shop(20.+) were to faar away; MultiShopLarge was 25.96055, always... (why is it different to the multishop, who knows; i still do not have a grasp on how far 1 unit is so idk if thats too far at this point, but i hope its fine)
            {
                int index = GetPlayerIndexFromInteractionObject(smoll);
                AddToPlayerInventory(item.itemIndex, index);
                return;
            }

            AddToPlayerInventory(item.itemIndex);
        }

        private void Purchase(On.RoR2.PurchaseInteraction.orig_OnInteractionBegin orig, PurchaseInteraction self, Interactor activator)
        {
            //for some reason this was not always set which i implicitly assumed for 'AddDropletDirectly'; for lunar bazaar specifically i could do self.gameObject.GetComponent<ShopTerminalBehavior>.NetworkpickupIndex to get the item but i don't think it would work for the others(tough they might have similar options); i would have to stop On.RoR2.PurchaseInteraction.CreateItemTakenOrb and it should still be the same but Im probably not going to make such huge changes if there are no big problems (i found this option to late sadly)
            self.lastActivator = activator;
            orig(self, activator); //not sure if that was exlusive to the ones were it did not work, or if its normal but it normally sets the activator at the start of orig, but for the bazaar taking a lunar item lastActivator was not set and calling orig just spawned the item
        }

        private (float, GameObject) FindNearest(Vector3 position)
        {
            GameObject smoll = null;
            float smallestDistance = float.MaxValue;

            foreach (GameObject dropletSpawner in purchaseInteractables)
            {
                //idk why but sometimes the list had a lot of null entries; i am unsure where my Repopulation failed, but i think this should be a fine enugh solution
                if (dropletSpawner == null) //Repopulation on StageStart failed for some reason
                {
                    RePopulateInteractList();
                    return FindNearest(position);
                }

                //actually distance squared but i heard computers are faster not doing sqrt(), which makes sense
                float distance = (position - dropletSpawner.transform.position).sqrMagnitude;

                // Update the smallest distance if the current distance is smaller
                if (distance < smallestDistance)
                {
                    smallestDistance = distance;
                    smoll = dropletSpawner;
                }
            }

            return (smallestDistance, smoll);
        }

        private void ExtraBleed(On.RoR2.DotController.orig_AddDot orig, DotController self, GameObject attackerObject, float duration, DotController.DotIndex dotIndex, float damageMultiplier, uint? maxStacksFromAttacker, float? totalDamage, DotController.DotIndex? preUpgradeDotIndex)
        {
            CharacterBody attacker = attackerObject.GetComponent<CharacterBody>();

            //check for if the option is enabled, this add is about a bleed stack and there is a attacker (i cant get the bleed chance otherwise)
            if (!enableBleed || DotController.DotIndex.Bleed != dotIndex || attacker == null)
            {
                orig(self, attackerObject, duration, dotIndex, damageMultiplier, maxStacksFromAttacker, totalDamage, preUpgradeDotIndex);
                return;
            }

            int extraBleedStacks = (int)(attacker.bleedChance / 100f) - 1; //same as crit, no idea why store it as 100f = 100% and not 1f = 100%
            if (extraBleedStacks < 0) //no change if the bleed chance, is below 100%
            {
                orig(self, attackerObject, duration, dotIndex, damageMultiplier, maxStacksFromAttacker, totalDamage, preUpgradeDotIndex);
                return;
            }
            float remainingChance = attacker.bleedChance % 100f;

            //Im just did same rng as for crit (check my notes there for more info as to why this is this way) without making super sure
            if (remainingChance > 0)
            {
                float roll = UnityEngine.Random.value * 100.0f;
                if (roll < remainingChance)
                {
                    extraBleedStacks += 1;
                }
            }

            orig(self, attackerObject, duration, dotIndex, damageMultiplier, maxStacksFromAttacker, totalDamage, preUpgradeDotIndex);
            //repeating Add_Dot, for each extra Stack, which hopefully means they are added correctly
            for (int i = 0; i < extraBleedStacks; i++)
            {
                orig(self, attackerObject, duration, dotIndex, damageMultiplier, maxStacksFromAttacker, totalDamage, preUpgradeDotIndex);
            }
        }

        //it seemed like any modification to/with damageInfo before this point did not reach to here, not sure why
        private void ExtraCrit(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            //abort if crit modification is disable or this is not a crit or there is no attacker
            if (!enableCrit || !damageInfo.crit || !damageInfo.attacker)
            {
                orig(self, damageInfo);
                return;
            }

            CharacterBody attacker = damageInfo.attacker.GetComponent<CharacterBody>();
            if (attacker == null) //also abort if attacker has no characterBody
            {
                orig(self, damageInfo);
                return;
            }

            int critMult = (int)(attacker.crit / 100f) - 1; //I am unsure why attacker.crit is stored as a float but 100% = 100f and not 1f
            if (critMult < 0) //no change if the crit chance, is below 100%
            {
                orig(self, damageInfo);
                return;
            }
            float remainingChance = attacker.crit % 100f;

            //i could not find a rng object for the crits, and testing with properSave showed apperant randomness even from the same load (so using normal unity randomness just because it's probably close enough)
            if (remainingChance > 0)
            {
                float roll = UnityEngine.Random.value * 100.0f;
                if (roll < remainingChance)
                {
                    critMult += 1;
                }
            }

            //critMultiplier is very shortly increase to the desired value for the dmg calculation and reset after
            float OGMult = attacker.critMultiplier;
            if (critMults)
            {
                attacker.critMultiplier += (int)Math.Pow(2, (double)critMult);
            }
            else
            {
                attacker.critMultiplier += critMult;
            }

            orig(self, damageInfo);
            attacker.critMultiplier = OGMult;
            return;
        }

        private void ResetRunVars(On.RoR2.Run.orig_Start orig, Run self)
        {
            if (wasLoaded)
            {
                wasLoaded = false;
                orig(self);
                return;
            }
            latestInventoryItems = new int[0];
            arenaCount = -1;
            orig(self);
        }

        private IEnumerator CheckTeleporterInstance(On.RoR2.Stage.orig_Start orig, Stage self)
        {
            //Extra Logic used when ItemDrops are prevented to determin things like scrap owner
            StartCoroutine(DelayRePopulate()); //i am a bit unclear with the wait a second thing I do here with c#/unity; it says startCoroutine does that mean a new thread? because i do not believe i do anything threadsafe at all, i could look it up or just write this here and ignore it if until it becomes a problem

            //there is 'self.sceneDef.baseSceneName' but its seems to not be an instance for some reason, so i found this: 'SceneInfo.instance.sceneDef.baseSceneName'
            if (SceneInfo.instance.sceneDef.baseSceneName == "arena")
            {
                if (skipVanilla && arenaCount < 0) arenaCount = 0;
                arenaCount += 1; //counter how often we entered the void fields
                if (arenaCap > 0 && arenaCount > arenaCap) arenaCount = arenaCap;
                currentCell = -1; //reset current Cell counter; example use in 'ActivateCell' and 'ZoneCharge' [-1 because it does +1 always and 0-index]
                DifficultyCounter = 0; //reset DifficultyCounter even tough it may not be used depening on choosen options

                ArenaMissionController controller = FindObjectOfType<ArenaMissionController>();
                //non-host check
                if (controller == null)
                {
                    return orig(self);
                }
                //remove cards from the pool the Controller chooses the monsters from
                RemoveMatchingMonsterCards(controller); //Matching as in matching the filter given by the config option, which at this point i have not decieded on how to implement

                //this should start the enemies with the items of the last attempts
                if (latestInventoryItems.Length > 0)
                {
                    Inventory inv = controller.inventory;
                    //adding the stored items back orderd by tier
                    foreach (ItemIndex index in ItemCatalog.tier1ItemList)
                    {
                        inv.GiveItem(index, latestInventoryItems[(int)index]);
                    }
                    foreach (ItemIndex index in ItemCatalog.tier2ItemList)
                    {
                        inv.GiveItem(index, latestInventoryItems[(int)index]);
                    }
                    foreach (ItemIndex index in ItemCatalog.tier3ItemList)
                    {
                        inv.GiveItem(index, latestInventoryItems[(int)index]);
                    }
                }

                if (useShrine)
                {
                    //the 'arena' also known as the void fields, does not have a teleporter, but i want to activate mountain shrines anyway
                    GameObject portal = Instantiate(teleporterPrefab, new Vector3(0, -1000, 0), Quaternion.identity); // I hope -1000 is away from everything/unreachable
                    //btw i do not sync portal to client, which i had to do for the null portal, but its supposed to be inaccesible anyway, so that should be fine

                    //Allow time for other mods to add shrines on stage start
                    StartCoroutine(AddShrinesDelay(controller));
                }
                else //we do not need a portal now, and the shrines are replaces by DifficultyCounter
                {
                    DifficultyCounter += numShrines;

                    if (expScaling && numShrines > 0)
                    {
                        DifficultyCounter += (int) (arenaCount * DifficultyCounter * Math.Pow(2.0, (double)numShrines));
                    }
                    else
                    {
                        DifficultyCounter += arenaCount * numShrines;
                    }

                    controller.baseMonsterCredit += extraMonsterCredits * DifficultyCounter;
                }

            }
            return orig(self);
        }

        private IEnumerator DelayRePopulate()
        {
            //waiting because for example TripleShopController spawns the PurchaseInteraction thingys later, i assume 1 frame but waiting a bit anyway
            yield return new WaitForSeconds(0.1f);
            RePopulateInteractList();
        }

        private void RePopulateInteractList()
        {
            purchaseInteractables.Clear();
            GameObject[] allGameObjects = FindObjectsOfType<GameObject>();

            //trying to get the objects that spawn items and hold who interacted with them which was more complicated before i had the very smart idea to directly check the relevant component
            purchaseInteractables = allGameObjects
            .Where(obj => obj.GetComponent<PurchaseInteraction>() != null || obj.GetComponent<ScrapperController>() != null)
            .Distinct()
            .ToList();
        }

        private IEnumerator AddShrinesDelay(ArenaMissionController controller)
        {
            yield return new WaitForSeconds(0.1f);

            int toAdd = numShrines * arenaCount;
            if (expScaling && toAdd > 0) //i think it would add 1 shrine anyway if i do not check that its disabled here
            {
                if (TeleporterInteraction.instance.shrineBonusStacks <= 0)
                {
                    TeleporterInteraction.instance.AddShrineStack();
                    toAdd -= 1; //toAdd might now be 0, in which case we do 1 unnecessary calculation, but its not that bad
                }
                toAdd = (int)(TeleporterInteraction.instance.shrineBonusStacks * Math.Pow(2.0, toAdd)) - TeleporterInteraction.instance.shrineBonusStacks;
            }

            for (int i = 0; i < toAdd; i++)
            {
                TeleporterInteraction.instance.AddShrineStack(); //So i am not 100% sure what else happens other than shrineBonusStacks += 1, but there are hooks and such, so i used a loop here
            }
            //adding the extra Credits from the config
            controller.baseMonsterCredit += extraMonsterCredits * TeleporterInteraction.instance.shrineBonusStacks;

        }

        public void ZoneCharge(On.RoR2.HoldoutZoneController.orig_Update orig, HoldoutZoneController self)
        {
            //non-host check
            if (!self.hasAuthority)
            {
                orig(self);
                return;
            }

            //Custom charge logic only applies in the void fields, otherwise the normal tp would be affected
            if (SceneInfo.instance.sceneDef.baseSceneName == "arena")
            {
                if (self.charge <= maxCharges[currentCell]) orig(self);
                else if (self.charge < 0.98f) //if it works correctly this if branched should only be reached once per controller after which it disables itself; but it did not, hence i added the 0.99 check
                {
                    orig(self);
                    self.FullyChargeHoldoutZone();
                }
                else
                {
                    //this should be last cell; maybe not even but just to be sure
                    orig(self);
                }
            }
            else
            {
                //this should be the normal teleporter
                orig(self);
            }

        }

        private void MultiplyItemReward(On.RoR2.PickupPickerController.orig_CreatePickup_PickupIndex orig, PickupPickerController self, PickupIndex pickupIndex)
        {

            int total;
            if (useShrine) total = Math.Max((int)Math.Floor(TeleporterInteraction.instance.shrineBonusStacks * extraRewards), 1);//if you are confused what this does check the code for the enemy items (extraItems), its the same thing just better explained
            else total = Math.Max((int)Math.Floor(DifficultyCounter * extraRewards), 1);

            

            if (preventDrops)
            {
                int playerIndex = GetPlayerIndexFromInteractionObject(self.gameObject);
                AddToPlayerInventory(pickupIndex.pickupDef.itemIndex, playerIndex, total);
            }
            else
            {
                for (int i = 0; i < total; i++)
                {
                    orig(self, pickupIndex); 
                }
            }
        }

        private void ActivateCell(On.RoR2.ArenaMissionController.orig_BeginRound orig, ArenaMissionController self)
        {
            
            orig(self);
            //non-host check
            if (!self.hasAuthority)
            {
                return;
            }

            currentCell += 1; //increase counter cuse thing happened
            if (currentCell > 8) currentCell = 8; //there was a error that i think happened if the reset errored on client; added just to be sure

            //should adjust based on all the settings
            HoldoutZoneController cell = self.nullWards[self.currentRound - 1].GetComponent<HoldoutZoneController>();
            cell.baseRadius *= voidRadius;
            cell.baseChargeDuration *= chargeDurationMult;
            cell.charge = startCharges[currentCell];
        }

        private void FinishCell(On.RoR2.ArenaMissionController.orig_EndRound orig, ArenaMissionController self)
        {
            if (stageIncrease > 0)
            {
                int diffCounter;
                if (useShrine) diffCounter = TeleporterInteraction.instance.shrineBonusStacks;
                else diffCounter = DifficultyCounter;
                Run.instance.stageClearCount += (int)Math.Floor(stageIncrease * diffCounter);
            }

            orig(self);
        }

        private void MultiplyEnemyItem(On.RoR2.ArenaMissionController.orig_AddItemStack orig, ArenaMissionController self)
        {
            //inventory not being present caused errors on client which makes sense
            if (!self.inventory)
            {
                orig(self);
                return;
            }
            Inventory inv = self.inventory;

            // Track how many items are being added by checking the previous state (before adding new items)
            int[] originalItemStacks = (int[])inv.itemStacks.Clone(); // Clone the current stacks for comparison later

            //increase extraStacks by how many times the Threshold was reached; reminder that this is a int div; 0 should be disable
            int totalStacks = extraStacks;
            if (extraStacksThreshold > 0)
            {
                if (useShrine) totalStacks += TeleporterInteraction.instance.shrineBonusStacks / extraStacksThreshold;
                else totalStacks += DifficultyCounter / extraStacksThreshold;
            }

            if (allowDuplicates)
            {
                //storing to be cleared items
                int[] tempStacks = (int[])inv.itemStacks.Clone();
                inv.CleanInventory(); //temporarly clearing the inventory to allow orig to add all items

                //call orig(self) mutliple times and clear inventory after, to make the game pull from all possible items again
                for (int i = 0; i < totalStacks; i++)
                {
                    
                    orig(self);
                    //gets and stores the added item
                    ItemIndex index = self.inventory.itemAcquisitionOrder[0];
                    tempStacks[(int)index] += self.inventory.GetItemCount(index);

                    inv.CleanInventory();

                    self.nextItemStackIndex -= 1;
                }

                //adding the stored items back orderd by tier
                foreach (ItemIndex index in ItemCatalog.tier1ItemList)
                {
                    inv.GiveItem(index, tempStacks[(int) index]);
                }
                foreach (ItemIndex index in ItemCatalog.tier2ItemList)
                {
                    inv.GiveItem(index, tempStacks[(int)index]);
                }
                foreach (ItemIndex index in ItemCatalog.tier3ItemList)
                {
                    inv.GiveItem(index, tempStacks[(int)index]);
                }
            }
            else
            {
                for (int i = 0; i < totalStacks; i++)
                {
                    orig(self);
                    self.nextItemStackIndex -= 1;
                }
            }

            //nextItemStackIndex sets the rarity of the ItemStack rolled for and increase by 1 after orig
            self.nextItemStackIndex += 1;

            latestInventoryItems = new int[inv.itemStacks.Length];
            // Compare the item stacks before and after; This should work a bit more generally than just for 1 item only like the void fields, so i might do something else with it later idk
            for (int i = 0; i < inv.itemStacks.Length; i++)
            {
                if (KillMeOption == false)
                {
                    if (extraItems > 0)
                    {
                        // Calculate the shrine bonus stacking, rounded down; This will only work for max ~2 Billion items, but if you manage that ingame it probably does not matter. besides Ror2 stores the items in an intArray which will have the same problem
                        int bonus;
                        if (useShrine) bonus = (int)Math.Floor(TeleporterInteraction.instance.shrineBonusStacks * extraItems);
                        else bonus = (int)Math.Floor(DifficultyCounter * extraItems);

                        // Ensure the enmies get at least 1 item
                        bonus = Math.Max(bonus, 1);

                        // Apply the bonus to the inventory item stack
                        if (inv.itemStacks[i] > originalItemStacks[i])
                        {
                            inv.itemStacks[i] = originalItemStacks[i] + (inv.itemStacks[i] - originalItemStacks[i]) * bonus;
                        }
                    }
                }
                else
                {
                    if (useShrine) inv.itemStacks[i] += Math.Max((int)Math.Floor(TeleporterInteraction.instance.shrineBonusStacks * extraItems), 1); //should add the floor of extraItems min 1, to all items available in inventory
                    else inv.itemStacks[i] += Math.Max((int)Math.Floor(DifficultyCounter * extraItems), 1);
                }

                latestInventoryItems[i] = inv.itemStacks[i];
            }
        }

        private void MultiplyEnemyType(On.RoR2.ArenaMissionController.orig_AddMonsterType orig, ArenaMissionController self)
        {
            //non-host check
            if (!self.hasAuthority)
            {
                orig(self);
                return;
            }

            //increase MonsterTypes by how many times the Threshold was reached; reminder that this is a int div; 0 should be disable
            int total = extraMonsterTypes;//'extra' MonsterTypes is at least 1
            if (extraStacksThreshold > 0)
            {
                if (useShrine) total += TeleporterInteraction.instance.shrineBonusStacks / extraMonsterTypesThreshold;
                else total += DifficultyCounter / extraMonsterTypesThreshold;
            }

            //Extra Combat directors are needed because the fields (not sure how its on a normal stage) use 1 director per type
            CombatDirector[] directors = self.combatDirectors;

            int originalMaxIndex = directors.Length; //technically length is +1 because its 0-indexed but you know
            int directorsNeeded = (total - 1); //how many new directors are needed; 1 call is coverd by the base game so we increase for all the extra ones
            int newSize = directors.Length + directorsNeeded;

            Array.Resize(ref self.combatDirectors, newSize);

            //Add new CombatDirectors to array; not all may be used but that is then only because the potential enemy pool is empty, which is impossible to know (i think)
            for (int i = originalMaxIndex; i < newSize; i++)
            {
                self.combatDirectors[i] = NewCombatDirector(self.combatDirectors[originalMaxIndex - 1]); // Use the first director as reference
            }
            
            //repeate original AddMonsterType()
            for (int i = 0; i < total; i++)
            {
                orig(self);
            }
        }

        // I found this out based on the prefab from ArenaMissionController but i believe it should work like this (this changed a few times at this point its mainly the name of the GameObject that's left, the other stuff is just from the reference)
        public static CombatDirector NewCombatDirector(CombatDirector referenceDirector)
        {
            
            GameObject newCombatDirectorObject = new GameObject("ArenaMissionController");

            // Add the CombatDirector component to the new GameObject.
            newCombatDirectorObject.SetActive(false); //directly adding causes an error the Ror2.CombatDirector.Awake() method so i have to disably it until i added all the reference variables (some of them may be overrriden or set differently actually but i do not know what awake does exactly, so i just copied everything i could)
            CombatDirector newCombatDirector = newCombatDirectorObject.AddComponent<CombatDirector>();

            //copy from the reference
            newCombatDirector.monsterCredit = referenceDirector.monsterCredit;
            newCombatDirector.refundedMonsterCredit = referenceDirector.refundedMonsterCredit;
            newCombatDirector.expRewardCoefficient = referenceDirector.expRewardCoefficient;
            newCombatDirector.goldRewardCoefficient = referenceDirector.goldRewardCoefficient;
            newCombatDirector.minSeriesSpawnInterval = referenceDirector.minSeriesSpawnInterval;
            newCombatDirector.maxSeriesSpawnInterval = referenceDirector.maxSeriesSpawnInterval;
            newCombatDirector.minRerollSpawnInterval = referenceDirector.minRerollSpawnInterval;
            newCombatDirector.maxRerollSpawnInterval = referenceDirector.maxRerollSpawnInterval;
            newCombatDirector.moneyWaveIntervals = referenceDirector.moneyWaveIntervals;
            newCombatDirector.teamIndex = referenceDirector.teamIndex;
            newCombatDirector.creditMultiplier = referenceDirector.creditMultiplier;
            newCombatDirector.spawnDistanceMultiplier = referenceDirector.spawnDistanceMultiplier;
            newCombatDirector.maxSpawnDistance = referenceDirector.maxSpawnDistance;
            newCombatDirector.minSpawnRange = referenceDirector.minSpawnRange;
            newCombatDirector.shouldSpawnOneWave = referenceDirector.shouldSpawnOneWave;
            newCombatDirector.targetPlayers = referenceDirector.targetPlayers;
            newCombatDirector.skipSpawnIfTooCheap = referenceDirector.skipSpawnIfTooCheap;
            newCombatDirector.maxConsecutiveCheapSkips = referenceDirector.maxConsecutiveCheapSkips;
            newCombatDirector.resetMonsterCardIfFailed = referenceDirector.resetMonsterCardIfFailed;
            newCombatDirector.maximumNumberToSpawnBeforeSkipping = referenceDirector.maximumNumberToSpawnBeforeSkipping;
            newCombatDirector.eliteBias = referenceDirector.eliteBias;
            newCombatDirector.combatSquad = referenceDirector.combatSquad;
            newCombatDirector.ignoreTeamSizeLimit = referenceDirector.ignoreTeamSizeLimit;
            newCombatDirector._monsterCards = referenceDirector._monsterCards;
            newCombatDirector.fallBackToStageMonsterCards = referenceDirector.fallBackToStageMonsterCards;
            newCombatDirector.hasStartedWave = referenceDirector.hasStartedWave;
            newCombatDirector.rng = referenceDirector.rng;
            newCombatDirector.currentMonsterCard = referenceDirector.currentMonsterCard;
            newCombatDirector.currentActiveEliteTier = referenceDirector.currentActiveEliteTier;
            newCombatDirector.currentActiveEliteDef = referenceDirector.currentActiveEliteDef;
            newCombatDirector.currentMonsterCardCost = referenceDirector.currentMonsterCardCost;
            newCombatDirector.monsterCardsSelection = referenceDirector.monsterCardsSelection;
            newCombatDirector.consecutiveCheapSkips = referenceDirector.consecutiveCheapSkips;
            newCombatDirector.playerRetargetTimer = referenceDirector.playerRetargetTimer;
            newCombatDirector.spawnCountInCurrentWave = referenceDirector.spawnCountInCurrentWave;
            newCombatDirector.moneyWaves = referenceDirector.moneyWaves;
            newCombatDirector.isHalcyonShrineSpawn = referenceDirector.isHalcyonShrineSpawn;
            newCombatDirector.shrineHalcyoniteDifficultyLevel = referenceDirector.shrineHalcyoniteDifficultyLevel;
            newCombatDirector.enabled = referenceDirector.enabled;
            newCombatDirector.useGUILayout = referenceDirector.useGUILayout;
            newCombatDirector.monsterSpawnTimer = referenceDirector.monsterSpawnTimer;
            newCombatDirector.lastAttemptedMonsterCard = referenceDirector.lastAttemptedMonsterCard;
            newCombatDirector.totalCreditsSpent = referenceDirector.totalCreditsSpent;
            newCombatDirector.onSpawnedServer = referenceDirector.onSpawnedServer;
            newCombatDirector.spawnEffectPrefab = referenceDirector.spawnEffectPrefab;
            newCombatDirector.currentSpawnTarget = referenceDirector.currentSpawnTarget;

            newCombatDirectorObject.SetActive(true);//enable the object and cause Awake() to run only now
            return newCombatDirector;
        }

        public void RemoveMatchingMonsterCards(ArenaMissionController controller)
        {
            List<int> toBeRemovedIndices = new List<int>();
            String[] BlacklistUsables = { }; //empty array as default so that even if an error occurs it will just act as if there is no blacklist
            
            //if the monsterBlackList just skip
            if (monsterBlacklist.Equals("")) return;
            try
            {
                BlacklistUsables = monsterBlacklist.Split(',');
            }
            catch (Exception ex)
            {
                Log.Error($"Unable to parse Monster Blacklist: {ex.Message}");
            }

            //check because this method caused errors on client in multiplayer sessions
            if (controller.availableMonsterCards == null) return;

            for (int i = 0; i < controller.availableMonsterCards.Count; i++)
            {
                var choiceInfo = controller.availableMonsterCards.GetChoice(i);
                var directorCard = choiceInfo.value;

                //Blacklist check if the current MonsterCard name is found to contain a BlackListedterm
                bool isBlacklisted = false;
                foreach (string blacklistItem in BlacklistUsables)
                {
                    //skip for potential faulty entries
                    if (blacklistItem.Equals("")) continue;

                    if (directorCard.spawnCard.name.Contains(blacklistItem))
                    {
                        isBlacklisted = true;
                        break;
                    }
                }

                if (isBlacklisted)
                {
                    Log.Info($"Removed Monster {directorCard.spawnCard.name} due to Blacklist");
                    toBeRemovedIndices.Add(i);
                }
            }

            // Iterating in reverse order because removing an option also shifts everything above down by 1 (which should have been predictable and i still missed it)
            foreach (int index in toBeRemovedIndices.OrderByDescending(i => i))
            {
                controller.availableMonsterCards.RemoveChoice(index);
            }
        }

        private void CheckNullPortal(On.RoR2.BazaarController.orig_OnStartServer orig, BazaarController self)
        {
            //non-host check
            if (!Run.instance)
            {
                orig(self);
                return;
            }

            orig(self);
            self.StartCoroutine(CheckNullDelay());

        }

        private IEnumerator CheckNullDelay()
        {
            yield return new WaitForSeconds(1f);

            // check all gamebojects because this way was easiest, probably improvable
            GameObject[] obj = FindObjectsOfType<GameObject>();
            bool found = false;

            foreach (GameObject portal in obj)
            {
                if (portal.name.Contains("PortalArena")) // Check if this is a Null portal; no idea why its called PortalArena
                {
                    found = true;

                    break;
                }
            }

            if (!found)
            {
                //I copied Vector/Quaternion directly by getting them from the vanilla spawn, so it should be the same
                GameObject portal = Instantiate(raidPortalPrefab, new Vector3(281.10f, -446.82f, -126.10f), new Quaternion(0.00000f, -0.73274f, 0.00000f, 0.68051f));
                SyncObject(portal);
            }
        }

        private int GetPlayerIndexFromInteractionObject(GameObject Object)
        {
            //alot of checks to find the Index of the player from their interaction with the passed object
            CharacterBody body = null;
            PurchaseInteraction interaction = Object.GetComponent<PurchaseInteraction>();
            ScrapperController scrapper = Object.GetComponent<ScrapperController>();

            if (interaction != null || scrapper)
            {
                Interactor interactor;

                if (interaction) interactor = interaction.lastActivator;
                else interactor = scrapper.interactor;

                if (interactor != null)
                {
                    CharacterMaster master = interactor.GetComponent<CharacterMaster>();
                    if (master != null) body = master.playerCharacterMasterController.body;
                    else
                    {
                        //the Warning below was removed due to this situation happening alot; the null check is still there even if uneccessary just for the Log
                        //Log.Warning($"Could not find CharacterMaster for interactor: {interactor.name}");

                        CharacterBody controller = interactor.GetComponent<CharacterBody>();
                        if (controller != null) body = controller;
                        else Log.Warning($"Could not find CharacterBody for: {interactor.name}");
                    }
                }//I should not have named them interactor and interaction, the names are too similar
                else Log.Warning($"Could not find interactor for interaction: {interaction.name}");
            } 
            else
            {
                //is probably OptionPickup (void potential)
                //Log.Warning($"Could not find interaction for: {Object.name}");

                NetworkUIPromptController uiController = Object.GetComponent<NetworkUIPromptController>();
                if (uiController != null)
                {
                    CharacterMaster master = uiController.currentParticipantMaster;
                    if (master != null) body = master.playerCharacterMasterController.body;
                    else Log.Warning($"Could not find CharacterMaster for: {uiController.name} (probably not OptionPickup?: {Object.name})");
                }
            }

            if (body != null)
            {
                //find index of controller
                PlayerCharacterMasterController controller = body.master.playerCharacterMasterController;
                for (int i = 0; i < PlayerCharacterMasterController.instances.Count; i++)
                {
                    if (PlayerCharacterMasterController.instances[i] == controller)
                    {
                        return i;
                    }
                }
                Log.Warning($"Could not find {controller} in PlayerCharacterMasterController.instances (body: {body}, master: {body.master})");
            }
            return -1;
        }

        [Server]
        private void AddToPlayerInventory(ItemIndex item, int target = -1, int total = 1)
        {
            if (target == -1)
            {
                if (currentPlayer >= NetworkUser.readOnlyInstancesList.Count)
                {
                    currentPlayer = 0;
                }
                target = currentPlayer;
            }

            if (target >= NetworkUser.readOnlyInstancesList.Count)
            {
                Log.Error($"tried to target invalid position in Connected Users Pos: {target} with CollectionSize: {NetworkUser.readOnlyInstancesList.Count}");
                return;
            }

            NetworkUser user = NetworkUser.readOnlyInstancesList[target];
            CharacterMaster master = user.master;
            An_Network networkItem = new(item); //this is some network vodoo in my opinion

            if (master == null)
            {
                Log.Error($"Could not find Master for '{user}' default sending {item} to host");

                NetworkServer.SendToClient(0, networkId, networkItem);
                return;
            }

            master.inventory.GiveItem(item, total);

            NetworkServer.SendToClient(target, networkId, networkItem);
            currentPlayer++;
        }

        private void TryRegisterNetwork(NetworkUser networkUser)
        {
            var client = NetworkManager.singleton?.client;

            if (client == null || client.handlers == null ||client.handlers.ContainsKey(networkId))
            {
                Log.Warning($"could not register NetworkId {networkId}, because either client is null ({client == null}) or it is already registerd");
                return;
            }

            client.RegisterHandler(networkId, RecieveItem);
            Log.Info($"Registerd NetworkId {networkId}");
        }

        //this should happen when a item is send over the network via my mod which is then queued as a pickupnotification for the reciever
        private static void RecieveItem(NetworkMessage networkMessage)
        {
            var item = networkMessage.ReadMessage<An_Network>().Item;
            var localPlayer = PlayerCharacterMasterController.instances.FirstOrDefault(x => x.networkUser.isLocalPlayer);

            if (localPlayer == null)
            {
                return;
            }
            
            CharacterMasterNotificationQueue.PushItemNotification(localPlayer.master, item);
        }

        [Server]
        private void SyncObject(GameObject obj)
        {
            NetworkServer.Spawn(obj); //this should sync the object to all
        }

        /*private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F2))
            {
                // Get the player body to use a position:
                var player = PlayerCharacterMasterController.instances[0];
                for (int i = 10; i <= 20; i++) player.master.inventory.GiveItem((ItemIndex) i, 5);
                var transform = player.master.GetBodyObject().transform;
                //i do not want to die while testing
                player.master.godMode = true;

                ForceSpawnPortal(transform.position);
            }
        }

        private void ForceSpawnPortal(Vector3 position)
        {
            // Instantiate the portal prefab at the specified position
            GameObject portal = Instantiate(raidPortalPrefab, position + new Vector3(5, 0, 0), Quaternion.identity);
            GameObject portal2 = Instantiate(shopPortalPrefab, position + new Vector3(-5, 0, 0), Quaternion.identity);
        }*/
    }
}
