using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace JonasTechExpanded.Tests
{
    /// <summary>
    /// Static checks on the workbench's socket marker cubes, no server needed. Markers are moved freely in
    /// VS Model Creator, so these catch the mistakes that are easy to make there: a marker left visible, a gap in
    /// the numbering, a marker nested where it can't be hidden, or the blueprint marker drifting off its rail.
    /// </summary>
    public class WorkbenchLayoutTests
    {
        private static readonly string ModAssets = Path.Combine(TestKit.RepoRoot, "assets", "jonastechexpanded");

        private static JObject Shape => JObject.Parse(File.ReadAllText(Path.Combine(ModAssets, "shapes", "block", "jonasworkbench.json")));
        private static JObject Block => JObject.Parse(File.ReadAllText(Path.Combine(ModAssets, "blocktypes", "jonasworkbench.json")));

        private static bool isMarker(string name) => name?.StartsWith(WorkbenchSocketMarkers.SocketMarkerPrefix) == true;

        [Fact]
        public void Blueprint_marker_sits_at_the_center_of_the_blueprint_rail()
        {
            var shape = Shape;
            var (from, to) = absoluteBox((JArray)shape["elements"], "BlueprintToolRail", new double[3])
                ?? throw new Xunit.Sdk.XunitException("Shape has no BlueprintToolRail element");
            var marker = shape["elements"].Single(e => e["name"]?.ToString() == WorkbenchSocketMarkers.BlueprintMarker);

            var placement = WorkbenchSocketMarkers.FromMarker(marker.ToObject<ShapeElement>(), 1, 0);
            float[] actual = { placement.X, placement.Y, placement.Z };
            string[] axes = { "x", "y", "z" };
            for (int i = 0; i < 3; i++)
            {
                double center = (from[i] + to[i]) / 2;
                Assert.True(Math.Abs(actual[i] - center) < 0.01,
                    $"{WorkbenchSocketMarkers.BlueprintMarker} {axes[i]} is {actual[i]}, BlueprintToolRail center is {center}");
            }
        }

        [Fact]
        public void Socket_markers_are_numbered_without_gaps()
        {
            var names = Shape["elements"].Select(e => e["name"]?.ToString()).Where(isMarker)
                .Where(n => n != WorkbenchSocketMarkers.BlueprintMarker).ToList();

            Assert.NotEmpty(names);
            var expected = Enumerable.Range(1, names.Count).Select(i => WorkbenchSocketMarkers.SocketMarkerPrefix + i);
            Assert.Equal(expected.OrderBy(n => n), names.OrderBy(n => n));
        }

        [Fact]
        public void Every_marker_is_top_level_and_hidden()
        {
            var shape = Shape;
            var ignored = Block["shape"]["ignoreElements"]?.Select(n => n.ToString()).ToHashSet() ?? new HashSet<string>();

            foreach (var element in shape["elements"])
            {
                foreach (var nested in element.SelectTokens("..children[*].name").Select(n => n.ToString()).Where(isMarker))
                {
                    Assert.Fail($"{nested} is nested inside {element["name"]}; socket markers must be top-level elements");
                }

                string name = element["name"]?.ToString();
                if (isMarker(name)) Assert.True(ignored.Contains(name), $"{name} is not in the block's shape.ignoreElements, so it would render");
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(15)]
        [InlineData(-20)]
        public void Marker_rotation_and_bottom_center_carry_over(double rotationY)
        {
            var marker = new ShapeElement
            {
                From = new double[] { 4, 14, 2 },
                To = new double[] { 10, 14.5, 8 },
                RotationOrigin = new double[] { 7, 14, 5 },
                RotationY = rotationY
            };

            var placement = WorkbenchSocketMarkers.FromMarker(marker, 0.4f, 0);

            Assert.Equal(7, placement.X, 3);
            Assert.Equal(14, placement.Y, 3);
            Assert.Equal(5, placement.Z, 3);
            Assert.Equal(rotationY, placement.RotateY, 3);
            Assert.Equal(0.4f, placement.Scale);
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
