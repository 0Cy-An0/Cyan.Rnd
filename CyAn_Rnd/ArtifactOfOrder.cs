using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using UnityEngine;

namespace CyAn_Rnd
{
    public class ArtifactOfOrder : CyAn_RndArtifactBase
    {
        public override string ArtifactName => "Artifact of Order";

        public override string ArtifactLangTokenName => "ORDER";

        public override string ArtifactDescription => "forces only one different item per tier";
        public override Sprite ArtifactEnabledIcon => CyAn_Rnd.LoadEmbeddedSprite("CyAn_Rnd.Resources.order_enabled.png");

        public override Sprite ArtifactDisabledIcon => CyAn_Rnd.LoadEmbeddedSprite("CyAn_Rnd.Resources.order_disabled.png");

        public static Dictionary<ItemTier, ItemIndex> tierToItemMap = new();

        public static EquipmentIndex allowedEquipment = EquipmentIndex.None;

        private bool isEnforcingRestrictions = false;

        public static List<PickupIndex> originalTier1DropList;
        public static List<PickupIndex> originalTier2DropList;
        public static List<PickupIndex> originalTier3DropList;
        public static List<PickupIndex> originalBossDropList;
        public static List<PickupIndex> originalLunarDropList;
        public static List<PickupIndex> originalVoidTier1DropList;
        public static List<PickupIndex> originalVoidTier2DropList;
        public static List<PickupIndex> originalVoidTier3DropList;
        public static List<PickupIndex> originalVoidBossDropList;
        public static bool orderActive = false;

        public List<PickupIndex> originalEquipmentDropList;
        public List<PickupIndex> originalLunarEquipmentDropList;

        public PickupDropTable MonsterDropTable;

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

        private void SaveOriginalDropLists(Run run)
        {
            originalTier1DropList = new List<PickupIndex>(run.availableTier1DropList);
            originalTier2DropList = new List<PickupIndex>(run.availableTier2DropList);
            originalTier3DropList = new List<PickupIndex>(run.availableTier3DropList);
            originalBossDropList = new List<PickupIndex>(run.availableBossDropList);
            originalLunarDropList = new List<PickupIndex>(run.availableLunarItemDropList);
            originalVoidTier1DropList = new List<PickupIndex>(run.availableVoidTier1DropList);
            originalVoidTier2DropList = new List<PickupIndex>(run.availableVoidTier2DropList);
            originalVoidTier3DropList = new List<PickupIndex>(run.availableVoidTier3DropList);
            originalVoidBossDropList = new List<PickupIndex>(run.availableVoidBossDropList);

            originalEquipmentDropList = new List<PickupIndex>(run.availableEquipmentDropList);
            originalLunarEquipmentDropList = new List<PickupIndex>(run.availableLunarEquipmentDropList);
        }

        private void ModifyItemDropTables(Run run)
        {
            if (!ArtifactEnabled)
            {
                orderActive = false;
                return;
            }
            orderActive = true;

            SaveOriginalDropLists(run);

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

            List<PickupIndex> equipmentPickups = new(run.availableEquipmentDropList);
            equipmentPickups.AddRange(run.availableLunarEquipmentDropList);

            if (!CyAn_Rnd.wasLoaded)
            {
                tierToItemMap.Clear();

                // Choose one item per tier at random and store
                foreach (var (tier, dropList) in tierDropListMap)
                {
                    if (dropList.Count == 0) continue;

                    PickupIndex chosen = dropList[UnityEngine.Random.Range(0, dropList.Count)];
                    PickupDef chosenDef = PickupCatalog.GetPickupDef(chosen);

                    if (chosenDef.itemIndex != ItemIndex.None)
                    {
                        tierToItemMap[tier] = chosenDef.itemIndex;
                        Log.Info($"Chosen allowed Item for Tier {tier}: {Language.GetString(chosenDef.nameToken)}");
                    }
                }

                // Choose one equipment at random if available
                if (equipmentPickups.Count > 0)
                {
                    PickupIndex chosenEquipPickup = equipmentPickups[UnityEngine.Random.Range(0, equipmentPickups.Count)];
                    allowedEquipment = PickupCatalog.GetPickupDef(chosenEquipPickup).equipmentIndex;
                    Log.Info($"Chosen allowed equipment: {Language.GetString(EquipmentCatalog.GetEquipmentDef(allowedEquipment).nameToken)}");
                }
                else
                {
                    allowedEquipment = EquipmentIndex.None;
                }
            }

            // Disable unchosen items
            foreach (var dropList in tierDropListMap.Values)
            {
                foreach (var pickup in dropList)
                {
                    PickupDef def = PickupCatalog.GetPickupDef(pickup);
                    //could use droplist.Key instead of def.itemTier but this is probably a bit extra safe so ill do this
                    if (!tierToItemMap.TryGetValue(def.itemTier, out var allowedItem) || def.itemIndex != allowedItem)
                    {
                        run.DisablePickupDrop(pickup);
                    }
                }
            }

            // Disable unchosen equipment
            foreach (var pickup in equipmentPickups)
            {
                PickupDef def = PickupCatalog.GetPickupDef(pickup);
                if (def.equipmentIndex != allowedEquipment)
                {
                    run.DisablePickupDrop(pickup);
                }
            }
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
            if (!ArtifactEnabled || !inventory.gameObject.name.Contains("Player") || inventory == null || Run.instance == null || isEnforcingRestrictions || CyAn_Rnd.wasLoaded)
            {
                return;
            }
            isEnforcingRestrictions = true;

            //For some reason, im very mad about; if its loaded via ProperSave the older foreach loop over just the inventory is just the same useless info; so well have to do this; i hope it doesnt lag (but it shouldnt)
            foreach (ItemIndex itemIndex in ItemCatalog.allItems)
            {
                int count = inventory.GetItemCount(itemIndex);
                if (count <= 0) continue;

                ItemDef itemDef = ItemCatalog.GetItemDef(itemIndex);
                if (itemDef == null || itemDef.tier == ItemTier.NoTier) continue;

                if (tierToItemMap.ContainsValue(itemIndex)) continue;

                if (tierToItemMap.TryGetValue(itemDef.tier, out ItemIndex replacementItem))
                {
                    inventory.RemoveItem(itemIndex, count);
                    inventory.GiveItem(replacementItem, count);
                    Log.Info($"Replaced {Language.GetString(itemDef.nameToken)} with {Language.GetString(ItemCatalog.GetItemDef(replacementItem).nameToken)}");
                }
                else
                {
                    Log.Warning($"No replacement found for item tier: {itemDef.tier} (item: {itemDef.nameToken})");
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
