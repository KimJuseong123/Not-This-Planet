# Unity Project Context

<!-- unity-onboarding:generated:start -->

## Project Summary

- Project root: `METAGAME/`
- Last analyzed: 2026-08-25
- Last analyzed commit: `ed8ad6d` (`chore: initialize Unity project and gitignore`); current working branch: `develop`.

## Confirmed Environment

- Unity version: `6000.5.4f1` (`d550df8bd089`)
- Render pipeline: Universal Render Pipeline, package `17.5.0`
- Input system: Input System package `1.19.0`; `activeInputHandler: 1` indicates the new Input System
- Target platforms: Standalone and Android settings are present; the final supported-platform decision is unknown

## Important Packages And Frameworks

| Area | Finding | Confidence | Evidence |
| --- | --- | --- | --- |
| Rendering | URP `17.5.0`, Shader Graph, PC/Mobile pipeline assets | Confirmed | `Packages/manifest.json`, `Assets/Settings/`, `ProjectSettings/GraphicsSettings.asset` |
| Input | Unity Input System `1.19.0` with an input-actions asset | Confirmed | `Packages/manifest.json`, `Assets/InputSystem_Actions.inputactions`, `ProjectSettings/ProjectSettings.asset` |
| Navigation | AI Navigation `2.0.13` | Confirmed | `Packages/manifest.json` |
| UI/content | uGUI `2.5.0`, Timeline `1.8.12`, Visual Scripting `1.9.11` | Confirmed | `Packages/manifest.json` |
| Multiplayer tooling | Multiplayer Center `1.0.1`; runtime multiplayer architecture not yet established | Confirmed / Unknown | `Packages/manifest.json`; no first-party gameplay code found |
| Testing | Unity Test Framework `1.7.0` | Confirmed | `Packages/manifest.json`; no project tests found |

## Directory Structure

| Path | Purpose | Confidence | Evidence |
| --- | --- | --- | --- |
| `Assets/Scenes/` | Build scenes; currently contains `SampleScene.unity` | Confirmed | `ProjectSettings/EditorBuildSettings.asset` |
| `Assets/Settings/` | URP assets, renderer assets, and volume profiles | Confirmed | Directory contents |
| `Assets/_Project/` | New first-party content scaffold for project-owned assets | Confirmed | Initialization change; subfolders include art, content, scripts, tests, and third-party isolation |
| `Assets/InputSystem_Actions.inputactions` | Input action definitions | Confirmed | File exists and is referenced by build settings |
| `Assets/TutorialInfo/` | Template readme asset, editor helper, and tutorial layout | Confirmed | Directory contents and scripts |
| `Packages/` | Manifest and lockfile for package dependencies | Confirmed | `Packages/manifest.json`, `Packages/packages-lock.json` |
| `ProjectSettings/` | Unity project and build/editor configuration | Confirmed | Directory contents |
| `Docs/AI/` | Persistent project context for future agents | Confirmed | This document |

## Assembly Boundaries

| Assembly | Responsibility | Key references | Notes |
| --- | --- | --- | --- |
| `Metagame.Core` | Shared runtime primitives and services | None | Initial boundary; no project code yet |
| `Metagame.Gameplay` | Gameplay systems and feature logic | `Metagame.Core` | Initial boundary; no project code yet |
| `Metagame.UI` | Runtime UI systems | `Metagame.Core` | Initial boundary; no project code yet |
| `Assembly-CSharp` | Existing template runtime scripts | Unity defaults | `TutorialInfo` remains outside the new first-party boundaries |
| `Assembly-CSharp-Editor` | Default editor assembly | `UnityEditor` and Unity defaults | Contains the template `ReadmeEditor` script; no custom editor boundary yet |

## Scenes And Startup Flow

- Build scenes: `Assets/Scenes/SampleScene.unity` is the only enabled build scene.
- Likely startup scene: `SampleScene` by current build-settings order.
- Scene loading flow: No custom scene-loading system exists yet; gameplay startup flow is unknown.

## Architecture

| Pattern | Finding | Confidence | Evidence |
| --- | --- | --- | --- |
| Project maturity | New URP template with tutorial/readme content; no first-party gameplay systems yet | Confirmed | `Assets/` inventory |
| Runtime architecture | Initial Core/Gameplay/UI assembly boundaries are in place; gameplay implementation is not established | Confirmed | `Assets/_Project/Scripts/Runtime/` and three `.asmdef` files |
| Editor architecture | Template custom inspector for `Readme` | Confirmed | `Assets/TutorialInfo/Scripts/Editor/ReadmeEditor.cs` |

## Coding Conventions

- Namespace style: New first-party assemblies use `Metagame.<Feature>`; template scripts still use the global namespace.
- Serialized fields: Template uses public fields and `[SerializeField]` for editor-only style state; project convention is not established.
- Async/events: Unknown.
- Comments/docs: Only template comments are present; project convention is not established.

## Testing And Validation

- EditMode tests: No first-party tests found.
- PlayMode tests: No first-party tests found.
- CI/build validation: No CI configuration or build script found.

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
- Initialization added empty first-party folders and Core/Gameplay/UI assembly definitions; existing scenes and template assets were not moved or edited.
- Large binary assets are configured for Git LFS in the repository-root `.gitattributes`; Unity YAML assets remain regular text files because project serialization is ForceText.

## Unknowns And Confidence

- Gameplay architecture, namespaces, folder conventions, save/networking design, and supported release platforms are not established yet.
- Multiplayer Center is installed, but package presence alone does not confirm multiplayer gameplay.
- The only build scene is the template `SampleScene`; a production boot/menu flow is not defined.

## Source Files Inspected

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
- `Assets/_Project/Scripts/Runtime/*/*.asmdef`
- `.gitattributes`
- `README.md`
- `METAGAME/.vscode/*`

<!-- unity-onboarding:generated:end -->
