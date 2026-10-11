"""Bundle the vanilla textures a shape references by absolute disk path, and point the shape at the copies.

VS Model Creator saves textures picked from the game install as absolute paths
("C:/Users/.../Vintagestory/assets/survival/textures/block/wood/bark/oak"), which the game can't resolve.
This copies each one into assets/jonastechexpanded/textures/ and rewrites the entry as the unprefixed
(mod-domain) path. Re-run after every VSMC save. A path outside the game install is pointed at our
copy with the same file name, if there is exactly one; otherwise it's only reported.

    python tools/localize-textures.py [shape.json ...]   # default: every shape in the mod
"""
import pathlib
import re
import shutil
import sys

REPO = pathlib.Path(__file__).resolve().parent.parent
MOD_TEXTURES = REPO / "assets" / "jonastechexpanded" / "textures"
# ".../assets/<survival|game|creative>/textures/<path>" -> <path>
VANILLA = re.compile(r"^(?P<root>.*/assets/(?:survival|game|creative)/textures)/(?P<path>.+)$")
TEXTURES_BLOCK = re.compile(r'"textures"\s*:\s*\{[^}]*\}')
ENTRY = re.compile(r'("(?P<key>[^"]+)"\s*:\s*")(?P<value>[A-Za-z]:/[^"]+)(")')


def localize(shape: pathlib.Path) -> bool:
    text = shape.read_text(encoding="utf-8")
    ok = True

    def replace(m: re.Match) -> str:
        nonlocal ok
        value = m.group("value")
        vanilla = VANILLA.match(value)
        if not vanilla:
            # A custom texture (e.g. drawn in GIMP): reuse our copy if exactly one has the same file name.
            name = pathlib.PurePosixPath(value).name + ".png"
            ours = list(MOD_TEXTURES.rglob(name))
            if len(ours) == 1:
                path = ours[0].relative_to(MOD_TEXTURES).with_suffix("").as_posix()
                print(f"  {shape.name}: '{m.group('key')}' -> {path} (our existing copy)")
                return m.group(1) + path + m.group(4)
            print(f"  {shape.name}: '{m.group('key')}' -> {value} is not in the game install; copy it into textures/ by hand")
            ok = False
            return m.group(0)

        source = pathlib.Path(value + ".png")
        target = MOD_TEXTURES / (vanilla.group("path") + ".png")
        if not source.exists():
            print(f"  {shape.name}: '{m.group('key')}' -> {source} does not exist")
            ok = False
            return m.group(0)
        if not target.exists():
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, target)
            print(f"  copied {target.relative_to(REPO)}")
        print(f"  {shape.name}: '{m.group('key')}' -> {vanilla.group('path')}")
        return m.group(1) + vanilla.group("path") + m.group(4)

    # Only the "textures" object; other keys (e.g. VSMC's backDropShape) also hold disk paths.
    fixed = TEXTURES_BLOCK.sub(lambda block: ENTRY.sub(replace, block.group(0)), text)
    if fixed != text:
        # newline="" keeps the file's own line endings.
        with open(shape, "w", encoding="utf-8", newline="") as f:
            f.write(fixed)
    return ok


def main() -> int:
    shapes = [pathlib.Path(a) for a in sys.argv[1:]] or sorted((REPO / "assets" / "jonastechexpanded" / "shapes").rglob("*.json"))
    return 0 if all([localize(s) for s in shapes]) else 1


if __name__ == "__main__":
    sys.exit(main())
