#!/usr/bin/env python3
"""
Generates Packages/nox.audio/Assets/audio/mixer.mixer with 256 tracks (00..FF).

Unity AudioMixer assets (.mixer) are YAML-serialized and cannot be created at
runtime via the public API, so we emit the serialized format directly.

Run:  python generate_voice_mixer.py
"""

import os
import uuid

OUTPUT_PATH = os.path.join(
    os.path.dirname(os.path.abspath(__file__)),
    "..", "Assets", "audio", "mixer.mixer",
)

TRACK_COUNT = 256  # 0x00 .. 0xFF

# Base fileIDs (mirror the existing hand-made mixer layout).
MIXER_ID = 24100000
MASTER_GROUP_ID = 24300002
MASTER_EFFECT_ID = 24400004
SNAPSHOT_ID = 24500006

# Each track gets a unique group + effect id range.
TRACK_GROUP_BASE = 25000000
TRACK_EFFECT_BASE = 26000000


def guid() -> str:
    return uuid.uuid4().hex


def emit_group(f, file_id: int, name: str, mixer_id: int, group_guid: str,
               volume_guid: str, pitch_guid: str, effect_id: int,
               children: list[int] | None = None) -> None:
    children_str = "[]" if not children else (
        "[" + ", ".join(f"{{fileID: {c}}}" for c in children) + "]"
    )
    f.write(f"""--- !u!243 &{file_id}
AudioMixerGroupController:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: {name}
  m_AudioMixer: {{fileID: {mixer_id}}}
  m_GroupID: {group_guid}
  m_Children: {children_str}
  m_Volume: {volume_guid}
  m_Pitch: {pitch_guid}
  m_Send: 00000000000000000000000000000000
  m_Effects:
  - {{fileID: {effect_id}}}
  m_UserColorIndex: 0
  m_Mute: 0
  m_Solo: 0
  m_BypassEffects: 0
""")


def emit_effect(f, effect_id: int) -> None:
    f.write(f"""--- !u!244 &{effect_id}
AudioMixerEffectController:
  m_ObjectHideFlags: 3
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: 
  m_EffectID: 00b48eaed59d99f41ae3cedf79fa66d7
  m_EffectName: Attenuation
  m_MixLevel: 22549546fc0700847819c6fe1c5e377a
  m_Parameters: []
  m_SendTarget: {{fileID: 0}}
  m_EnableWetMix: 0
  m_Bypass: 0
""")


def main() -> None:
    os.makedirs(os.path.dirname(OUTPUT_PATH), exist_ok=True)

    track_group_ids = [TRACK_GROUP_BASE + i for i in range(TRACK_COUNT)]
    track_effect_ids = [TRACK_EFFECT_BASE + i for i in range(TRACK_COUNT)]

    with open(OUTPUT_PATH, "w", encoding="utf-8", newline="\n") as f:
        f.write("%YAML 1.1\n")
        f.write("%TAG !u! tag:unity3d.com,2011:\n")

        # Pre-allocate a volume param GUID per track so we can expose it.
        track_volume_guids = [guid() for _ in range(TRACK_COUNT)]
        exposed_params = []
        for i in range(TRACK_COUNT):
            exposed_params.append(
                f"  - guid: {track_volume_guids[i]}\n"
                f"    name: Volume_{i:02X}\n"
                f"    guidParam: {track_volume_guids[i]}\n"
                f"    effectID: 00000000000000000000000000000000"
            )
        exposed_block = "\n".join(exposed_params)
        snapshot_values = "\n".join(
            f"    {track_volume_guids[i]}: 0.0"
            for i in range(TRACK_COUNT)
        )

        # ── AudioMixerController ──
        f.write(f"""--- !u!241 &{MIXER_ID}
AudioMixerController:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: VoiceMixer
  m_OutputGroup: {{fileID: 0}}
  m_MasterGroup: {{fileID: {MASTER_GROUP_ID}}}
  m_Snapshots:
  - {{fileID: {SNAPSHOT_ID}}}
  m_StartSnapshot: {{fileID: {SNAPSHOT_ID}}}
  m_SuspendThreshold: -80
  m_EnableSuspend: 1
  m_UpdateMode: 0
  m_ExposedParameters:
{exposed_block}
  m_AudioMixerGroupViews: []
  m_CurrentViewIndex: 0
  m_TargetSnapshot: {{fileID: {SNAPSHOT_ID}}}
""")

        # ── Master group (parent of all tracks) ──
        emit_group(
            f, MASTER_GROUP_ID, "Master", MIXER_ID,
            guid(), guid(), guid(), MASTER_EFFECT_ID,
            children=track_group_ids,
        )
        emit_effect(f, MASTER_EFFECT_ID)

        # ── 256 track groups "00".."FF" ──
        for i in range(TRACK_COUNT):
            name = f"{i:02X}"
            emit_group(
                f, track_group_ids[i], name, MIXER_ID,
                guid(), track_volume_guids[i], guid(), track_effect_ids[i],
            )
            emit_effect(f, track_effect_ids[i])

        # ── Snapshot ──
        f.write(f"""--- !u!245 &{SNAPSHOT_ID}
AudioMixerSnapshotController:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: Snapshot
  m_AudioMixer: {{fileID: {MIXER_ID}}}
  m_SnapshotID: {guid()}
  m_FloatValues:
{snapshot_values}
  m_TransitionOverrides: {{}}
""")

    print(f"Generated {OUTPUT_PATH} with {TRACK_COUNT} tracks (00..FF).")


if __name__ == "__main__":
    main()