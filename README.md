# ArmatureSyncControl

A Unity Editor tool for synchronizing armature hierarchies and related avatar settings.

## Concept

ArmatureSyncControl was created to simplify avatar update workflows.

When transferring clothing, accessories, or other elements between avatar projects, differences between armature hierarchies and component settings can require repetitive manual adjustments.

This tool synchronizes matching bones between a reference armature and a target armature, helping reduce that work.

## Overview

- **Unity Version:** 2022.3.22f1
- **Menu:** `nur_ieren > Armature Sync Tool`

## Features

- **Transform Synchronization** — Copies local position, rotation, and scale from matching reference bones.
- **Active State and Tag Synchronization** — Copies GameObject active states and tags.
- **PhysBone Synchronization** — Adds, removes, and copies settings for matching PhysBone and PhysBoneCollider component types.
- **Other Component Synchronization** — Adds missing component types and copies serialized settings, including support for components such as MA Scale Adjuster.
- **Reference Remapping** — Attempts to remap component references from the reference hierarchy to corresponding objects in the target hierarchy.
- **BlendShape Synchronization** — Copies BlendShape weights by name between matching SkinnedMeshRenderers on corresponding sibling objects.
- **Optional Modular Avatar Component Removal** — Can remove Modular Avatar Blendshape Sync and Shape Changer components from the target avatar before synchronizing BlendShapes.
- **Unmatched Bone Listing** — Lists bones present only in the target hierarchy and allows them to be selected in the Hierarchy.
- **Prefab and Bone Reference Protection** — Excludes nested prefab contents from synchronization and protects certain unmatched bones referenced by SkinnedMeshRenderers.
- **Undo Support** — Synchronization changes are grouped for Undo where supported.

## How to use

1. Open your avatar project in Unity 2022.3.22f1.
2. Open `nur_ieren > Armature Sync Tool`.
3. Assign the reference armature and the target armature.
4. Select the settings you want to synchronize.
5. Click `実行（同期 + 適応側にしかないボーンをリスト化）` to apply the synchronization and generate the unmatched bone list.
6. Review the unmatched bones and select them in the Hierarchy if necessary.

You can also click `リストだけ更新` to refresh the unmatched bone list without applying synchronization.

### Important notes

- The reference armature is the source of settings; the target armature receives the changes.
- Bones are matched primarily by name. If duplicate names exist, the tool attempts to match them by hierarchy path.
- The top-level armatures are paired regardless of their names.
- PhysBone-related components may be removed from the target when they are absent from the reference or when the reference contains fewer instances of the same component type.
- Other component types are not removed automatically.
- Unmatched bones are not automatically deleted. The tool only lists them.
- Review the selected synchronization options before applying changes, especially when working with Modular Avatar components.

## Development

This project was created to reduce repetitive adjustments when updating avatar armatures and component settings.

## Author

nur_ieren

[X](https://x.com/nur_ieren)

## License
This project is licensed under the MIT License. See LICENSE file for details.

[MIT License](https://opensource.org/license/mit)
