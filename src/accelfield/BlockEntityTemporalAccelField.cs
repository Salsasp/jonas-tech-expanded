using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace JonasTechExpanded
{
    /// <summary>
    /// The temporal acceleration field machine: one temporal gear fuel slot, an on/off switch, a selectable field
    /// size and speed, and a fuel buffer. The machine itself has no tick; <see cref="AccelFieldModSystem"/> drives
    /// both the acceleration and <see cref="ConsumeFuel"/> from one clock.
    ///
    /// It is running while switched on with fuel in the buffer. Every change of that state goes through
    /// <see cref="OnRunningChanged"/> and the <see cref="RunningChanged"/> event on both sides, which is where
    /// animations and sounds hook in. The default client handling plays the block JSON's <c>runningAnimation</c>
    /// (needs the <c>Animatable</c> behavior) and loops its <c>runningSound</c>.
    /// </summary>
    public class BlockEntityTemporalAccelField : BlockEntityOpenableContainer
    {
        /// <summary>Item attribute on fuel items saying how many fuel units they are worth to the field.</summary>
        public const string FuelItemAttribute = "accelFieldFuelHours";

        public const int PacketSetSize = 1001;
        public const int PacketSetSpeed = 1002;
        public const int PacketSetVisible = 1003;
        public const int PacketSetEnabled = 1004;

        private readonly InventoryGeneric inventory;
        private GuiDialogTemporalAccelField clientDialog;
        private bool lastRunning;
        private ILoadedSound runningSound;

        /// <summary>
        /// Raised on both sides whenever <see cref="IsActive"/> flips: switched on/off, fuel ran out, or a gear
        /// was lit. On the client this follows the server's synced state.
        /// </summary>
        public event Action<BlockEntityTemporalAccelField, bool> RunningChanged;

        public AccelFieldTunables Tunables { get; private set; } = new AccelFieldTunables();

        public int SizeIndex { get; private set; }
        public int SpeedIndex { get; private set; }

        /// <summary>
        /// Whether the cosmetic force-field sphere is drawn (to everyone nearby) while running. Unrelated to the
        /// ctrl + right-click range preview.
        /// </summary>
        public bool FieldVisible { get; private set; }

        /// <summary>
        /// Fuel units left from the gear being burned. One unit lasts one in-game hour at the cheapest
        /// settings; <see cref="FuelPerHour"/> scales that up.
        /// </summary>
        public double FuelBuffer { get; set; }

        /// <summary>The GUI's power switch. While off, nothing is accelerated and no fuel burns.</summary>
        public bool Enabled { get; private set; } = true;

        /// <summary>Running: switched on with fuel in the buffer. Only running fields accelerate anything.</summary>
        public bool IsActive => Enabled && FuelBuffer > 0;

        public int Size => Tunables.Sizes[SizeIndex];
        public float Speed => Tunables.Speeds[SpeedIndex];
        public float FuelPerHour => Tunables.SizeFuelMuls[SizeIndex] * Tunables.SpeedFuelMuls[SpeedIndex];

        public ItemSlot FuelSlot => inventory[0];

        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => "temporalaccelfield";

        public BlockEntityTemporalAccelField()
        {
            inventory = new InventoryGeneric(1, null, null, (id, self) => new ItemSlotAccelFieldFuel(self));
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            Tunables = AccelFieldTunables.FromAttributes(Block.Attributes);
            SizeIndex = Math.Clamp(SizeIndex, 0, Tunables.Sizes.Length - 1);
            SpeedIndex = Math.Clamp(SpeedIndex, 0, Tunables.Speeds.Length - 1);

            api.ModLoader.GetModSystem<AccelFieldModSystem>()?.Register(this);

            if (api.Side == EnumAppSide.Client) animUtil?.InitializeAnimator("temporalaccelfield");
            updateRunningState();
        }

        /// <summary>Whether a position is inside this field. The machine's own block doesn't count.</summary>
        public bool Contains(BlockPos pos)
        {
            if (pos.dimension != Pos.dimension || pos.Equals(Pos)) return false;

            int half = Size / 2;
            return Math.Abs(pos.X - Pos.X) <= half && Math.Abs(pos.Y - Pos.Y) <= half && Math.Abs(pos.Z - Pos.Z) <= half;
        }

        /// <summary>Every position in the field cube, the machine's own included.</summary>
        public IEnumerable<BlockPos> Positions()
        {
            int half = Size / 2;
            for (int dx = -half; dx <= half; dx++)
            {
                for (int dy = -half; dy <= half; dy++)
                {
                    for (int dz = -half; dz <= half; dz++)
                    {
                        yield return Pos.AddCopy(dx, dy, dz);
                    }
                }
            }
        }

        #region Fuel

        /// <summary>Fuel units the stack is worth per item, 0 if it isn't field fuel.</summary>
        public static float GetStackFuel(ItemStack stack)
        {
            return stack?.ItemAttributes?[FuelItemAttribute].AsFloat(0) ?? 0;
        }

        /// <summary>Fuel units one temporal gear is worth: the gear's own attribute, else the block's default.</summary>
        public float GearFuel
        {
            get
            {
                var gear = Api?.World.GetItem(new AssetLocation("game:gear-temporal"));
                float fuel = gear?.Attributes?[FuelItemAttribute].AsFloat(0) ?? 0;
                return fuel > 0 ? fuel : Tunables.DefaultGearFuel;
            }
        }

        /// <summary>In-game hours the buffer plus every gear in the slot last at the current settings.</summary>
        public double HoursRemaining => (FuelBuffer + FuelSlot.StackSize * GetStackFuel(FuelSlot.Itemstack)) / FuelPerHour;

        /// <summary>In-game hours one temporal gear lasts at the current settings.</summary>
        public double HoursPerGear => GearFuel / FuelPerHour;

        /// <summary>
        /// Server: burns <paramref name="hours"/> in-game hours of fuel at the current settings, then lights the next
        /// gear from the slot if the buffer ran dry.
        /// </summary>
        public void ConsumeFuel(double hours)
        {
            if (!Enabled) return;

            bool wasActive = IsActive;
            double before = FuelBuffer;

            if (IsActive) FuelBuffer -= hours * FuelPerHour;
            while (FuelBuffer <= 0 && burnGear()) { }
            if (FuelBuffer < 0) FuelBuffer = 0;

            if (wasActive != IsActive || before != FuelBuffer) MarkDirty(wasActive != IsActive);
            updateRunningState();
        }

        private bool burnGear()
        {
            float fuel = GetStackFuel(FuelSlot.Itemstack);
            if (fuel <= 0) return false;

            FuelSlot.TakeOut(1);
            FuelSlot.MarkDirty();
            FuelBuffer += fuel;
            return true;
        }

        #endregion

        #region Running state hooks

        private BlockEntityAnimationUtil animUtil => GetBehavior<BEBehaviorAnimatable>()?.animUtil;

        private void updateRunningState()
        {
            bool running = IsActive;
            if (running == lastRunning) return;
            lastRunning = running;

            OnRunningChanged(running);
            RunningChanged?.Invoke(this, running);
        }

        /// <summary>
        /// Called on both sides when the machine starts or stops running, before <see cref="RunningChanged"/>.
        /// The default starts/stops the running animation and loop sound on the client.
        /// </summary>
        protected virtual void OnRunningChanged(bool running)
        {
            if (Api?.Side != EnumAppSide.Client) return;

            toggleAnimation(running);
            toggleSound(running);
        }

        private void toggleAnimation(bool running)
        {
            string code = Tunables.RunningAnimation;
            if (animUtil == null || string.IsNullOrEmpty(code)) return;

            if (running)
            {
                animUtil.StartAnimation(new AnimationMetaData { Animation = code, Code = code, AnimationSpeed = 1, EaseInSpeed = 1, EaseOutSpeed = 2 });
            }
            else
            {
                animUtil.StopAnimation(code);
            }
        }

        private void toggleSound(bool running)
        {
            var location = Tunables.RunningSound;
            if (location == null) return;

            if (running)
            {
                if (runningSound != null) return;

                runningSound = ((IClientWorldAccessor)Api.World).LoadSound(new SoundParams
                {
                    Location = location,
                    ShouldLoop = true,
                    Position = Pos.ToVec3f().Add(0.5f, 0.5f, 0.5f),
                    DisposeOnFinish = false,
                    Volume = 0,
                    Range = Tunables.RunningSoundRange,
                    SoundType = EnumSoundType.Ambient
                });
                runningSound?.Start();
                runningSound?.FadeTo(Tunables.RunningSoundVolume, 1f, _ => { });
            }
            else if (runningSound != null)
            {
                var fading = runningSound;
                runningSound = null;
                fading.FadeOut(0.5f, sound => sound.Dispose());
            }
        }

        #endregion

        #region Interaction

        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (Api is ICoreClientAPI capi)
            {
                toggleInventoryDialogClient(byPlayer, () =>
                {
                    clientDialog = new GuiDialogTemporalAccelField(Lang.Get("jonastechexpanded:accelfield-title"), Inventory, Pos, capi, this);
                    clientDialog.OnClosed += () => clientDialog = null;
                    return clientDialog;
                });
            }

            return true;
        }

        /// <summary>Client: asks the server to change a setting. The dialog updates once the server syncs it back.</summary>
        public void SendSetting(int packetId, int value)
        {
            (Api as ICoreClientAPI)?.Network.SendBlockEntityPacket(Pos, packetId, BitConverter.GetBytes(value));
        }

        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            if (packetid != PacketSetSize && packetid != PacketSetSpeed && packetid != PacketSetVisible && packetid != PacketSetEnabled)
            {
                base.OnReceivedClientPacket(player, packetid, data);
                return;
            }

            if (data == null || data.Length < 4) return;
            if (!Api.World.Claims.TryAccess(player, Pos, EnumBlockAccessFlags.Use)) return;

            int value = BitConverter.ToInt32(data, 0);
            switch (packetid)
            {
                case PacketSetSize:
                    if (value < 0 || value >= Tunables.Sizes.Length) return;
                    SizeIndex = value;
                    break;
                case PacketSetSpeed:
                    if (value < 0 || value >= Tunables.Speeds.Length) return;
                    SpeedIndex = value;
                    break;
                case PacketSetVisible:
                    FieldVisible = value != 0;
                    break;
                case PacketSetEnabled:
                    Enabled = value != 0;
                    // Switching on lights a gear right away instead of on the next pass.
                    ConsumeFuel(0);
                    break;
            }

            MarkDirty(true);
            updateRunningState();
        }

        #endregion

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            // Skip the container's "stored food perish speed" line; there's no food in here.
            if (IsActive)
            {
                dsc.AppendLine(Lang.Get("jonastechexpanded:accelfield-info-active", Speed, Size));
                dsc.AppendLine(Lang.Get("jonastechexpanded:accelfield-info-fuel", HoursRemaining));
            }
            else if (!Enabled)
            {
                dsc.AppendLine(Lang.Get("jonastechexpanded:accelfield-info-off"));
            }
            else
            {
                dsc.AppendLine(Lang.Get("jonastechexpanded:accelfield-info-inactive"));
            }
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetInt("sizeIndex", SizeIndex);
            tree.SetInt("speedIndex", SpeedIndex);
            tree.SetBool("fieldVisible", FieldVisible);
            tree.SetBool("enabled", Enabled);
            tree.SetDouble("fuelBuffer", FuelBuffer);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            SizeIndex = tree.GetInt("sizeIndex");
            SpeedIndex = tree.GetInt("speedIndex");
            FieldVisible = tree.GetBool("fieldVisible");
            Enabled = tree.GetBool("enabled", true);
            FuelBuffer = tree.GetDouble("fuelBuffer");

            // Before Initialize the tunables are still the defaults; Initialize clamps again with the real ones.
            SizeIndex = Math.Clamp(SizeIndex, 0, Tunables.Sizes.Length - 1);
            SpeedIndex = Math.Clamp(SpeedIndex, 0, Tunables.Speeds.Length - 1);

            clientDialog?.Refresh();
            if (Api != null) updateRunningState();
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            onGone();
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            onGone();
        }

        private void onGone()
        {
            Api?.ModLoader.GetModSystem<AccelFieldModSystem>()?.Unregister(this);

            runningSound?.Stop();
            runningSound?.Dispose();
            runningSound = null;

            clientDialog?.TryClose();
            clientDialog?.Dispose();
            clientDialog = null;
        }
    }

    /// <summary>The field's fuel slot: only takes items that are worth field fuel.</summary>
    public class ItemSlotAccelFieldFuel : ItemSlot
    {
        public ItemSlotAccelFieldFuel(InventoryBase inventory) : base(inventory) { }

        public override bool CanHold(ItemSlot sourceSlot)
        {
            return BlockEntityTemporalAccelField.GetStackFuel(sourceSlot?.Itemstack) > 0 && base.CanHold(sourceSlot);
        }

        public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
        {
            return BlockEntityTemporalAccelField.GetStackFuel(sourceSlot?.Itemstack) > 0 && base.CanTakeFrom(sourceSlot, priority);
        }
    }
}
