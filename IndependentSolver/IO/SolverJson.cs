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
    internal sealed class ShapeDefinition
    {
        public int holeType;
        public Cell[] footprint;
    }

    [Serializable]
    internal sealed class ShapeCatalog
    {
        public List<ShapeDefinition> shapes = new List<ShapeDefinition>();
        [JsonIgnore]
        internal Dictionary<int, Cell[]> map;
    }

    internal static class SolverJson
    {
        public static ShapeCatalog ReadCatalog(string json)
        {
            var catalog = JsonConvert.DeserializeObject<ShapeCatalog>(json) ?? throw new ArgumentException("Missing shape catalog.");
            catalog.map = catalog.shapes.ToDictionary(s => s.holeType, s => s.footprint);
            return catalog;
        }

        public static BoardInput ReadLevel(string json, ShapeCatalog catalog)
        {
            if (catalog?.shapes == null)
                throw new ArgumentException("Shape catalog is required.");
            var root = JObject.Parse(json);
            var board = new BoardInput
            {
                width = (int? )root["sizeX"] ?? 0,
                height = (int? )root["sizeY"] ?? 0
            };
            var shapeMap = catalog.map ?? catalog.shapes.ToDictionary(s => s.holeType, s => s.footprint);
            var obstacles = new HashSet<Cell>();
            foreach (string field in new[]
            {
                "obstacleInfos",
                "brokenObstacleInfos"
            }

            )
                foreach (var item in Items(root[field]))
                {
                    var cell = ReadCell(item);
                    // Match LevelController.AddNodeToGrid: off-board wall decoration is not a grid blocker.
                    if (cell.x >= 0 && cell.y >= 0 && cell.x < board.width && cell.y < board.height)
                        obstacles.Add(cell);
                }

            foreach (var item in Items(root["catBoxInfos"]))
            {
                var position = ReadCell(item["position"]);
                if (position.x >= 0 && position.y >= 0 && position.x < board.width && position.y < board.height)
                    obstacles.Add(position);
                int dir = (int? )item["direction"] ?? 0;
                if (dir < 0 || dir > 3)
                    throw new ArgumentException("Box direction must be 0..3.");
                var delta = new[]
                {
                    new Cell(0, 1),
                    new Cell(-1, 0),
                    new Cell(0, -1),
                    new Cell(1, 0)
                }[dir];
                var queue = Items(item["colorIds"]).Select(c => ReadCat(c, -1, "boxCat", board)).ToArray();
                board.boxes.Add(new BoxInput { id = board.boxes.Count, position = position, direction = dir, requiredHolesToUnlock = (int? )item["requiredHolesToUnlock"] ?? 0, mouth = new Cell(position.x + delta.x, position.y + delta.y), colors = queue.Select(c => c.color).ToArray(), cats = queue });
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
                if (!shapeMap.TryGetValue(type, out var shape))
                    throw new ArgumentException($"Missing shape for holeType {type}.");
                var colors = Items(item["colorIds"]).Select(c => (int)c).ToArray();
                var counts = Items(item["numOfCats"]).Select(c => (int)c).ToArray();
                if (colors.Length == 0 || counts.Length == 0)
                    throw new ArgumentException("Hole color/capacity is missing.");
                if (colors.Length != counts.Length || colors.Length > 2)
                    throw new ArgumentException("Gameplay requires one or two matching hole color/capacity layers.");
                board.holes.Add(new HoleInput { id = board.holes.Count, color = colors[0], remaining = counts[0], layerColors = colors, layerCounts = counts, numIced = Math.Max(0, (int? )item["numIced"] ?? 0), hiddenCount = Math.Max(0, (int? )item["hiddenCount"] ?? 0), lockColorId = (int? )item["lockColorId"] ?? -1, locked = ((int? )item["lockColorId"] ?? -1) >= 0, gates = ReadGates(item["gateInfos"]), movementType = (int? )item["movementType"] ?? 0, movementTypeLimit = Math.Max(0, (int?)item["movementTypeLimit"] ?? 0), bombRemaining = Math.Max(0, (int?)item["timeLimited"] ?? 0), countdownMoves = Math.Max(0, (int?)item["countdownMoveLimit"] ?? 0), holeType = type, expandCell = item["posExpand"] == null || item["posExpand"].Type == JTokenType.Null ? new Cell(-1,-1) : ReadCell(item["posExpand"]), isPlug = (bool?)item["isPlug"] ?? false, plugColorId = (int?)item["plugColorId"] ?? -1, axe = (bool?)item["axe"] ?? false, isOpenClose = (bool?)item["isOpenClose"] ?? false, closed = ((bool?)item["isOpenClose"] ?? false) && ((bool?)item["isOpenCloseInitiallyClosed"] ?? true), runesealedUnlockHoleIndex = (int?)item["runesealedUnlockHoleIndex"] ?? -1, position = ReadCell(item["position"]), footprint = (Cell[])shape.Clone() });
                RecordExtraFields(item as JObject, new[] { "position", "colorIds", "numOfCats", "holeType", "movementType", "movementTypeLimit", "countdownMoveLimit", "timeLimited", "posExpand", "isPlug", "plugColorId", "runesealedUnlockHoleIndex", "axe", "isOpenClose", "isOpenCloseInitiallyClosed", "numIced", "hiddenCount", "lockColorId", "gateInfos" }, "hole", board.ignoredMechanics);
            }

            // LevelController transfers the legacy level countdown to the first
            // board hole only when no board hole already has its own countdown.
            int levelCountdown = Math.Max(0, (int?)root["countdownMoveLimit"] ?? 0);
            if (levelCountdown > 0 && board.holes.Count > 0 && !board.holes.Exists(h => h.countdownMoves > 0))
                board.holes[0].countdownMoves = levelCountdown;

            foreach (var item in Items(root["holeSpawners"]))
            {
                var spawner = new SpawnerInput { position = ReadCell(item["position"]), direction = (int)item["direction"] };
                var ids = new List<int>();
                foreach (var queued in Items(item["holeInfos"]))
                {
                    int type = (int)queued["holeType"];
                    if (!shapeMap.TryGetValue(type,out var shape)) throw new ArgumentException("Missing queued hole shape.");
                    var anchor = SpawnerGeometry.Anchor(spawner.position,spawner.direction,shape);
                    var normalized = (JObject)queued.DeepClone();
                    normalized["position"] = JObject.FromObject(anchor);
                    var queuedRoot = new JObject { ["sizeX"] = board.width, ["sizeY"] = board.height, ["holeInfos"] = new JArray(normalized) };
                    var parsed = ReadLevel(queuedRoot.ToString(Formatting.None),catalog);
                    var hole = parsed.holes[0];
                    hole.id = board.holes.Count; hole.pendingSpawn = true;
                    ids.Add(hole.id); board.holes.Add(hole);
                    board.ignoredMechanics.AddRange(parsed.ignoredMechanics);
                }
                spawner.holeIds = ids.ToArray(); board.spawners.Add(spawner);
                RecordExtraFields(item as JObject,new[] { "position", "direction", "holeInfos" },"holeSpawner",board.ignoredMechanics);
            }
            foreach (var item in Items(root["catInfos"]))
            {
                board.cats.Add(ReadCat(item, board.cats.Count, "cat", board));
            }

            foreach (var item in Items(root["plugs"]))
            {
                board.plugs.Add(new PlugInput { id = board.plugs.Count, color = (int)item["colorId"], position = ReadCell(item["position"]) });
                RecordExtraFields(item as JObject, new[] { "position", "colorId" }, "plug", board.ignoredMechanics);
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

            foreach (var item in Items(root["ropeInfos"]))
            {
                board.covers.Add(new CoverInput { id = board.covers.Count, rope = true, color = (int?)item["colorId"] ?? 0,
                    remainingHits = Math.Max(0, (int?)item["count"] ?? 3), cells = Items(item["positions"]).Select(ReadCell).ToArray() });
                RecordExtraFields(item as JObject, new[] { "colorId", "count", "positions" }, "rope", board.ignoredMechanics);
            }
            foreach (var item in Items(root["barricades"]))
            {
                var origin = ReadCell(item["pos"]);
                int direction = (int?)item["direction"] ?? 0, range = (int?)item["range"] ?? 0;
                if (direction < 0 || direction > 3 || range < 0 || range > board.width + board.height)
                    throw new ArgumentException("Invalid barricade direction/range.");
                var delta = new[] { new Cell(0,1), new Cell(1,0), new Cell(0,-1), new Cell(-1,0) }[direction];
                var cells = new Cell[range + 1];
                for (int i = 0; i <= range; i++) cells[i] = new Cell(origin.x + delta.x*i, origin.y + delta.y*i);
                board.covers.Add(new CoverInput { id = board.covers.Count, barricade = true, remainingHits = range + 1, cells = cells });
                RecordExtraFields(item as JObject, new[] { "pos", "direction", "range" }, "barricade", board.ignoredMechanics);
            }
            foreach (var item in Items(root["secretLockboxInfos"]))
            {
                var origin = ReadCell(item["position"]);
                int width = Math.Max(1, (int?)item["sizeX"] ?? 3), height = Math.Max(1, (int?)item["sizeY"] ?? 3);
                var cells = new List<Cell>();
                for (int x = Math.Max(0, origin.x); x < Math.Min(board.width, (long)origin.x + width); x++)
                    for (int y = Math.Max(0, origin.y); y < Math.Min(board.height, (long)origin.y + height); y++) cells.Add(new Cell(x,y));
                var colors = Items(item["colorIds"]).ToArray();
                var counts = Items(item["numCats"]).ToArray();
                var requirements = new Dictionary<int,int>();
                for (int i = 0; i < Math.Min(colors.Length, counts.Length); i++)
                {
                    int amount = Math.Max(0, (int)counts[i]), color = (int)colors[i];
                    if (amount == 0) continue;
                    requirements.TryGetValue(color, out int previous);
                    requirements[color] = checked(previous + amount);
                }
                // No positive requirement never clears in SecretLockbox.Initialize.
                if (requirements.Count == 0) requirements[-1] = 1;
                foreach (var requirement in requirements)
                    board.covers.Add(new CoverInput { id = board.covers.Count, secretLockCounter = true,
                        color = requirement.Key, remainingHits = requirement.Value, cells = cells.ToArray() });
                RecordExtraFields(item as JObject, new[] { "position", "sizeX", "sizeY", "colorIds", "numCats" }, "secretLockbox", board.ignoredMechanics);
            }
            foreach (var item in Items(root["curtainAreaInfos"]))
            {
                var origin = ReadCell(item["position"]);
                int width = Math.Max(1, (int?)item["sizeX"] ?? 1), height = Math.Max(1, (int?)item["sizeY"] ?? 1);
                var cells = new List<Cell>();
                // Runtime areas may extend beyond the board; only grid cells block.
                for (int x = Math.Max(0, origin.x); x < Math.Min(board.width, (long)origin.x + width); x++)
                    for (int y = Math.Max(0, origin.y); y < Math.Min(board.height, (long)origin.y + height); y++)
                        cells.Add(new Cell(x,y));
                board.covers.Add(new CoverInput { id = board.covers.Count, finishHoleCounter = true,
                    remainingHits = Math.Max(0, (int?)item["requiredFinishHoleCount"] ?? 1), cells = cells.ToArray() });
                RecordExtraFields(item as JObject, new[] { "position", "sizeX", "sizeY", "requiredFinishHoleCount" }, "curtainArea", board.ignoredMechanics);
            }
            foreach (var item in Items(root["fenceInfos"]))
            {
                var positions = Items(item["positions"]).ToArray();
                var directions = Items(item["directions"]).ToArray();
                for (int i = 0; i < Math.Min(positions.Length, directions.Length); i++)
                {
                    int direction = (int)directions[i];
                    if (positions[i].Type == JTokenType.Null || direction < 0 || direction > 3) continue;
                    board.fences.Add(new FenceEdgeInput { position = ReadCell(positions[i]), direction = direction,
                        color = (int?)item["colorId"] ?? -1 });
                }
                RecordExtraFields(item as JObject, new[] { "positions", "directions", "colorId" }, "fence", board.ignoredMechanics);
            }
            RecordExtraFields(root, new[] { "sizeX", "sizeY", "holeInfos", "catInfos", "catBoxInfos", "obstacleInfos", "brokenObstacleInfos", "time", "countdownMoveLimit", "difficulty", "boxCatAsCell", "towerInfos", "linkInfos", "colorPaths", "coverBoxInfos", "ropeInfos", "fenceInfos", "curtainAreaInfos", "barricades", "secretLockboxInfos", "plugs", "holeSpawners", "barrieInfos", "elevatorInfos" }, "level", board.ignoredMechanics);
            // Ads-booster cells remain blocked for a solution using only hole drags.
            // Removing them requires an external booster action, not a puzzle transition.
            foreach(var item in Items(root["barrieInfos"]))
            {
                int id=(int?)item["id"]??-1;
                if(id<0 || id>9)continue; // Runtime ignores invalid group IDs.
                bool ValidBarrieCell(Cell cell)=>cell.x>=0 && cell.y>=0 && cell.x<board.width && cell.y<board.height;
                var group=board.barries.Find(g=>g.id==id);
                if(group==null){group=new BarrieInput{id=id};board.barries.Add(group);}
                group.positions=group.positions.Concat(Items(item["positions"]).Where(t=>t.Type!=JTokenType.Null).Select(ReadCell).Where(ValidBarrieCell)).Distinct().ToArray();
                group.switchPositions=group.switchPositions.Concat(Items(item["switchPositions"]).Where(t=>t.Type!=JTokenType.Null).Select(ReadCell).Where(ValidBarrieCell)).Distinct().ToArray();
                RecordExtraFields(item as JObject,new[]{"id","positions","switchPositions"},"barrie",board.ignoredMechanics);
            }
            foreach(var item in Items(root["elevatorInfos"]))
            {
                var child=item["levelInfo"] as JObject;
                if(child==null)throw new ArgumentException("Elevator child level is missing.");
                var parsedChild=ReadLevel(child.ToString(Formatting.None),catalog);
                board.ignoredMechanics.AddRange(parsedChild.ignoredMechanics);
                if(parsedChild.elevators.Count>0 || parsedChild.spawners.Count>0 || parsedChild.barries.Count>0 || parsedChild.obstacles.Count>0 || parsedChild.colorPaths.Count>0)
                    board.ignoredMechanics.Add("elevator child runtime does not spawn nested elevators/spawners/barries/obstacles/colorPaths");
                board.elevators.Add(new ElevatorInput{position=ReadCell(item["position"]),child=parsedChild});
                RecordExtraFields(item as JObject,new[]{"position","levelInfo"},"elevator",board.ignoredMechanics);
            }
            return board;
        }

        public static GateInput[] ReadGates(JToken token)
        {
            if (!(token is JObject obj))
                return Array.Empty<GateInput>();
            return obj.Properties().Select(p =>
            {
                var xy = p.Name.Trim('(', ')').Split(',');
                if (xy.Length != 2)
                    throw new ArgumentException("Invalid gate coordinate: " + p.Name);
                int value = (int)p.Value;
                if (value < 0 || value > 15)
                    throw new ArgumentException("Gate directions must be a four-bit mask.");
                return new GateInput
                {
                    local = new Cell(int.Parse(xy[0]), int.Parse(xy[1])),
                    directions = value
                };
            }).ToArray();
        }

        private static CatInput ReadCat(JToken item, int id, string prefix, BoardInput board)
        {
            // Gameplay timers are intentionally excluded from spatial solving.
            // Applies equally to board cats and cats queued in boxes/towers.
            RecordExtraFields(item as JObject, new[] { "position", "colorId", "keyColorIds", "pickaxeColorId", "scissorsColorId", "mysteryBoxId", "numIced", "bonusTime", "timeLimited" }, prefix, board.ignoredMechanics);
            return new CatInput
            {
                id = id,
                color = (int)item["colorId"],
                position = ReadCell(item["position"]),
                keyColorId = (int? )item["keyColorIds"] ?? -1,
                pickaxeColorId = (int? )item["pickaxeColorId"] ?? -1,
                scissorsColorId = (int?)item["scissorsColorId"] ?? -1,
                mysteryBoxId = (int?)item["mysteryBoxId"] ?? -1,
                numIced = Math.Max(0, (int? )item["numIced"] ?? 0)
            };
        }

        public static void RecordExtraFields(JObject source, string[] supported, string prefix, List<string> warnings)
        {
            if (source == null)
                return;
            var allowed = new HashSet<string>(supported);
            foreach (var p in source.Properties())
            {
                if (allowed.Contains(p.Name) || p.Value.Type == JTokenType.Null || (p.Value is JContainer container && !container.HasValues))
                    continue;
                if (p.Value.Type == JTokenType.Boolean && !(bool)p.Value)
                    continue;
                if (p.Value.Type == JTokenType.Integer || p.Value.Type == JTokenType.Float)
                {
                    double value = (double)p.Value;
                    // Zero is a real color/index for key, lock and similar mechanics.
                    if (value < 0 || (value == 0 && !p.Name.EndsWith("Id", StringComparison.Ordinal) && !p.Name.EndsWith("Index", StringComparison.Ordinal)))
                        continue;
                }

                AddWarning(warnings, prefix + "." + p.Name + " ignored");
            }
        }

        private static void AddWarning(List<string> warnings, string warning)
        {
            if (!warnings.Contains(warning))
                warnings.Add(warning);
        }

        private static IEnumerable<JToken> Items(JToken token) => token is JArray array ? array : Enumerable.Empty<JToken>();
        private static Cell ReadCell(JToken token)
        {
            if (token == null || token["x"] == null || token["y"] == null)
                throw new ArgumentException("Missing cell coordinates.");
            return new Cell((int)token["x"], (int)token["y"]);
        }
    }
}



