using Metagame.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public class AstraV2MechanismTests
    {
        [TearDown] public void Cleanup() { WorldEvents.RemoteDecides=false; }
        [Test] public void CircuitRequiresAllThreeTargetsAndStaysSolved()
        {
            var go=new GameObject("test circuit");
            try
            {
                var puzzle=go.AddComponent<AstraV2Puzzle>();
                puzzle.TurnDial(0); puzzle.TurnDial(0);
                Assert.That(puzzle.Solved,Is.False);
                puzzle.TurnDial(1); puzzle.TurnDial(1);
                Assert.That(puzzle.Solved,Is.False);
                puzzle.TurnDial(2); puzzle.TurnDial(2);
                Assert.That(puzzle.Solved,Is.True);
                puzzle.TurnDial(0);
                Assert.That(puzzle.Solved,Is.True);
                Assert.That(puzzle.OpenFraction,Is.Zero,"Solving only reveals the plate, it does not open the gate.");
            }
            finally { Object.DestroyImmediate(go); }
        }
        [Test] public void ClientCannotSolveCircuitOrReleaseLift()
        {
            var go=new GameObject("remote mechanisms");
            try
            {
                WorldEvents.RemoteDecides=true;
                var puzzle=go.AddComponent<AstraV2Puzzle>(); var lift=go.AddComponent<AstraV2FreightLift>();
                var before=puzzle.Snapshot; puzzle.TurnDial(0);
                Assert.That(puzzle.Snapshot.A,Is.EqualTo(before.A)); Assert.That(lift.TryUnlock(),Is.False);
            }
            finally { Object.DestroyImmediate(go); }
        }
        [Test] public void PlateRejectsPlayerBelowAboveAndOutside()
        {
            var top=new Vector3(2,5,7); var size=new Vector3(1.6f,.25f,1.6f);
            Assert.That(AstraV2Puzzle.FootOnPlate(top,top,size),Is.True);
            Assert.That(AstraV2Puzzle.FootOnPlate(top+Vector3.down*2,top,size),Is.False);
            Assert.That(AstraV2Puzzle.FootOnPlate(top+Vector3.up,top,size),Is.False);
            Assert.That(AstraV2Puzzle.FootOnPlate(top+Vector3.right,top,size),Is.False);
        }
        [Test] public void LiftNeedsUnlockAndHeldBrakeAndStopsOnRelease()
        {
            Assert.That(AstraV2FreightLift.StepTravel(0,false,true,false,1,6),Is.Zero);
            Assert.That(AstraV2FreightLift.StepTravel(.3f,true,false,false,1,6),Is.EqualTo(.3f));
            Assert.That(AstraV2FreightLift.StepTravel(.3f,true,true,false,1,6),Is.GreaterThan(.3f));
            Assert.That(AstraV2FreightLift.StepTravel(1,true,true,false,1,6),Is.EqualTo(1));
            Assert.That(AstraV2FreightLift.StepTravel(.3f,true,false,true,1,6),Is.LessThan(.3f));
        }
        [Test] public void LateJoinRestoresSolvedGateAndLiftState()
        {
            var go=new GameObject("observer");
            try
            {
                var puzzle=go.AddComponent<AstraV2Puzzle>(); var lift=go.AddComponent<AstraV2FreightLift>();
                puzzle.ApplySnapshot(new AstraV2Puzzle.State {A=0,B=1,C=2,Solved=true,Occupied=true,Reveal=1,Open=.6f});
                lift.ApplySnapshot(new AstraV2FreightLift.State {Unlocked=true,Travel=.7f});
                Assert.That(puzzle.Solved && puzzle.Occupied,Is.True); Assert.That(puzzle.OpenFraction,Is.EqualTo(.6f));
                Assert.That(lift.Unlocked,Is.True); Assert.That(lift.Travel,Is.EqualTo(.7f));
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
