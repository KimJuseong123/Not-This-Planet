# 594 Astra scene conflict repair

- Broken revision: `25956ddb` (merge of develop into 594).
- Its Stage1_Astra.unity Git blob contained two `oid` lines and two `size` lines. This is not a valid LFS pointer; checkout could not restore Unity YAML.
- Symptoms: scene load errors, missing SpaceBootstrap/AuthoredMapSettings, zero outline targets, and dependent test failures.
- Integrated origin/develop at `c8658abf` into the 594 branch only.
- Recovered actual YAML from the original 594 scene and latest develop LFS object. Compared both against `d8c573d1` by serialized object ID.
- Preserved develop's existing scene objects and changes; applied only the four tool object/transform changes, eight display blocks, and appended the display scene root. Combined the scene-root additions from both branches.
- All local serialized fileID references resolve. Result: 5,689 YAML blocks, 5,716,019 bytes.
- The staged blob is a canonical single-object LFS pointer. Smudging that pointer reproduces the repaired scene byte-for-byte.

Validation results are recorded after the Unity test run below.

Unity 6000.5.4f1 EditMode run (including EnterPlayMode integration tests): 61 total, 60 passed, 1 failed. All ten newly reported failures passed. Selected suites: FirstPersonHandViewTests, OccludedInteractionTests, PracticeLobbyTests, Stage1CutsceneTests, Stage1SkyCoverageTests, ToolDisplayMountTests, MagneticStormImpulseTests.

Remaining failure: MagneticStormImpulseTests.TriggerKicksInteractiveBodiesAndWholePlayerOncePerSecondOnlyWithAuthority (expected < 0.02, actual 18.4390907). The user separately identified this revived failure; it creates an empty scene and does not load Astra. Its test and hazard code match develop. It remains unresolved, not suppressed. Evidence: artifacts/shelter-tool-stands/repair-tests.xml.

Unity-generated rendering settings changes were discarded. Existing debris prefab reference-type warnings remain outside this repair. No player build or remote CI rerun result is claimed.
