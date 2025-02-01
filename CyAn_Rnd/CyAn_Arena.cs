using BepInEx;
using RoR2;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace CyAn_Rnd
{
    public class CyAn_Arena
    {
        public static int arenaCount = -1; //this will count how many times the void fields were entered; just using the name convention of base RoR2 for the stage
        //starts at -1 so that first entry is 0

        //Cap for arenaCounter
        public static int arenaCap = 0;
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
        //max charge for all 9 cells (void fields)
        public static float[] maxCharges = [0.11f, 0.22f, 0.33f, 0.44f, 0.55f, 0.66f, 0.77f, 0.88f, 1f];
        //starting charge for all 9 cells (void fields)
        public static float[] startCharges = [0f, 0.11f, 0.22f, 0.33f, 0.44f, 0.55f, 0.66f, 0.77f, 0.88f];
        //charge duration multiplier (void fields)
        public static float chargeDurationMult = 1f;
        //current cell Counter for the void fields; used for 'maxCharges'
        public static int currentCell = -1;
        //only used if useShrine is false; substitutes/is substituted by mountain shrines
        public static int DifficultyCounter = 0;
        //should be what exactly what the name says. Check method 'RemoveMatchingMonsterCards' for specific use
        public static String monsterBlacklist = "";
        //Optionto roll same itemstacks multiple times
        public static bool allowDuplicates = false;
        //couldnt set the recieved data as is so i store it here first
        public static float recievedSize = 0f;
        public void RegisterArenaHooks()
        {
            On.RoR2.BazaarController.OnStartServer += CheckNullPortal;
            On.RoR2.PickupPickerController.CreatePickup_PickupIndex += MultiplyItemReward;
            On.RoR2.ArenaMissionController.AddItemStack += MultiplyEnemyItem;
            On.RoR2.ArenaMissionController.AddMonsterType += MultiplyEnemyType;
            On.RoR2.ArenaMissionController.BeginRound += ActivateCell;
            On.RoR2.ArenaMissionController.EndRound += FinishCell;
            On.RoR2.HoldoutZoneController.Update += ZoneCharge;
        }

        public void ZoneCharge(On.RoR2.HoldoutZoneController.orig_Update orig, HoldoutZoneController self)
        {
            //non-host check
            if (!self.hasAuthority)
            {
                if (recievedSize > 0f)
                {
                    self.baseRadius = recievedSize;
                    recievedSize = 0f;
                }
                orig(self);
                return;
            }

            //Custom charge logic only applies in the void fields, otherwise the normal tp would be affected
            if (SceneInfo.instance.sceneDef.baseSceneName == "arena")
            {
                if (self.charge <= maxCharges[currentCell]) orig(self);
                else if (self.charge <= 0.99f) //if it works correctly this if branched should only be reached once per Mastercontroller after which it disables itself; but it did not, hence i added the 0.99 check
                {
                    orig(self);
                    self.charge = 1f;
                    //self.FullyChargeHoldoutZone(); <- this caused the zone to be stuck at 99% sometimes; I am just throwing this with this updated as a bonus, if its a big problem i will investigate correctly later
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
            //just wanna make sure this only applies to void fields
            if (SceneInfo.instance.sceneDef.baseSceneName != "arena") return;

            int total;
            if (useShrine) total = Math.Max((int)Math.Floor(TeleporterInteraction.instance.shrineBonusStacks * extraRewards), 1);//if you are confused what this does check the code for the enemy items (extraItems), its the same thing just better explained
            else total = Math.Max((int)Math.Floor(DifficultyCounter * extraRewards), 1);



            if (CyAn_Rnd.preventDrops)
            {
                int playerIndex = CyAn_Rnd.GetPlayerIndexFromInteractionObject(self.gameObject);
                CyAn_Rnd.AddToPlayerInventory(pickupIndex.pickupDef.itemIndex, playerIndex, total);
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
            //sync radius to players
            CyAn_Network Network = new(cell.baseRadius);
            NetworkServer.SendToAll(CyAn_Rnd.networkId, Network);
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
                    inv.GiveItem(index, tempStacks[(int)index]);
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
            if (extraMonsterTypesThreshold > 0)
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

        public void ArenaStageStart()
        {
            if (skipVanilla && arenaCount < 0) arenaCount = 0;
            arenaCount += 1; //counter how often we entered the void fields
            if (arenaCap > 0 && arenaCount > arenaCap) arenaCount = arenaCap;
            currentCell = -1; //reset current Cell counter; example use in 'ActivateCell' and 'ZoneCharge' [-1 because it does +1 always and 0-index]
            DifficultyCounter = 0; //reset DifficultyCounter even tough it may not be used depening on choosen options

            ArenaMissionController controller = UnityEngine.Object.FindObjectOfType<ArenaMissionController>();
            //non-host check
            if (controller == null)
            {
                return;
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
                GameObject portal = UnityEngine.Object.Instantiate(CyAn_Rnd.teleporterPrefab, new Vector3(0, -1000, 0), Quaternion.identity); // I hope -1000 is away from everything/unreachable
                                                                                                                  //btw i do not sync portal to client, which i had to do for the null portal, but its supposed to be inaccesible anyway, so that should be fine

                //Allow time for other mods to add shrines on stage start
                controller.StartCoroutine(AddShrinesDelay(controller));
            }
            else //we do not need a portal now, and the shrines are replaces by DifficultyCounter
            {
                DifficultyCounter += numShrines;

                if (expScaling && numShrines > 0)
                {
                    DifficultyCounter += (int)(arenaCount * DifficultyCounter * Math.Pow(2.0, (double)numShrines));
                }
                else
                {
                    DifficultyCounter += arenaCount * numShrines;
                }

                controller.baseMonsterCredit += extraMonsterCredits * DifficultyCounter;
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
            GameObject[] obj = UnityEngine.Object.FindObjectsOfType<GameObject>();
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
                GameObject portal = UnityEngine.Object.Instantiate(CyAn_Rnd.raidPortalPrefab, new Vector3(281.10f, -446.82f, -126.10f), new Quaternion(0.00000f, -0.73274f, 0.00000f, 0.68051f));
                CyAn_Rnd.SyncObject(portal);
            }
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

        public static void RecieveData(CyAn_Network data)
        {
            float cellSize = data.CellZoneSize;
            recievedSize = cellSize;
        }
    }
}
