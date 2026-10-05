"""Create missing metadata and the minimal, deterministically composed level scene.

This authoring helper never replaces existing metadata, source documents or scene GUIDs.
Geometry and component composition are owned by ForsakenLevel.cs, not generated YAML objects.
"""
from pathlib import Path
import json
import uuid

ROOT = Path(__file__).resolve().parents[1]


def ensure_meta(path):
    meta = Path(str(path) + ".meta")
    if meta.exists():
        return meta.read_text(encoding="utf-8").split("guid: ")[1].splitlines()[0]
    guid = uuid.uuid4().hex
    folder = "folderAsset: yes\n" if path.is_dir() else ""
    meta.write_text(f"fileFormatVersion: 2\nguid: {guid}\n{folder}", encoding="utf-8")
    return guid


def main():
    assets = ROOT / "Assets"
    for path in [assets / "Level", *(assets / "Level").rglob("*")]:
        if path.suffix != ".meta":
            ensure_meta(path)
    shader = ensure_meta(assets / "Level/CastleSurface.shader")
    material = assets / "Level/CastleSurface.mat"
    if not material.exists():
        material.write_text(f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!21 &2100000
Material:
  serializedVersion: 8
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: CastleSurface
  m_Shader: {{fileID: 4800000, guid: {shader}, type: 3}}
  m_Parent: {{fileID: 0}}
  m_ModifiedSerializedProperties: 0
  m_ValidKeywords: []
  m_InvalidKeywords: []
  m_LightmapFlags: 4
  m_EnableInstancingVariants: 1
  m_DoubleSidedGI: 0
  m_CustomRenderQueue: -1
  stringTagMap: {{}}
  disabledShaderPasses: []
  m_LockedProperties:
  m_SavedProperties:
    serializedVersion: 3
    m_TexEnvs: []
    m_Ints: []
    m_Floats: []
    m_Colors:
    - _Color: {{r: 0.2, g: 0.23, b: 0.25, a: 1}}
    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}
  m_BuildTextureStacks: []
""", encoding="utf-8")
    material_guid = ensure_meta(material)
    script_guid = ensure_meta(assets / "Level/ForsakenLevel.cs")
    scene = assets / "Scenes/ForsakenCastle.unity"
    if not scene.exists():
        scene.write_text(f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &1000
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: 1001}}
  - component: {{fileID: 1002}}
  m_Layer: 0
  m_Name: Shadows of the Forsaken
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &1001
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 1000}}
  serializedVersion: 2
  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_LocalPosition: {{x: 0, y: 0, z: 0}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {{fileID: 0}}
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
--- !u!114 &1002
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 1000}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {script_guid}, type: 3}}
  m_Name:
  m_EditorClassIdentifier:
  inputActions: {{fileID: -944628639613478452, guid: 052faaac586de48259a63d0c4782560b, type: 3}}
  surfaceMaterial: {{fileID: 2100000, guid: {material_guid}, type: 2}}
  nightSky: {{fileID: 2100000, guid: 7e1e5781d39549148abcbf66c428df00, type: 2}}
--- !u!1660057539 &9223372036854775807
SceneRoots:
  m_ObjectHideFlags: 0
  m_Roots:
  - {{fileID: 1001}}
""", encoding="utf-8")
    scene_guid = ensure_meta(scene)
    build = ROOT / "ProjectSettings/EditorBuildSettings.asset"
    build_text = build.read_text(encoding="utf-8")
    if "Assets/Scenes/ForsakenCastle.unity" not in build_text:
        build_text = build_text.replace("  m_Scenes:\n", "  m_Scenes:\n  - enabled: 1\n    path: Assets/Scenes/ForsakenCastle.unity\n    guid: " + scene_guid + "\n")
        build.write_text(build_text, encoding="utf-8")
    actions = assets / "InputSystem_Actions.inputactions"
    data = json.loads(actions.read_text(encoding="utf-8"))
    player = next(m for m in data["maps"] if m["name"] == "Player")
    if not any(a["name"] == "Restart" for a in player["actions"]):
        player["actions"].append({"name": "Restart", "type": "Button", "id": str(uuid.uuid4()),
                                  "expectedControlType": "Button", "processors": "", "interactions": "", "initialStateCheck": False})
        player["bindings"].append({"name": "", "id": str(uuid.uuid4()), "path": "<Keyboard>/r", "interactions": "",
                                   "processors": "", "groups": "Keyboard&Mouse", "action": "Restart", "isComposite": False, "isPartOfComposite": False})
        actions.write_text(json.dumps(data, indent=4) + "\n", encoding="utf-8")
    for relative in ("Assets/Tests/EditMode/Shadows.EditMode.Tests.asmdef", "Assets/Tests/PlayMode/Shadows.PlayMode.Tests.asmdef"):
        path = ROOT / relative
        data = json.loads(path.read_text(encoding="utf-8"))
        for reference in ("Shadows.Combat.Core", "Shadows.Puzzles.Core"):
            if reference not in data["references"]:
                data["references"].append(reference)
        path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
