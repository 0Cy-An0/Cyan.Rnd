using An_Rnd;
using BepInEx;
using RiskOfOptions.Options;
using R2API;
using RoR2;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AddressableAssets;
using BepInEx.Configuration;
using RiskOfOptions;
using System.Reflection;

namespace An_Rnd
{
    //RoR2Api Dependecys
    [BepInDependency(ItemAPI.PluginGUID)]

    [BepInDependency(LanguageAPI.PluginGUID)]

    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]

    //Risk of options
    [BepInDependency("com.rune580.riskofoptions", BepInDependency.DependencyFlags.SoftDependency)]

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
        //this will store the inventory of the enemies last void Fields; Items are stored as an array of Ints
        public static int[] latestInventoryItems;

        // The Awake() method is run at the very start when the game is initialized.
        public void Awake()
        {
            // Init our logging class so that we can properly log for debugging
            Log.Init(Logger);
            InitPortalPrefab();
            TryInitRiskOfOptions();
            
            On.RoR2.BazaarController.OnStartServer += CheckNullPortal;
            On.RoR2.PickupPickerController.CreatePickup_PickupIndex += MultiplyItemReward;
            On.RoR2.ArenaMissionController.AddItemStack += MultiplyEnemyItem;
            On.RoR2.Stage.Start += CheckTeleporterInstance;
        }

        private void TryInitRiskOfOptions()
        {
            //Configs for all Options with Category, Name, Default, Description
            ConfigEntry<int> numShrinesConfig = Config.Bind(
                "General",
                "Number of Shrines",
                5,
                "How many shrines activate per extra entry for the void fields. so second entry will activate this number third this *2, etc."
            );

            //If ChatGpt helped me correctly this try block should make it work with and without RiskOfOptions. Do i Trust that this is the best way to do this? no! do i have a better alternative on hand... sadly i have no other ideas.
            try
            {
                // Check if RiskOfOptions.ModSettingsManager exists
                Type modSettingsManagerType = Type.GetType("RiskOfOptions.ModSettingsManager, RiskOfOptions");
                if (modSettingsManagerType != null)
                {
                    // Create IntSliderConfig dynamically
                    Type intSliderConfigType = Type.GetType("RiskOfOptions.OptionConfigs.IntSliderConfig, RiskOfOptions");
                    var intSliderConfig = Activator.CreateInstance(intSliderConfigType);

                    // Set properties for IntSliderConfig
                    intSliderConfigType.GetProperty("min")?.SetValue(intSliderConfig, 1);
                    intSliderConfigType.GetProperty("max")?.SetValue(intSliderConfig, 100);

                    // Create IntSliderOption dynamically
                    Type intSliderOptionType = Type.GetType("RiskOfOptions.Options.IntSliderOption, RiskOfOptions");
                    var intSliderOption = Activator.CreateInstance(intSliderOptionType, numShrinesConfig, intSliderConfig);

                    // Resolve AddOption(BaseOption option) method
                    var baseOptionType = Type.GetType("RiskOfOptions.Options.BaseOption, RiskOfOptions");
                    var addOptionMethod = modSettingsManagerType.GetMethod(
                        "AddOption",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { baseOptionType },
                        null
                    );

                    // Invoke AddOption(BaseOption option)
                    addOptionMethod?.Invoke(null, new[] { intSliderOption });

                    Debug.Log("RiskOfOptions integration successful.");
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

            //hook when the settings are changed, so that i can hopefully make RiskOfOptions optional
            numShrinesConfig.SettingChanged += (sender, args) =>
            {
                numShrines = numShrinesConfig.Value;
            };
        }

        private void InitPortalPrefab()
        {
            shopPortalPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/PortalShop/PortalShop.prefab").WaitForCompletion();
            raidPortalPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/PortalArena/PortalArena.prefab").WaitForCompletion();
            //Change flags, such that Null Portal actually connects to the void fields.
            raidPortalPrefab.GetComponent<SceneExitController>().useRunNextStageScene = false;
            teleporterPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Teleporters/Teleporter1.prefab").WaitForCompletion();
        }

        //The Update() method is run on every frame of the game.
        private void Update()
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

        private IEnumerator CheckTeleporterInstance(On.RoR2.Stage.orig_Start orig, Stage self)
        {
            //there is 'self.sceneDef.baseSceneName' but its seems to not be an instance for some reason, so i found this: 'SceneInfo.instance.sceneDef.baseSceneName'
            if (SceneInfo.instance.sceneDef.baseSceneName == "arena")
            {
                arenaCount += 1; //counter how often we entered the void fields

                //this should start the enemies with the items of the last attempts
                if (latestInventoryItems != null)
                {
                    ArenaMissionController controller = FindObjectOfType<ArenaMissionController>();
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
            }
            return orig(self);
        }

        //just to note for future reference, using this caused some wired error. Maybe something else was going on at the time, but for now ill say it does not work
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

            //self.StartCoroutine(ChunkRewards(orig, self, pickupIndex));
            // This drops the item after the selection so we just call it as many times as items are needed
            int total = (TeleporterInteraction.instance.shrineBonusStacks);
            for (int i = 0; i < total; i++)
            {
                orig(self, pickupIndex);
            }
        }

        private IEnumerator ChunkRewards(On.RoR2.PickupPickerController.orig_CreatePickup_PickupIndex orig, PickupPickerController self, PickupIndex pickupIndex)
        {
            int totalItems = (TeleporterInteraction.instance.shrineBonusStacks);
            if (totalItems == 0) totalItems = 1; //if no shrines are active we are in the first run so 1 item

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

            // Call the original method to add the items
            orig(self);

            latestInventoryItems = new int[inv.itemStacks.Length];
            // Compare the item stacks before and after; This should work a bit more generally than just for 1 item only like the void fields, so i might do something else with it later idk
            for (int i = 0; i < inv.itemStacks.Length; i++)
            {
                if (inv.itemStacks[i] > originalItemStacks[i])
                {
                    inv.itemStacks[i] = originalItemStacks[i] + (inv.itemStacks[i] - originalItemStacks[i]) * (TeleporterInteraction.instance.shrineBonusStacks);
                }
                latestInventoryItems[i] = inv.itemStacks[i];
            }
        }

        private void ForceSpawnPortal(Vector3 position)
        {
            // Instantiate the portal prefab at the specified position
            GameObject portal = Instantiate(raidPortalPrefab, position + new Vector3(5, 0, 0), Quaternion.identity);
            GameObject portal2 = Instantiate(shopPortalPrefab, position + new Vector3(-5, 0, 0), Quaternion.identity);
        }

        private void CheckNullPortal(On.RoR2.BazaarController.orig_OnStartServer orig, BazaarController self)
        {
            //bit unsure if i should do a serverside check here, i assume not because this hooks into OnStartServer, but who knows; remind me to check here if there is a problem
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
            }
        }
    }
}
