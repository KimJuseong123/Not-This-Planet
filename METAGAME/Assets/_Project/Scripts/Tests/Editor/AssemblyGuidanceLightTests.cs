using Metagame.Gameplay.Parts;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    public sealed class AssemblyGuidanceLightTests
    {
        [TestCase(0, false, 0)]
        [TestCase(1, false, 3)]
        [TestCase(1, true, 1)]
        [TestCase(2, true, 2)]
        [TestCase(3, true, 3)]
        [TestCase(4, true, -1)]
        [TestCase(0, true, 0)]
        public void GuidanceFollowsTractorUnlockThenAssembly(int placed, bool unlocked, int expected)
        {
            var floors = new ShipAssembly.FloorState[4];
            for (int i = 0; i < placed; i++) floors[i] = ShipAssembly.FloorState.Stacked;
            Assert.AreEqual(expected, AssemblyGuidanceLight.RequiredSignal(floors, unlocked));
        }

        [Test]
        public void EmptyTowerRequestsEngine()
        {
            Assert.AreEqual(0, AssemblyGuidanceLight.RequiredFloor(new ShipAssembly.FloorState[4]));
        }

        [Test]
        public void StackedFloorDoesNotRequireWeldingToAdvance()
        {
            Assert.AreEqual(2, AssemblyGuidanceLight.RequiredFloor(new[] {
                ShipAssembly.FloorState.Welded, ShipAssembly.FloorState.Stacked,
                ShipAssembly.FloorState.Empty, ShipAssembly.FloorState.Empty }));
        }

        [Test]
        public void WrongFloorTakesPriorityOverLaterEmptyFloor()
        {
            Assert.AreEqual(1, AssemblyGuidanceLight.RequiredFloor(new[] {
                ShipAssembly.FloorState.Welded, ShipAssembly.FloorState.WrongPart,
                ShipAssembly.FloorState.Empty, ShipAssembly.FloorState.Empty }));
        }

        [Test]
        public void CompletedTowerHasNoNextFloor()
        {
            Assert.AreEqual(-1, AssemblyGuidanceLight.RequiredFloor(new[] {
                ShipAssembly.FloorState.Welded, ShipAssembly.FloorState.Stacked,
                ShipAssembly.FloorState.Stacked, ShipAssembly.FloorState.Stacked }));
        }
    }
}
