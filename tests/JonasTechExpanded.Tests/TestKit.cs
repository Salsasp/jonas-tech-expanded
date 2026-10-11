using System.IO;
using Atlas.Api;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace JonasTechExpanded.Tests
{
    /// <summary>Shared setup for scenarios: item lookup, fueled stacks, equipping, game modes.</summary>
    internal static class TestKit
    {
        public const string Domain = "jonastechexpanded";

        /// <summary>
        /// The repository checkout (modinfo.json + assets/). Found from the test assembly's location, not
        /// AppContext.BaseDirectory: Atlas points that at the game install once a server has booted.
        /// </summary>
        public static readonly string RepoRoot = findRepoRoot();

        private static string findRepoRoot()
        {
            string start = Path.GetDirectoryName(typeof(TestKit).Assembly.Location);
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "modinfo.json")) && Directory.Exists(Path.Combine(dir.FullName, "assets"))
                    && !dir.FullName.Contains("atlas-mods"))
                {
                    return dir.FullName;
                }
            }
            throw new InvalidOperationException("Repository root (modinfo.json + assets/) not found above " + start);
        }

        public static Item Item(IWorldSession world, string code)
        {
            var item = world.Api.World.GetItem(new AssetLocation(code));
            Assert.True(item != null, "Item not registered: " + code);
            return item;
        }

        public static Block Block(IWorldSession world, string code)
        {
            var block = world.Api.World.GetBlock(new AssetLocation(code));
            Assert.True(block != null && block.Id != 0, "Block not registered: " + code);
            return block;
        }

        /// <summary>A new stack of a fuel-powered wearable holding the given hours of fuel.</summary>
        public static ItemStack Fueled(IWorldSession world, string code, double fuelHours)
        {
            var item = Item(world, code);
            var stack = new ItemStack(item);
            ((ItemFueledWearable)item).SetFuelHours(stack, fuelHours);
            return stack;
        }

        public static ItemSlot ArmorSlot(ITestPlayer player, EnumCharacterDressType slot)
        {
            var inv = player.Player.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName);
            return inv[(int)slot];
        }

        public static ItemSlot Equip(ITestPlayer player, EnumCharacterDressType slot, ItemStack stack)
        {
            var target = ArmorSlot(player, slot);
            target.Itemstack = stack;
            target.MarkDirty();
            return target;
        }

        public static void SetGameMode(ITestPlayer player, EnumGameMode mode)
        {
            player.Player.WorldData.CurrentGameMode = mode;
            ((IServerPlayer)player.Player).BroadcastPlayerData();
        }

        public static ItemSlot HotbarSlot(ITestPlayer player, int index)
        {
            return player.Player.InventoryManager.GetHotbarInventory()[index];
        }

        public static void PutInHotbar(ITestPlayer player, int index, ItemStack stack)
        {
            var slot = HotbarSlot(player, index);
            slot.Itemstack = stack;
            slot.MarkDirty();
        }

        public static void ClearHotbar(ITestPlayer player)
        {
            foreach (var slot in player.Player.InventoryManager.GetHotbarInventory())
            {
                if (slot.Empty) continue;
                slot.Itemstack = null;
                slot.MarkDirty();
            }
        }

        /// <summary>How many of the given item the player carries across hotbar and backpack.</summary>
        public static int CountCarried(ITestPlayer player, string code)
        {
            var loc = new AssetLocation(code);
            int count = 0;
            foreach (var inv in new[] { player.Player.InventoryManager.GetHotbarInventory(), player.Player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName) })
            {
                if (inv == null) continue;
                foreach (var slot in inv)
                {
                    if (slot.Itemstack?.Collectible.Code.Equals(loc) == true) count += slot.StackSize;
                }
            }
            return count;
        }

        /// <summary>Runs until the mod systems' 1 second server tick has had a chance to fire, or the condition holds.</summary>
        public static Task UntilTicked(IWorldSession world, Func<bool> condition) => world.Until(condition, 300);
    }
}
