using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;
using UnityEngine.Networking;
using static Rewired.Utils.Classes.Utility.ObjectInstanceTracker;

namespace CyAn_Rnd
{
    public class ArtifactOfOrder : CyAn_RndArtifactBase
    {
        public override string ArtifactName => "Artifact of Order";

        public override string ArtifactLangTokenName => "ORDER";

        public override string ArtifactDescription => "forces only one different item per tier";
        public override Sprite ArtifactEnabledIcon => CyAn_Rnd.LoadEmbeddedSprite("CyAn_Rnd.Resources.order_enabled.png");

        public override Sprite ArtifactDisabledIcon => CyAn_Rnd.LoadEmbeddedSprite("CyAn_Rnd.Resources.order_disabled.png");

        private Dictionary<ItemTier, ItemIndex> tierToItemMap = new();

        private EquipmentIndex allowedEquipment = EquipmentIndex.None;


        private bool isEnforcingRestrictions = false;

        public override void Init(ConfigFile config)
        {
            CreateLang();
            CreateArtifact();
            Hooks();
        }

        public override void Hooks()
        {
            Run.onRunStartGlobal += ModifyItemDropTables;
            SceneDirector.onGenerateInteractableCardSelection += RemovePrintersAndScrappers;
            Inventory.onInventoryChangedGlobal += EnforceArtifactItemRestrictions;
        }

        private void PopulateTierToItemMap()
        {
            tierToItemMap.Clear();

            if (Run.instance == null)
                return;

            Dictionary<ItemTier, List<PickupIndex>> tierDropListMap = new()
            {
                { ItemTier.Tier1, new List<PickupIndex>(Run.instance.availableTier1DropList) },
                { ItemTier.Tier2, new List<PickupIndex>(Run.instance.availableTier2DropList) },
                { ItemTier.Tier3, new List<PickupIndex>(Run.instance.availableTier3DropList) },
                { ItemTier.Lunar, new List<PickupIndex>(Run.instance.availableLunarItemDropList) },
                { ItemTier.Boss, new List<PickupIndex>(Run.instance.availableBossDropList) },
                { ItemTier.VoidTier1, new List<PickupIndex>(Run.instance.availableVoidTier1DropList) },
                { ItemTier.VoidTier2, new List<PickupIndex>(Run.instance.availableVoidTier2DropList) },
                { ItemTier.VoidTier3, new List<PickupIndex>(Run.instance.availableVoidTier3DropList) },
                { ItemTier.VoidBoss, new List<PickupIndex>(Run.instance.availableVoidBossDropList) }
            };

            foreach (var kvp in tierDropListMap)
            {
                List<PickupIndex> dropList = kvp.Value;
                if (dropList.Count == 0) continue;

                // Pick one item at random from the tier's drop list
                PickupIndex chosenPickup = dropList[UnityEngine.Random.Range(0, dropList.Count)];
                ItemIndex itemIndex = PickupCatalog.GetPickupDef(chosenPickup).itemIndex;

                if (itemIndex != ItemIndex.None)
                {
                    tierToItemMap[kvp.Key] = itemIndex;
                    Log.Info($"Chosen allowed Item for Tier {kvp.Key}: {chosenPickup.pickupDef}");
                }
            }

            List<PickupIndex> equipmentPickups = new(Run.instance.availableEquipmentDropList);
            equipmentPickups.AddRange(Run.instance.availableLunarEquipmentDropList);

            if (equipmentPickups.Count > 0)
            {
                PickupIndex chosenEquipment = equipmentPickups[UnityEngine.Random.Range(0, equipmentPickups.Count)];
                EquipmentIndex equipmentIndex = PickupCatalog.GetPickupDef(chosenEquipment).equipmentIndex;

                if (equipmentIndex != EquipmentIndex.None)
                {
                    allowedEquipment = equipmentIndex;
                    Log.Info($"Chosen allowed equipment: {EquipmentCatalog.GetEquipmentDef(allowedEquipment).name}");
                }
            }
            else
            {
                allowedEquipment = EquipmentIndex.None;
                //Log.Info("No available equipment to choose from.");
            }
        }


        private void ModifyItemDropTables(Run run)
        {
            if (!ArtifactEnabled) return;

            // --- Handle Items: Keep only one per tier ---

            Dictionary<ItemTier, List<PickupIndex>> tierDropListMap = new()
            {
                { ItemTier.Tier1, new List<PickupIndex>(run.availableTier1DropList) },
                { ItemTier.Tier2, new List<PickupIndex>(run.availableTier2DropList) },
                { ItemTier.Tier3, new List<PickupIndex>(run.availableTier3DropList) },
                { ItemTier.Lunar, new List<PickupIndex>(run.availableLunarItemDropList) },
                { ItemTier.Boss, new List<PickupIndex>(run.availableBossDropList) },
                { ItemTier.VoidTier1, new List<PickupIndex>(run.availableVoidTier1DropList) },
                { ItemTier.VoidTier2, new List<PickupIndex>(run.availableVoidTier2DropList) },
                { ItemTier.VoidTier3, new List<PickupIndex>(run.availableVoidTier3DropList) },
                { ItemTier.VoidBoss, new List<PickupIndex>(run.availableVoidBossDropList) }
            };

            foreach (var kvp in tierDropListMap)
            {
                List<PickupIndex> dropList = kvp.Value;
                if (dropList.Count <= 1) continue;

                // Pick one to keep
                PickupIndex chosenPickup = dropList[UnityEngine.Random.Range(0, dropList.Count)];

                // Disable all others
                foreach (var pickup in dropList)
                {
                    if (pickup != chosenPickup)
                    {
                        run.DisablePickupDrop(pickup);
                    }
                }
            }

            // --- Handle Equipment: Pick 1 from either normal or lunar ---

            List<PickupIndex> equipmentPickups = new(run.availableEquipmentDropList);
            equipmentPickups.AddRange(run.availableLunarEquipmentDropList);

            if (equipmentPickups.Count > 1)
            {
                PickupIndex chosenPickup = equipmentPickups[UnityEngine.Random.Range(0, equipmentPickups.Count)];

                foreach (var pickup in equipmentPickups)
                {
                    if (pickup != chosenPickup)
                    {
                        run.DisablePickupDrop(pickup);
                    }
                }
            }

            PopulateTierToItemMap();
        }

        private void RemovePrintersAndScrappers(SceneDirector director, DirectorCardCategorySelection dccs)
        {
            if (!ArtifactEnabled) return;

            // Perform removal with debug on each card
            dccs.RemoveCardsThatFailFilter(card =>
            {
                bool fail = true;
                if (card.spawnCard != null)
                {
                    string scName = card.spawnCard.name;
                    if (scName.StartsWith("iscDuplicator") || scName == "iscScrapper")
                    {
                        fail = false;
                    }
                }

                /*if (fail)
                {
                    Log.Info($"[Artifact of Order] Keeping card: spawnCard.name = \"{card.spawnCard?.name}\"");
                }
                else
                {
                    Log.Info($"[Artifact of Order] Removing card: spawnCard.name = \"{card.spawnCard?.name}\"");
                }*/

                return fail;
            });
        }

        private void EnforceArtifactItemRestrictions(Inventory inventory)
        {
            if (!ArtifactEnabled || !inventory.gameObject.name.Contains("Player") || inventory == null || Run.instance == null || isEnforcingRestrictions)
            {
                return;
            }

            isEnforcingRestrictions = true;
            // Populate if empty
            if (tierToItemMap.Count == 0)
            {
                PopulateTierToItemMap();
            }

            foreach (ItemIndex itemIndex in inventory.itemStacks.ToArray())
            {
                ItemDef itemDef = ItemCatalog.GetItemDef(itemIndex);
                if (itemDef == null || itemDef.tier == ItemTier.NoTier) continue;
                
                if (tierToItemMap.ContainsValue(itemIndex))
                {
                    continue;
                }

                if (tierToItemMap.TryGetValue(itemDef.tier, out ItemIndex replacementItem))
                {
                    int count = inventory.GetItemCount(itemIndex);
                    if (count > 0)
                    {
                        inventory.RemoveItem(itemIndex, count);
                        inventory.GiveItem(replacementItem, count);
                    }
                }
                else
                {
                    Log.Info($"failed to get info: {itemDef} , {itemDef.tier}");
                }
            }

            if (allowedEquipment != EquipmentIndex.None)
            {
                int slotCount = inventory.GetEquipmentSlotCount();

                for (uint slot = 0; slot < slotCount; slot++)
                {
                    EquipmentDef currentEquipmentDef = EquipmentCatalog.GetEquipmentDef(inventory.GetEquipment(slot).equipmentIndex);

                    if (currentEquipmentDef != null && inventory.GetEquipment(slot).equipmentIndex != allowedEquipment)
                    {
                        Log.Info($"Replacing equipment: {currentEquipmentDef.name} with {EquipmentCatalog.GetEquipmentDef(allowedEquipment).name}");
                        inventory.SetEquipmentIndex(allowedEquipment);
                    }
                }
            }

            isEnforcingRestrictions = false;
        }

    }
}
