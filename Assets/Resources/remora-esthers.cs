using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

static class RemoraEsthers
{
    enum SpiralDirection
    {
        Up,
        Right,
        Down,
        Left
    }

    static bool registered;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void RegisterRuntime()
    {
        Register();
    }

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    static void RegisterEditor()
    {
        Register();
    }
#endif

    static void Register()
    {
        if (registered)
            return;

        RemoraDev.Console.Cmds.Register("health", new Func<string, object>(Health), new RemoraDev.Console.Cmds.Help
        {
            Description = "Read or set the player's health.",
            Args = new[]
            {
                new RemoraDev.Console.Cmds.Arg
                {
                    Name = "amount",
                    Type = "int",
                    Description = "New health. Omit this to print the current health.",
                    Optional = true
                }
            }
        });
        RemoraDev.Console.Cmds.Register("startWave", new Func<string, object>(StartWave), new RemoraDev.Console.Cmds.Help
        {
            Description = "Spawn the next wave, or a specific wave number.",
            Args = new[]
            {
                new RemoraDev.Console.Cmds.Arg
                {
                    Name = "wave",
                    Type = "int",
                    Description = "Wave number to start. Omit this to spawn the next wave.",
                    Optional = true
                }
            }
        });
        RemoraDev.Console.Cmds.Register("canDie", new Func<string, object>(CanDie), new RemoraDev.Console.Cmds.Help
        {
            Description = "Read or set whether the player can die.",
            Args = new[]
            {
                new RemoraDev.Console.Cmds.Arg
                {
                    Name = "value",
                    Type = "true|false",
                    Description = "true lets the player die. false keeps them alive. Omit this to print the current value.",
                    Optional = true
                }
            }
        });
        RemoraDev.Console.Cmds.Register("pillows", new Func<string, object>(Pillows), new RemoraDev.Console.Cmds.Help
        {
            Description = "Read or set the player's pillow count.",
            Args = new[]
            {
                new RemoraDev.Console.Cmds.Arg
                {
                    Name = "amount",
                    Type = "int",
                    Description = "New pillow count. Omit this to print the current count.",
                    Optional = true
                }
            }
        });
        RemoraDev.Console.Cmds.Register("player.playsfx", new Func<string[], object>(PlayerPlaySfx), new RemoraDev.Console.Cmds.Help
        {
            Description = "Play a sound effect on the player.",
            Args = new[]
            {
                new RemoraDev.Console.Cmds.Arg
                {
                    Name = "id",
                    Type = "id",
                    Description = "Sound id. Spaces are kept.",
                    Variadic = true
                }
            }
        });
        RemoraDev.Console.Cmds.Register("player.playtrack", new Func<string[], object>(PlayerPlayTrack), new RemoraDev.Console.Cmds.Help
        {
            Description = "Play a music track on the player.",
            Args = new[]
            {
                new RemoraDev.Console.Cmds.Arg
                {
                    Name = "id",
                    Type = "id",
                    Description = "Track id. Spaces are kept.",
                    Variadic = true
                }
            }
        });
        RemoraDev.Console.Cmds.Register("playsfx", new Func<string[], object>(PlaySfx), new RemoraDev.Console.Cmds.Help
        {
            Description = "Play a global sound effect.",
            Args = new[]
            {
                new RemoraDev.Console.Cmds.Arg
                {
                    Name = "id",
                    Type = "id",
                    Description = "Sound id. Spaces are kept.",
                    Variadic = true
                }
            }
        });
        RemoraDev.Console.Cmds.Register("playtrack", new Func<string[], object>(PlayTrack), new RemoraDev.Console.Cmds.Help
        {
            Description = "Play a global music track.",
            Args = new[]
            {
                new RemoraDev.Console.Cmds.Arg
                {
                    Name = "id",
                    Type = "id",
                    Description = "Track id. Spaces are kept.",
                    Variadic = true
                }
            }
        });
        RemoraDev.Console.Cmds.Register("build", new Func<string, string[], object>(Build), new RemoraDev.Console.Cmds.Help
        {
            Description = "Place, remove, read, fill, or spiral buildings on the pathfinding grid.",
            Args = new[]
            {
                new RemoraDev.Console.Cmds.Arg
                {
                    Name = "action",
                    Type = "select|place|remove|get|fill|spiral",
                    Description = "select <selection>. place <x> <y> <selection>. remove <x> <y>. get <x> <y>. fill <selection> [overriding]. spiral <selection> <amount> [skip-buildings]."
                },
                new RemoraDev.Console.Cmds.Arg
                {
                    Name = "args",
                    Type = "args",
                    Description = "Arguments for the action.",
                    Optional = true,
                    Variadic = true
                }
            }
        });
        RemoraDev.Console.Cmds.Register("getPlayerNode", new Func<object>(GetPlayerNode), new RemoraDev.Console.Cmds.Help
        {
            Description = "Print the grid cell under the player."
        });
        RemoraDev.Console.Cmds.Register("volume", new Func<string[], object>(Volume), new RemoraDev.Console.Cmds.Help
        {
            Description = "Read the mixer volumes, or set master, music, or sfx.",
            Args = new[]
            {
                new RemoraDev.Console.Cmds.Arg
                {
                    Name = "args",
                    Type = "channel amount",
                    Description = "Omit both to print master, music, and sfx. Otherwise pass master, music, or sfx, then a volume.",
                    Optional = true,
                    Variadic = true
                }
            }
        });
        RemoraDev.Console.Cmds.Register("summon", new Func<string[], object>(Summon), new RemoraDev.Console.Cmds.Help
        {
            Description = "Spawn an entity on a cell, in a spiral around the player, or on every open cell. Extra words are a U-NOR diff applied to each spawn.",
            Args = new[]
            {
                new RemoraDev.Console.Cmds.Arg
                {
                    Name = "args",
                    Type = "entity|fill|spiral",
                    Description = "summon <entity_id> <x> <y> [u-nor]. summon fill <entity_id> [u-nor]. summon spiral <entity_id> <amount> [u-nor].",
                    Variadic = true
                }
            }
        });
        RemoraDev.Console.Cmds.Register("object", new Func<string, string[], object>(ObjectCommand), new RemoraDev.Console.Cmds.Help
        {
            Description = "Select a live object with U-NOR and apply a U-NOR diff to it.",
            Args = new[]
            {
                new RemoraDev.Console.Cmds.Arg
                {
                    Name = "selector",
                    Type = "u-nor-select",
                    Description = "U-NOR selector for the object to change."
                },
                new RemoraDev.Console.Cmds.Arg
                {
                    Name = "data",
                    Type = "u-nor-data",
                    Description = "U-NOR diff. Quote it when it contains spaces.",
                    Variadic = true
                }
            }
        });
        RemoraDev.Console.Cmds.Register("inflation", new Func<string[], object>(Inflation), new RemoraDev.Console.Cmds.Help
        {
            Description = "Read or set buy, sell, and pickup inflation.",
            Args = new[]
            {
                new RemoraDev.Console.Cmds.Arg
                {
                    Name = "args",
                    Type = "channel amount",
                    Description = "Omit both to print all three. One number sets all three. buy, sell, or pickup plus a number sets one.",
                    Optional = true,
                    Variadic = true
                }
            }
        });
        RemoraDev.Console.Cmds.Register("buildBounds", new Func<object>(BuildBounds), new RemoraDev.Console.Cmds.Help
        {
            Description = "Print the grid cells that hold a building or a bed."
        });
        RemoraDev.Console.Cmds.Register("buildBoundsWu", new Func<object>(BuildBoundsWu), new RemoraDev.Console.Cmds.Help
        {
            Description = "Print those same outer cells in world units, expanded by the node radius."
        });
        RemoraDev.Console.Cmds.Register("exportGrid", new Func<string, bool, object>(ExportGrid), new RemoraDev.Console.Cmds.Help
        {
            Description = "Write the grid to a JSON file in the Exports folder next to the project.",
            Args = new[]
            {
                new RemoraDev.Console.Cmds.Arg
                {
                    Name = "filename",
                    Type = "file.json",
                    Description = "File name. .json is added when it is missing."
                },
                new RemoraDev.Console.Cmds.Arg
                {
                    Name = "include-empty",
                    Type = "true|false",
                    Description = "Include cells with no building and no bed.",
                    Optional = true
                }
            }
        });
        RemoraDev.Console.Cmds.Register("importGrid", new Func<string, bool, object>(ImportGrid), new RemoraDev.Console.Cmds.Help
        {
            Description = "Read a grid JSON file from the Exports folder and apply it.",
            Args = new[]
            {
                new RemoraDev.Console.Cmds.Arg
                {
                    Name = "filename",
                    Type = "file.json",
                    Description = "File name. It must end with .json."
                },
                new RemoraDev.Console.Cmds.Arg
                {
                    Name = "override",
                    Type = "true|false",
                    Description = "Replace buildings that are already on a cell.",
                    Optional = true
                }
            }
        });
        registered = true;
    }

    static object Health(string amount = null)
    {
        PlayerHealth health = Player().GetComponent<PlayerHealth>();
        if (health == null)
            throw new InvalidOperationException("player has no health");
        if (amount == null)
            return Say("Health: " + health.GetHealth().ToString(CultureInfo.InvariantCulture));

        health.SetHealth(Int(amount, "a health value"));
        return Say("Health: " + health.GetHealth().ToString(CultureInfo.InvariantCulture));
    }

    static object StartWave(string wave = null)
    {
        EnemySpawner spawner = Spawner();
        if (wave == null)
        {
            spawner.SpawnWave();
            return Say("Started wave " + spawner.currentWave.ToString(CultureInfo.InvariantCulture));
        }

        int waveIndex = Int(wave, "a wave number") - 1;
        if (waveIndex <= 0)
            throw new InvalidOperationException("Wave index to low!");

        spawner.currentWave = waveIndex;
        spawner.SpawnWave();
        return Say("Started wave " + spawner.currentWave.ToString(CultureInfo.InvariantCulture));
    }

    static object CanDie(string value = null)
    {
        PlayerHealth health = Player().GetComponent<PlayerHealth>();
        if (health == null)
            throw new InvalidOperationException("player has no health");
        if (value == null)
            return Say("Can die: " + (health.canDie ? "true" : "false"));

        health.canDie = Bool(value, "true or false");
        return Say("Can die: " + (health.canDie ? "true" : "false"));
    }

    static object Pillows(string amount = null)
    {
        PlayerHand hand = Hand();
        if (amount == null)
            return Say("Pillows: " + hand.getPillows().ToString(CultureInfo.InvariantCulture));

        hand.setPillows(Int(amount, "a pillow count"));
        return Say("Pillows: " + hand.getPillows().ToString(CultureInfo.InvariantCulture));
    }

    static object PlayerPlaySfx(string[] id)
    {
        LocalSoundComposer composer = Player().GetComponent<LocalSoundComposer>();
        if (composer == null)
            throw new InvalidOperationException("player has no sound composer");
        composer.PlayFx(Joined(id, "id"));
        return null;
    }

    static object PlayerPlayTrack(string[] id)
    {
        LocalSoundComposer composer = Player().GetComponent<LocalSoundComposer>();
        if (composer == null)
            throw new InvalidOperationException("player has no sound composer");
        composer.PlayTrack(Joined(id, "id"));
        return null;
    }

    static object PlaySfx(string[] id)
    {
        if (GlobalSoundComposer.Instance == null)
            throw new InvalidOperationException("global sound composer is not ready");
        GlobalSoundComposer.Instance.PlayFx(Joined(id, "id"));
        return null;
    }

    static object PlayTrack(string[] id)
    {
        if (GlobalSoundComposer.Instance == null)
            throw new InvalidOperationException("global sound composer is not ready");
        GlobalSoundComposer.Instance.PlayTrack(Joined(id, "id"));
        return null;
    }

    static object Build(string action, string[] args)
    {
        if (args == null)
            args = new string[0];
        if (string.Equals(action, "select", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 1)
                throw new InvalidOperationException("usage: build select <selection>");
            Hand().SwitchBuildSelection(Int(args[0], "a selection"));
            return null;
        }

        if (string.Equals(action, "spiral", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 2)
                throw new InvalidOperationException("usage: build spiral <selection> <amount> [skip-buildings]");
            int selection = Int(args[0], "a selection");
            int amount = Int(args[1], "an amount");
            bool skipBuildings = args.Length > 2 && Bool(args[2], "true or false");
            Node origin = Grid().NodeFromWorldPoint(Player().transform.position);
            List<Vector2Int> cells = GetSpiralNodes(origin.gridX, origin.gridY, amount, skipBuildings);
            PlayerHand hand = Hand();
            for (int i = 0; i < cells.Count; i++)
            {
                hand.SwitchBuildSelection(selection);
                hand.ForcePlaceBuildingAt(cells[i].x, cells[i].y);
            }

            return Say("Placed " + cells.Count.ToString(CultureInfo.InvariantCulture));
        }

        if (string.Equals(action, "place", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 3)
                throw new InvalidOperationException("usage: build place <x> <y> <selection>");
            int x = Int(args[0], "an x");
            int y = Int(args[1], "a y");
            int selection = Int(args[2], "a selection");
            Cell(x, y);
            PlayerHand hand = Hand();
            hand.SwitchBuildSelection(selection);
            hand.ForcePlaceBuildingAt(x, y);
            return null;
        }

        if (string.Equals(action, "remove", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 2)
                throw new InvalidOperationException("usage: build remove <x> <y>");
            int x = Int(args[0], "an x");
            int y = Int(args[1], "a y");
            Cell(x, y);
            Grid().ForceRemoveBuildingAtNode(x, y);
            return null;
        }

        if (string.Equals(action, "get", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 2)
                throw new InvalidOperationException("usage: build get <x> <y>");
            int x = Int(args[0], "an x");
            int y = Int(args[1], "a y");
            Cell(x, y);
            GameObject building = Grid().GetBuildingAtNode(x, y);
            return Say(building == null ? "No building at node" : building.name);
        }

        if (string.Equals(action, "fill", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 1)
                throw new InvalidOperationException("usage: build fill <selection> [overriding]");
            int selection = Int(args[0], "a selection");
            bool overriding = args.Length > 1 && Bool(args[1], "true or false");
            PlayerHand hand = Hand();
            hand.SwitchBuildSelection(selection);
            BuildingScriptableObject selected = hand.GetSelectedBuilding();
            if (selected == null || selected.buildingPrefab == null)
                throw new InvalidOperationException("selection has no building");
            Grid().DEBUG_FillGridWith(selected.buildingPrefab, overriding);
            return null;
        }

        throw new InvalidOperationException("unknown subcommand \"" + action + "\"");
    }

    static object GetPlayerNode()
    {
        Node node = Grid().NodeFromWorldPoint(Player().transform.position);
        return Say("Player node: " + node.gridX.ToString(CultureInfo.InvariantCulture) + ", " + node.gridY.ToString(CultureInfo.InvariantCulture));
    }

    static object Volume(string[] args)
    {
        if (args == null || args.Length == 0)
        {
            return Say(
                "Master volume: " + PlayerPrefs.GetFloat("masterVolume", 1f).ToString(CultureInfo.InvariantCulture)
                + "\nMusic volume: " + PlayerPrefs.GetFloat("musicVolume", 1f).ToString(CultureInfo.InvariantCulture)
                + "\nSFX volume: " + PlayerPrefs.GetFloat("sfxVolume", 1f).ToString(CultureInfo.InvariantCulture));
        }

        if (args.Length != 2)
            throw new InvalidOperationException("usage: volume <master|music|sfx> <volume>");

        float volume = Float(args[1], "a volume");
        SoundMixer_Handler mixer = Mixer();
        if (string.Equals(args[0], "master", StringComparison.OrdinalIgnoreCase))
            mixer.SetMasterVolume(volume);
        else if (string.Equals(args[0], "music", StringComparison.OrdinalIgnoreCase))
            mixer.SetMusicVolume(volume);
        else if (string.Equals(args[0], "sfx", StringComparison.OrdinalIgnoreCase))
            mixer.SetSFXVolume(volume);
        else
            throw new InvalidOperationException("usage: volume <master|music|sfx> <volume>");

        return Say(args[0] + " volume: " + volume.ToString(CultureInfo.InvariantCulture));
    }

    static object Summon(string[] args)
    {
        if (args == null || args.Length == 0)
            throw new InvalidOperationException("usage: summon <entity_id> <x> <y> [u-nor]\nsummon fill <entity_id> [u-nor]\nsummon spiral <entity_id> <amount> [u-nor]");

        if (string.Equals(args[0], "fill", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 2)
                throw new InvalidOperationException("usage: summon fill <entity_id> [u-nor]");
            GameObject prefab = Entity(args[1]);
            string diff = JoinFrom(args, 2);
            PathfindingGrid grid = Grid();
            int width = grid.grid.GetLength(0);
            int height = grid.grid.GetLength(1);
            int count = 0;
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Node node = grid.grid[x, y];
                    if (node.isBed || node.building != null)
                        continue;
                    Spawn(prefab, node, diff);
                    count++;
                }
            }

            return Say("Summoned " + count.ToString(CultureInfo.InvariantCulture));
        }

        if (string.Equals(args[0], "spiral", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 3)
                throw new InvalidOperationException("usage: summon spiral <entity_id> <amount> [u-nor]");
            GameObject prefab = Entity(args[1]);
            int amount = Int(args[2], "an amount");
            string diff = JoinFrom(args, 3);
            Node origin = Grid().NodeFromWorldPoint(Player().transform.position);
            List<Vector2Int> cells = GetSpiralNodes(origin.gridX, origin.gridY, amount, false);
            for (int i = 0; i < cells.Count; i++)
                Spawn(prefab, Cell(cells[i].x, cells[i].y), diff);
            return Say("Summoned " + cells.Count.ToString(CultureInfo.InvariantCulture));
        }

        if (args.Length < 3)
            throw new InvalidOperationException("usage: summon <entity_id> <x> <y> [u-nor]");

        GameObject one = Entity(args[0]);
        int summonX = Int(args[1], "an x");
        int summonY = Int(args[2], "a y");
        Spawn(one, Cell(summonX, summonY), JoinFrom(args, 3));
        return Say("Summoned " + args[0]);
    }

    static object ObjectCommand(string selector, string[] data)
    {
        if (string.IsNullOrEmpty(selector) || data == null || data.Length == 0)
            throw new InvalidOperationException("usage: object <u-nor-select> <u-nor-data>");

        object selected = RemoraDev.Console.Cmds.ManInvoke("unor", "select", new string[] { selector });
        if (selected == null)
            return null;

        object targets = EvaluateSelector(selector);
        if (targets == null)
            throw new InvalidOperationException("Object not found");

        ApplyDiff(targets, string.Join(" ", data));
        return null;
    }

    static object Inflation(string[] args)
    {
        PlayerHand hand = Hand();
        if (args == null || args.Length == 0)
        {
            return Say(
                "Buy inflation: " + hand.GlobalBuyInflationMultiplier.ToString(CultureInfo.InvariantCulture)
                + "\nSell inflation: " + hand.GlobalSellInflationMultiplier.ToString(CultureInfo.InvariantCulture)
                + "\nPickup inflation: " + hand.GlobalPickupInflationMultiplier.ToString(CultureInfo.InvariantCulture));
        }

        if (args.Length == 1)
        {
            float inflation = Float(args[0], "an inflation value");
            hand.GlobalBuyInflationMultiplier = inflation;
            hand.GlobalSellInflationMultiplier = inflation;
            hand.GlobalPickupInflationMultiplier = inflation;
            return Say("Inflation: " + inflation.ToString(CultureInfo.InvariantCulture));
        }

        if (args.Length != 2)
            throw new InvalidOperationException("usage: inflation <buy|sell|pickup> <amount>");

        float amount = Float(args[1], "an inflation value");
        if (string.Equals(args[0], "buy", StringComparison.OrdinalIgnoreCase))
            hand.GlobalBuyInflationMultiplier = amount;
        else if (string.Equals(args[0], "sell", StringComparison.OrdinalIgnoreCase))
            hand.GlobalSellInflationMultiplier = amount;
        else if (string.Equals(args[0], "pickup", StringComparison.OrdinalIgnoreCase))
            hand.GlobalPickupInflationMultiplier = amount;
        else
            throw new InvalidOperationException("usage: inflation <buy|sell|pickup> <amount>");

        return Say(args[0] + " inflation: " + amount.ToString(CultureInfo.InvariantCulture));
    }

    static object BuildBounds()
    {
        int xLow;
        int xHigh;
        int yLow;
        int yHigh;
        GetOuterNodeBounds(out xLow, out xHigh, out yLow, out yHigh);
        return Say(
            "Outer bounds: x: " + xLow.ToString(CultureInfo.InvariantCulture) + "-" + xHigh.ToString(CultureInfo.InvariantCulture)
            + ", y: " + yLow.ToString(CultureInfo.InvariantCulture) + "-" + yHigh.ToString(CultureInfo.InvariantCulture));
    }

    static object BuildBoundsWu()
    {
        int xLow;
        int xHigh;
        int yLow;
        int yHigh;
        GetOuterNodeBounds(out xLow, out xHigh, out yLow, out yHigh);
        PathfindingGrid grid = Grid();
        float xLowMiddle = grid.grid[xLow, yLow].worldPosition.x;
        float xHighMiddle = grid.grid[xHigh, yLow].worldPosition.x;
        float yLowMiddle = grid.grid[xLow, yLow].worldPosition.y;
        float yHighMiddle = grid.grid[xLow, yHigh].worldPosition.y;
        float radius = grid.nodeRadius;
        return Say(
            "Outer bounds: x: " + (xLowMiddle - radius).ToString(CultureInfo.InvariantCulture) + "-" + (xHighMiddle + radius).ToString(CultureInfo.InvariantCulture)
            + ", y: " + (yLowMiddle - radius).ToString(CultureInfo.InvariantCulture) + "-" + (yHighMiddle + radius).ToString(CultureInfo.InvariantCulture)
            + " (NodeRad:" + radius.ToString(CultureInfo.InvariantCulture) + ")");
    }

    static object ExportGrid(string filename, bool includeEmpty = false)
    {
        string path = ExportFile(filename, true);
        string folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        JObject json = DebugConsole_GridSerializer.GridToJson(Hand().buildings, includeEmpty);
        File.WriteAllText(path, json.ToString());
        return Say("Exported grid to " + path);
    }

    static object ImportGrid(string filename, bool overrideExisting = false)
    {
        string path = ExportFile(filename, false);
        if (!File.Exists(path))
            throw new InvalidOperationException("\"" + path + "\" was not found");

        JObject json = JObject.Parse(File.ReadAllText(path));
        DebugConsole_GridSerializer.JsonToGrid(json, Hand().buildings, overrideExisting);
        return Say("Imported grid from " + path);
    }

    static List<Vector2Int> GetSpiralNodes(int startX, int startY, int amount, bool skipBuildings)
    {
        var spiralNodes = new List<Vector2Int>();
        if (amount <= 0)
            return spiralNodes;

        PathfindingGrid grid = Grid();
        int gridWidth = grid.grid.GetLength(0);
        int gridHeight = grid.grid.GetLength(1);
        int placedCount = 0;
        int xOffset = 0;
        int yOffset = 1;
        int moveCount = 1;
        int moveIncrement = 1;
        int steps = 0;
        int limit = Math.Max(1, gridWidth * gridHeight) * 8;
        SpiralDirection currentDirection = SpiralDirection.Right;

        while (placedCount < amount)
        {
            for (int i = 0; i < moveCount; i++)
            {
                if (steps++ > limit)
                    throw new InvalidOperationException("not enough free nodes");

                int x = startX + xOffset;
                int y = startY + yOffset;
                if (x >= 0 && x < gridWidth && y >= 0 && y < gridHeight)
                {
                    Node currentNode = grid.grid[x, y];
                    if (!currentNode.isBed && (!skipBuildings || currentNode.building == null))
                    {
                        spiralNodes.Add(new Vector2Int(x, y));
                        placedCount++;
                        if (placedCount >= amount)
                            break;
                    }
                }

                switch (currentDirection)
                {
                    case SpiralDirection.Right:
                        xOffset++;
                        break;
                    case SpiralDirection.Down:
                        yOffset--;
                        break;
                    case SpiralDirection.Left:
                        xOffset--;
                        break;
                    case SpiralDirection.Up:
                        yOffset++;
                        break;
                }
            }

            if (currentDirection == SpiralDirection.Left || currentDirection == SpiralDirection.Right)
                moveIncrement++;
            moveCount = moveIncrement;
            currentDirection = (SpiralDirection)(((int)currentDirection + 1) % 4);
        }

        return spiralNodes;
    }

    static void GetOuterNodeBounds(out int xLow, out int xHigh, out int yLow, out int yHigh)
    {
        xLow = int.MaxValue;
        xHigh = int.MinValue;
        yLow = int.MaxValue;
        yHigh = int.MinValue;
        bool foundNode = false;
        PathfindingGrid grid = Grid();
        int width = grid.grid.GetLength(0);
        int height = grid.grid.GetLength(1);
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Node node = grid.grid[x, y];
                if (node.building == null && !node.isBed)
                    continue;

                foundNode = true;
                if (x < xLow)
                    xLow = x;
                if (x > xHigh)
                    xHigh = x;
                if (y < yLow)
                    yLow = y;
                if (y > yHigh)
                    yHigh = y;
            }
        }

        if (!foundNode)
        {
            xLow = 0;
            xHigh = 0;
            yLow = 0;
            yHigh = 0;
        }
    }

    static void Spawn(GameObject prefab, Node node, string diff)
    {
        GameObject entity = UnityEngine.Object.Instantiate(prefab, node.worldPosition, Quaternion.identity);
        if (!string.IsNullOrEmpty(diff))
            ApplyDiff(entity, diff);
    }

    static object EvaluateSelector(string selector)
    {
        Type type = UnorType();
        MethodInfo method = type.GetMethod("EvaluateSelector", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(string), typeof(bool) }, null);
        if (method == null)
            throw new InvalidOperationException("unor returned no object");

        try
        {
            return method.Invoke(null, new object[] { selector, true });
        }
        catch (TargetInvocationException exception)
        {
            string message = exception.InnerException != null ? exception.InnerException.Message : exception.Message;
            throw new InvalidOperationException(message);
        }
    }

    static void ApplyDiff(object target, string diff)
    {
        Type type = UnorType();
        MethodInfo method = type.GetMethod("ApplyDiff", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(object), typeof(string), typeof(bool) }, null);
        if (method == null)
            throw new InvalidOperationException("unor returned no object");

        try
        {
            method.Invoke(null, new object[] { target, diff, false });
        }
        catch (TargetInvocationException exception)
        {
            string message = exception.InnerException != null ? exception.InnerException.Message : exception.Message;
            throw new InvalidOperationException(message);
        }
    }

    static Type UnorType()
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type type = assemblies[i].GetType("UNOR");
            if (type != null)
                return type;
        }

        throw new InvalidOperationException("unor returned no object");
    }

    static GameObject Player()
    {
        if (PlayerController.player == null)
            throw new InvalidOperationException("player is not ready");
        return PlayerController.player;
    }

    static PlayerHand Hand()
    {
        PlayerHand hand = Player().GetComponent<PlayerHand>();
        if (hand == null)
            throw new InvalidOperationException("player has no hand");
        return hand;
    }

    static PathfindingGrid Grid()
    {
        if (PathfindingGrid.instance == null || PathfindingGrid.instance.grid == null)
            throw new InvalidOperationException("pathfinding grid is not ready");
        return PathfindingGrid.instance;
    }

    static Node Cell(int x, int y)
    {
        PathfindingGrid grid = Grid();
        int width = grid.grid.GetLength(0);
        int height = grid.grid.GetLength(1);
        if (x < 0 || y < 0 || x >= width || y >= height)
            throw new InvalidOperationException("node " + x.ToString(CultureInfo.InvariantCulture) + ", " + y.ToString(CultureInfo.InvariantCulture) + " is outside the grid");
        return grid.grid[x, y];
    }

    static EnemySpawner Spawner()
    {
        if (EnemySpawner.instance == null)
            throw new InvalidOperationException("enemy spawner is not ready");
        return EnemySpawner.instance;
    }

    static SoundMixer_Handler Mixer()
    {
        SoundMixer_Handler mixer = UnityEngine.Object.FindFirstObjectByType<SoundMixer_Handler>(FindObjectsInactive.Exclude);
        if (mixer == null)
            throw new InvalidOperationException("sound mixer is not ready");
        return mixer;
    }

    static GameObject Entity(string entityId)
    {
        if (GlobalEntityHolder.Instance == null)
            throw new InvalidOperationException("entity holder is not ready");
        GameObject prefab = GlobalEntityHolder.Instance.Resolve(entityId);
        if (prefab == null)
            throw new InvalidOperationException("entity \"" + entityId + "\" was not found");
        return prefab;
    }

    static string ExportFile(string filename, bool addExtension)
    {
        if (string.IsNullOrWhiteSpace(filename))
            throw new InvalidOperationException("usage: a file name is required");
        filename = filename.Trim();
        if (filename.IndexOf('/') >= 0 || filename.IndexOf('\\') >= 0 || filename.IndexOf("..", StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("filename must be a file name, not a path");
        if (addExtension)
        {
            if (!filename.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                filename += ".json";
        }
        else if (!filename.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("filename must end with .json");
        }

        return Path.Combine(Application.dataPath, "..", "Exports", filename);
    }

    static string Joined(string[] parts, string label)
    {
        string text = JoinFrom(parts, 0);
        if (text.Length == 0)
            throw new InvalidOperationException("missing <" + label + ">");
        return text;
    }

    static string JoinFrom(string[] parts, int start)
    {
        if (parts == null || start >= parts.Length)
            return "";
        if (start == 0)
            return string.Join(" ", parts);
        var slice = new string[parts.Length - start];
        Array.Copy(parts, start, slice, 0, slice.Length);
        return string.Join(" ", slice);
    }

    static int Int(string text, string label)
    {
        int value;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            throw new InvalidOperationException("\"" + text + "\" is not " + label);
        return value;
    }

    static float Float(string text, string label)
    {
        float value;
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            throw new InvalidOperationException("\"" + text + "\" is not " + label);
        return value;
    }

    static bool Bool(string text, string label)
    {
        bool value;
        if (bool.TryParse(text, out value))
            return value;
        throw new InvalidOperationException("\"" + text + "\" is not " + label);
    }

    static string Say(string text)
    {
        RemoraDev.Console.SendTextIn(text ?? "", true);
        return text ?? "";
    }
}
