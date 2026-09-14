using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Metagame.Gameplay.Parts;

namespace Metagame.Gameplay.Tests
{
    public sealed class AstraExpansionTests
    {
        readonly List<GameObject> objects = new List<GameObject>();
        GameObject New(string name) { var go = new GameObject(name); objects.Add(go); return go; }
        [SetUp] public void Setup() => WorldEvents.RemoteDecides = false;
        [TearDown] public void Cleanup()
        {
            for(int i=objects.Count-1;i>=0;i--) if(objects[i]!=null) Object.DestroyImmediate(objects[i]);
            objects.Clear(); WorldEvents.RemoteDecides=false;
        }
        [Test] public void TankStartsEmptyAndNeverOverfills()
        {
            var tank=New("tank").AddComponent<AstraFuelTank>();
            Assert.That(tank.HasFuel,Is.False);
            Assert.That(tank.Add(100),Is.EqualTo(60));
            Assert.That(tank.Add(10),Is.Zero);
            Assert.That(tank.Units,Is.EqualTo(tank.Capacity));
        }
        [Test] public void RefuellingConservesFuelWhenTankIsNearlyFull()
        {
            var tank=New("tank").AddComponent<AstraFuelTank>();tank.Add(50);
            var can=New("can").AddComponent<AstraFuelCan>();
            Assert.That(can.TransferTo(tank),Is.EqualTo(10));
            Assert.That(can.Units,Is.EqualTo(20));
            Assert.That(can.TransferTo(tank),Is.Zero);
        }
        [Test] public void FuelConsumptionStopsAtZeroAndBoostCostsMore()
        {
            var tank=New("tank").AddComponent<AstraFuelTank>();tank.Add(10);
            Assert.That(tank.Consume(2,true),Is.True);
            Assert.That(tank.Units,Is.EqualTo(6.8f).Within(.001));
            Assert.That(tank.Consume(20),Is.False);
            Assert.That(tank.Units,Is.Zero);
        }
        [Test] public void InvalidFuelInputDoesNotCorruptTank()
        {
            var tank=New("tank").AddComponent<AstraFuelTank>();tank.Add(10);
            Assert.That(tank.Add(float.NaN),Is.Zero);
            Assert.That(tank.Add(float.PositiveInfinity),Is.Zero);
            Assert.That(tank.Consume(-10),Is.False);
            Assert.That(tank.Units,Is.EqualTo(10));
        }
        [Test] public void RemoteClientCannotGenerateOrSpendFuel()
        {
            var tank=New("tank").AddComponent<AstraFuelTank>();tank.ApplySnapshot(8);
            var can=New("can").AddComponent<AstraFuelCan>();
            WorldEvents.RemoteDecides=true;
            Assert.That(can.TransferTo(tank),Is.Zero);
            Assert.That(tank.Consume(5),Is.False);
            Assert.That(tank.Units,Is.EqualTo(8));
            Assert.That(can.Units,Is.EqualTo(30));
        }
        [Test] public void FuelRequirementIsOptionalForOriginalVehicles()
        {
            var vehicle=New("vehicle").AddComponent<TransportVehicle>();
            Assert.That(vehicle.TryGetComponent<AstraFuelTank>(out _),Is.False);
        }
        AstraRocketTool Rocket()
        {
            var go=New("rocket");var body=go.AddComponent<Rigidbody>();
            var rocket=go.AddComponent<AstraRocketTool>();
            var player=New("pilot").AddComponent<PlayerRig>();
            rocket.Equip(player,body);return rocket;
        }
        [Test] public void RocketBurnIsTwoSecondsAndCannotRetriggerDuringBurn()
        {
            var rocket=Rocket();
            Assert.That(rocket.TryIgnite(Vector3.up),Is.True);
            Assert.That(rocket.TryIgnite(Vector3.right),Is.False);
            Assert.That(rocket.ViewEffectDirection,Is.EqualTo(Vector3.up));
            rocket.Step(1.99f);Assert.That(rocket.Boosting,Is.True);
            rocket.Step(.02f);Assert.That(rocket.Boosting,Is.False);
            Assert.That(rocket.TryIgnite(Vector3.right),Is.False,"cooling down");
        }
        [Test] public void DroppingRocketCancelsThrust()
        {
            var rocket=Rocket();rocket.TryIgnite(Vector3.up);rocket.Unequip();
            Assert.That(rocket.Boosting,Is.False);
            Assert.That(rocket.TryIgnite(Vector3.up),Is.False);
        }
        [Test] public void FatalTrenchTestsFeetAgainstRotatedVolume()
        {
            var go=New("trench");go.transform.SetPositionAndRotation(new Vector3(4,-2,5),Quaternion.Euler(0,40,0));
            go.AddComponent<BoxCollider>().size=new Vector3(3,4,12);
            var pit=go.AddComponent<AstraFatalTrench>();
            Assert.That(pit.ContainsFoot(go.transform.position),Is.True);
            Assert.That(pit.ContainsFoot(new Vector3(4,1,5)),Is.False,"safe rim above trench");
            Assert.That(pit.ContainsFoot(go.transform.TransformPoint(new Vector3(3,0,0))),Is.False);
        }
        [Test] public void InkChoosesVisibleSurfaceAndEnforcesCooldown()
        {
            var wall=New("wall");wall.transform.position=Vector3.forward*4;wall.AddComponent<BoxCollider>();
            var go=New("ink");var tool=go.AddComponent<AstraInkLauncher>();
            tool.Equip(New("pilot").AddComponent<PlayerRig>(),go.AddComponent<Rigidbody>());
            Physics.SyncTransforms();
            Assert.That(tool.Fire(Vector3.forward),Is.True);
            Assert.That(tool.Snapshot.Hit,Is.True);
            Assert.That(tool.Snapshot.End.z,Is.EqualTo(3.5f).Within(.02));
            Assert.That(tool.Fire(Vector3.forward),Is.False);
        }
    }
}
