using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    public sealed class VegetationInteractionTests
    {
        readonly List<GameObject> _objects = new();
        readonly Collider[] _hits = new Collider[4];
        static readonly Vector3 Origin = new(80f, 80f, 80f);

        PlantRustle Plant(float multiplier)
        {
            var root = new GameObject("Test vegetation") { layer = GameLayers.Prop };
            _objects.Add(root);
            root.transform.position = Origin;
            new GameObject("Visual").transform.SetParent(root.transform, false);
            root.AddComponent<BoxCollider>().isTrigger = true;
            var plant = root.AddComponent<PlantRustle>();
            var serialized = new SerializedObject(plant);
            serialized.FindProperty("_movementSpeedMultiplier").floatValue = multiplier;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Physics.SyncTransforms();
            return plant;
        }

        float Sample(Vector3 position) => PlantRustle.SampleMovementMultiplier(position, _hits);

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _objects) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        [Test] public void ShortGrassDoesNotSlowOrBlock()
        {
            var plant = Plant(1f);
            Assert.That(Sample(Origin), Is.EqualTo(1f));
            Assert.That(plant.GetComponent<Collider>().isTrigger, Is.True);
            Assert.That(Physics.Raycast(Origin + Vector3.left * 2f, Vector3.right, 4f,
                GameLayers.PropMask, QueryTriggerInteraction.Ignore), Is.False);
        }

        [Test] public void TallGrassSlowsOnlyInsideItsVolume()
        {
            Plant(.65f);
            Assert.That(Sample(Origin), Is.EqualTo(.65f).Within(.001f));
            Assert.That(Sample(Origin + Vector3.right * 3f), Is.EqualTo(1f));
        }

        [Test] public void OverlappingPlantsUseStrongestRatherThanMultiplying()
        {
            Plant(.8f); Plant(.65f); Plant(.7f);
            Assert.That(Sample(Origin), Is.EqualTo(.65f).Within(.001f));
        }

        [Test] public void SaturatedBufferStillFindsStrongestSlowdown()
        {
            for (int i = 0; i < 8; i++) Plant(.8f);
            Plant(.4f);
            Assert.That(Sample(Origin), Is.EqualTo(.4f).Within(.001f));
        }

        [Test] public void DisabledComponentAndColliderReleaseSlowdownImmediately()
        {
            var plant = Plant(.65f);
            plant.enabled = false;
            Assert.That(Sample(Origin), Is.EqualTo(1f));
            plant.enabled = true;
            plant.GetComponent<Collider>().enabled = false;
            Physics.SyncTransforms();
            Assert.That(Sample(Origin), Is.EqualTo(1f));
        }

        [Test] public void DestroyAndTeleportLeaveNoSlowdownState()
        {
            var plant = Plant(.65f);
            Assert.That(Sample(Origin), Is.LessThan(1f));
            plant.transform.position += Vector3.forward * 10f;
            Physics.SyncTransforms();
            Assert.That(Sample(Origin), Is.EqualTo(1f));
            Object.DestroyImmediate(plant.gameObject);
            Assert.That(Sample(Origin), Is.EqualTo(1f));
        }

        [Test] public void PulseChangesSwayWithoutMovingRootOrCollider()
        {
            var plant = Plant(1f);
            var before = plant.GetComponent<Collider>().bounds;
            plant.Pulse(Vector3.forward, 4f);
            Assert.That(plant.CurrentSway.sqrMagnitude, Is.GreaterThan(.001f));
            Assert.That(plant.transform.position, Is.EqualTo(Origin));
            Assert.That(plant.GetComponent<Collider>().bounds, Is.EqualTo(before));
        }

        [Category("Heavy")]
        [UnityTest]
        public IEnumerator PlayerContactActuallyTriggersVisibleSwayAndSettles()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            var plant = Plant(.45f);
            Assert.That(Application.isPlaying, Is.True);
            var player = new GameObject("Contact test player") { layer = GameLayers.Player };
            _objects.Add(player);
            player.transform.position = Origin + Vector3.right * 2f;
            var rig = player.AddComponent<PlayerRig>();
            rig.enabled = false; // This probe tests physics contact, not input/bootstrap.
            var body = player.AddComponent<Rigidbody>();
            body.useGravity = false;
            player.AddComponent<SphereCollider>().radius = .2f;
            rig.Doll = new Doll { Hips = body };
            Assert.That(Physics.GetIgnoreLayerCollision(GameLayers.Player, GameLayers.Prop), Is.False);
            yield return new WaitForFixedUpdate();
            player.transform.position = Origin + Vector3.right * .2f;
            Physics.SyncTransforms();
            Assert.That(Sample(player.transform.position), Is.EqualTo(.45f).Within(.001f));
            for (int i = 0; i < 5 && plant.CurrentSway.sqrMagnitude < .01f; i++)
            {
                yield return new WaitForFixedUpdate();
                yield return null;
            }
            Assert.That(plant.CurrentSway.magnitude, Is.GreaterThan(1f),
                $"Real trigger must visibly bend the plant. mode={Physics.simulationMode}, player={player.transform.position}, plant={plant.transform.position}, playing={Application.isPlaying}, dt={Time.deltaTime}");
            Assert.That(Quaternion.Angle(plant.Visual.localRotation, Quaternion.identity), Is.GreaterThan(1f));
            Assert.That(Sample(body.position), Is.EqualTo(.45f).Within(.001f));
            player.transform.position = Origin + Vector3.right * 3f;
            Physics.SyncTransforms();
            Assert.That(Sample(body.position), Is.EqualTo(1f), "Leaving releases speed immediately.");
            // EditMode's EnterPlayMode bridge does not reliably honor WaitForSeconds.
            float settleUntil = Time.time + 2.5f;
            while (Time.time < settleUntil) yield return null;
            Assert.That(plant.CurrentSway.magnitude, Is.LessThan(.15f), "Sway should settle back.");
            Cleanup();
            yield return new ExitPlayMode();
        }
    }
}
