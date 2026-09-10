using System;
using UnityEditor;
using UnityEngine;
using Metagame.Gameplay.Audio;

namespace Metagame.EditorTools
{
    /// <summary>
    /// AudioManager의 이벤트 슬롯을 이름별로 보여 주고, Project 창의 WAV를
    /// 본음 또는 선택적 노이즈 레이어에 직접 드롭할 수 있게 하는 Inspector다.
    /// </summary>
    [CustomEditor(typeof(AudioManager))]
    [CanEditMultipleObjects]
    public sealed class AudioManagerEditor : Editor
    {
        SerializedProperty soundBank;
        SerializedProperty globalStyle;
        SerializedProperty masterVolume;
        SerializedProperty muted;
        SerializedProperty outputMixerGroup;
        SerializedProperty spatialMinDistance;
        SerializedProperty defaultFadeInSeconds;
        SerializedProperty defaultFadeOutSeconds;
        SerializedProperty noiseMasterVolume;
        SerializedProperty noiseMasterPitch;
        SerializedProperty eventOverrides;
        bool showEvents = true;
        bool[] expandedEvents;

        void OnEnable()
        {
            soundBank = serializedObject.FindProperty("soundBank");
            globalStyle = serializedObject.FindProperty("globalStyle");
            masterVolume = serializedObject.FindProperty("masterVolume");
            muted = serializedObject.FindProperty("muted");
            outputMixerGroup = serializedObject.FindProperty("outputMixerGroup");
            spatialMinDistance = serializedObject.FindProperty("spatialMinDistance");
            defaultFadeInSeconds = serializedObject.FindProperty("defaultFadeInSeconds");
            defaultFadeOutSeconds = serializedObject.FindProperty("defaultFadeOutSeconds");
            noiseMasterVolume = serializedObject.FindProperty("noiseMasterVolume");
            noiseMasterPitch = serializedObject.FindProperty("noiseMasterPitch");
            eventOverrides = serializedObject.FindProperty("eventOverrides");
            SyncFoldoutState();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Audio Manager", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(soundBank, new GUIContent("Sound Bank"));
            EditorGUILayout.PropertyField(globalStyle, new GUIContent("Global Style"));

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("전체 재생", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(masterVolume, new GUIContent("Master Volume"));
            EditorGUILayout.PropertyField(muted, new GUIContent("Muted"));
            EditorGUILayout.PropertyField(outputMixerGroup, new GUIContent("Output Mixer Group"));

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("인게임 BGM", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("musicClip"), new GUIContent("Music Clip"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("musicBaseVolume"), new GUIContent("기본 음량"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("musicFadeInSeconds"), new GUIContent("페이드 인 (초)"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("musicFadeOutSeconds"), new GUIContent("페이드 아웃 (초)"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("musicRepeatDelaySeconds"), new GUIContent("곡 사이 대기 (초)"));
            EditorGUILayout.HelpBox("음악은 기본 음량 × 설정의 음악 음량 × 전체 음량으로 재생됩니다. 효과음 설정과는 독립적입니다.", MessageType.Info);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("공간음 기본값", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(spatialMinDistance,
                new GUIContent("Spatial Min Distance"));

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("페이드·노이즈 기본값", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(defaultFadeInSeconds,
                new GUIContent("Default Fade In (초)"));
            EditorGUILayout.PropertyField(defaultFadeOutSeconds,
                new GUIContent("Default Fade Out (초)"));
            EditorGUILayout.PropertyField(noiseMasterVolume,
                new GUIContent("Noise Master Volume"));
            EditorGUILayout.PropertyField(noiseMasterPitch,
                new GUIContent("Noise Master Pitch"));

            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(
                "Project 창의 .wav를 각 이벤트의 '본음 WAV' 또는 '노이즈 WAV' 칸으로 드래그하세요. " +
                "노이즈 WAV가 비어 있으면 노이즈 레이어 없이 재생됩니다. " +
                "Concurrency 설정은 단발성 원샷의 중복 재생을 제어합니다.",
                MessageType.Info);

            SyncFoldoutState();
            showEvents = EditorGUILayout.BeginFoldoutHeaderGroup(showEvents, "이벤트별 WAV 및 재생 설정");
            if (showEvents)
            {
                if (eventOverrides == null || eventOverrides.arraySize == 0)
                {
                    EditorGUILayout.HelpBox("이벤트 슬롯이 없습니다. Stage1AudioManager 에셋을 다시 생성하거나 슬롯을 추가하세요.",
                        MessageType.Warning);
                }
                else
                {
                    for (var i = 0; i < eventOverrides.arraySize; i++)
                        DrawEventOverride(i, eventOverrides.GetArrayElementAtIndex(i));
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            serializedObject.ApplyModifiedProperties();
        }

        void DrawEventOverride(int index, SerializedProperty element)
        {
            var eventId = element.FindPropertyRelative("eventId");
            var id = eventId != null ? eventId.enumValueIndex : -1;
            var title = id >= 0 && id < (int)SoundEventId.Count
                ? ((SoundEventId)id).ToString() + "  (ID " + id + ")"
                : "Event Slot " + index;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            expandedEvents[index] = EditorGUILayout.Foldout(expandedEvents[index], title, true);
            if (expandedEvents[index])
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(eventId, new GUIContent("Event ID"));
                EditorGUILayout.PropertyField(element.FindPropertyRelative("enabled"),
                    new GUIContent("Enabled"));
                DrawAudioClipField(element.FindPropertyRelative("clipOverride"), "본음 WAV");
                DrawAudioClipField(element.FindPropertyRelative("noiseClipOverride"), "노이즈 WAV");
                EditorGUILayout.PropertyField(element.FindPropertyRelative("volumeScale"),
                    new GUIContent("Volume Scale"));
                EditorGUILayout.PropertyField(element.FindPropertyRelative("pitchScale"),
                    new GUIContent("Pitch Scale"));
                EditorGUILayout.PropertyField(element.FindPropertyRelative("maxDistanceScale"),
                    new GUIContent("Max Distance Scale"));
                EditorGUILayout.PropertyField(element.FindPropertyRelative("fadeInScale"),
                    new GUIContent("Fade In Scale"));
                EditorGUILayout.PropertyField(element.FindPropertyRelative("fadeOutScale"),
                    new GUIContent("Fade Out Scale"));
                EditorGUILayout.PropertyField(element.FindPropertyRelative("noiseVolumeScale"),
                    new GUIContent("Noise Volume Scale"));
                EditorGUILayout.PropertyField(element.FindPropertyRelative("noisePitchScale"),
                    new GUIContent("Noise Pitch Scale"));
                EditorGUILayout.PropertyField(element.FindPropertyRelative("loopPolicy"),
                    new GUIContent("Loop Policy"));
                EditorGUILayout.PropertyField(element.FindPropertyRelative("concurrencyPolicy"),
                    new GUIContent("Concurrency Policy"));
                EditorGUILayout.PropertyField(element.FindPropertyRelative("maxInstances"),
                    new GUIContent("Max Instances"));
                EditorGUILayout.PropertyField(element.FindPropertyRelative("minRetriggerInterval"),
                    new GUIContent("Min Retrigger Interval (초)"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
        }

        static void DrawAudioClipField(SerializedProperty property, string label)
        {
            if (property == null) return;

            var previousMixedValue = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            var clip = EditorGUILayout.ObjectField(new GUIContent(label),
                property.objectReferenceValue, typeof(AudioClip), false);
            if (EditorGUI.EndChangeCheck()) property.objectReferenceValue = clip;
            EditorGUI.showMixedValue = previousMixedValue;
        }

        void SyncFoldoutState()
        {
            var size = eventOverrides != null ? eventOverrides.arraySize : 0;
            if (expandedEvents != null && expandedEvents.Length == size) return;

            var previous = expandedEvents;
            expandedEvents = new bool[size];
            if (previous == null) return;
            Array.Copy(previous, expandedEvents, Mathf.Min(previous.Length, expandedEvents.Length));
        }
    }
}
