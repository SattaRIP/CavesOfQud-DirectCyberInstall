using System;
using System.Collections.Generic;
using XRL;
using XRL.Core;
using XRL.UI;
using XRL.World;
using XRL.World.Parts;
using XRL.World.Anatomy;

namespace XRL.World.Parts
{
    // Initialization for new games
    [PlayerMutator]
    public class DirectCyberInstall_NewGame : IPlayerMutator
    {
        public void mutate(GameObject player)
        {
            player.RequirePart<DirectCyberInstall_PlayerPart>();
        }
    }

    // Initialization for loaded saves
    [HasCallAfterGameLoadedAttribute]
    public class DirectCyberInstall_LoadGame
    {
        [CallAfterGameLoadedAttribute]
        public static void Initialize()
        {
            GameObject player = XRLCore.Core?.Game?.Player?.Body;
            if (player != null)
            {
                player.RequirePart<DirectCyberInstall_PlayerPart>();
            }
        }
    }

    // The main part that adds inventory actions to cybernetics
    [Serializable]
    public class DirectCyberInstall_PlayerPart : IPart
    {
        public override bool WantEvent(int ID, int cascade)
        {
            return base.WantEvent(ID, cascade)
                || ID == OwnerGetInventoryActionsEvent.ID
                || ID == InventoryActionEvent.ID;
        }

        public override bool HandleEvent(OwnerGetInventoryActionsEvent E)
        {
            if (E.Object == null) return base.HandleEvent(E);

            CyberneticsBaseItem cyber = E.Object.GetPart<CyberneticsBaseItem>();
            if (cyber == null) return base.HandleEvent(E);

            if (cyber.ImplantedOn == null)
            {
                // Uninstalled cybernetic - add install options
                E.AddAction(
                    Name: "InstallAuto",
                    Display: "install cybernetic ({{W|a}}utomatic)",
                    Command: "InstallCyberneticAuto",
                    Key: 'a',
                    FireOnActor: true,
                    Default: -1
                );
                E.AddAction(
                    Name: "InstallManual",
                    Display: "install cybernetic ({{W|m}}anual)",
                    Command: "InstallCyberneticManual",
                    Key: 'm',
                    FireOnActor: true,
                    Default: -1
                );
            }
            else
            {
                // Installed cybernetic - add uninstall option
                E.AddAction(
                    Name: "Uninstall",
                    Display: "{{W|u}}ninstall cybernetic",
                    Command: "UninstallCybernetic",
                    Key: 'u',
                    FireOnActor: true,
                    Default: -1
                );
            }

            return base.HandleEvent(E);
        }

        public override bool HandleEvent(InventoryActionEvent E)
        {
            if (E.Item == null) return base.HandleEvent(E);

            CyberneticsBaseItem cyber = E.Item.GetPart<CyberneticsBaseItem>();
            if (cyber == null) return base.HandleEvent(E);

            GameObject player = ParentObject;
            if (player == null) return base.HandleEvent(E);

            if (E.Command == "InstallCyberneticAuto")
            {
                InstallAutomatic(cyber, E.Item, player);
                E.RequestInterfaceExit();
                return base.HandleEvent(E);
            }
            else if (E.Command == "InstallCyberneticManual")
            {
                InstallManual(cyber, E.Item, player);
                E.RequestInterfaceExit();
                return base.HandleEvent(E);
            }
            else if (E.Command == "UninstallCybernetic")
            {
                Uninstall(cyber, E.Item, player);
                E.RequestInterfaceExit();
                return base.HandleEvent(E);
            }

            return base.HandleEvent(E);
        }

        private void InstallAutomatic(CyberneticsBaseItem cyber, GameObject item, GameObject player)
        {
            if (cyber.ImplantedOn != null)
            {
                Popup.Show("Already installed.");
                return;
            }

            // Find first compatible empty cybernetic slot
            // Prioritize dedicated ImplantSlot* types over regular equipment slots
            BodyPart targetSlot = null;
            string slots = cyber.Slots;

            if (!string.IsNullOrEmpty(slots))
            {
                string[] slotArray = slots.Split(',');

                // Pass 1: ImplantSlot* types only (dedicated cybernetic slots)
                foreach (string slot in slotArray)
                {
                    string trimmedSlot = slot.Trim();
                    if (!trimmedSlot.StartsWith("ImplantSlot")) continue;
                    foreach (BodyPart part in player.Body.GetParts())
                    {
                        if (part.Type == trimmedSlot && part.Cybernetics == null)
                        {
                            targetSlot = part;
                            break;
                        }
                    }
                    if (targetSlot != null) break;
                }

                // Pass 2: Any other compatible slot types
                if (targetSlot == null)
                {
                    foreach (string slot in slotArray)
                    {
                        string trimmedSlot = slot.Trim();
                        if (trimmedSlot.StartsWith("ImplantSlot")) continue;
                        foreach (BodyPart part in player.Body.GetParts())
                        {
                            if (part.Type == trimmedSlot && part.Cybernetics == null)
                            {
                                targetSlot = part;
                                break;
                            }
                        }
                        if (targetSlot != null) break;
                    }
                }
            }

            if (targetSlot != null)
            {
                targetSlot.Implant(item);
                if (player.IsPlayer())
                {
                    XRL.Messages.MessageQueue.AddPlayerMessage("{{G|Installed " + item.DisplayName + " to " + targetSlot.GetOrdinalName() + ".}}");
                }
            }
            else
            {
                Popup.Show("{{R|No empty compatible slot available.}}");
            }
        }

        private void InstallManual(CyberneticsBaseItem cyber, GameObject item, GameObject player)
        {
            if (cyber.ImplantedOn != null)
            {
                Popup.Show("Already installed.");
                return;
            }

            // Find all compatible empty cybernetic slots
            // ImplantSlot* types listed first, then regular slots
            List<BodyPart> implantSlots = new List<BodyPart>();
            List<BodyPart> regularSlots = new List<BodyPart>();
            string slots = cyber.Slots;

            if (!string.IsNullOrEmpty(slots))
            {
                string[] slotArray = slots.Split(',');
                foreach (string slot in slotArray)
                {
                    string trimmedSlot = slot.Trim();
                    foreach (BodyPart part in player.Body.GetParts())
                    {
                        if (part.Type == trimmedSlot && part.Cybernetics == null)
                        {
                            if (trimmedSlot.StartsWith("ImplantSlot"))
                                implantSlots.Add(part);
                            else
                                regularSlots.Add(part);
                        }
                    }
                }
            }
            List<BodyPart> compatibleSlots = new List<BodyPart>(implantSlots);
            compatibleSlots.AddRange(regularSlots);

            if (compatibleSlots.Count == 0)
            {
                Popup.Show("{{R|No empty compatible slots available.}}");
                return;
            }

            // Build menu options
            List<string> options = new List<string>();
            foreach (BodyPart part in compatibleSlots)
            {
                options.Add(part.GetOrdinalName() + " (" + part.Type + ")");
            }

            // Show selection menu
            int choice = Popup.PickOption("Install to which slot?", Options: options.ToArray());

            if (choice >= 0 && choice < compatibleSlots.Count)
            {
                BodyPart targetSlot = compatibleSlots[choice];
                targetSlot.Implant(item);
                if (player.IsPlayer())
                {
                    XRL.Messages.MessageQueue.AddPlayerMessage("{{G|Installed " + item.DisplayName + " to " + targetSlot.GetOrdinalName() + ".}}");
                }
            }
        }

        private void Uninstall(CyberneticsBaseItem cyber, GameObject item, GameObject player)
        {
            if (cyber.ImplantedOn == null)
            {
                Popup.Show("Not installed.");
                return;
            }

            // Unimplant using the game's built-in method, keeping item in inventory
            item.Unimplant(MoveToInventory: false);
            if (player.IsPlayer())
            {
                XRL.Messages.MessageQueue.AddPlayerMessage("{{Y|Uninstalled " + item.DisplayName + ".}}");
            }
        }
    }
}
