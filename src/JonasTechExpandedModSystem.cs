using Vintagestory.API.Common;

namespace JonasTechExpanded
{
    /// <summary>
    /// Assembly entry ModSystem. Feature logic lives in <see cref="ExoskeletonModSystem"/> and
    /// <see cref="SpringBootsModSystem"/>.
    /// </summary>
    public class JonasTechExpandedModSystem : ModSystem
    {
        public override bool ShouldLoad(EnumAppSide forSide) => true;
    }
}
