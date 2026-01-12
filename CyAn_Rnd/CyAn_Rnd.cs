using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using ProperSave;
using RiskOfOptions;
using RoR2;
using RoR2BepInExPack.GameAssetPaths;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using static RoR2.PickupPickerController;

namespace CyAn_Rnd
{
    //RoR2API stuff
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.rune580.riskofoptions", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.KingEnderBrine.ProperSave", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.bepis.r2api.language")]
    [BepInDependency("com.bepis.r2api.content_management")]

    public class CyAn_Rnd : BaseUnityPlugin
    {
        public const string PluginGUID = "Cyan.Rnd";
        public const string PluginAuthor = "Cy/an";
        public const string PluginName = "Cy/an Rnd";
        public const string PluginVersion = "1.2.3";

        //shopPortal is not neccessary anymore but ill leave it to reuse use when testing
        public static GameObject shopPortalPrefab;
        public static GameObject raidPortalPrefab;
        public static GameObject teleporterPrefab;

        //Will remove all pre-game(logbook, select, etc.) Hightlights automatically if true
        public static bool noHightlights = false;
        //Option for Bleed-Stacking
        public static bool enableBleed = false;
        //Option for Crit-Stacking
        public static bool enableCrit = false;
        //Option if second crit should multiply the crit damage by 2 (base *4) instead of just base damage *3;will effect third crits, etc. the same way
        public static bool critMults = false;
        //Option to add items to inventory directly
        public static bool preventDrops = false;
        //Option to specifically deal with lunar items when 'preventDrops' is enabled
        public static bool preventLunar = false;
        //Need a way to keep track if properSave was used. (relevant in 'ResetRunVars')
        public static bool wasLoaded = false;
        //current PlayerCounter for item distribution
        public static int currentPlayer = 0;
        //Will update on Stage start to contain all Printers, Scrappers, Cauldrons and the portal. Used to determin which gameobject spawned a given droplet
        public static List<GameObject> purchaseInteractables = []; //do not ask me why the component thingy, which the list is named after, is named PurchaseInteraction if its used for things that cost something and things without (actually i might be stupid, i think it counts if you use money OR an item; Scrapper needed to be handled seperatly anyway)
        //remind me to use PurchaseInteraction.Awake
        public static short networkId = 4379;
        
        private static readonly CyAn_Arena CyAn_Arena =  new();

        //Artifact stuff
        private readonly List<CyAn_RndArtifactBase> artifacts = new();

        // The Awake() method is run at the very start when the game is initialized.
        public void Awake()
        {
            // Init our logging class so that we can properly log for debugging
            Log.Init(Logger);
            //loads(creates) config file and tries to load anything marked with SoftDependency
            ConfigandTrySoft();

            InitPrefabs();
            CyAn_Arena.RegisterArenaHooks();

            On.RoR2.Stage.Start += CheckTeleporterInstance;
            On.RoR2.GenericPickupController.CreatePickup += AddItemDirectly;
            On.RoR2.PickupDropletController.CreatePickupDroplet_CreatePickupInfo_Vector3_Vector3 += AddDropletDirectly;
            On.RoR2.HealthComponent.TakeDamage += ExtraCrit;
            On.RoR2.DotController.AddDot_GameObject_float_DotIndex_float_Nullable1_Nullable1_Nullable1 += ExtraBleed;
            On.RoR2.Run.Start += ResetRunVars;
            On.RoR2.UserProfile.HasViewedViewable += Viewed;
            On.RoR2.PurchaseInteraction.OnInteractionBegin += Purchase;
            NetworkUser.onPostNetworkUserStart += TryRegisterNetwork;
            On.RoR2.CharacterMasterNotificationQueue.PushItemNotification_CharacterMaster_ItemIndex += OrderNotificationOverwrite;
            On.RoR2.CharacterMasterNotificationQueue.PushEquipmentNotification += OrderNotificationOverwrite2;
            On.RoR2.PickupDropletController.CreatePickup += OrderDropletOverwrite;

            // Register Artifact of Order
            AddArtifact(new ArtifactOfOrder());
            var harmony = new Harmony(PluginGUID);
            harmony.PatchAll();
        }

        private void AddArtifact(CyAn_RndArtifactBase artifact)
        {
            artifact.Init(Config);
            artifacts.Add(artifact);
        }

        private void InitPrefabs()
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
                    CyAn_Arena.latestInventoryItems,
                    CyAn_Arena.arenaCount,
                    currentPlayer,
                    ArtifactOfOrder.tierToItemMap.Select(kvp => new KeyValuePair<int, int>((int)kvp.Key, (int)kvp.Value)).ToList(),
                    (int)ArtifactOfOrder.allowedEquipment
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
                if (savedData.Value is object[] loadedData && loadedData.Length == 5)
                {
                    //So apparently ProperSaves loads saved int[] as object (which i kinda had to figure out via Consol Logs, because i am stupid) so here is a conversion, that i defintily came up with my self and did not ask chatGpt for
                    try
                    {
                        // Casts loadedData[0](object) to List<object> and converts this to int[](by casting it to List<int> and then converting to array._.) because of course i can't just cast to (int[])
                        CyAn_Arena.latestInventoryItems = ((List<object>)loadedData[0]).Cast<int>().ToArray();
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Failed to Load save Data because: \"Failure to convert object to int[]. Exception: {ex.Message}\"");
                    }

                    CyAn_Arena.arenaCount = Convert.ToInt32(loadedData[1]);
                    currentPlayer = Convert.ToInt32(loadedData[2]);
                    wasLoaded = true; //this is so that the loaded variables are not reset for 'a new run'

                    try
                    {
                        List<object> rawList = (List<object>)loadedData[3];

                        ArtifactOfOrder.tierToItemMap = rawList.Select(obj =>
                            {
                                var dict = (Dictionary<string, object>)obj;
                                int key = Convert.ToInt32(dict["Key"]);
                                int value = Convert.ToInt32(dict["Value"]);
                                return new KeyValuePair<ItemTier, ItemIndex>((ItemTier)key, (ItemIndex)value);
                            }).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Failed to Load save Data because: \"Failure to convert object to Dictionary<ItemTier, ItemIndex>. Exception: {ex.Message}\"");
                    }

                    ArtifactOfOrder.allowedEquipment = (EquipmentIndex) Convert.ToInt32(loadedData[4]);

                    Log.Info($"Loaded latestInventoryItems, arenaCount({CyAn_Arena.arenaCount}) and currentPlayer({currentPlayer}) with ProperSave.");
                    Log.Info($"[Artifact of Order] Chosen allowed equipment: {Language.GetString(EquipmentCatalog.GetEquipmentDef(ArtifactOfOrder.allowedEquipment).nameToken)}");
                    foreach (var kvp in ArtifactOfOrder.tierToItemMap)
                    {
                        var itemDef = ItemCatalog.GetItemDef(kvp.Value);
                        string itemName = itemDef != null ? Language.GetString(itemDef.nameToken) : $"Unknown({kvp.Key} : {kvp.Value})";
                        Log.Info($"[Artifact of Order] Loaded Item for {kvp.Key}: {itemName}");
                    }
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

            foreach (var (config, varType, action, min, max) in configEntries)
            {
                AddOption(config, varType, action, min, max);
            }

            //set the Icon
            ModSettingsManager.SetModIcon(LoadEmbeddedSprite("CyAn_Rnd.Resources.icon.png"), PluginGUID, PluginName);

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
                else if (StaticType == typeof(RiskOfOptions.Options.GenericButtonOption))
                {
                    //button is special
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
                    Config.Bind("General", "Bleed Stacking", false, "If enabled, anytime a bleed effect is applied and the chance was over 100% there may be a second bleed stack with the remainder, which is added at the same time\nso with 600% crit chance you get 6 bleed stacks per hit\nwill just repeat the buff adding action of base ror2 for every extra bleed. My game starting lagging at ~10k stacks per hit"),
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
                    null,
                    typeof(RiskOfOptions.Options.GenericButtonOption),
                    new Action<object>(_ => CheatUnlockAllItemsAndSurvivors()),
                    "Unlock all",
                    "General"
                ),
                (
                    Config.Bind("Void Fields", "Use Shrines", false, "If enabled, will spawn a unusable teleporter in the void fields to activate mountainShrines instead of just an internal counter\nAny use of the difficulty counter is replaced by the current active mountain Shrines\nThis Option will probably do nothing if you do not have other mods that interact with mountain shrines, i reccomend looking for one that makes them persist over the whole run after activiating"),
                    typeof(bool),
                    new Action<object>(value => CyAn_Arena.useShrine = (bool)value),
                    null,
                    null
                ),
                (
                    Config.Bind("Void Fields", "Difficulty Scaling", 5, "Number the Internal void field difficulty counter is set to per additional void field entry\n with otherwise unmodded entrys it goes 0,num,2xnum,3xnum,..."),
                    typeof(int),
                    new Action<object>(value => CyAn_Arena.numShrines = (int)value),
                    0,
                    10000
                ),
                (
                    Config.Bind("Void Fields", "Skip unmodified fields", false, "If enabled, skips the first normal void fields and will directly apply the difficulty modifier"),
                    typeof(bool),
                    new Action<object>(value => CyAn_Arena.skipVanilla = (bool)value),
                    null,
                    null
                ),
                (
                    Config.Bind("Void Fields", "exponential Scaling", false, "If enabled, will use 'Difficulty Scaling' to *2 instead of just adding\nfor example if the difficulty were normally set to 4 this would make it add 1 and then do *2,*2,*2 for a total of 8 (adding 1 first so that its not 0*2)"),
                    typeof(bool),
                    new Action<object>(value => CyAn_Arena.expScaling = (bool)value),
                    null,
                    null
                ),
                (
                    Config.Bind("Void Fields", "Stage Counter Cap", 0, "Sets a cap for the Stage Counter, which is what 'Difficulty Scaling' is multiplied by for each entry\n0 for disabled\nExample with a cap of 2 and a scaling of 3 would be 0*3=0, 1*3=3, 2*3=6, 2*3=6, ...\nThis only effects what is added if you, for example, use shrines and have a persistent shrine mod they will still be uncapped and will just always scale by the current settings *Cap at most"),
                    typeof(int),
                    new Action<object>(value => CyAn_Arena.arenaCap = (int)value),
                    0,
                    1000
                ),
                (
                    Config.Bind("Void Fields", "Enemy ItemStacks", 1, "Sets the number of itemStacks the void fields enemies can obtain.\n1 per activation is vanilla, but with this you can get for example goat's hoof and crit classes at the same time\ndisabled if Kill Me is checked"),
                    typeof(int),
                    new Action<object>(value => CyAn_Arena.extraStacks = (int)value),
                    1,
                    1000
                ),
                (
                    Config.Bind("Void Fields", "Enemy Extra ItemStacks Threshold", 0, "Difficulty Count required to increase 'Enemy Extra ItemStacks' by 1\nExample if Set to 100 and the Counter is at 225, it will use your ItemStacks setting +2\n0 for disabled"),
                    typeof(int),
                    new Action<object>(value => CyAn_Arena.extraStacksThreshold = (int)value),
                    0,
                    10000
                ),
                (
                    Config.Bind("Void Fields", "Allow Duplicates", false, "If enabled this allows the void fields to roll the same item twice\nNormally as long as you god crit glasses once they will not be added to the inventory again, this will make it so you could get crit glasses twice thus possibly having to deal with a higher number(of the same) but less differnt items"),
                    typeof(bool),
                    new Action<object>(value => CyAn_Arena.allowDuplicates = (bool)value),
                    null,
                    null
                ),
                (
                    Config.Bind("Void Fields", "Monsters", 1, "Sets the number of Monsters the void fields add at once.\nReminder that this will be capped by your enemies as in the further you are in stage/lvl you may get harder enemies which you will never get even if you try 100 times (=this set to 100) stage 1\nStill causes an error but seems to work anyway, error is logged, if you are curious, but feel safe to use this for now"),
                    typeof(int),
                    new Action<object>(value => CyAn_Arena.extraMonsterTypes = (int)value),
                    1,
                    1000
                ),
                (
                    Config.Bind("Void Fields", "Monsters Threshold", 0, "Difficulty Count required to increase 'Monsters' by 1\nExample if Set to 20 and the Counter is at 60, it will use your Monster Setting +3\n0 for disabled"),
                    typeof(int),
                    new Action<object>(value => CyAn_Arena.extraMonsterTypesThreshold = (int)value),
                    0,
                    10000
                ),
                (
                    Config.Bind("Void Fields", "Monster Blacklist", "", "Any String written here in the form of '[Name],[Name],...' Will be matched to the potential enemy pool and removed if a match is found\nExample, RoR2 has the Spawn Card 'cscLesserWisp' so having this set to 'cscLesserWisp' will remove only the Wisp from the potential Enemies. Setting it to 'cscLesserWisp,cscGreaterWisp' will remove both lesser and greater Wisp, wereas 'Wisp' will remove any that have the name Wisp in them which might remove other modded entries like Ancient Wisp\nAt this point you just have to know or guess the names of the SpawnCards"),
                    typeof(String),
                    new Action<object>(value => CyAn_Arena.monsterBlacklist = (String)value),
                    null,
                    null
                ),
                (
                    Config.Bind("Void Fields", "Extra Credits", 0f, "How many extra credits are given to the void fields per Difficulty Counter\n0 for disabled[i mean you add 0, so...]\nI am not 100% sure but RoR2 may move unused credits to the next stage combat director, so be aware"),
                    typeof(float),
                    new Action<object>(value => CyAn_Arena.extraMonsterCredits = (float)value),
                    0f,
                    10000f
                ),
                (
                    Config.Bind("Void Fields", "Enemy Extra Items", 1f, "Multiplier for void field enemy items per Difficulty Count.\n0 for disable"),
                    typeof(float),
                    new Action<object>(value => CyAn_Arena.extraItems = (float)value),
                    0f,
                    10000f
                ),
                (
                    Config.Bind("Void Fields", "Reward Item Multiplier", 1f, "Multiplier for void field rewards per Difficulty Count.\n0 for disable"),
                    typeof(float),
                    new Action<object>(value => CyAn_Arena.extraRewards = (float)value),
                    0f,
                    10000f
                ),
                (
                    Config.Bind("Void Fields", "Increase Stage Counter after Cell Finish", 0f, "This value will be multiplied by the difficulty counter and Increase the Stage after each cell by that amount\nAs only whole numbers are allowed it will use the floor so it could be 0 for the first few times if the difficulty and this setting is low enough\n0 for disable"),
                    typeof(float),
                    new Action<object>(value => CyAn_Arena.stageIncrease = (float)value),
                    0f,
                    10f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell Radius", 1f, "Multiplies the base radius with the given number"),
                    typeof(float),
                    new Action<object>(value => CyAn_Arena.voidRadius = (float)value),
                    0f,
                    10000f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell Charge duration", 2f, "Multiplies the base charge duration with the given number\nDefault is 2, which is twice as long, because by default settings all cells only need 11%. All in all you go from 0 to 100% once only for a total speed increase of 4.5(if i mathed(?) correctly)"),
                    typeof(float),
                    new Action<object>(value => CyAn_Arena.chargeDurationMult = (float)value),
                    0.0001f,
                    1000f
                ),
                ( //i wanted to add these via a loop but that caused some wierd error with RiskOfOptions... [I replaced this with an array at a later point; it seemed more fitting because of such things too]
                    Config.Bind("Void Fields", "Void Cell 1 start Charge", 0f, "The void cell starts at the given percentage for Cell 1"),
                    typeof(float),
                    new Action<object>(value => CyAn_Arena.startCharges[0] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 1 max Charge", 0.11f, "At the given percent, the void cell instantly finishes for Cell 1"),
                    typeof(float),
                    new Action<object>(value => CyAn_Arena.maxCharges[0] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 2 start Charge", 0.11f, "The void cell starts at the given percentage for Cell 2"),
                    typeof(float),
                    new Action<object>(value => CyAn_Arena.startCharges[1] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 2 max Charge", 0.22f, "At the given percent, the void cell instantly finishes for Cell 2"),
                    typeof(float),
                    new Action<object>(value => CyAn_Arena.maxCharges[1] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 3 start Charge", 0.22f, "The void cell starts at the given percentage for Cell 3"),
                    typeof(float),
                    new Action<object>(value => CyAn_Arena.startCharges[2] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 3 max Charge", 0.33f, "At the given percent, the void cell instantly finishes for Cell 3"),
                    typeof(float),
                    new Action<object>(value => CyAn_Arena.maxCharges[2] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 4 start Charge", 0.33f, "The void cell starts at the given percentage for Cell 4"),
                    typeof(float),
                    new Action<object>(value => CyAn_Arena.startCharges[3] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 4 max Charge", 0.44f, "At the given percent, the void cell instantly finishes for Cell 4"),
                    typeof(float),
                    new Action<object>(value => CyAn_Arena.maxCharges[3] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 5 start Charge", 0.44f, "The void cell starts at the given percentage for Cell 5"),
                    typeof(float),
                    new Action<object>(value => CyAn_Arena.startCharges[4] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 5 max Charge", 0.55f, "At the given percent, the void cell instantly finishes for Cell 5"),
                    typeof(float),
                    new Action<object>(value => CyAn_Arena.maxCharges[4] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 6 start Charge", 0.55f, "The void cell starts at the given percentage for Cell 6"),
                    typeof(float),
                    new Action<object>(value => CyAn_Arena.startCharges[5] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 6 max Charge", 0.66f, "At the given percent, the void cell instantly finishes for Cell 6"),
                    typeof(float),
                    new Action<object>(value => CyAn_Arena.maxCharges[5] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 7 start Charge", 0.66f, "The void cell starts at the given percentage for Cell 7"),
                    typeof(float),
                    new Action<object>(value => CyAn_Arena.startCharges[6] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 7 max Charge", 0.77f, "At the given percent, the void cell instantly finishes for Cell 7"),
                    typeof(float),
                    new Action<object>(value => CyAn_Arena.maxCharges[6] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 8 start Charge", 0.77f, "The void cell starts at the given percentage for Cell 8"),
                    typeof(float),
                    new Action<object>(value => CyAn_Arena.startCharges[7] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 8 max Charge", 0.88f, "At the given percent, the void cell instantly finishes for Cell 8"),
                    typeof(float),
                    new Action<object>(value => CyAn_Arena.maxCharges[7] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 9 start Charge", 0.88f, "The void cell starts at the given percentage for Cell 9"),
                    typeof(float),
                    new Action<object>(value => CyAn_Arena.startCharges[8] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Void Cell 9 max Charge", 1f, "At the given percent, the void cell instantly finishes for Cell 9"),
                    typeof(float),
                    new Action<object>(value => CyAn_Arena.maxCharges[8] = (float)value),
                    0f,
                    1f
                ),
                (
                    Config.Bind("Void Fields", "Kill Me", false, "If enabled, enemies gain one of every item type instead of a single item type.\nWill also add all Equipment at the same time\nVoid field rewards are now only lunar items(idk why). Items/Equipment may not show up in the listing but are still present\nTHIS OPTION HAS A SPECIFIC NAME FOR A REASON\nTHIS OPTION HAS A SPECIFIC NAME FOR A REASON\nTHIS OPTION HAS A SPECIFIC NAME FOR A REASON"),
                    typeof(bool),
                    new Action<object>(value => CyAn_Arena.KillMeOption = (bool)value),
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
                else if (type == typeof(RiskOfOptions.Options.GenericButtonOption))
                {
                    //button has no update
                }
                else
                {
                    string key = config?.Definition?.Key ?? "UnknownKey";
                    Log.Error($"{key} is of invalid type: {type}");
                }
            }

            return configEntries;
        }

        //Adds RiskOfOptions Option
        private void AddOption(ConfigEntryBase config, Type varType, Action<object> action, object min, object max)
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
            else if (varType == typeof(RiskOfOptions.Options.GenericButtonOption)) // Button case
            {
                ModSettingsManager.AddOption(new RiskOfOptions.Options.GenericButtonOption(
                    "Does what it says",
                    (string)max,
                    "Button that unlocks every item and survivor. Helpfull if you switch mods alot",
                    (string)min,
                    () => action(null)
                ));
                Log.Info($"Added RiskOfOption '{min}' as GenericButton");
            }
            else
            {
                string key = config?.Definition?.Key ?? "UnknownKey";
                Log.Error($"Failed to create option for {key} because type was {varType}");
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

        private void ExtraBleed(On.RoR2.DotController.orig_AddDot_GameObject_float_DotIndex_float_Nullable1_Nullable1_Nullable1 orig, DotController self, GameObject attackerObject, float duration, DotController.DotIndex dotIndex, float damageMultiplier, uint? maxStacksFromAttacker, float? totalDamage, DotController.DotIndex? preUpgradeDotIndex)
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
                //making sure wasLoaded is set to fals only after the normal run start; so that other run starts can check on this
                orig(self);
                wasLoaded = false;
                return;
            }
            CyAn_Arena.latestInventoryItems = new int[0];
            CyAn_Arena.arenaCount = -1;
            orig(self);
        }

        private IEnumerator CheckTeleporterInstance(On.RoR2.Stage.orig_Start orig, Stage self)
        {
            //Extra Logic used when ItemDrops are prevented to determin things like scrap owner
            StartCoroutine(DelayRePopulate()); //i am a bit unclear with the wait a second thing I do here with c#/unity; it says startCoroutine does that mean a new thread? because i do not believe i do anything threadsafe at all, i could look it up or just write this here and ignore it if until it becomes a problem

            //there is 'self.sceneDef.baseSceneName' but its seems to not be an instance for some reason, so i found this: 'SceneInfo.instance.sceneDef.baseSceneName'
            if (SceneInfo.instance.sceneDef.baseSceneName == "arena")
            {
                CyAn_Arena.ArenaStageStart();

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

        public static int GetPlayerIndexFromInteractionObject(GameObject Object)
        {
            //alot of checks to find the Index of the player from their interaction with the passed object
            CharacterBody body = null;
            PlayerCharacterMasterController Mastercontroller = null;
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
                    if (master != null) Mastercontroller = master.playerCharacterMasterController; //special option because apperently for the pickup the server has the body as null at this instance
                    else Log.Warning($"Could not find PlayerCharacterMasterController for: {uiController.name} (probably not OptionPickup?: {Object.name})");
                }
                else Log.Warning($"Could not find uiController for: {Object.name}");
            }

            if (body != null || Mastercontroller != null)
            {
                //find index of Mastercontroller;
                if (Mastercontroller == null) Mastercontroller = body.master.playerCharacterMasterController;
                for (int i = 0; i < PlayerCharacterMasterController.instances.Count; i++)
                {
                    if (PlayerCharacterMasterController.instances[i] == Mastercontroller)
                    {
                        return i;
                    }
                }
                Log.Warning($"Could not find {Mastercontroller} in PlayerCharacterMasterController.instances (body: {body}, master: {body.master})");
            }
            return -1;
        }

        [Server]
        public static void AddToPlayerInventory(ItemIndex item, int target = -1, int total = 1)
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
            CyAn_Network networkItem = new(item); //this is some network vodoo in my opinion

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

            client.RegisterHandler(networkId, RecieveData);
            Log.Info($"Registerd NetworkId {networkId}");
        }

        //this should happen when a item is send over the network via my mod which is then queued as a pickupnotification for the reciever
        //Now if any data is send
        private static void RecieveData(NetworkMessage networkMessage)
        {
            CyAn_Network data = networkMessage.ReadMessage<CyAn_Network>();
            if (data.MsgType == 1)
            {
                CyAn_Arena.RecieveData(data);
                return;
            }
            ItemIndex item = data.Item;
            PlayerCharacterMasterController localPlayer = PlayerCharacterMasterController.instances.FirstOrDefault(x => x.networkUser.isLocalPlayer);

            if (localPlayer == null)
            {
                return;
            }
            
            CharacterMasterNotificationQueue.PushItemNotification(localPlayer.master, item);
        }

        [Server]
        public static void SyncObject(GameObject obj)
        {
            NetworkServer.Spawn(obj); //this should sync the object to all
        }

        private void Update()
        {
            /*if (Input.GetKeyDown(KeyCode.F2))
            {
                // Get the player body to use a position:
                var player = PlayerCharacterMasterController.instances[0];
                for (int i = 10; i <= 20; i++) player.master.inventory.GiveItem((ItemIndex) i, 5);
                var transform = player.master.GetBodyObject().transform;
                //i do not want to die while testing
                player.master.godMode = true;

                ForceSpawnPortal(transform.position);
            }
            if (Input.GetKeyDown(KeyCode.F3))
            {
                Log.Info("All Charges: ");
                ArenaMissionController controller = FindObjectOfType<ArenaMissionController>();
                foreach (GameObject obj in controller.nullWards)
                {
                    HoldoutZoneController zone = obj.GetComponent<HoldoutZoneController>();
                    Log.Info(zone.charge);
                }
            }*/
        }

        private void ForceSpawnPortal(Vector3 position)
        {
            // Instantiate the portal prefab at the specified position
            GameObject portal = Instantiate(raidPortalPrefab, position + new Vector3(5, 0, 0), Quaternion.identity);
            GameObject portal2 = Instantiate(shopPortalPrefab, position + new Vector3(-5, 0, 0), Quaternion.identity);
        }

        public static Sprite LoadEmbeddedSprite(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    Log.Error($"[ArtifactOfOrder] Embedded resource not found: {resourceName}");
                    return null;
                }

                byte[] data = new byte[stream.Length];
                stream.Read(data, 0, data.Length);

                Texture2D tex = new Texture2D(2, 2);
                tex.LoadImage(data);
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
        }

        private static void CheatUnlockAllItemsAndSurvivors()
        {
            var userProfile = LocalUserManager.GetFirstLocalUser()?.userProfile;
            if (userProfile == null)
            {
                Log.Warning("No user profile found.");
                return;
            }

            int unlocked = 0;

            foreach (var itemDef in ItemCatalog.allItemDefs)
            {
                Log.Info($"checking Item : {itemDef.nameToken}");
                if (itemDef?.unlockableDef && !userProfile.HasUnlockable(itemDef.unlockableDef))
                {
                    userProfile.GrantUnlockable(itemDef.unlockableDef);
                    unlocked++;
                }
            }

            foreach (var equipmentDef in EquipmentCatalog.equipmentDefs)
            {
                Log.Info($"checking equipment : {equipmentDef.nameToken}");
                if (equipmentDef?.unlockableDef && !userProfile.HasUnlockable(equipmentDef.unlockableDef))
                {
                    userProfile.GrantUnlockable(equipmentDef.unlockableDef);
                    unlocked++;
                }
            }

            foreach (var survivorDef in SurvivorCatalog.orderedSurvivorDefs)
            {
                Log.Info($"checking equipment : {survivorDef.displayNameToken}");
                if (survivorDef?.unlockableDef && !userProfile.HasUnlockable(survivorDef.unlockableDef))
                {
                    userProfile.GrantUnlockable(survivorDef.unlockableDef);
                    unlocked++;
                }
            }

            userProfile.RequestEventualSave();
            Log.Message($"[Cheat] Unlocked {unlocked} entries (items/survivors).");
        }

        private void OrderNotificationOverwrite(On.RoR2.CharacterMasterNotificationQueue.orig_PushItemNotification_CharacterMaster_ItemIndex orig, CharacterMaster characterMaster, ItemIndex itemIndex)
        {
            if (!ArtifactOfOrder.orderActive)
            {
                orig(characterMaster, itemIndex);
                return;
            }

            orig(characterMaster, ArtifactOfOrder.tierToItemMap[ItemCatalog.GetItemDef(itemIndex).tier]);
        }

        private void OrderNotificationOverwrite2(On.RoR2.CharacterMasterNotificationQueue.orig_PushEquipmentNotification orig, CharacterMaster characterMaster, EquipmentIndex equipmentIndex, int upgradeCount)
        {
            if (!ArtifactOfOrder.orderActive)
            {
                orig(characterMaster, equipmentIndex, upgradeCount);
                return;
            }

            orig(characterMaster, ArtifactOfOrder.allowedEquipment, upgradeCount);
        }

        private void OrderDropletOverwrite(On.RoR2.PickupDropletController.orig_CreatePickup orig, PickupDropletController self)
        {
            if (!ArtifactOfOrder.orderActive)
            {
                orig(self);
                return;
            }
            var pickupDef = PickupCatalog.GetPickupDef(self.pickupState.pickupIndex);

            if (pickupDef.itemTier != ItemTier.NoTier)
            {
                self.pickupState.pickupIndex = ItemCatalog.GetItemDef(ArtifactOfOrder.tierToItemMap[pickupDef.itemTier]).CreatePickupDef().pickupIndex;
                //both old versions:
                //self.pickupIndex = ItemCatalog.GetItemDef(ArtifactOfOrder.tierToItemMap[PickupCatalog.GetPickupDef(self.pickupIndex).itemTier]).CreatePickupDef().pickupIndex;
                //self.createPickupInfo.pickupIndex = self.pickupIndex;
            }
            else if (pickupDef.isLunar) //probably lunar coin
            {
                orig(self);
                return;
            }
            else //equipment
            {
                self.pickupState.pickupIndex = EquipmentCatalog.GetEquipmentDef(ArtifactOfOrder.allowedEquipment).CreatePickupDef().pickupIndex; ;
                //self.pickupIndex = EquipmentCatalog.GetEquipmentDef(ArtifactOfOrder.allowedEquipment).CreatePickupDef().pickupIndex;
                //self.createPickupInfo.pickupIndex = self.pickupIndex;
            }

            orig(self);
        }
    }

    //ArenaMonsterItemDropTable drop override; for when Order is active
    [HarmonyPatch(typeof(ArenaMonsterItemDropTable), nameof(ArenaMonsterItemDropTable.GenerateDropPreReplacement))]
    public static class Patch_ArenaMonsterItemDropTable_GenerateDrop
    {
        public static bool Prefix(
            ArenaMonsterItemDropTable __instance,
            Xoroshiro128Plus rng,
            ref PickupIndex __result)
        {
            if (!ArtifactOfOrder.orderActive)
                return true; // fall back to vanilla

            // Reflect private 'selector' field
            var selectorField = typeof(ArenaMonsterItemDropTable).GetField("selector", BindingFlags.NonPublic | BindingFlags.Instance);
            var selector = (WeightedSelection<PickupIndex>)selectorField.GetValue(__instance);

            // Clear selector
            selector.Clear();

            // Add custom items
            Add(__instance, selector, ArtifactOfOrder.originalTier1DropList, __instance.tier1Weight);
            Add(__instance, selector, ArtifactOfOrder.originalTier2DropList, __instance.tier2Weight);
            Add(__instance, selector, ArtifactOfOrder.originalTier3DropList, __instance.tier3Weight);
            Add(__instance, selector, ArtifactOfOrder.originalBossDropList, __instance.bossWeight);
            Add(__instance, selector, ArtifactOfOrder.originalLunarDropList, __instance.lunarItemWeight);
            Add(__instance, selector, ArtifactOfOrder.originalVoidTier1DropList, __instance.voidTier1Weight);
            Add(__instance, selector, ArtifactOfOrder.originalVoidTier2DropList, __instance.voidTier2Weight);
            Add(__instance, selector, ArtifactOfOrder.originalVoidTier3DropList, __instance.voidTier3Weight);
            Add(__instance, selector, ArtifactOfOrder.originalVoidBossDropList, __instance.voidBossWeight);

            // Generate drop
            __result = PickupDropTable.GenerateDropFromWeightedSelection(rng, selector);
            return false; // Skip original method
        }

        // Helper to call Add while accessing private methods
        private static void Add(ArenaMonsterItemDropTable instance, WeightedSelection<PickupIndex> selector, List<PickupIndex> sourceList, float weight)
        {
            if (weight <= 0f || sourceList == null || sourceList.Count == 0)
                return;

            MethodInfo passesFilterMethod = typeof(ArenaMonsterItemDropTable).GetMethod("PassesFilter", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (PickupIndex pickup in sourceList)
            {
                bool passes = (bool)passesFilterMethod.Invoke(instance, new object[] { pickup });
                if (passes)
                {
                    selector.AddChoice(pickup, weight);
                }
            }
        }
    }
}
