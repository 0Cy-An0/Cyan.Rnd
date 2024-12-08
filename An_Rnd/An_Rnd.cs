using BepInEx;
using ExamplePlugin;
using R2API;
using RoR2;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using static Rewired.UI.ControlMapper.ControlMapper;

namespace An_Rnd
{
    // This attribute specifies that we have a dependency on a given BepInEx Plugin,
    // We need the R2API ItemAPI dependency because we are using for adding our item to the game.
    // You don't need this if you're not using R2API in your plugin,
    // it's just to tell BepInEx to initialize R2API before this plugin so it's safe to use R2API.
    [BepInDependency(ItemAPI.PluginGUID)]

    // This one is because we use a .language file for language tokens
    // More info in https://risk-of-thunder.github.io/R2Wiki/Mod-Creation/Assets/Localization/
    [BepInDependency(LanguageAPI.PluginGUID)]

    // This attribute is required, and lists metadata for your plugin.
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]

    public class An_Rnd : BaseUnityPlugin
    {
        public const string PluginGUID = "Cyan.Rnd";
        public const string PluginAuthor = "Cy/an";
        public const string PluginName = "Cy/an Rnd";
        public const string PluginVersion = "1.0.0";

        // We need our item definition to persist through our functions, and therefore make it a class field.
        //private static ItemDef myItemDef;

        private static GameObject shopPortalPrefab;
        private static GameObject raidPortalPrefab;
        // The Awake() method is run at the very start when the game is initialized.
        public void Awake()
        {
            // Init our logging class so that we can properly log for debugging
            Log.Init(Logger);
            InitPortalPrefab();
            On.RoR2.BazaarController.Awake += GetNullPos;
            //On.RoR2.VoidRaidEncounterController
            //On.RoR2.VoidRaidGauntletController
        }

        private void InitPortalPrefab()
        {
            shopPortalPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/PortalShop/PortalShop.prefab").WaitForCompletion();
            raidPortalPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/PortalArena/PortalArena.prefab").WaitForCompletion();
            //Change flags, such that Null Portal actually connects to the void fields.
            raidPortalPrefab.GetComponent<SceneExitController>().useRunNextStageScene = false;
        }

        // The Update() method is run on every frame of the game.
        private void Update()
        {
            // This if statement checks if the player has currently pressed F2.
            if (Input.GetKeyDown(KeyCode.F2))
            {
                // Get the player body to use a position:
                var transform = PlayerCharacterMasterController.instances[0].master.GetBodyObject().transform;

                // And then drop our defined item in front of the player.

                //Log.Info($"Player pressed F2. try Spawning portal at coordinates {transform.position}");
                ForceSpawnPortal(transform.position);
                //PickupDropletController.CreatePickupDroplet(PickupCatalog.FindPickupIndex(myItemDef.itemIndex), transform.position, transform.forward * 20f);
            }
        }

        private void ForceSpawnPortal(Vector3 position)
        {
            // Instantiate the portal prefab at the specified position
            GameObject portal = Instantiate(raidPortalPrefab, position + new Vector3(5, 0, 0), Quaternion.identity);
            portal.GetComponent<SceneExitController>().useRunNextStageScene = false;
            GameObject portal2 = Instantiate(shopPortalPrefab, position + new Vector3(-5, 0, 0), Quaternion.identity);
        }

        private void GetNullPos(On.RoR2.BazaarController.orig_Awake orig, BazaarController self)
        {
            orig(self);
            // Find all portals in the scene
            GameObject[] portals = FindObjectsOfType<GameObject>(); //this could probably take a long time

            // Look for the Null portal (e.g., PortalArena, which is the Null portal)
            foreach (GameObject portal in portals)
            {
                if (portal.name.Contains("PortalArena")) // Check if this is a Null portal
                {
                    Logger.LogInfo("Found a Null portal!");
                    // Get Positon
                    Logger.LogInfo("pos: " + portal.transform.position);
                    Logger.LogInfo("rot: " + portal.transform.rotation);

                    break;
                }
            }
        }
    }
}
