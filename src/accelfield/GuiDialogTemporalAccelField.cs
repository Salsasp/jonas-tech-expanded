using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace JonasTechExpanded
{
    /// <summary>
    /// Fuel slot, status readout, one button per field size and speed, a power switch and a visibility toggle. Buttons only
    /// send a request; their state follows what the server syncs back through <see cref="Refresh"/>.
    /// </summary>
    public class GuiDialogTemporalAccelField : GuiDialogBlockEntity
    {
        private const double ButtonWidth = 100;
        private const double ButtonHeight = 25;
        private const double ButtonGap = 5;

        private readonly BlockEntityTemporalAccelField be;

        public GuiDialogTemporalAccelField(string title, InventoryBase inventory, BlockPos pos, ICoreClientAPI capi, BlockEntityTemporalAccelField be)
            : base(title, inventory, pos, capi)
        {
            if (IsDuplicate) return;
            this.be = be;

            capi.World.Player.InventoryManager.OpenInventory(Inventory);
            compose();
        }

        private void compose()
        {
            var tunables = be.Tunables;
            int columns = System.Math.Max(tunables.Sizes.Length, tunables.Speeds.Length);
            double width = columns * (ButtonWidth + ButtonGap) - ButtonGap;

            ElementBounds slotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 30, 1, 1);
            ElementBounds infoBounds = ElementBounds.Fixed(65, 30, width - 65, 100);
            ElementBounds sizeLabelBounds = ElementBounds.Fixed(0, 140, width, 20);
            double sizeRow = 165;
            ElementBounds speedLabelBounds = ElementBounds.Fixed(0, 200, width, 20);
            double speedRow = 225;
            double halfWidth = (width - ButtonGap) / 2;
            ElementBounds powerBounds = ElementBounds.Fixed(0, 265, halfWidth, ButtonHeight);
            ElementBounds visibleBounds = ElementBounds.Fixed(halfWidth + ButtonGap, 265, halfWidth, ButtonHeight);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(slotBounds, infoBounds, sizeLabelBounds, speedLabelBounds, powerBounds, visibleBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);

            ClearComposers();
            var composer = capi.Gui
                .CreateCompo("temporalaccelfield" + BlockEntityPosition, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(DialogTitle, () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddItemSlotGrid(Inventory, sendInvPacket, 1, new[] { 0 }, slotBounds, "fuelSlot")
                    .AddDynamicText("", CairoFont.WhiteSmallText(), infoBounds, "info")
                    .AddStaticText(Lang.Get("jonastechexpanded:accelfield-gui-size"), CairoFont.WhiteSmallText(), sizeLabelBounds)
                    .AddStaticText(Lang.Get("jonastechexpanded:accelfield-gui-speed"), CairoFont.WhiteSmallText(), speedLabelBounds);

            for (int i = 0; i < tunables.Sizes.Length; i++)
            {
                int index = i;
                int size = tunables.Sizes[i];
                composer.AddToggleButton(Lang.Get("jonastechexpanded:accelfield-gui-sizeoption", size), CairoFont.ButtonText(),
                    _ => select(BlockEntityTemporalAccelField.PacketSetSize, index), buttonBounds(i, sizeRow), "size" + i);
            }

            for (int i = 0; i < tunables.Speeds.Length; i++)
            {
                int index = i;
                composer.AddToggleButton(Lang.Get("jonastechexpanded:accelfield-gui-speedoption", tunables.Speeds[i]), CairoFont.ButtonText(),
                    _ => select(BlockEntityTemporalAccelField.PacketSetSpeed, index), buttonBounds(i, speedRow), "speed" + i);
            }

            composer.AddToggleButton(Lang.Get("jonastechexpanded:accelfield-gui-power"), CairoFont.ButtonText(),
                on => be.SendSetting(BlockEntityTemporalAccelField.PacketSetEnabled, on ? 1 : 0), powerBounds, "power");

            composer.AddToggleButton(Lang.Get("jonastechexpanded:accelfield-gui-visible"), CairoFont.ButtonText(),
                on => be.SendSetting(BlockEntityTemporalAccelField.PacketSetVisible, on ? 1 : 0), visibleBounds, "visible");

            SingleComposer = composer.EndChildElements().Compose();
            Refresh();
        }

        private static ElementBounds buttonBounds(int column, double y)
        {
            return ElementBounds.Fixed(column * (ButtonWidth + ButtonGap), y, ButtonWidth, ButtonHeight);
        }

        private void select(int packetId, int index)
        {
            be.SendSetting(packetId, index);
            // Keep the buttons acting as a radio group until the server's answer arrives.
            Refresh();
        }

        /// <summary>Redraws the readout and button states from the block entity's current (synced) values.</summary>
        public void Refresh()
        {
            if (SingleComposer == null || be == null) return;

            var tunables = be.Tunables;
            for (int i = 0; i < tunables.Sizes.Length; i++) SingleComposer.GetToggleButton("size" + i)?.SetValue(i == be.SizeIndex);
            for (int i = 0; i < tunables.Speeds.Length; i++) SingleComposer.GetToggleButton("speed" + i)?.SetValue(i == be.SpeedIndex);
            SingleComposer.GetToggleButton("visible")?.SetValue(be.FieldVisible);
            SingleComposer.GetToggleButton("power")?.SetValue(be.Enabled);

            string status = be.IsActive ? "running" : be.Enabled ? "nofuel" : "off";

            string info = string.Join("\n", new[]
            {
                Lang.Get("jonastechexpanded:accelfield-gui-status-" + status),
                Lang.Get("jonastechexpanded:accelfield-gui-currentsize", be.Size),
                Lang.Get("jonastechexpanded:accelfield-gui-currentspeed", be.Speed),
                Lang.Get("jonastechexpanded:accelfield-gui-remaining", be.HoursRemaining),
                Lang.Get("jonastechexpanded:accelfield-gui-pergear", be.HoursPerGear)
            });
            SingleComposer.GetDynamicText("info")?.SetNewText(info);
        }

        private void onSlotModified(int slotId)
        {
            // Called mid inventory update; redrawing right away can throw.
            capi.Event.EnqueueMainThreadTask(Refresh, "accelfieldrefresh");
        }

        private void sendInvPacket(object packet)
        {
            capi.Network.SendBlockEntityPacket(BlockEntityPosition.X, BlockEntityPosition.Y, BlockEntityPosition.Z, packet);
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            Inventory.SlotModified += onSlotModified;
        }

        public override void OnGuiClosed()
        {
            Inventory.SlotModified -= onSlotModified;
            SingleComposer?.GetSlotGrid("fuelSlot")?.OnGuiClosed(capi);
            base.OnGuiClosed();
        }
    }
}
