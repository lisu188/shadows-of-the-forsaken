"""Export the small castle layout as editable Unity assets without starting Unity.

This is a deterministic bootstrap/portability exporter, not an engine validator.
The native Castle Layout editor command consumes the same JSON and may legitimately
reserialize its YAML differently. --check only checks this exporter's own output.
"""

import argparse
import json
import math
from pathlib import Path
import re
import uuid


ROOT = Path(__file__).resolve().parents[1]
LAYOUT = "Assets/LevelLayout/Editor/CastleLayout.json"
SCENE = "Assets/Scenes/ForsakenCastle.unity"
MATERIALS = "Assets/LevelLayout/Materials"
HEADER = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n"
ZERO = {"x": 0, "y": 0, "z": 0}
ONE = {"x": 1, "y": 1, "z": 1}
COMMON = ("  m_ObjectHideFlags: 0\n"
          "  m_CorrespondingSourceObject: {fileID: 0}\n"
          "  m_PrefabInstance: {fileID: 0}\n"
          "  m_PrefabAsset: {fileID: 0}\n")


def guid(path):
    return uuid.uuid5(uuid.NAMESPACE_URL, "shadows-of-the-forsaken/" + path).hex


def file_id(name, component):
    return (uuid.UUID(guid(SCENE + "/" + name + "/" + component)).int & ((1 << 62) - 1)) + 100


def number(value):
    if not math.isfinite(value):
        raise ValueError("Non-finite layout coordinate")
    return format(0 if abs(value) < 1e-10 else value, ".9g")


def vector(value, keys="xyz"):
    return "{" + ", ".join(key + ": " + number(value[key]) for key in keys) + "}"


def point(x=0, y=0, z=0):
    return dict(x=x, y=y, z=z)


def multiply(a, b):
    ax, ay, az, aw = (a[k] for k in "xyzw")
    bx, by, bz, bw = (b[k] for k in "xyzw")
    return dict(x=aw * bx + ax * bw + ay * bz - az * by,
                y=aw * by - ax * bz + ay * bw + az * bx,
                z=aw * bz + ax * by - ay * bx + az * bw,
                w=aw * bw - ax * bx - ay * by - az * bz)


def quaternion(euler):
    """Unity's Quaternion.Euler applies Z, then X, then Y rotation."""
    angles = {key: math.radians(euler[key]) / 2 for key in "xyz"}
    rotations = []
    for axis in "xyz":
        rotation = dict(x=0, y=0, z=0, w=math.cos(angles[axis]))
        rotation[axis] = math.sin(angles[axis])
        rotations.append(rotation)
    return multiply(multiply(rotations[1], rotations[0]), rotations[2])


def inverse(rotation):
    return {key: rotation[key] * (1 if key == "w" else -1) for key in "xyzw"}


def rotate(rotation, position):
    return multiply(multiply(rotation, dict(position, w=0)), inverse(rotation))


def meta(path, folder=False, material=False):
    output = "fileFormatVersion: 2\nguid: " + guid(path) + "\n"
    if folder:
        output += "folderAsset: yes\n"
    output += "NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 2100000\n" if material else "DefaultImporter:\n  externalObjects: {}\n"
    return output + "  userData: \n  assetBundleName: \n  assetBundleVariant: \n"


def material_text(material):
    color = vector(material["color"], "rgba")
    emission = vector(material["emission"], "rgba")
    emitting = any(material["emission"][key] > 0 for key in "rgb")
    return HEADER + "--- !u!21 &2100000\nMaterial:\n  serializedVersion: 8\n" + COMMON + f"""  m_Name: {material['name']}
  m_Shader: {{fileID: 4800000, guid: 933532a4fcc9baf4fa0491de14d08ed7, type: 3}}
  m_Parent: {{fileID: 0}}
  m_ModifiedSerializedProperties: 0
  m_ValidKeywords: {'[_EMISSION]' if emitting else '[]'}
  m_InvalidKeywords: []
  m_LightmapFlags: {2 if emitting else 4}
  m_EnableInstancingVariants: 1
  m_DoubleSidedGI: 0
  m_CustomRenderQueue: -1
  stringTagMap:
    RenderType: Opaque
  disabledShaderPasses: []
  m_LockedProperties:
  m_SavedProperties:
    serializedVersion: 3
    m_TexEnvs:
    - _BaseMap:
        m_Texture: {{fileID: 0}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    - _EmissionMap:
        m_Texture: {{fileID: 0}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    m_Ints: []
    m_Floats:
    - _AlphaClip: 0
    - _Blend: 0
    - _BumpScale: 1
    - _Cull: 2
    - _Cutoff: 0.5
    - _DstBlend: 0
    - _DstBlendAlpha: 0
    - _EnvironmentReflections: 1
    - _Metallic: 0
    - _OcclusionStrength: 1
    - _QueueOffset: 0
    - _ReceiveShadows: 1
    - _Smoothness: 0.15
    - _SpecularHighlights: 1
    - _SrcBlend: 1
    - _SrcBlendAlpha: 1
    - _Surface: 0
    - _WorkflowMode: 1
    - _ZWrite: 1
    m_Colors:
    - _BaseColor: {color}
    - _Color: {color}
    - _EmissionColor: {emission}
    - _SpecColor: {{r: 0.2, g: 0.2, b: 0.2, a: 1}}
  m_BuildTextureStacks: []
  m_AllowLocking: 1
"""


class Scene:
    def __init__(self, root, layout):
        self.root = root
        self.layout = layout
        self.objects = {}
        sample = (root / "Assets/Scenes/SampleScene.unity").read_text(encoding="utf-8")
        self.templates = re.findall(r"--- !u!\d+ &\d+\n.*?(?=--- !u!|\Z)", sample, re.S)

    def add(self, name, parent="", position=None, rotation=None, scale=None,
            kind="Empty", material=None, collider=False, tag="Untagged", display_name=None):
        if name in self.objects:
            raise ValueError("Duplicate scene object: " + name)
        if not re.fullmatch(r"[A-Za-z0-9 _.\-]+", name):
            raise ValueError("Unsupported YAML object name: " + name)
        self.objects[name] = dict(name=name, parent=parent, position=position or ZERO,
                                  rotation=rotation or ZERO, scale=scale or ONE, kind=kind,
                                  material=material, collider=collider, tag=tag, extra=[],
                                  display_name=display_name or name)
        return self.objects[name]

    def component(self, name, class_id, type_name, body):
        identifier = file_id(name, type_name)
        text = f"--- !u!{class_id} &{identifier}\n{type_name}:\n" + COMMON
        text += f"  m_GameObject: {{fileID: {file_id(name, 'GameObject')}}}\n" + body
        self.objects[name]["extra"].append((identifier, text))

    def mono(self, name, script, body=""):
        identifier = file_id(name, script)
        text = f"--- !u!114 &{identifier}\nMonoBehaviour:\n" + COMMON
        text += f"""  m_GameObject: {{fileID: {file_id(name, 'GameObject')}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {script}, type: 3}}
  m_Name:
  m_EditorClassIdentifier:
""" + body
        self.objects[name]["extra"].append((identifier, text))

    def template(self, name, type_name, script=None, replacements=None):
        matches = [block for block in self.templates if "\n" + type_name + ":\n" in block
                   and (script is None or script in block)]
        if len(matches) != 1:
            raise ValueError("Expected one SampleScene component template: " + type_name)
        identifier = file_id(name, script or type_name)
        block = re.sub(r"^(--- !u!\d+ &)\d+", lambda m: m[1] + str(identifier), matches[0])
        block = re.sub(r"m_GameObject: \{fileID: \d+\}",
                       "m_GameObject: {fileID: " + str(file_id(name, "GameObject")) + "}", block)
        for field, value in (replacements or {}).items():
            block, count = re.subn(r"(?m)^(  " + re.escape(field) + r": ).*$",
                                   lambda m: m[1] + str(value), block)
            if count != 1:
                raise ValueError("Expected one template field: " + field)
        self.objects[name]["extra"].append((identifier, block))

    def populate(self):
        for group in ("Geometry", "Rooms", "Passages", "Anchors", "Lighting"):
            self.add(group)
        for item in self.layout["objects"]:
            self.add(**item)
        for room in self.layout["rooms"]:
            self.add(room["objectName"], "Rooms", room["position"])
        for passage in self.layout["passages"]:
            name = passage["objectName"]
            self.add(name, "Passages")
            for index, waypoint in enumerate(passage["waypoints"]):
                self.add(name + "_Waypoint_" + format(index, "02d"), name, waypoint,
                         display_name="Waypoint_" + format(index, "02d"))
            self.add(name + "_GateAnchor", name, passage["gatePosition"], passage["gateRotation"],
                     display_name="GateAnchor")
        for anchor in self.layout["anchors"]:
            self.add(**anchor)
        spawn = self.layout["spawn"]
        self.add("Player", position=spawn, tag="Player")
        self.component("Player", 143, "CharacterController", """  m_Material: {fileID: 0}
  m_IncludeLayers:
    serializedVersion: 2
    m_Bits: 0
  m_ExcludeLayers:
    serializedVersion: 2
    m_Bits: 0
  m_LayerOverridePriority: 0
  m_ProvidesContacts: 0
  m_Enabled: 1
  serializedVersion: 3
  m_Height: 2
  m_Radius: 0.3
  m_SlopeLimit: 45
  m_StepOffset: 0.3
  m_SkinWidth: 0.02
  m_MinMoveDistance: 0
  m_Center: {x: 0, y: 1, z: 0}
""")
        self.mono("Player", "963b3f782c6d71942ad1e73d117b2820", """  speed: 5
  rotationSpeed: 120
  gravity: 9.81
  jumpForce: 5
  inputActions: {fileID: -944628639613478452, guid: 052faaac586de48259a63d0c4782560b, type: 3}
""")
        materials = {material["name"] for material in self.layout["materials"]}
        self.add("Player Body", "Player", point(spawn["x"], spawn["y"] + 1, spawn["z"]),
                 scale=point(.6, 1, .6), kind="Cylinder", material="Player" if "Player" in materials else "Stone")
        self.add("Main Camera", position=point(spawn["x"], spawn["y"] + 2, spawn["z"] - 5),
                 rotation=point(math.degrees(math.atan2(1, 5)), 0, 0), tag="MainCamera")
        self.template("Main Camera", "Camera")
        self.template("Main Camera", "AudioListener")
        self.template("Main Camera", "MonoBehaviour", "a79441f348de89743a2939f4d699eac1",
                      {"m_RenderPostProcessing": 0})
        self.mono("Main Camera", "9191262690f98974abc0076595479fd6", f"""  player: {{fileID: {file_id('Player', 'Transform')}}}
  distance: 5
  height: 2
  smoothSpeed: 2
  collisionRadius: 0.2
  collisionPadding: 0.05
  pivotHeight: 1
  teleportDistance: 8
  obstructionMask:
    serializedVersion: 2
    m_Bits: 4294967295
  findTaggedPlayer: 0
""")
        self.add("Directional Light", "Lighting", point(0, 3, 0), point(50, -30, 0))
        self.template("Directional Light", "Light", replacements={
            "m_Color": "{r: 0.72, g: 0.8, b: 1, a: 1}", "m_Intensity": 1,
            "m_UseColorTemperature": 0})
        self.template("Directional Light", "MonoBehaviour", "474bcb49853aa07438625e644c072ee6")
        for light in self.layout["lights"]:
            self.add(light["name"], "Lighting", light["position"])
            self.template(light["name"], "Light", replacements={
                "m_Type": 2, "m_Color": vector(light["color"], "rgba"),
                "m_Intensity": light["intensity"], "m_Range": light["range"],
                "m_UseColorTemperature": 0})
            # Directional light template has shadows; point lights are unshadowed.
            identifier, text = self.objects[light["name"]]["extra"][-1]
            self.objects[light["name"]]["extra"][-1] = (
                identifier, text.replace("  m_Shadows:\n    m_Type: 2\n", "  m_Shadows:\n    m_Type: 0\n"))
            self.template(light["name"], "MonoBehaviour", "474bcb49853aa07438625e644c072ee6")
        self.add("Layout Preview")
        self.mono("Layout Preview", guid("Assets/LevelLayout/CastleLayoutPreview.cs"))
        for item in self.objects.values():
            if item["parent"] and item["parent"] not in self.objects:
                raise ValueError("Unknown parent: " + item["parent"])
            if item["kind"] != "Empty" and item["material"] not in materials:
                raise ValueError("Unknown material: " + str(item["material"]))

    def primitive(self, item):
        name = item["name"]
        meshes = {"Cube": 10202, "Cylinder": 10206}
        self.component(name, 33, "MeshFilter", "  m_Mesh: {fileID: " + str(meshes[item["kind"]])
                       + ", guid: 0000000000000000e000000000000000, type: 0}\n")
        self.component(name, 23, "MeshRenderer", f"""  m_Enabled: 1
  m_CastShadows: 1
  m_ReceiveShadows: 1
  m_DynamicOccludee: 1
  m_StaticShadowCaster: 0
  m_MotionVectors: 1
  m_LightProbeUsage: 1
  m_ReflectionProbeUsage: 1
  m_RayTracingMode: 2
  m_RayTraceProcedural: 0
  m_RenderingLayerMask: 1
  m_RendererPriority: 0
  m_Materials:
  - {{fileID: 2100000, guid: {guid(MATERIALS + '/' + item['material'] + '.mat')}, type: 2}}
  m_StaticBatchInfo:
    firstSubMesh: 0
    subMeshCount: 0
  m_StaticBatchRoot: {{fileID: 0}}
  m_ProbeAnchor: {{fileID: 0}}
  m_LightProbeVolumeOverride: {{fileID: 0}}
  m_ScaleInLightmap: 1
  m_ReceiveGI: 1
  m_PreserveUVs: 0
  m_IgnoreNormalsForChartDetection: 0
  m_ImportantGI: 0
  m_StitchLightmapSeams: 1
  m_SelectedEditorRenderState: 3
  m_MinimumChartSize: 4
  m_AutoUVMaxDistance: 0.5
  m_AutoUVMaxAngle: 89
  m_LightmapParameters: {{fileID: 0}}
  m_SortingLayerID: 0
  m_SortingLayer: 0
  m_SortingOrder: 0
  m_AdditionalVertexStreams: {{fileID: 0}}
""")
        if item["collider"]:
            # Match the native builder's deliberately conservative prop bounds.
            self.component(name, 65, "BoxCollider", """  m_Material: {fileID: 0}
  m_IncludeLayers:
    serializedVersion: 2
    m_Bits: 0
  m_ExcludeLayers:
    serializedVersion: 2
    m_Bits: 0
  m_LayerOverridePriority: 0
  m_IsTrigger: 0
  m_ProvidesContacts: 0
  m_Enabled: 1
  serializedVersion: 3
  m_Size: """ + ("{x: 1, y: 2, z: 1}" if item["kind"] == "Cylinder" else "{x: 1, y: 1, z: 1}")
                           + "\n  m_Center: {x: 0, y: 0, z: 0}\n")

    def object_text(self, item):
        name = item["name"]
        if item["kind"] != "Empty":
            self.primitive(item)
        parent = self.objects.get(item["parent"])
        rotation = quaternion(item["rotation"])
        position, scale = item["position"], item["scale"]
        if parent:
            parent_rotation = quaternion(parent["rotation"])
            relative = {key: position[key] - parent["position"][key] for key in "xyz"}
            relative = rotate(inverse(parent_rotation), relative)
            position = {key: relative[key] / parent["scale"][key] for key in "xyz"}
            scale = {key: scale[key] / parent["scale"][key] for key in "xyz"}
            rotation = multiply(inverse(parent_rotation), rotation)
        components = [file_id(name, "Transform")] + [identifier for identifier, _ in item["extra"]]
        output = f"--- !u!1 &{file_id(name, 'GameObject')}\nGameObject:\n" + COMMON
        output += "  serializedVersion: 6\n  m_Component:\n"
        output += "".join(f"  - component: {{fileID: {identifier}}}\n" for identifier in components)
        output += f"""  m_Layer: 0
  m_Name: {item['display_name']}
  m_TagString: {item['tag']}
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &{file_id(name, 'Transform')}
Transform:
""" + COMMON + f"""  m_GameObject: {{fileID: {file_id(name, 'GameObject')}}}
  serializedVersion: 2
  m_LocalRotation: {vector(rotation, 'xyzw')}
  m_LocalPosition: {vector(position)}
  m_LocalScale: {vector(scale)}
  m_ConstrainProportionsScale: 0
"""
        children = [child["name"] for child in self.objects.values() if child["parent"] == name]
        output += "  m_Children:\n" + "".join(f"  - {{fileID: {file_id(child, 'Transform')}}}\n" for child in children) if children else "  m_Children: []\n"
        output += f"  m_Father: {{fileID: {file_id(parent['name'], 'Transform') if parent else 0}}}\n"
        output += "  m_LocalEulerAnglesHint: " + vector(item["rotation"] if not parent else ZERO) + "\n"
        return output + "".join(text for _, text in item["extra"])

    def text(self):
        self.populate()
        settings = "".join(self.templates[:4])
        skybox = (self.root / "Assets/FreeNightSky/Materials/nightsky1.mat.meta").read_text()
        sky_guid = re.search(r"(?m)^guid: ([0-9a-f]{32})$", skybox)[1]
        settings = re.sub(r"(?m)^  m_SkyboxMaterial:.*$", "  m_SkyboxMaterial: {fileID: 2100000, guid: " + sky_guid + ", type: 2}", settings)
        settings = re.sub(r"(?m)^  m_AmbientSkyColor:.*$", "  m_AmbientSkyColor: {r: 0.18, g: 0.21, b: 0.28, a: 1}", settings)
        settings = settings.replace("  m_AmbientMode: 0\n", "  m_AmbientMode: 3\n")
        settings = settings.replace("  m_Sun: {fileID: 0}\n", f"  m_Sun: {{fileID: {file_id('Directional Light', 'Light')}}}\n")
        text = HEADER + settings + "".join(self.object_text(item) for item in self.objects.values())
        text += "--- !u!1660057539 &9223372036854775807\nSceneRoots:\n  m_ObjectHideFlags: 0\n  m_Roots:\n"
        text += "".join(f"  - {{fileID: {file_id(item['name'], 'Transform')}}}\n" for item in self.objects.values() if not item["parent"])
        identifiers = re.findall(r"(?m)^--- !u!\d+ &(\d+)$", text)
        if len(identifiers) != len(set(identifiers)):
            raise ValueError("Scene fileID collision")
        if len(text.encode("utf-8")) > 3_000_000:
            raise ValueError("Scene exceeds the bounded 3 MB output budget")
        return text


def outputs(root=ROOT):
    layout = json.loads((root / LAYOUT).read_text(encoding="utf-8"))
    if layout["version"] != 1:
        raise ValueError("Unsupported castle layout version")
    result = {SCENE: Scene(root, layout).text(), SCENE + ".meta": meta(SCENE),
              MATERIALS + ".meta": meta(MATERIALS, folder=True)}
    for material in layout["materials"]:
        if not re.fullmatch(r"[A-Za-z0-9_-]+", material["name"]):
            raise ValueError("Unsafe material name")
        path = MATERIALS + "/" + material["name"] + ".mat"
        result[path] = material_text(material)
        result[path + ".meta"] = meta(path, material=True)
    build_path = "ProjectSettings/EditorBuildSettings.asset"
    build = (root / build_path).read_text(encoding="utf-8")
    entry = f"  - enabled: 1\n    path: {SCENE}\n    guid: {guid(SCENE)}\n"
    build = re.sub(r"(?m)^  - enabled: [01]\n    path: " + re.escape(SCENE) + r"\n    guid: [0-9a-f]{32}\n", "", build)
    if "  m_Scenes:\n" not in build:
        raise ValueError("Expected scene list in build settings")
    result[build_path] = build.replace("  m_Scenes:\n", "  m_Scenes:\n" + entry, 1)
    return {name: "\n".join(line.rstrip() for line in text.splitlines()) + "\n"
            for name, text in result.items()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Check exporter output without writing; a native editor save may differ")
    args = parser.parse_args()
    assets = outputs()
    changed = []
    for name, content in assets.items():
        path = ROOT / name
        if path.is_file() and path.read_text(encoding="utf-8") == content:
            continue
        changed.append(name)
        if not args.check:
            if path.is_symlink() or not path.resolve().is_relative_to(ROOT.resolve()):
                raise ValueError("Unsafe output path: " + name)
            path.parent.mkdir(parents=True, exist_ok=True)
            with path.open("w", encoding="utf-8", newline="\n") as stream:
                stream.write(content)
    if args.check and changed:
        parser.exit(1, "Exporter output differs: " + ", ".join(changed) + "\n")
    print(f"Castle exporter: {len(assets)} assets, {sum(len(text.encode('utf-8')) for text in assets.values()):,} bytes; "
          + ("matches" if args.check else f"{len(changed)} updated") + ". Unity execution is not verified.")


if __name__ == "__main__":
    main()
