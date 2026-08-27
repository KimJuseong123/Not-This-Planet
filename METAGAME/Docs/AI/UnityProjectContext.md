# Unity Project Context

<!-- unity-onboarding:generated:start -->

## Project Summary

- Latest feature change: PlayerRig now uses stable target-velocity locomotion, first-person camera mode, configurable health/fall damage, two-hand tool handling with charged Q throws, heavy-object fixed gripping, crosshair-locked light-tool carrying, and SampleScene starts the same runtime bootstrap as Surface.

- Project root: `METAGAME/`
- Last analyzed: 2026-08-27
- Last analyzed commit: `8768648` (`Merge branch 'S15P21B202-21' into 'develop'`); current working branch: `S15P21B202-25-character`.
- Latest structural change: the `Assets/SpaceFlop/` prototype tree was folded into the `Assets/_Project/` convention layout (folders, assemblies, namespaces, and hardcoded asset paths).

## Confirmed Environment

- Unity version: `6000.5.4f1` (`d550df8bd089`)
- Render pipeline: Universal Render Pipeline, package `17.5.0`
- Input system: Input System package `1.19.0` is installed, but `activeInputHandler: 0` means only the old Input Manager is active. The prototype reads `UnityEngine.Input` directly (`Controls.cs`), so this must stay 0 unless that code is ported.
- Target platforms: Standalone and Android settings are present; the final supported-platform decision is unknown

## Important Packages And Frameworks

| Area | Finding | Confidence | Evidence |
| --- | --- | --- | --- |
| Rendering | URP `17.5.0`, Shader Graph, PC/Mobile pipeline assets | Confirmed | `Packages/manifest.json`, `Assets/Settings/`, `ProjectSettings/GraphicsSettings.asset` |
| Input | Unity Input System `1.19.0` with an input-actions asset | Confirmed | `Packages/manifest.json`, `Assets/InputSystem_Actions.inputactions`, `ProjectSettings/ProjectSettings.asset` |
| Navigation | AI Navigation `2.0.13` | Confirmed | `Packages/manifest.json` |
| UI/content | uGUI `2.5.0`, Timeline `1.8.12`, Visual Scripting `1.9.11` | Confirmed | `Packages/manifest.json` |
| Multiplayer tooling | Multiplayer Center `1.0.1`; runtime multiplayer architecture not yet established | Confirmed / Unknown | `Packages/manifest.json`; gameplay code is present but single-player only |
| Testing | Unity Test Framework `1.7.0` | Confirmed | `Packages/manifest.json`; no UTF tests found. The prototype ships its own runtime self-test (`SpaceSelfTest`) |

## Directory Structure

| Path | Purpose | Confidence | Evidence |
| --- | --- | --- | --- |
| `Assets/Scenes/` | Unity template scene `SampleScene.unity`; not registered in build settings | Confirmed | Directory contents, `ProjectSettings/EditorBuildSettings.asset` |
| `Assets/Settings/` | URP assets, renderer assets, and volume profiles | Confirmed | Directory contents |
| `Assets/_Project/` | All first-party assets: `Models/`, `Prefabs/Resources/`, `Scenes/`, `Scripts/`, `Shaders/Resources/`, `Textures/Concept/`, plus the still-empty scaffold folders | Confirmed | Directory contents |
| `Assets/_Project/Scripts/Runtime/` | `Core/`, `Gameplay/` (`Player/`, `World/`), `UI/`, `Bootstrap/` — one assembly per folder | Confirmed | `.asmdef` files |
| `Assets/_Project/Scripts/Editor/` | Build, scene-builder, and model tooling; Editor-platform only | Confirmed | `Metagame.Editor.asmdef` |
| `Assets/InputSystem_Actions.inputactions` | Input action definitions | Confirmed | File exists and is referenced by build settings |
| `Assets/TutorialInfo/` | Template readme asset, editor helper, and tutorial layout | Confirmed | Directory contents and scripts |
| `Packages/` | Manifest and lockfile for package dependencies | Confirmed | `Packages/manifest.json`, `Packages/packages-lock.json` |
| `ProjectSettings/` | Unity project and build/editor configuration | Confirmed | Directory contents |
| `Docs/AI/` | Persistent project context for future agents | Confirmed | This document |

## Assembly Boundaries

| Assembly | Responsibility | Key references | Notes |
| --- | --- | --- | --- |
| `Metagame.Core` | Tuning constants, input polling, primitive/material factory | None | Must stay free of gameplay types — a reference back to `Metagame.Gameplay` would be a cycle |
| `Metagame.Gameplay` | Ragdoll player, world building, level parts, session state | `Metagame.Core` | `GameSession` lives here because it holds `PlayerRig`, `LevelBuilder`, and `Checkpoint` |
| `Metagame.UI` | IMGUI HUD | `Metagame.Core`, `Metagame.Gameplay` | Reads player and session state directly |
| `Metagame.Bootstrap` | Scene entry point, self-test, screenshot runner | `Metagame.Core`, `Metagame.Gameplay`, `Metagame.UI` | A separate assembly so `Gameplay` never has to reference `UI` |
| `Metagame.Editor` | Build pipeline, scene builder, FBX-to-Resources linking, model inspection | The four above, plus `UnityEditor` | `includePlatforms` is `Editor` only |
| `Assembly-CSharp` | Existing template runtime scripts | Unity defaults | `TutorialInfo` remains outside the first-party boundaries |
| `Assembly-CSharp-Editor` | Default editor assembly | `UnityEditor` and Unity defaults | Contains the template `ReadmeEditor` script |

## Scenes And Startup Flow

- Build scenes: `Assets/_Project/Scenes/Surface.unity` is the only enabled build scene.
- Startup scene: `Surface`. It holds one GameObject with a `SpaceBootstrap` component; the player ragdoll, level, HUD, and sky are all built in code at runtime.
- Scene loading flow: there is no scene-loading system — the game is a single scene. `SpaceSceneBuilder` regenerates `Surface.unity` from the editor menu.

## Architecture

| Pattern | Finding | Confidence | Evidence |
| --- | --- | --- | --- |
| Project maturity | The SpaceFlop physics-ragdoll prototype sits on top of the URP template | Confirmed | `Assets/_Project/` inventory, `METAGAME/README.md` |
| Runtime architecture | Code-driven: one bootstrap component builds the whole world; no prefab or scene composition | Confirmed | `SpaceBootstrap.cs`, `LevelBuilder.cs` |
| Assembly architecture | Core to Gameplay to UI to Bootstrap, one direction only; Editor sits on top | Confirmed | Five `.asmdef` files |
| Editor architecture | `Metagame.Editor` build/scene/art tooling, plus the template `ReadmeEditor` | Confirmed | `Assets/_Project/Scripts/Editor/`, `Assets/TutorialInfo/Scripts/Editor/ReadmeEditor.cs` |

## Coding Conventions

- Namespace style: `Metagame.Core`, `Metagame.Gameplay`, `Metagame.UI`, `Metagame.Bootstrap`, `Metagame.EditorTools`, each matching its assembly's `rootNamespace`. Template scripts still use the global namespace.
- File naming: one public type per file, file name equal to the type name.
- Serialized fields: prototype code exposes public fields on `MonoBehaviour`s instead of `[SerializeField] private`, and private fields are not `_camelCase` yet. The README states both rules as recommendations.
- Async/events: no async; per-frame polling in `Update`/`FixedUpdate`, with coroutines for the self-test.
- Comments/docs: Korean XML doc comments on most types, explaining the physics reasoning behind the numbers.

## Testing And Validation

- EditMode tests: none; `Assets/_Project/Scripts/Tests/Editor/` is still empty.
- PlayMode tests: none; `Assets/_Project/Scripts/Tests/Runtime/` is still empty.
- Runtime self-test: `SpaceSelfTest` feeds input straight into `PlayerRig.SetInput()` and asserts roughly 109 invariants. Run the built player with `-batchmode -nographics -space-selftest`; exit code 0 means it passed.
- CI/build validation: no CI configuration, but `Metagame.EditorTools.SpaceBuild.BuildWindows` is callable via `-executeMethod`.

## Available Unity Tooling

| Capability | Status | Evidence |
| --- | --- | --- |
| Unity MCP/editor connection | unavailable in this session | No Unity-specific MCP tool is exposed to Codex; findings are repository-based |
| Unity test/console/scene inspection | unavailable in this session | No Unity-specific MCP tool is exposed to Codex |
| VS Code attach configuration | available | `METAGAME/.vscode/launch.json`, `extensions.json` |

## Important Constraints

- Commit `Assets/`, `Packages/`, `ProjectSettings/`, and all Unity `.meta` files.
- Do not commit Unity-generated caches, local editor state, generated IDE files, build outputs, or signing credentials; repository-root `.gitignore` covers these.
- Keep `Packages/manifest.json` and `Packages/packages-lock.json` versioned so collaborators resolve the same dependencies.
- Assets moved between folders must carry their `.meta` files so GUIDs, and therefore scene and prefab references, survive.
- `Prim` and the model loaders resolve assets by `Resources.Load` name, so `Shaders/Resources/` and `Prefabs/Resources/` have to stay inside a folder literally named `Resources`.
- Editor tooling holds absolute asset paths (`ArtLink`, `SpaceBuild`, `SpaceSceneBuilder`, `ArtInspect`, `SpaceFlopModelValidator`); moving the models or the scene means updating those constants.
- Large binary assets are configured for Git LFS in the repository-root `.gitattributes`; Unity YAML assets remain regular text files because project serialization is ForceText.

## Unknowns And Confidence

- Save/networking design and supported release platforms are not established yet.
- Multiplayer Center is installed, but package presence alone does not confirm multiplayer gameplay.
- There is no menu or boot flow: the build starts straight into `Surface`.
- The metagame layer this repository is named for has not been built; only the SpaceFlop prototype exists.

## Source Files Inspected

- Updated Assets/_Project/Scripts/Runtime/Gameplay/Player/PlayerRig.cs, Assets/_Project/Scripts/Runtime/Gameplay/Player/Hand.cs, Assets/_Project/Scripts/Runtime/Gameplay/DamageSource.cs, Assets/_Project/Scripts/Runtime/Gameplay/GameSession.cs, Assets/_Project/Scripts/Runtime/Gameplay/Player/OrbitCamera.cs, Assets/_Project/Scripts/Runtime/Gameplay/World/LevelBuilder.cs, Assets/_Project/Scripts/Runtime/Bootstrap/SpaceBootstrap.cs, Assets/_Project/Scripts/Runtime/Bootstrap/SpaceSelfTest.cs, Assets/_Project/Scripts/Runtime/Core/Controls.cs, Assets/_Project/Scripts/Runtime/Core/Tuning.cs, Assets/_Project/Scripts/Runtime/UI/GameHud.cs, and Assets/Scenes/SampleScene.unity.
- `ProjectSettings/ProjectVersion.txt`
- `Packages/manifest.json`
- `Packages/packages-lock.json`
- `ProjectSettings/GraphicsSettings.asset`
- `ProjectSettings/ProjectSettings.asset`
- `ProjectSettings/EditorBuildSettings.asset`
- `ProjectSettings/VersionControlSettings.asset`
- `ProjectSettings/EditorSettings.asset`
- `Assets/TutorialInfo/Scripts/Readme.cs`
- `Assets/TutorialInfo/Scripts/Editor/ReadmeEditor.cs`
- `Assets/_Project/Scripts/Runtime/*/*.asmdef`, `Assets/_Project/Scripts/Editor/Metagame.Editor.asmdef`
- `Assets/_Project/Scripts/**/*.cs`
- `Assets/_Project/Scenes/Surface.unity`
- `METAGAME/README.md`
- `.gitattributes`
- `README.md`
- `METAGAME/.vscode/*`

<!-- unity-onboarding:generated:end -->
