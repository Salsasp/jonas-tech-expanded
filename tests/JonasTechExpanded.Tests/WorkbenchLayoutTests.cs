using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace JonasTechExpanded.Tests
{
    /// <summary>
    /// Static checks tying the workbench's socket placements to its model, no server needed. The placements are
    /// hand-copied numbers, so these catch the model being moved in VS Model Creator without the JSON following.
    /// </summary>
    public class WorkbenchLayoutTests
    {
        private static readonly string ModAssets = Path.Combine(TestKit.RepoRoot, "assets", "jonastechexpanded");

        [Fact]
        public void Blueprint_socket_is_the_center_of_the_blueprint_rail()
        {
            var shape = JObject.Parse(File.ReadAllText(Path.Combine(ModAssets, "shapes", "block", "jonasworkbench.json")));
            var (from, to) = absoluteBox((JArray)shape["elements"], "BlueprintToolRail", new double[3])
                ?? throw new Xunit.Sdk.XunitException("Shape has no BlueprintToolRail element");

            var block = JObject.Parse(File.ReadAllText(Path.Combine(ModAssets, "blocktypes", "jonasworkbench.json")));
            var blueprint = block["attributes"]["workbench"]["blueprint"];

            string[] axes = { "x", "y", "z" };
            for (int i = 0; i < 3; i++)
            {
                double center = (from[i] + to[i]) / 2;
                Assert.True(Math.Abs(blueprint[axes[i]].Value<double>() - center) < 0.01,
                    $"blueprint socket {axes[i]} is {blueprint[axes[i]]}, BlueprintToolRail center is {center}");
            }
        }

        /// <summary>The named element's box in model voxels, adding up parent offsets. Rotated ancestors aren't supported.</summary>
        private static (double[] from, double[] to)? absoluteBox(JArray elements, string name, double[] offset)
        {
            foreach (var element in elements ?? new JArray())
            {
                var from = element["from"].Select((v, i) => v.Value<double>() + offset[i]).ToArray();
                var to = element["to"].Select((v, i) => v.Value<double>() + offset[i]).ToArray();
                if (element["name"]?.ToString() == name) return (from, to);

                var found = absoluteBox(element["children"] as JArray, name, from);
                if (found == null) continue;

                bool rotated = new[] { "rotationX", "rotationY", "rotationZ" }.Any(r => (element[r]?.Value<double>() ?? 0) != 0);
                Assert.False(rotated, $"{element["name"]} (ancestor of {name}) is rotated; this check only adds offsets");
                return found;
            }
            return null;
        }
    }
}
