using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace JonasTechExpanded.Tests
{
    /// <summary>
    /// Static checks over the asset JSON, no server needed. A missing texture renders invisible in-game with
    /// only a log line, and the server never loads textures, so the scenarios can't catch these.
    /// </summary>
    public class AssetReferenceTests
    {
        private const string Domain = "jonastechexpanded";

        private static readonly string RepoRoot = findRepoRoot();
        private static readonly string ModAssets = Path.Combine(RepoRoot, "assets", Domain);
        private static readonly string GameAssets = Path.Combine(
            Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? throw new InvalidOperationException("VINTAGE_STORY is not set"),
            "assets");

        /// <summary>The "game" domain is spread over these folders of the install.</summary>
        private static readonly string[] GameDomainFolders = { "survival", "game", "creative" };

        public static IEnumerable<object[]> ShapeFiles => jsonFiles("shapes");
        public static IEnumerable<object[]> TypeFiles => jsonFiles("itemtypes").Concat(jsonFiles("blocktypes"));
        public static IEnumerable<object[]> PatchFiles => jsonFiles("patches");

        [Theory]
        [MemberData(nameof(ShapeFiles))]
        public void Shape_textures_exist(string file)
        {
            var textures = parse(file)["textures"] as JObject;
            if (textures == null) return;

            foreach (var texture in textures.Properties())
            {
                assertExists(texture.Value.ToString(), "textures", ".png", $"{file}: texture '{texture.Name}'");
            }
        }

        [Theory]
        [MemberData(nameof(TypeFiles))]
        public void Type_textures_and_shapes_exist(string file)
        {
            var json = parse(file);

            if (json["textures"] is JObject textures)
            {
                foreach (var texture in textures.Properties())
                {
                    string path = texture.Value["base"]?.ToString();
                    if (path != null) assertExists(path, "textures", ".png", $"{file}: texture '{texture.Name}'");
                }
            }

            string shape = json["shape"]?["base"]?.ToString();
            if (shape != null) assertExists(shape, "shapes", ".json", $"{file}: shape");
        }

        [Theory]
        [MemberData(nameof(PatchFiles))]
        public void Patch_targets_exist(string file)
        {
            foreach (var patch in (JArray)JToken.Parse(File.ReadAllText(Path.Combine(ModAssets, file))))
            {
                string target = patch["file"]?.ToString();
                Assert.False(string.IsNullOrEmpty(target), $"{file}: patch without a target file");

                var (domain, path) = split(target);
                Assert.True(candidates(domain, path, "", "").Any(File.Exists), $"{file}: patch target {target} not found");
            }
        }

        private static void assertExists(string location, string category, string extension, string what)
        {
            // Variant placeholders and wildcards are resolved by the engine per variant.
            if (location.Contains('{') || location.Contains('*')) return;

            var (domain, path) = split(location);
            // VS Model Creator can save absolute disk paths ("C:/Users/..."); the game can't resolve those,
            // and without this check Path.Combine would find the file on this machine and pass.
            Assert.True(domain == Domain || domain == "game", $"{what}: {location} is not a game: or {Domain}: asset path");
            Assert.False(Path.IsPathRooted(path), $"{what}: {location} is an absolute path");

            var paths = candidates(domain, path, category, extension).ToList();
            Assert.True(paths.Any(File.Exists), $"{what}: {location} not found (looked in {string.Join(", ", paths)})");
        }

        private static IEnumerable<string> candidates(string domain, string path, string category, string extension)
        {
            string relative = Path.Combine(category, path + extension);
            if (domain == Domain) return new[] { Path.Combine(ModAssets, relative) };
            return GameDomainFolders.Select(folder => Path.Combine(GameAssets, folder, relative));
        }

        /// <summary>Unprefixed paths in this mod's assets belong to this mod's domain.</summary>
        private static (string domain, string path) split(string location)
        {
            int colon = location.IndexOf(':');
            return colon < 0 ? (Domain, location) : (location[..colon], location[(colon + 1)..]);
        }

        private static JObject parse(string file) => JObject.Parse(File.ReadAllText(Path.Combine(ModAssets, file)));

        private static IEnumerable<object[]> jsonFiles(string category)
        {
            string dir = Path.Combine(ModAssets, category);
            if (!Directory.Exists(dir)) return Enumerable.Empty<object[]>();

            return Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories)
                .Select(f => new object[] { Path.GetRelativePath(ModAssets, f).Replace('\\', '/') });
        }

        private static string findRepoRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "modinfo.json")) && Directory.Exists(Path.Combine(dir.FullName, "assets"))
                    && !dir.FullName.Contains("atlas-mods"))
                {
                    return dir.FullName;
                }
            }
            throw new InvalidOperationException("Repository root (modinfo.json + assets/) not found above " + AppContext.BaseDirectory);
        }
    }
}
