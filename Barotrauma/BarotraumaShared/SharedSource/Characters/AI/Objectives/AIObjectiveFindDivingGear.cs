﻿using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveFindDivingGear : AIObjective
    {
        public override Identifier Identifier { get; set; } = "find diving gear".ToIdentifier();
        public override string DebugTag => $"{Identifier} ({gearTag})";
        public override bool ForceRun => true;
        public override bool KeepDivingGearOn => true;
        public override bool AbandonWhenCannotCompleteSubjectives => false;
        public override bool AllowWhileHandcuffed => false;

        private readonly Identifier gearTag;

        private AIObjectiveGetItem getDivingGear;
        private AIObjectiveContainItem getOxygen;
        private Item targetItem;
        private int? oxygenSourceSlotIndex;

        public const float MIN_OXYGEN = 10;

        protected override bool CheckObjectiveSpecific() => 
            targetItem != null && character.HasEquippedItem(targetItem, slotType: InvSlotType.OuterClothes | InvSlotType.InnerClothes | InvSlotType.Head);

        public AIObjectiveFindDivingGear(Character character, bool needsDivingSuit, AIObjectiveManager objectiveManager, float priorityModifier = 1) : base(character, objectiveManager, priorityModifier)
        {
            gearTag = needsDivingSuit ? Tags.HeavyDivingGear : Tags.LightDivingGear;
        }

        protected override void Act(float deltaTime)
        {
            TrySetTargetItem(character.Inventory.FindItem(it => it.HasTag(gearTag) && IsSuitablePressureProtection(it, gearTag, character), true));
            if (targetItem == null && gearTag == Tags.LightDivingGear)
            {
                TrySetTargetItem(character.Inventory.FindItem(
                    it => it.HasTag(Tags.HeavyDivingGear) && IsSuitablePressureProtection(it, Tags.HeavyDivingGear, character), recursive: true));
            }
            if (targetItem == null || 
                !character.HasEquippedItem(targetItem, slotType: InvSlotType.OuterClothes | InvSlotType.InnerClothes | InvSlotType.Head) && 
                targetItem.ContainedItems.Any(it => IsSuitableContainedOxygenSource(it)))
            {
                bool mustFindMorePressureProtection =
                    !objectiveManager.FailedToFindDivingGearForDepth &&
                    character.Inventory.FindItem(
                        it => it.HasTag(Tags.HeavyDivingGear) && !IsSuitablePressureProtection(it, Tags.HeavyDivingGear, character), recursive: true) != null;
                TryAddSubObjective(ref getDivingGear, () =>
                {
                    if (targetItem == null && character.IsOnPlayerTeam)
                    {
                        character.Speak(TextManager.Get("DialogGetDivingGear").Value, null, 0.0f, "getdivinggear".ToIdentifier(), 30.0f);
                    }
                    var getItemObjective = new AIObjectiveGetItem(character, gearTag, objectiveManager, equip: true)
                    {
                        AllowStealing = HumanAIController.NeedsDivingGear(character.CurrentHull, out _),
                        AllowToFindDivingGear = false,
                        AllowDangerousPressure = true,
                        EquipSlotType = InvSlotType.OuterClothes | InvSlotType.InnerClothes | InvSlotType.Head,
                        Wear = true
                    };
                    if (gearTag == Tags.HeavyDivingGear)
                    {
                        if (mustFindMorePressureProtection)
                        {
                            //if we're looking for a suit specifically because the current suit isn't enough, 
                            //let's ignore unsuitable suits altogether...
                            getItemObjective.ItemFilter = it => IsSuitablePressureProtection(it, gearTag, character);
                        }
                        else
                        {
                            //...Otherwise it's fine to give a very small priority
                            //to inadequate suits (a suit not adequate for the depth is better than no suit)
                            getItemObjective.GetItemPriority = it => IsSuitablePressureProtection(it, gearTag, character) ? 1000.0f : 1.0f;
                        }
                        getItemObjective.GetItemPriority = it =>
                        {
                            if (IsSuitablePressureProtection(it, gearTag, character))
                            {
                                return 1000.0f;
                            }
                            else
                            {
                                //if we're looking for a suit specifically because the current suit isn't enough, 
                                //let's ignore unsuitable suits altogether. Otherwise it's fine to give a very small priority
                                //to inadequate suits (a suit not adequate for the depth is better than no suit)
                                return mustFindMorePressureProtection ? 0.0f : 1.0f;
                            }
                        };
                    }
                    return getItemObjective;
                }, 
                onAbandon: () =>
                {
                    if (mustFindMorePressureProtection) { objectiveManager.FailedToFindDivingGearForDepth = true; }
                    Abandon = true;
                },
                onCompleted: () =>
                {
                    RemoveSubObjective(ref getDivingGear);
                    if (gearTag == Tags.HeavyDivingGear && HumanAIController.HasItem(character, Tags.LightDivingGear, out IEnumerable<Item> masks, requireEquipped: true))
                    {
                        foreach (Item mask in masks)
                        {
                            if (mask != targetItem)
                            {
                                character.Inventory.TryPutItem(mask, character, CharacterInventory.AnySlot);
                            }
                        }
                    }
                });
            }
            else
            {
                float min = GetMinOxygen(character);
                if (targetItem.OwnInventory != null && targetItem.OwnInventory.AllItems.None(it => IsSuitableContainedOxygenSource(it)))
                {
                    TryAddSubObjective(ref getOxygen, () =>
                    {
                        if (character.IsOnPlayerTeam)
                        {
                            if (HumanAIController.HasItem(character, Tags.OxygenSource, out _, conditionPercentage: min))
                            {
                                character.Speak(TextManager.Get("dialogswappingoxygentank").Value, null, 0, "swappingoxygentank".ToIdentifier(), 30.0f);
                                if (character.Inventory.FindAllItems(i => i.HasTag(Tags.OxygenSource) && i.Condition > min, recursive: true).Count == 1)
                                {
                                    character.Speak(TextManager.Get("dialoglastoxygentank").Value, null, 0.0f, "dialoglastoxygentank".ToIdentifier(), 30.0f);
                                }
                            }
                            else
                            {
                                character.Speak(TextManager.Get("DialogGetOxygenTank").Value, null, 0, "getoxygentank".ToIdentifier(), 30.0f);
                            }
                        }
                        var container = targetItem.GetComponent<ItemContainer>();                        
                        var objective = new AIObjectiveContainItem(character, Tags.OxygenSource, container, objectiveManager, spawnItemIfNotFound: character.TeamID == CharacterTeamType.FriendlyNPC)
                        {
                            AllowToFindDivingGear = false,
                            AllowDangerousPressure = true,
                            ConditionLevel = MIN_OXYGEN,
                            RemoveExistingWhenNecessary = true,
                            TargetSlot = oxygenSourceSlotIndex
                        };
                        if (container.HasSubContainers)
                        {
                            objective.TargetSlot = container.FindSuitableSubContainerIndex(Tags.OxygenSource);
                        }
                        // Only remove the oxygen source being replaced
                        objective.RemoveExistingPredicate = i => objective.IsInTargetSlot(i);
                        return objective;
                    },
                    onAbandon: () =>
                    {
                        getOxygen = null;
                        int remainingTanks = ReportOxygenTankCount();
                        // Try to seek any oxygen sources, even if they have minimal amount of oxygen.
                        TryAddSubObjective(ref getOxygen, () =>
                        {
                            return new AIObjectiveContainItem(character, Tags.OxygenSource, targetItem.GetComponent<ItemContainer>(), objectiveManager, spawnItemIfNotFound: character.TeamID == CharacterTeamType.FriendlyNPC)
                            {
                                AllowToFindDivingGear = false,
                                AllowDangerousPressure = true,
                                RemoveExisting = true
                            };
                        },
                        onAbandon: () =>
                        {
                            Abandon = true;
                            if (remainingTanks > 0 && !HumanAIController.HasItem(character, Tags.OxygenSource, out _, conditionPercentage: 0.01f))
                            {
                                character.Speak(TextManager.Get("dialogcantfindtoxygen").Value, null, 0, "cantfindoxygen".ToIdentifier(), 30.0f);
                            }
                        },
                        onCompleted: () => RemoveSubObjective(ref getOxygen));
                    },
                    onCompleted: () =>
                    {
                        RemoveSubObjective(ref getOxygen);
                        ReportOxygenTankCount();
                    });

                    int ReportOxygenTankCount()
                    {
                        if (character.Submarine != Submarine.MainSub) { return 1; }
                        int remainingOxygenTanks = Submarine.MainSub.GetItems(false).Count(i => i.HasTag(Tags.OxygenSource) && i.Condition > 1);
                        if (remainingOxygenTanks == 0)
                        {
                            character.Speak(TextManager.Get("DialogOutOfOxygenTanks").Value, null, 0.0f, "outofoxygentanks".ToIdentifier(), 30.0f);
                        }
                        else if (remainingOxygenTanks < 10)
                        {
                            character.Speak(TextManager.Get("DialogLowOnOxygenTanks").Value, null, 0.0f, "lowonoxygentanks".ToIdentifier(), 30.0f);
                        }
                        return remainingOxygenTanks;
                    }
                }
            }
        }

        public static bool IsSuitablePressureProtection(Item item, Identifier tag, Character character)
        {
            if (tag == Tags.HeavyDivingGear)
            {
                float realWorldDepth = Level.Loaded?.GetRealWorldDepth(character.WorldPosition.Y) ?? 0.0f;
                if (item.GetComponent<Wearable>() is not { } wearable || wearable.PressureProtection < realWorldDepth + Steering.PressureWarningThreshold)
                {
                    return false;
                }
            }
            return true;
        }


        private bool IsSuitableContainedOxygenSource(Item item)
        {
            return 
                item != null &&
                item.HasTag(Tags.OxygenSource) && 
                item.Condition > 0 && 
                (oxygenSourceSlotIndex == null || item.ParentInventory.IsInSlot(item, oxygenSourceSlotIndex.Value));
        }

        private void TrySetTargetItem(Item item)
        {
            if (targetItem == item) { return; }
            targetItem = item;
            if (targetItem != null)
            {
                oxygenSourceSlotIndex = targetItem.GetComponent<ItemContainer>()?.FindSuitableSubContainerIndex(Tags.OxygenSource);
            }
            else
            {
                oxygenSourceSlotIndex = null;
            }
        }

        public override void Reset()
        {
            base.Reset();
            getDivingGear = null;
            getOxygen = null;
            targetItem = null;
            oxygenSourceSlotIndex = null;
        }

        public static float GetMinOxygen(Character character)
        {
            // Seek oxygen that has at least 10% condition left, if we are inside a friendly sub.
            // The margin helps us to survive, because we might need some oxygen before we can find more oxygen.
            // When we are venturing outside of our sub, let's just suppose that we have enough oxygen with us and optimize it so that we don't keep switching off half used tanks.
            float min = 0.01f;
            float minOxygen = character.IsInFriendlySub ? MIN_OXYGEN : min;
            if (minOxygen > min && character.Inventory.AllItems.Any(i => i.HasTag(Tags.OxygenSource) && i.ConditionPercentage >= minOxygen))
            {
                // There's a valid oxygen tank in the inventory -> no need to swap the tank too early.
                minOxygen = min;
            }
            return minOxygen;
        }
    }
}
