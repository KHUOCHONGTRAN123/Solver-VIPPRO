using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CatDom.CoreSolver;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace CatDom.CoreSolver.IO
{
    [Serializable]
    public sealed class ShapeDefinition { public int holeType; public Cell[] footprint; }

    [Serializable]
    public sealed class ShapeCatalog { public List<ShapeDefinition> shapes = new List<ShapeDefinition>(); }

    public static class SolverJson
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Converters = new List<JsonConverter> { new StringEnumConverter() }
        };

        public static ShapeCatalog ReadCatalog(string json) => JsonConvert.DeserializeObject<ShapeCatalog>(json) ?? throw new ArgumentException("Missing shape catalog.");
        public static string WriteResult(SolveResult result) => JsonConvert.SerializeObject(result, Settings);
        public static string WriteInput(BoardInput input) => JsonConvert.SerializeObject(input, Settings);
        public static BoardInput ReadInput(string json) => JsonConvert.DeserializeObject<BoardInput>(json) ?? throw new ArgumentException("Missing board input.");

        public static BoardInput ReadLevel(string json, ShapeCatalog catalog)
        {
            if (catalog?.shapes == null) throw new ArgumentException("Shape catalog is required.");
            var root = JObject.Parse(json);
            var board = new BoardInput { width = (int?)root["sizeX"] ?? 0, height = (int?)root["sizeY"] ?? 0 };
            var shapeMap = catalog.shapes.ToDictionary(s => s.holeType, s => s.footprint);
            var obstacles = new HashSet<Cell>();
            foreach (string field in new[] { "obstacleInfos", "brokenObstacleInfos" })
                foreach (var item in Items(root[field]))
                {
                    var cell = ReadCell(item);
                    // Match LevelController.AddNodeToGrid: off-board wall decoration is not a grid blocker.
                    if (cell.x >= 0 && cell.y >= 0 && cell.x < board.width && cell.y < board.height) obstacles.Add(cell);
                }
            foreach (var item in Items(root["catBoxInfos"]))
            {
                var position = ReadCell(item["position"]);
                if (position.x >= 0 && position.y >= 0 && position.x < board.width && position.y < board.height) obstacles.Add(position);
                int dir = (int?)item["direction"] ?? 0;
                if (dir < 0 || dir > 3) throw new ArgumentException("Box direction must be 0..3.");
                var delta = new[] { new Cell(0, 1), new Cell(-1, 0), new Cell(0, -1), new Cell(1, 0) }[dir];
                var queue = Items(item["colorIds"]).Select(c => ReadCat(c, -1, "boxCat", board)).ToArray();
                board.boxes.Add(new BoxInput { id = board.boxes.Count, position = position, direction = dir, requiredHolesToUnlock = (int?)item["requiredHolesToUnlock"] ?? 0, mouth = new Cell(position.x + delta.x, position.y + delta.y), colors = queue.Select(c => c.color).ToArray(), cats = queue });
                RecordExtraFields(item as JObject, new[] { "position", "colorIds", "direction", "requiredHolesToUnlock" }, "box", board.ignoredMechanics);
            }
            foreach (var item in Items(root["towerInfos"]))
            {
                var position = ReadCell(item["position"]);
                obstacles.Add(position);
                var queue = Items(item["colorIds"]).Select(c => ReadCat(c, -1, "towerCat", board)).ToArray();
                board.boxes.Add(new BoxInput { id = board.boxes.Count, position = position, mouth = position, tower = true, colors = queue.Select(c => c.color).ToArray(), cats = queue });
                RecordExtraFields(item as JObject, new[] { "position", "colorIds" }, "tower", board.ignoredMechanics);
            }
            board.obstacles.AddRange(obstacles.OrderBy(c => c.y).ThenBy(c => c.x));
            foreach (var item in Items(root["holeInfos"]))
            {
                int type = (int)item["holeType"];
                if (!shapeMap.TryGetValue(type, out var shape)) throw new ArgumentException($"Missing shape for holeType {type}.");
                var colors = Items(item["colorIds"]).Select(c => (int)c).ToArray();
                var counts = Items(item["numOfCats"]).Select(c => (int)c).ToArray();
                if (colors.Length == 0 || counts.Length == 0) throw new ArgumentException("Hole color/capacity is missing.");
                if (colors.Length != counts.Length || colors.Length > 2) throw new ArgumentException("Gameplay requires one or two matching hole color/capacity layers.");
                board.holes.Add(new HoleInput { id = board.holes.Count, color = colors[0], remaining = counts[0], layerColors = colors, layerCounts = counts, numIced = Math.Max(0, (int?)item["numIced"] ?? 0), hiddenCount = Math.Max(0, (int?)item["hiddenCount"] ?? 0), lockColorId = (int?)item["lockColorId"] ?? -1, locked = ((int?)item["lockColorId"] ?? -1) >= 0, gates = ReadGates(item["gateInfos"]), movementType = (int?)item["movementType"] ?? 0, position = ReadCell(item["position"]), footprint = (Cell[])shape.Clone() });
                RecordExtraFields(item as JObject, new[] { "position", "colorIds", "numOfCats", "holeType", "movementType", "numIced", "hiddenCount", "lockColorId", "gateInfos" }, "hole", board.ignoredMechanics);
            }
            foreach (var item in Items(root["catInfos"]))
            {
                board.cats.Add(ReadCat(item, board.cats.Count, "cat", board));
            }
            foreach (var item in Items(root["linkInfos"]))
            {
                board.links.Add(new LinkInput { holeId1 = (int)item["idHole1"], holeId2 = (int)item["idHole2"] });
                RecordExtraFields(item as JObject, new[] { "idHole1", "idHole2" }, "link", board.ignoredMechanics);
            }
            foreach (var item in Items(root["colorPaths"]))
            {
                board.colorPaths.Add(new ColorPathInput { position = ReadCell(item["position"]), color = (int)item["colorId"] });
                RecordExtraFields(item as JObject, new[] { "position", "colorId" }, "colorPath", board.ignoredMechanics);
            }
            int pickaxes = board.cats.Count(c => c.pickaxeColorId > 0) + board.boxes.Sum(b => b.cats.Count(c => c.pickaxeColorId > 0));
            foreach (var item in Items(root["coverBoxInfos"]))
            {
                board.covers.Add(new CoverInput { id = board.covers.Count, remainingHits = pickaxes, cells = Items(item["positions"]).Select(ReadCell).ToArray() });
                RecordExtraFields(item as JObject, new[] { "positions" }, "cover", board.ignoredMechanics);
            }
            RecordExtraFields(root, new[] { "sizeX", "sizeY", "holeInfos", "catInfos", "catBoxInfos", "obstacleInfos", "brokenObstacleInfos", "time", "difficulty", "boxCatAsCell", "towerInfos", "linkInfos", "colorPaths", "coverBoxInfos" }, "level", board.ignoredMechanics);
            // Ads-booster cells remain blocked for a solution using only hole drags.
            // Removing them requires an external booster action, not a puzzle transition.
            return board;
        }

        public static GateInput[] ReadGates(JToken token)
        {
            if (!(token is JObject obj)) return Array.Empty<GateInput>();
            return obj.Properties().Select(p =>
            {
                var xy = p.Name.Trim('(', ')').Split(',');
                if (xy.Length != 2) throw new ArgumentException("Invalid gate coordinate: " + p.Name);
                int value = (int)p.Value;
                if (value < 0 || value > 15) throw new ArgumentException("Gate directions must be a four-bit mask.");
                return new GateInput { local = new Cell(int.Parse(xy[0]), int.Parse(xy[1])), directions = value };
            }).ToArray();
        }

        private static CatInput ReadCat(JToken item, int id, string prefix, BoardInput board)
        {
            RecordExtraFields(item as JObject, new[] { "position", "colorId", "keyColorIds", "pickaxeColorId", "numIced" }, prefix, board.ignoredMechanics);
            return new CatInput { id = id, color = (int)item["colorId"], position = ReadCell(item["position"]), keyColorId = (int?)item["keyColorIds"] ?? -1, pickaxeColorId = (int?)item["pickaxeColorId"] ?? -1, numIced = Math.Max(0, (int?)item["numIced"] ?? 0) };
        }

        public static void RecordExtraFields(JObject source, string[] supported, string prefix, List<string> warnings)
        {
            if (source == null) return;
            var allowed = new HashSet<string>(supported);
            foreach (var p in source.Properties())
            {
                if (allowed.Contains(p.Name) || p.Value.Type == JTokenType.Null || (p.Value is JContainer container && !container.HasValues)) continue;
                if (p.Value.Type == JTokenType.Boolean && !(bool)p.Value) continue;
                if (p.Value.Type == JTokenType.Integer || p.Value.Type == JTokenType.Float)
                {
                    double value = (double)p.Value;
                    // Zero is a real color/index for key, lock and similar mechanics.
                    if (value < 0 || (value == 0 && !p.Name.EndsWith("Id", StringComparison.Ordinal) && !p.Name.EndsWith("Index", StringComparison.Ordinal))) continue;
                }
                AddWarning(warnings, prefix + "." + p.Name + " ignored");
            }
        }

        private static void AddWarning(List<string> warnings, string warning) { if (!warnings.Contains(warning)) warnings.Add(warning); }
        private static IEnumerable<JToken> Items(JToken token) => token is JArray array ? array : Enumerable.Empty<JToken>();
        private static Cell ReadCell(JToken token)
        {
            if (token == null || token["x"] == null || token["y"] == null) throw new ArgumentException("Missing cell coordinates.");
            return new Cell((int)token["x"], (int)token["y"]);
        }

        public static string ToMoveText(SolveResult result)
        {
            var text = new StringBuilder(); text.AppendLine(result.status + ": " + result.message);
            for (int i = 0; i < result.moves.Count; i++)
            {
                var m = result.moves[i];
                text.Append(i + 1).Append(". H").Append(m.holeId).Append(": ");
                text.Append(string.Join(" -> ", m.path.Select(p => p.ToString())));
                if (m.eaten.Count > 0) text.Append("; eat ").Append(m.eaten.Count);
                text.AppendLine();
            }
            return text.ToString();
        }
    }
}
