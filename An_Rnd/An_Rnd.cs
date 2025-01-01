using BepInEx;
using R2API;
using RoR2;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AddressableAssets;
using BepInEx.Configuration;
using System.Reflection;
using System.Collections.Generic;
using ProperSave.Data;
using System.Linq;
using UnityEngine.Networking;

namespace An_Rnd
{
    //RoR2Api Dependecys
    [BepInDependency(ItemAPI.PluginGUID)]

    [BepInDependency(LanguageAPI.PluginGUID)]

    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]

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
        public static int extraMonsterCredits = 0;
        //How many extra items are given to the enemies per active mountain shrine (Rounded down based on the number normally given)
        public static float extraItems = 0f;
        //How many items are spawned after picking per active mountain shrine (Rounded down, because i can't spawn fractions of items)
        public static float extraRewards = 0f;
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
        public static int[] latestInventoryItems = new int[0]; //if i do not set a default, this causes problem with ProperSave
        //Will remove all pre-game(logbook, select, etc.) Hightlights automatically if true
        public static bool noHightlights = false;
        //Option for Bleed-Stacking
        public static bool enableBleed = false;
        //Option for Crit-Stacking
        public static bool enableCrit = false;
        //Option if second crit should multiply the crit damage by 2 (base *4) instead of just base damage *3;will effect third crits, etc. the same way
        public static bool critMults = false;
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
        //only used if useShrine is false; substitutes/is substituted by mountain shrines
        public static int DifficultyCounter = 0;
        //should be what exactly what the name says. Check method 'RemoveMatchingMonsterCards' for specific use
        public static String monsterBlacklist = "";

        // The Awake() method is run at the very start when the game is initialized.
        public void Awake()
        {
            // Init our logging class so that we can properly log for debugging
            Log.Init(Logger);
            InitPortalPrefab();
            //I think there is like softDependcy or stuff with BepInEx... I did it in a worse way but it works so thats fine
            TryInitRiskOfOptions();
            TryInitProperSave();

            On.RoR2.BazaarController.OnStartServer += CheckNullPortal;
            On.RoR2.PickupPickerController.CreatePickup_PickupIndex += MultiplyItemReward;
            On.RoR2.ArenaMissionController.AddItemStack += MultiplyEnemyItem;
            On.RoR2.ArenaMissionController.AddMonsterType += MultiplyEnemyType;
            On.RoR2.ArenaMissionController.BeginRound += ActivateCell;
            On.RoR2.HoldoutZoneController.Update += ZoneCharge;
            On.RoR2.HealthComponent.TakeDamage += ExtraCrit;
            On.RoR2.DotController.AddDot += ExtraBleed;
            On.RoR2.Stage.Start += CheckTeleporterInstance;
            On.RoR2.Run.Start += ResetRunVars;
            On.RoR2.UserProfile.HasViewedViewable += Viewed;
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

        private void InitPortalPrefab()
        {
            shopPortalPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/PortalShop/PortalShop.prefab").WaitForCompletion();
            raidPortalPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/PortalArena/PortalArena.prefab").WaitForCompletion();
            //Change flags, such that Null Portal actually connects to the void fields.
            raidPortalPrefab.GetComponent<SceneExitController>().useRunNextStageScene = false;
            teleporterPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Teleporters/Teleporter1.prefab").WaitForCompletion();
        }

        private void TryInitProperSave()
        {
            try
            {
                // Get the ProperSave SaveFile type dynamically
                Type properSaveType = Type.GetType("ProperSave.SaveFile, ProperSave");
                if (properSaveType == null)
                {
                    Log.Warning("ProperSave is not available.");
                    return;
                }

                Type loadingType = Type.GetType("ProperSave.Loading, ProperSave");

                // Hook into OnGatherSaveData event dynamically
                EventInfo onGatherSaveDataEvent = properSaveType.GetEvent("OnGatherSaveData");
                if (onGatherSaveDataEvent != null)
                {
                    Action<object> gatherSaveDataHandler = (sender) =>
                    {
                        try
                        {
                            Log.Info("ProperSave is gathering save data.");

                            // Create a combined array. ProperSave readme said to best save as one
                            if (sender is IDictionary<string, object> saveDataDictionary)
                            {
                                // Create a combined array for saving data
                                object[] saveData = new object[]
                                {
                                    latestInventoryItems,
                                    arenaCount
                                };

                                // Add the array to the save data dictionary under the PluginGUID key
                                saveDataDictionary["Cyan"] = saveData;

                                Log.Info("Save data successfully added to ProperSave.");
                            }
                            else
                            {
                                Log.Error("Could not save data because sender is not expected type");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Error while trying to save data with ProperSave: {ex.Message}\\n{ex.StackTrace}");
                        }
                    };

                    Delegate gatherSaveDataDelegate = Delegate.CreateDelegate(onGatherSaveDataEvent.EventHandlerType, gatherSaveDataHandler.Target, gatherSaveDataHandler.Method);
                    onGatherSaveDataEvent.AddEventHandler(null, gatherSaveDataDelegate);
                }
                else
                {
                    Log.Error("Failed to hook into ProperSave.OnGatherSaveData.");
                }

                // Hook into Loading.OnLoadingStarted event dynamically
                EventInfo OnLoadingStartedEvent = loadingType.GetEvent("OnLoadingStarted");
                if (OnLoadingStartedEvent != null)
                {
                    Action<object> loadingStartedHandler = (saveFile) =>
                    {
                        //I had i weird error with the saveFile so i am adding a bunch of checks which will probably stay
                        if (saveFile == null)
                        {
                            Log.Error("saveFile is null.");
                            return;
                        }

                        try
                        {

                            var saveDataProperty = saveFile.GetType().GetProperty("ModdedData");
                            if (saveDataProperty == null)
                            {
                                Log.Error("ModdedData property not found on saveFile.");
                                return;
                            }
                            var moddedDataDictionary = saveDataProperty.GetValue(saveFile) as IDictionary<string, ModdedData>;
                            if (moddedDataDictionary == null)
                            {
                                Log.Error("ModdedData dictionary is null.");
                                return;
                            }

                            // Retrieve saved data
                            if (moddedDataDictionary.TryGetValue("Cyan", out ModdedData savedData))
                            {
                                if (savedData.Value is object[] loadedData && loadedData.Length == 2)
                                {
                                    //So apperently ProperSaves loads saved int[] as List<object> (which i kinda had to figure out via Consol Logs, would have probably been more obvious were I to use a more normal method of using ProperSave) so here is a conversion, that i defintily came up with my self and did not ask chatGpt for
                                    if (loadedData[0] is List<object> objList)
                                    {
                                        try
                                        {
                                            // Converts List<object> to int[]
                                            latestInventoryItems = objList.Select(e => Convert.ToInt32(e)).ToArray();
                                        }
                                        catch (Exception ex)
                                        {
                                            Log.Error($"Failed to convert List<object> to int[]. Exception: {ex.Message}");
                                            foreach (var item in objList)
                                            {
                                                Log.Error($"    Item: {item}, Type: {item?.GetType()}");
                                            }
                                        }
                                    }
                                    else
                                    {
                                        Log.Error($"Could not load latestInventoryItems, because it was not saved as List<object>. {loadedData[0].GetType()} instead");
                                    }

                                    arenaCount = Convert.ToInt32(loadedData[1]);
                                    wasLoaded = true; //this is so that the loaded variables are not reset for 'a new run'

                                    Log.Info($"Loaded latestInventoryItems and arenaCount({arenaCount}) with ProperSave.");
                                }
                                else
                                {
                                    Log.Error("Saved data format is invalid or unexpected.");
                                }
                            }
                            else
                            {
                                Log.Warning($"No save data found for Cyan.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Error while loading save data: {ex.Message}\\n{ex.StackTrace}");
                        }
                    };

                    Delegate loadingStartedDelegate = Delegate.CreateDelegate(OnLoadingStartedEvent.EventHandlerType, loadingStartedHandler.Target, loadingStartedHandler.Method);
                    OnLoadingStartedEvent.AddEventHandler(null, loadingStartedDelegate);
                }
                else
                {
                    Log.Error("Failed to hook into ProperSave.Loading.OnLoadingEnded.");
                }

                Log.Info("ProperSave integration successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"An error occurred while initializing ProperSave: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void TryInitRiskOfOptions()
        {
            var configEntries = CreateLoadConfig();

            try
            {
                // Check if RiskOfOptions is available
                Type modSettingsManagerType = Type.GetType("RiskOfOptions.ModSettingsManager, RiskOfOptions");
                if (modSettingsManagerType != null)
                {
                    // Dynamically create configurations and options
                    foreach (var (config, varType, _, min, max) in configEntries)
                    {
                        object option = CreateOption(config, varType, min, max); //vodoo method to create the option like RiskOfOptions expects it, with it's typing; i say vodoo because i do not feel very confidend in this and got a bit of a short 1hour lesson from ChatGpt that did not help me at all
                        if (option != null)
                        {
                            MethodInfo addOptionMethod = modSettingsManagerType.GetMethod(
                                "AddOption",
                                BindingFlags.Public | BindingFlags.Static,
                                null,
                                new[] { option.GetType().BaseType },
                                null
                            );
                            addOptionMethod?.Invoke(null, new[] { option }); //Invokes as in this calls the method of RiskOfOptions that then lists my option and stuff
                        }
                    }

                    Log.Info("RiskOfOptions integration successful.");
                }
                else
                {
                    Log.Warning("RiskOfOptions is not available. Falling back to default behavior.");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to integrate RiskOfOptions: {ex.Message}");
            }

            // Hook setting changes dynamically
            foreach (var (config, StaticType, updateStaticVar, _, _) in configEntries)
            {
                // Cast to the specific type of ConfigEntry<T> dynamically
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
                    Config.Bind("General", "Bleed Stacking", false, "If enabled, anytime a bleed effect is applied and the chance was over 100% there may be a second bleed stack with the remainder, which is added at the same time\nso with 600% crit chance you get 6 bleed stacks per hit"),
                    typeof(bool),
                    new Action<object>(value => enableCrit = (bool)value),
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
                    Config.Bind("Void Fields", "Use Shrines", false, "If enabled, will spawn a unusable teleporter in the void fields to activate mountainShrines instead of just an internal counter\nThis Option will probably do nothing if you do not have other mods that interact with mountain shrines, i reccomend looking for one that makes them persist over the whole run after activiating"),
                    typeof(bool),
                    new Action<object>(value => useShrine = (bool)value),
                    null,
                    null
                ),
                (
                    Config.Bind("Void Fields", "Number of Shrines", 5, "Number of shrines activated per additional void field entry."),
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
                    Config.Bind("Void Fields", "exponential Scaling", false, "If enabled, will use the above Number of Shrines to *2 instead of just adding\nfor example 4 for shrines would add 1 if 0 are active and then do *2,*2,*2 for a total of 8"),
                    typeof(bool),
                    new Action<object>(value => expScaling = (bool)value),
                    null,
                    null
                ),
                (
                    Config.Bind("Void Fields", "Enemy ItemStacks", 1, "Sets the number of itemStacks the void fields enemies can obtain.\n1 per activation is vanilla, but with this you can get for example goat's hoof and crit classes at the same time\ndisabled if Kill Me is checked"),
                    typeof(int),
                    new Action<object>(value => extraStacks = (int)value),
                    1,
                    1000
                ),
                (
                    Config.Bind("Void Fields", "Enemy Extra ItemStacks Threshold", 0, "Number of mountain shrines required to increase 'Enemy Extra ItemStacks' by 1.\n0 for disabled"),
                    typeof(int),
                    new Action<object>(value => extraStacksThreshold = (int)value),
                    0,
                    10000
                ),
                (
                    Config.Bind("Void Fields", "Monsters", 1, "Sets the number of Monsters the void fields add at once.\nReminder that this will be capped by your enemies as in the further you are in stage/lvl you may get harder enemies which you will never get even if you try 100 times (=this set to 100) stage 1\nStill causes an error but seems to work anyway, error is logged, if you are curious, but feel safe to use this for now"),
                    typeof(int),
                    new Action<object>(value => extraMonsterTypes = (int)value),
                    1,
                    1000
                ),
                (
                    Config.Bind("Void Fields", "Monsters Threshold", 0, "Number of mountain shrines required to increase 'Monsters' by 1.\n0 for disabled"),
                    typeof(int),
                    new Action<object>(value => extraMonsterTypesThreshold = (int)value),
                    0,
                    10000
                ),
                (
                    Config.Bind("Void Fields", "Monster Blacklist", "NoMonsterPlease", "Any String written here in the form of '[Name],[Name],...' Will be matched to the potential enemy pool and removed if a match is found\nExample, RoR2 has the Spawn Card 'cscLesserWisp' so having this set to 'cscLesserWisp' will remove only the Wisp from the potential Enemies. Setting it to 'cscLesserWisp,cscGreaterWisp' will remove both lesser and greater Wisp, wereas 'Wisp' will remove any that have the name Wisp in them which might remove other modded entries like Ancient Wisp\nAt this point you just have to know or guess the names of the SpawnCards\nCurrently leaving this empty causes every possible monster to be removed"),
                    typeof(String),
                    new Action<object>(value => monsterBlacklist = (String)value),
                    null,
                    null
                ),
                (
                    Config.Bind("Void Fields", "Extra Credits", 0, "How many extra credits are given to the void fields per active mountain shrine\n0 for disabled[i mean x+0=x...]"),
                    typeof(int),
                    new Action<object>(value => extraMonsterCredits = (int)value),
                    0,
                    10000
                ),
                (
                    Config.Bind("Void Fields", "Enemy Extra Items", 1f, "Multiplier for void field enemy items per active shrine.\n0 for disable"),
                    typeof(float),
                    new Action<object>(value => extraItems = (float)value),
                    0f,
                    10000f
                ),
                (
                    Config.Bind("Void Fields", "Reward Item Multiplier per Shrine", 1f, "Multiplier for void field rewards per active shrine.\n0 for disable"),
                    typeof(float),
                    new Action<object>(value => extraRewards = (float)value),
                    0f,
                    10000f
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
                ( //i wanted to add these via a loop but that caused some wierd error with RiskOfOptions... [I replaced this with an array at a later point, because of such things it seemed more fitting]
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

        private object CreateOption(ConfigEntryBase config, Type varType, object min, object max)
        {
            try
            {
                //make sure the config that is beeing used here is used with the correct typing. This is what prepares the options for RiskOfOptions, but its treated as if it may not even exist so that i can compile without, which is why it may look a bit wired
                if (varType == typeof(int))
                {
                    Type baseOptionType = Type.GetType("RiskOfOptions.Options.IntSliderOption, RiskOfOptions");
                    Type configType = Type.GetType("RiskOfOptions.OptionConfigs.IntSliderConfig, RiskOfOptions");
                    object configInstance = Activator.CreateInstance(configType);
                    configType.GetField("min")?.SetValue(configInstance, (int)min);
                    configType.GetField("max")?.SetValue(configInstance, (int)max);
                    Log.Info($"Added RiskOfOption '{config.Definition.Key}' as IntSlider");
                    return Activator.CreateInstance(baseOptionType, config, configInstance);
                }
                else if (varType == typeof(float))
                {
                    Type baseOptionType = Type.GetType("RiskOfOptions.Options.FloatFieldOption, RiskOfOptions");
                    Type configType = Type.GetType("RiskOfOptions.OptionConfigs.FloatFieldConfig, RiskOfOptions");
                    object configInstance = Activator.CreateInstance(configType);
                    // Use GetProperty to modify Min and Max properties; apparently FloatField uses Properties and not just public fields like intSlider, idk why
                    var minProperty = configType.GetProperty("Min");
                    var maxProperty = configType.GetProperty("Max");
                    if (minProperty != null && minProperty.CanWrite)
                    {
                        minProperty.SetValue(configInstance, min);
                    }
                    else
                    {
                        Log.Warning($"Unable to set Min property for {config.Definition.Key}");
                    }

                    if (maxProperty != null && maxProperty.CanWrite)
                    {
                        maxProperty.SetValue(configInstance, max);
                    }
                    else
                    {
                        Log.Warning($"Unable to set Max property for {config.Definition.Key}");
                    }

                    Log.Info($"Added RiskOfOption '{config.Definition.Key}' as FloatField");
                    return Activator.CreateInstance(baseOptionType, config, configInstance);
                }
                else if (varType == typeof(bool))
                {
                    Type baseOptionType = Type.GetType("RiskOfOptions.Options.CheckBoxOption, RiskOfOptions");
                    Log.Info($"Added RiskOfOption '{config.Definition.Key}' as CheckBox");
                    return Activator.CreateInstance(baseOptionType, config);
                }
                else if (varType == typeof(String))
                {
                    Type baseOptionType = Type.GetType("RiskOfOptions.Options.StringInputFieldOption, RiskOfOptions");

                    Log.Info($"Added RiskOfOption '{config.Definition.Key}' as StringInputField");
                    return Activator.CreateInstance(baseOptionType, config);
                }
                else
                {
                    Log.Error($"Failed to create option for {config.Definition.Key} because type was {varType}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to create option for {config.Definition.Key}: {ex.Message}");
                return null;
            }
        }

        //below this comment are hooks and stuff, above should only be awake/Init type stuff. (very good explain: me)
        public void ZoneCharge(On.RoR2.HoldoutZoneController.orig_Update orig, HoldoutZoneController self)
        {
            //non-host check
            if (!self)
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

        private bool Viewed(On.RoR2.UserProfile.orig_HasViewedViewable orig, UserProfile self, string viewableName)
        {
            if (noHightlights) return true;
            return orig(self, viewableName);
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
            //there is 'self.sceneDef.baseSceneName' but its seems to not be an instance for some reason, so i found this: 'SceneInfo.instance.sceneDef.baseSceneName'
            if (SceneInfo.instance.sceneDef.baseSceneName == "arena")
            {
                if (skipVanilla && arenaCount < 0) arenaCount = 0;
                arenaCount += 1; //counter how often we entered the void fields
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
                    // AddItemsFrom is a overloaded method, wich needs a filter to accept int[] as input; but we just want everything
                    Func<ItemIndex, bool> includeAllFilter = _ => true;
                    controller.inventory.AddItemsFrom(latestInventoryItems, includeAllFilter);
                }

                if (useShrine)
                {
                    //the 'arena' also known as the void fields, does not have a teleporter, but i want to activate mountain shrines anyway
                    GameObject portal = Instantiate(teleporterPrefab, new Vector3(0, -1000, 0), Quaternion.identity); // I hope -1000 is away from everything/unreachable
                    //btw i do not sync portal to client, which i had to do for the null portal, but its supposed to be inaccesible anyway, so that should be fine

                    int total = numShrines;
                    if (expScaling && total > 0) //i think it would add 1 shrine anyway if i do not check that its disabled here
                    {
                        if (TeleporterInteraction.instance.shrineBonusStacks == 0)
                        {
                            TeleporterInteraction.instance.AddShrineStack();
                            total -= 1;
                        }
                        total = (int) (TeleporterInteraction.instance.shrineBonusStacks * Math.Pow(2.0, (double)total));
                    }

                    for (int i = 0; i < arenaCount * total; i++)
                    {
                        TeleporterInteraction.instance.AddShrineStack(); //So i am not 100% sure what else happens other than shrineBonusStacks += 1, but there are hooks and such, so i used a loop here
                    }
                    //adding the extra Credits from the config
                    controller.baseMonsterCredit += extraMonsterCredits * TeleporterInteraction.instance.shrineBonusStacks;
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

        //just to note for future reference, using this caused some weird error. Maybe something else was going on at the time, but for now ill say it does not work
        private IEnumerator VoidTele()
        {
            //i want to avoid the teleportor showing up on the objectives list, and i am unsure when and were this happens. could search for a hook, could try this instead
            yield return new WaitForSeconds(0.1f);

            GameObject portal = Instantiate(teleporterPrefab, new Vector3(0, -1000, 0), Quaternion.identity); // I hope -1000 is away from everything/unreachable
            for (int i = 0; i < arenaCount * numShrines; i++)
            {
                TeleporterInteraction.instance.AddShrineStack();
            }
        }

        private void MultiplyItemReward(On.RoR2.PickupPickerController.orig_CreatePickup_PickupIndex orig, PickupPickerController self, PickupIndex pickupIndex)
        {
            //non-host check
            if (!TeleporterInteraction.instance)
            {
                orig(self, pickupIndex);
                return;
            }

            int total;
            if (useShrine) total = Math.Max((int)Math.Floor(TeleporterInteraction.instance.shrineBonusStacks * extraRewards), 1);//if you are confused what this does check the code for the enemy items (extraItems), its the same thing just better explained
            else total = Math.Max((int)Math.Floor(DifficultyCounter * extraRewards), 1);

            for (int i = 0; i < total; i++)
            {
                orig(self, pickupIndex);
            }
        }

        private IEnumerator ChunkRewards(On.RoR2.PickupPickerController.orig_CreatePickup_PickupIndex orig, PickupPickerController self, PickupIndex pickupIndex)
        {
            int totalItems;
            if (useShrine) totalItems = Math.Max((int)Math.Floor(TeleporterInteraction.instance.shrineBonusStacks * extraRewards), 1);//if you are confused what this does check the code for the enemy items (extraItems), its the same thing just better explained
            else totalItems = Math.Max((int)Math.Floor(DifficultyCounter * extraRewards), 1);
            //if mountain shrines are 0, it should still give 1 item (first run)
            for (int i = 0; i < totalItems; i++)
            {
                orig(self, pickupIndex);

                // Wait for 1 second after each chunk
                if (i > 0 && i % chunkSize == 0) yield return new WaitForSeconds(1f);
            }
        }

        private void ActivateCell(On.RoR2.ArenaMissionController.orig_BeginRound orig, ArenaMissionController self)
        {
            orig(self);
            currentCell += 1; //increase counter cuse thing happened
            if (currentCell > 8) currentCell = 8; //there was a error that i think happened if the reset errored on client; added just to be sure
            //non-host check
            if (!self.nullWards[self.currentRound - 1])
            {
                return;
            }
            //should adjust based on all the settings
            HoldoutZoneController cell = self.nullWards[self.currentRound - 1].GetComponent<HoldoutZoneController>();
            cell.baseRadius *= voidRadius;
            cell.baseChargeDuration *= chargeDurationMult;
            cell.charge = startCharges[currentCell];
        }

        private void MultiplyEnemyItem(On.RoR2.ArenaMissionController.orig_AddItemStack orig, ArenaMissionController self)
        {
            //non-host check
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

            // Call the original method to add the items
            for (int i = 0; i < totalStacks; i++) //extraStacks can be min set to 1. Extra callings of orig, rolls for a new itemStacks and adds it to the ItemPool
            {
                orig(self);
                self.nextItemStackIndex -= 1;
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
            if (!TeleporterInteraction.instance)
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
