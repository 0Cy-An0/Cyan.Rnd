using BepInEx;
using ExamplePlugin;
using R2API;
using RoR2;
using System.Collections;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UIElements;

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

        //shopPortal is not neccessary anymore but ill leave it to reuse use when testing
        private static GameObject shopPortalPrefab;
        private static GameObject raidPortalPrefab;
        // The Awake() method is run at the very start when the game is initialized.
        public void Awake()
        {
            // Init our logging class so that we can properly log for debugging
            Log.Init(Logger);
            InitPortalPrefab();
            On.RoR2.BazaarController.OnStartServer += CheckNullPortal;
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

        /* The Update() method is run on every frame of the game.
        private void Update()
        {
            
            if (Input.GetKeyDown(KeyCode.F2))
            {
                // Get the player body to use a position:
                var transform = PlayerCharacterMasterController.instances[0].master.GetBodyObject().transform;

                ForceSpawnPortal(transform.position);
            }
        }*/

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