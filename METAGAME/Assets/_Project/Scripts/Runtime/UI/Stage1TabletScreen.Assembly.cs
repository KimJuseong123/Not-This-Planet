using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using UnityEngine;
using UnityEngine.UI;

namespace Metagame.UI
{
    public sealed partial class Stage1TabletScreen
    {
        readonly RawImage[] _assemblyParts = new RawImage[4];
        readonly RawImage[] _assemblySteps = new RawImage[3];
        readonly Text[] _assemblyStepLabels = new Text[3];
        readonly Text[] _assemblyStepChecks = new Text[3];
        readonly Text[] _assemblyChecks = new Text[4];
        Material _assemblyInk;
        RawImage _assemblyActionArt;
        Text _assemblyAction;
        Text _assemblyCount;
        Texture2D _assemblyActions;

        static readonly Color Chalk = new(0.80f, 0.95f, 0.98f);
        static readonly Color GhostChalk = new(0.32f, 0.40f, 0.44f);

        // Approved illustrations stay as source textures. UV crops retain the original
        // proportions; the ink shader removes only the dark backing, never the linework.
        void CreateAssemblyArtwork()
        {
            var ship = Resources.Load<Texture2D>("TabletAssembly/Ship");
            _assemblyActions = Resources.Load<Texture2D>("TabletAssembly/Actions");
            var shader = Resources.Load<Shader>("TabletAssembly/ChalkInk");
            if (ship == null || _assemblyActions == null || shader == null) return;
            _assemblyInk = new Material(shader) { name = "Tablet assembly ink" };

            // Keep the existing silhouette as a fallback if a resource is unavailable.
            foreach (var floor in towerFloors)
            {
                floor.Fill.gameObject.SetActive(false);
                floor.Outline.gameObject.SetActive(false);
                floor.Name.gameObject.SetActive(false);
            }
            var diagram = Panel(partsRoot.transform, "AssemblyDiagram", Color.clear,
                new Vector2(0.08f, 0f), new Vector2(0.47f, 1f), Vector2.zero, Vector2.zero);
            var art = Panel(diagram.transform, "ProportionalArtwork", Color.clear,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var fit = art.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 710f / 860f;
            // Coordinates in the original 1774x887 source, top-left origin.
            Rect[] crops = { new(340, 643, 360, 217), new(340, 515, 360, 128),
                new(340, 291, 360, 224), new(340, 15, 360, 276) };
            for (var i = 0; i < crops.Length; i++)
            {
                var crop = crops[i];
                var min = new Vector2(crop.x / 710f, 1f - crop.yMax / 860f);
                var max = new Vector2(crop.xMax / 710f, 1f - crop.y / 860f);
                _assemblyParts[i] = Ink(art.transform, $"AssemblyPart{i + 1}", ship,
                    SourceUv(ship, crop), min, max, false);
                _assemblyChecks[i] = Label(art.transform, $"AssemblyCheck{i + 1}", 26,
                    TextAnchor.MiddleCenter, Chalk, new Vector2(0.97f, min.y),
                    new Vector2(1.08f, max.y), Vector2.zero, Vector2.zero);
            }
            Ink(art.transform, "AssemblyMascot", ship, SourceUv(ship, new Rect(35, 580, 230, 280)),
                new Vector2(0f, 0f), new Vector2(0.36f, 0.34f));

            string[] labels = { "가져오기", "배치", "용접" };
            Rect[] icons = { new(669, 116, 114, 110), new(843, 116, 114, 110),
                new(1031, 116, 114, 110) };
            for (var i = 0; i < 3; i++)
            {
                var x = 0.54f + i * 0.145f;
                _assemblySteps[i] = Ink(partsRoot.transform, $"AssemblyStep{i}", _assemblyActions,
                    NormalizedMockupUv(icons[i]), new Vector2(x, 0.71f), new Vector2(x + 0.11f, 0.98f));
                _assemblyStepLabels[i] = Label(partsRoot.transform, $"AssemblyStepLabel{i}", 25,
                    TextAnchor.MiddleCenter, Chalk, new Vector2(x - 0.015f, 0.60f),
                    new Vector2(x + 0.125f, 0.73f), Vector2.zero, Vector2.zero);
                _assemblyStepLabels[i].text = labels[i];
                _assemblyStepChecks[i] = Label(partsRoot.transform, $"AssemblyStepCheck{i}", 25,
                    TextAnchor.MiddleCenter, Chalk, new Vector2(x + 0.095f, 0.71f),
                    new Vector2(x + 0.13f, 0.82f), Vector2.zero, Vector2.zero);
            }
            _assemblyActionArt = Ink(partsRoot.transform, "AssemblyActionArt", _assemblyActions,
                NormalizedMockupUv(new Rect(739, 298, 309, 198)),
                new Vector2(0.58f, 0.27f), new Vector2(0.92f, 0.57f));
            _assemblyAction = Label(partsRoot.transform, "AssemblyAction", 28,
                TextAnchor.MiddleCenter, Chalk, new Vector2(0.50f, 0.07f),
                new Vector2(0.97f, 0.24f), Vector2.zero, Vector2.zero);
            _assemblyCount = Label(partsRoot.transform, "AssemblyCount", 24,
                TextAnchor.MiddleRight, Chalk, new Vector2(0.57f, -0.075f),
                new Vector2(0.94f, 0.035f), Vector2.zero, Vector2.zero);
        }

        void RefreshAssemblyArtwork(IReadOnlyList<ShipAssembly.FloorState> floors)
        {
            if (_assemblyInk == null) return;
            var placed = 0;
            var welded = 0;
            var wrong = HasWrongFloor(floors);
            for (var i = 0; i < _assemblyParts.Length; i++)
            {
                var state = floors != null && i < floors.Count ? floors[i] : ShipAssembly.FloorState.Empty;
                var present = state is ShipAssembly.FloorState.Stacked or ShipAssembly.FloorState.Welded;
                var complete = state == ShipAssembly.FloorState.Welded;
                if (present) placed++;
                if (complete) welded++;
                _assemblyParts[i].color = state is ShipAssembly.FloorState.WrongPart or ShipAssembly.FloorState.Tilted
                    ? Wrong : present ? Chalk : GhostChalk;
                _assemblyChecks[i].text = complete ? "✓" : "";
            }
            // Placement is not welding or launch readiness. Never show a ready-to-launch
            // claim merely because all four modules have been placed.
            var awaitingWeld = placed > welded;
            var step = wrong ? 1 : awaitingWeld || placed == 4 ? 2 : exploration.DiscoveredCount > placed ? 1 : 0;
            for (var i = 0; i < _assemblySteps.Length; i++)
            {
                var done = !wrong && (i < 2 ? placed == 4 : welded == 4);
                _assemblySteps[i].color = i == step || done ? Chalk : GhostChalk;
                _assemblyStepLabels[i].color = i == step || done ? Chalk : GhostChalk;
                _assemblyStepChecks[i].text = done ? "✓" : "";
            }
            _assemblyActionArt.uvRect = NormalizedMockupUv(awaitingWeld && !wrong
                ? new Rect(705, 920, 405, 200) : new Rect(739, 298, 309, 198));
            _assemblyActionArt.GetComponent<AspectRatioFitter>().aspectRatio =
                awaitingWeld && !wrong ? 405f / 200f : 309f / 198f;
            _assemblyActionArt.gameObject.SetActive(welded < 4 || wrong);
            _assemblyAction.text = wrong ? "파츠를 다시 놓아 주세요"
                : welded == 4 ? "조립 완료!"
                : awaitingWeld ? "제자리에 놓였어요!\n용접해 주세요"
                : "파츠를 가져와 주세요";
            _assemblyAction.color = wrong ? Wrong : Chalk;
            _assemblyCount.text = $"배치 {placed} / 4" + (awaitingWeld ? " · 용접 대기" : "");
        }

        static Rect SourceUv(Texture texture, Rect pixels) => new(
            pixels.x / texture.width, 1f - pixels.yMax / texture.height,
            pixels.width / texture.width, pixels.height / texture.height);

        // The comparison art has a fixed 1235:1274 composition, independent of export resolution.
        static Rect NormalizedMockupUv(Rect pixels) => new(
            pixels.x / 1235f, 1f - pixels.yMax / 1274f, pixels.width / 1235f, pixels.height / 1274f);

        RawImage Ink(Transform parent, string name, Texture texture, Rect uv,
            Vector2 min, Vector2 max, bool fit = true)
        {
            var frame = Panel(parent, name + "Frame", Color.clear, min, max, Vector2.zero, Vector2.zero);
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(frame.transform, false);
            var image = go.GetComponent<RawImage>();
            image.rectTransform.anchorMin = Vector2.zero;
            image.rectTransform.anchorMax = Vector2.one;
            image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
            image.texture = texture;
            image.uvRect = uv;
            image.material = _assemblyInk;
            image.color = Chalk;
            image.raycastTarget = false;
            if (fit)
            {
                var fitter = go.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fitter.aspectRatio = texture.width * uv.width / (texture.height * uv.height);
            }
            return image;
        }
    }
}
