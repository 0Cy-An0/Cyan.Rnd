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
        //Super Secret Option
        public static bool KillMeOption = false;
        //this will store the inventory of the enemies last void Fields; Items are stored as an array of Ints
        public static int[] latestInventoryItems;
        //Will remove all pre-game(logbook, select, etc.) Hightlights automatically if true
        public static bool NoHightlights = false;

        // The Awake() method is run at the very start when the game is initialized.
        public void Awake()
        {
            // Init our logging class so that we can properly log for debugging
            Log.Init(Logger);
            InitPortalPrefab();
            TryInitRiskOfOptions();
            TryInitProperSave();
            
            On.RoR2.BazaarController.OnStartServer += CheckNullPortal;
            On.RoR2.PickupPickerController.CreatePickup_PickupIndex += MultiplyItemReward;
            On.RoR2.ArenaMissionController.AddItemStack += MultiplyEnemyItem;
            On.RoR2.ArenaMissionController.AddMonsterType += MultiplyEnemyType;
            On.RoR2.Stage.Start += CheckTeleporterInstance;
            On.RoR2.Run.Start += ResetRunVars;
            On.RoR2.UserProfile.HasViewedViewable += Viewed;
        }

        private bool Viewed(On.RoR2.UserProfile.orig_HasViewedViewable orig, UserProfile self, string viewableName)
        {
            if (NoHightlights) return true;
            return orig(self, viewableName);
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
                        try
                        {

                            var saveDataProperty = saveFile.GetType().GetProperty("ModdedData");
                            var moddedDataDictionary = saveDataProperty.GetValue(saveFile) as IDictionary<string, ModdedData>;

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

                                    Log.Info($"Loaded latestInventoryItems({latestInventoryItems}) and arenaCount({arenaCount}) with ProperSave.");
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
            // Define the List to store ConfigEntry, Typing, the corresponding update action for the static variable and min/max for the slider/field
            //ConfigEntry is Configurations for option with Category, Name, Default, and Description
            var configEntries = new List<(ConfigEntryBase config, Type StaticType, Action<object> updateStaticVar, object min, object max)>
            {
                (
                    Config.Bind("General", "No Hightlights", false, "If enabled, anytime the game asks if you have viewed a 'viewable' it will skip the check and return true.\nThis should effect things like logbook entries and new characters/abilities in the select screen"),
                    typeof(bool),
                    new Action<object>(value => NoHightlights = (bool)value),
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
                    Config.Bind("Void Fields", "Monsters", 1, "Sets the number of Monsters the void fields add at once.\nCurrently Broken\nCurrently Broken"),
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
                    Config.Bind("Void Fields", "Extra Credits", 0, "How many extra credits are given to the void fields per active mountain shrine\n0 for disabled[i mean x+0=x...]"),
                    typeof(int),
                    new Action<object>(value => extraMonsterCredits = (int)value),
                    0,
                    100000
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
                    Config.Bind("Void Fields", "Kill Me", false, "If enabled, enemies gain one of every item type instead of a single item type.\nWill also add all Equipment at the same time\nVoid field rewards are now only lunar items(idk why). Items/Equipment may not show up in the listing but are still present\nTHIS OPTION HAS A SPECIFIC NAME FOR A REASON\nTHIS OPTION HAS A SPECIFIC NAME FOR A REASON\nTHIS OPTION HAS A SPECIFIC NAME FOR A REASON"),
                    typeof(bool),
                    new Action<object>(value => KillMeOption = (bool)value),
                    null, //a bool option does not need a min/max [should be somewhat obvious i guess]
                    null
                )
            };

            //change from default values if config is already present:
            foreach ( var (config, type, update, _, _) in configEntries )
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
                else
                {
                    Log.Error($"{config.Definition.Key} is of invalid type: {type}");
                }
            }

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
            foreach (var (config, StaticType, updateStaticVar, _ , _) in configEntries)
            {
                // Cast to the specific type of ConfigEntry<T> dynamically
                if (StaticType == typeof(int))
                {
                    ConfigEntry<int> castConfig = (ConfigEntry<int>) config;
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
                else
                {
                    Log.Error($"Could not get type {StaticType} to hook SettingChanged for {config.Definition.Key}");
                }
            }
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
                    configType.GetField("min")?.SetValue(configInstance, (int) min);
                    configType.GetField("max")?.SetValue(configInstance, (int) max);
                    Log.Info($"Option {config.Definition.Key} as IntSlider");
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

                    Log.Info($"Option {config.Definition.Key} as FloatField");
                    return Activator.CreateInstance(baseOptionType, config, configInstance);
                }
                else if (varType == typeof(bool))
                {
                    Type baseOptionType = Type.GetType("RiskOfOptions.Options.CheckBoxOption, RiskOfOptions");
                    Log.Info($"Option {config.Definition.Key} as CheckBox");
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

        private void ResetRunVars(On.RoR2.Run.orig_Start orig, Run self)
        {
            latestInventoryItems = null;
            arenaCount = -1;
            orig(self);
        }

        private IEnumerator CheckTeleporterInstance(On.RoR2.Stage.orig_Start orig, Stage self)
        {
            //there is 'self.sceneDef.baseSceneName' but its seems to not be an instance for some reason, so i found this: 'SceneInfo.instance.sceneDef.baseSceneName'
            if (SceneInfo.instance.sceneDef.baseSceneName == "arena")
            {
                arenaCount += 1; //counter how often we entered the void fields
                ArenaMissionController controller = FindObjectOfType<ArenaMissionController>();

                //this should start the enemies with the items of the last attempts
                if (latestInventoryItems != null)
                {
                    // AddItemsFrom is a overloaded method, wich needs a filter to accept int[] as input; but we just want everything
                    Func<ItemIndex, bool> includeAllFilter = _ => true;
                    controller.inventory.AddItemsFrom(latestInventoryItems, includeAllFilter);
                }

                //the 'arena' also known as the void fields, does not have a teleporter, but i want to activate mountain shrines anyway
                //VoidTele();
                GameObject portal = Instantiate(teleporterPrefab, new Vector3(0, -1000, 0), Quaternion.identity); // I hope -1000 is away from everything/unreachable
                for (int i = 0; i < arenaCount * numShrines; i++)
                {
                    TeleporterInteraction.instance.AddShrineStack();
                }
                //adding the extra Credits from the config
                controller.baseMonsterCredit += extraMonsterCredits * TeleporterInteraction.instance.shrineBonusStacks;
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
            int total = Math.Max((int)Math.Floor(TeleporterInteraction.instance.shrineBonusStacks * extraRewards), 1);//if you are confused what this does check the code for the enemy items (extraItems), its the same thing just better explained
            for (int i = 0; i < total; i++)
            {
                orig(self, pickupIndex);
            }
        }

        private IEnumerator ChunkRewards(On.RoR2.PickupPickerController.orig_CreatePickup_PickupIndex orig, PickupPickerController self, PickupIndex pickupIndex)
        {
            int totalItems = Math.Max((int)Math.Floor(TeleporterInteraction.instance.shrineBonusStacks * extraRewards), 1);//if you are confused what this does check the code for the enemy items (extraItems), its the same thing just better explained

            //if mountain shrines are 0, it should still give 1 item (first run)
            for (int i = 0; i < totalItems; i++)
            {
                orig(self, pickupIndex);

                // Wait for 1 second after each chunk
                if (i > 0 && i % chunkSize == 0) yield return new WaitForSeconds(1f);
            }
        }

        private void MultiplyEnemyItem(On.RoR2.ArenaMissionController.orig_AddItemStack orig, ArenaMissionController self)
        {
            Inventory inv = self.inventory;

            // Track how many items are being added by checking the previous state (before adding new items)
            int[] originalItemStacks = (int[])inv.itemStacks.Clone(); // Clone the current stacks for comparison later

            //increase extraStacks by how many times the Threshold was reached; reminder that this is a int div; 0 should be disable
            int totalStacks = extraStacks;
            if (extraStacksThreshold > 0) totalStacks += TeleporterInteraction.instance.shrineBonusStacks / extraStacksThreshold;

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
                        int bonus = (int)Math.Floor(TeleporterInteraction.instance.shrineBonusStacks * extraItems);

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
                    inv.itemStacks[i] += Math.Max((int) Math.Floor(TeleporterInteraction.instance.shrineBonusStacks *  extraItems), 1); //should add the floor of extraItems min 1, to all items available in inventory
                }
                
                latestInventoryItems[i] = inv.itemStacks[i];
            }
        }

        private void MultiplyEnemyType(On.RoR2.ArenaMissionController.orig_AddMonsterType orig, ArenaMissionController self)
        {
            //increase MonsterTypes by how many times the Threshold was reached; reminder that this is a int div; 0 should be disable
            int total = extraMonsterTypes;//'extra' MonsterTypes is at least 1
            if (extraStacksThreshold > 0) total += TeleporterInteraction.instance.shrineBonusStacks / extraMonsterTypesThreshold;
            for (int i = 0; i < total; i++)
            {
                orig(self);
            }
        }

        private void CheckNullPortal(On.RoR2.BazaarController.orig_OnStartServer orig, BazaarController self)
        {
            orig(self);
            self.StartCoroutine(CheckNullDelay());

        }

        private IEnumerator CheckNullDelay()
        {
            yield return new WaitForSeconds(1f);

            // check all gamebojects because this way was easiest, probably improvable
            GameObject[] portals = FindObjectsOfType<GameObject>();
            bool found = false;

            foreach (GameObject portal in portals)
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
