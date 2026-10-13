using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace JonasTechExpanded
{
    /// <summary>
    /// The field's selectable sizes and speeds and what each costs, read from the block JSON's
    /// <c>attributes.accelField</c>. Defaults mirror the JSON.
    /// </summary>
    public class AccelFieldTunables
    {
        /// <summary>Edge length in blocks of each selectable field cube, centered on the machine. Odd numbers.</summary>
        public int[] Sizes = { 3, 5, 9 };

        /// <summary>Fuel consumption multiplier for each entry of <see cref="Sizes"/>.</summary>
        public float[] SizeFuelMuls = { 1, 2, 3 };

        /// <summary>Time multiplier for each selectable speed.</summary>
        public float[] Speeds = { 2, 4, 8 };

        /// <summary>Fuel consumption multiplier for each entry of <see cref="Speeds"/>.</summary>
        public float[] SpeedFuelMuls = { 1, 4, 8 };

        /// <summary>Fuel units a temporal gear is worth if the gear has no <c>accelFieldFuelHours</c> attribute.</summary>
        public float DefaultGearFuel = 24;

        /// <summary>Shape animation played while running, if the block has the Animatable behavior. Null for none.</summary>
        public string RunningAnimation;

        /// <summary>Sound looped while running. Null for none.</summary>
        public AssetLocation RunningSound;
        public float RunningSoundVolume = 0.5f;
        public float RunningSoundRange = 8;

        public static AccelFieldTunables FromAttributes(JsonObject attributes)
        {
            var t = new AccelFieldTunables();
            var attr = attributes?["accelField"];
            if (attr == null || !attr.Exists) return t;

            t.Sizes = attr["sizes"].AsArray(t.Sizes);
            t.SizeFuelMuls = attr["sizeFuelMuls"].AsArray(t.SizeFuelMuls);
            t.Speeds = attr["speeds"].AsArray(t.Speeds);
            t.SpeedFuelMuls = attr["speedFuelMuls"].AsArray(t.SpeedFuelMuls);
            t.DefaultGearFuel = attr["defaultGearFuel"].AsFloat(t.DefaultGearFuel);
            t.RunningAnimation = attr["runningAnimation"].AsString();

            var sound = attr["runningSound"];
            string soundPath = sound["location"].AsString();
            if (!string.IsNullOrEmpty(soundPath)) t.RunningSound = AssetLocation.Create(soundPath, "jonastechexpanded");
            t.RunningSoundVolume = sound["volume"].AsFloat(t.RunningSoundVolume);
            t.RunningSoundRange = sound["range"].AsFloat(t.RunningSoundRange);

            if (t.Sizes.Length != t.SizeFuelMuls.Length || t.Speeds.Length != t.SpeedFuelMuls.Length || t.Sizes.Length == 0 || t.Speeds.Length == 0)
            {
                throw new InvalidOperationException("accelField: sizes/sizeFuelMuls and speeds/speedFuelMuls must be non-empty and the same length");
            }

            return t;
        }
    }
}
