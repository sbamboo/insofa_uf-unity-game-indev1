using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System;
using UnityEngine;
using System.Linq;

public class DebugConsole_GridSerializer : MonoBehaviour {
    /*
    * {
    *     "version": {
    *         "format": 0,
    *         "game": <0.5>,
    *         "unity": <6000.0.40f1>
    *     },
    *     "supported": false,
    *     "current_wave": <int>,
    *     "grid": [
    *         {"x":<int:node_x>, "y":<int:node_y> "world.x":<float:x>, "world.y":<float:y>, "building":<int:index>, "isbed":<bool>, "building_name":<string>, "building_health":<int>}
    *     ],
    *     "enemies": [],
    *     "options": {
    *         "grid_overrides_bed": false
    *     }
    * }
    */

    public static List<int> ValidFormatVersions = new List<int> { 0 };
    // Valid string versions can be "<entry>", "!<entry>" or "*" aswell as using ~ < > for ranges.
    public static List<string> ValidGameVersions = new List<string> { "0.5" };
    public static List<string> ValidUnityVersions = new List<string> { "*" };

    public static bool IsValidFormatVersion(int version) {
        return ValidFormatVersions.Contains(version);
    }

    private static int CompareVersions(string v1, string v2) {
        // Split versions into components
        string[] v1Parts = v1.Split('.');
        string[] v2Parts = v2.Split('.');

        // Get the length of the longest version string so we don't go out of bounds
        int maxLength = Math.Max(v1Parts.Length, v2Parts.Length);

        for (int i = 0; i < maxLength; i++) {
            // If the length of version string is exceeded, pad a zero so we can compare normally
            string v1Part = i < v1Parts.Length ? v1Parts[i] : "0";
            string v2Part = i < v2Parts.Length ? v2Parts[i] : "0";

            // Attempt to parse as integers for numeric comparison
            if (
                int.TryParse(v1Part, out int v1Num) &&
                int.TryParse(v2Part, out int v2Num)
            ) {
                if (v1Num > v2Num)
                    return 1;
                if (v1Num < v2Num)
                    return -1;
            } else {
                // If not integers, compare as strings (handles letters)
                int stringComparison = string.Compare(v1Part, v2Part);
                if (stringComparison != 0)
                    return stringComparison;
            }
        }

        return 0; // Versions are equal
    }

    public static bool ValidateSemanticVersion(
        string version,
        List<string> validVersions
    ) {
        // node package.json versioning
        // "~1.2.3" means >=1.2.3 <1.3.0
        // "^1.2.3" means >=1.2.3 <2.0.0
        // ">1.2.3" means >1.2.3
        // "<1.2.3" means <1.2.3
        // "1.2.3" means 1.2.3
        // ">=1.2.3" means >=1.2.3
        // "<=1.2.3" means <=1.2.3
        // "~1.2" means >=1.2.0 <1.3.0
        // "^1.2" means >=1.2.0 <2.0.0
        // "~1" means >=1.0.0 <2.0.0
        // "^1" means >=1.0.0 <2.0.0
        // ...
        // But a version may include [a-z] if so alphabetical order so a > b
        //   example: "~1.3.f" means >=1.3.6 <1.4.0
        //   example: "~1.3.1f" means >=1.3.1f <1.3.2
        // Negating is also allowed so 
        //   example: "!1.3.1f" means !=1.3.1f

        if (validVersions.Contains("*")) {
            return true;
        }

        foreach (string versionString in validVersions) {
            if (versionString.StartsWith("!")) {
                // Negated version
                string negatedVersion = versionString.Substring(1);
                if (CompareVersions(version, negatedVersion) != 0) {
                    return true;
                }
            } else if (versionString.StartsWith("~")) {
                // Tilde version range
                string tildeVersion = versionString.Substring(1);
                string[] tildeParts = tildeVersion.Split('.');
                string nextMinorVersion;

                if (tildeParts.Length == 1) {
                    // ~1 means >= 1.0.0 < 2.0.0
                    int major = int.Parse(tildeParts[0]);
                    nextMinorVersion = (major + 1) + ".0.0";
                    tildeVersion += ".0.0";
                } else if (tildeParts.Length == 2) {
                    // ~1.2 means >= 1.2.0 < 1.3.0
                    int major = int.Parse(tildeParts[0]);
                    int minor = int.Parse(tildeParts[1]);
                    nextMinorVersion = major + "." + (minor + 1) + ".0";
                    tildeVersion += ".0";
                } else {
                    // ~1.2.3 means >= 1.2.3 < 1.3.0
                    int major = int.Parse(tildeParts[0]);
                    int minor = int.Parse(tildeParts[1]);
                    nextMinorVersion = major + "." + (minor + 1) + ".0";
                }

                if (
                    CompareVersions(version, tildeVersion) >= 0 &&
                    CompareVersions(version, nextMinorVersion) < 0
                ) {
                    return true;
                }
            } else if (versionString.StartsWith("^")) {
                // Caret version range
                string caretVersion = versionString.Substring(1);
                string[] caretParts = caretVersion.Split('.');
                string nextMajorVersion;

                if (caretParts.Length == 1) {
                    // ^1 means >= 1.0.0 < 2.0.0
                    int major = int.Parse(caretParts[0]);
                    nextMajorVersion = (major + 1) + ".0.0";
                    caretVersion += ".0.0";
                } else {
                    // ^1.2 or ^1.2.3 means >= 1.2.0 < 2.0.0  (or >= 1.2.3 < 2.0.0)
                    int major = int.Parse(caretParts[0]);
                    nextMajorVersion = (major + 1) + ".0.0";
                }

                if (
                    CompareVersions(version, caretVersion) >= 0 &&
                    CompareVersions(version, nextMajorVersion) < 0
                ) {
                    return true;
                }
            } else if (versionString.StartsWith(">=")) {
                // Greater than or equal to
                string gteVersion = versionString.Substring(2);
                if (CompareVersions(version, gteVersion) >= 0) {
                    return true;
                }
            } else if (versionString.StartsWith("<=")) {
                // Less than or equal to
                string lteVersion = versionString.Substring(2);
                if (CompareVersions(version, lteVersion) <= 0) {
                    return true;
                }
            } else if (versionString.StartsWith(">")) {
                // Greater than
                string gtVersion = versionString.Substring(1);
                if (CompareVersions(version, gtVersion) > 0) {
                    return true;
                }
            } else if (versionString.StartsWith("<")) {
                // Less than
                string ltVersion = versionString.Substring(1);
                if (CompareVersions(version, ltVersion) < 0) {
                    return true;
                }
            } else {
                // Exact match
                if (CompareVersions(version, versionString) == 0) {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool IsValidGameVersion(string version) {
        return ValidateSemanticVersion(version, ValidGameVersions);
    }

    public static bool IsValidUnityVersion(string version) {
        return ValidateSemanticVersion(version, ValidUnityVersions);
    }

    public static JObject GridToJson(BuildingScriptableObject[] buildings, bool includeEmpty = false) {

        Node[,] grid = PathfindingGrid.instance.grid;

        // Create the JSON object
        JObject json = new JObject();

        // Add version information
        JObject version = new JObject();
        version["format"] = 0;
        version["game"] = Application.version;
        version["unity"] = Application.unityVersion;
        json["version"] = version;
        json["supported"] = false;

        // Add options
        JObject options = new JObject();
        options["grid_overrides_bed"] = false;
        json["options"] = options;

        // Add wave info
        json["current_wave"] = EnemySpawner.instance.currentWave;

        // Add grid data
        json["grid"] = new JArray();
        for (int x = 0; x < grid.GetLength(0); x++) {
            for (int y = 0; y < grid.GetLength(1); y++) {
                // if includeEmpty is false skip non-bed nodes with no building
                if (includeEmpty == false && !grid[x, y].isBed && !grid[x, y].building) {
                    continue;
                }

                Node node = grid[x, y];
                JObject nodeData = new JObject();
                nodeData["x"] = x;
                nodeData["y"] = y;
                nodeData["world.x"] = node.worldPosition.x;
                nodeData["world.y"] = node.worldPosition.y;

                nodeData["building"] = -1;
                if (node.building != null) {
                    // Check if building has BuildingHealth component, if so get the buildingScriptableObject from it and use it to get index
                    BuildingHealth buildingHealth = node.building.GetComponent<BuildingHealth>();
                    if (buildingHealth != null) {
                        int index = -1;
                        BuildingScriptableObject so = buildingHealth.buildingScriptableObject;
                        if (so != null) {
                            for (int i = 0; i < buildings.Length; i++) {
                                if (buildings[i] == so) {
                                    index = i;
                                    break;
                                }
                            }
                        }
                        nodeData["building"] = index;
                        // Get health
                        nodeData["building_health"] = buildingHealth.currentHealth;

                        nodeData["building_name"] = node.building.name;

                        if (buildingHealth.buildingScriptableObject) {
                            // Get building name from buildingHealth.buildingScriptableObject.buildingName if it exists else node.building.name
                            if (buildingHealth.buildingScriptableObject != null) {
                                nodeData["building_name"] = buildingHealth.buildingScriptableObject.buildingName;
                            }
                        }
                    }
                }
                nodeData["isbed"] = node.isBed;
                ((JArray)json["grid"]).Add(nodeData);
            }
        }

        // Add enemies data
        json["enemies"] = new JArray();

        return json;
    }

    public static void JsonToGrid(JObject json, BuildingScriptableObject[] buildings, bool overrideExisting = false) {
        // Validate the version
        JObject version = json["version"] as JObject;
        if (version == null) {
            throw new Exception("Version information missing from JSON");
        }

        int formatVersion = version["format"].Value<int>();
        if (!IsValidFormatVersion(formatVersion)) {
            throw new Exception("Invalid format version: " + formatVersion);
        }

        string gameVersion = version["game"].Value<string>();
        if (!IsValidGameVersion(gameVersion)) {
            throw new Exception("Invalid game version: " + gameVersion);
        }

        string unityVersion = version["unity"].Value<string>();
        if (!IsValidUnityVersion(unityVersion)) {
            throw new Exception("Invalid unity version: " + unityVersion);
        }

        // Set currentWave
        if (json["current_wave"] != null) {
            EnemySpawner.instance.currentWave = json["current_wave"].Value<int>();
            EnemySpawner.instance.UpdateWaveText();
        }

        // Parse the grid data (if options.grid_overrides_bed is true, include nodes with isbed set to true else just skip them entirely)
        // If overrideExisting is true, if node is not bed and has a building, destroy the building and replace it with the new building
        // If overrideExisting is false, if node is not bed and has a building, skip it entirely
        JObject options = json["options"] as JObject;
        bool gridOverridesBed = options != null && options["grid_overrides_bed"].Value<bool>();
        JArray serializedGrid = json["grid"] as JArray;

        Node[,] grid = PathfindingGrid.instance.grid;

        // If grid_override_bed is true, set all nodes to not be beds
        if (gridOverridesBed) {
            for (int x = 0; x < grid.GetLength(0); x++) {
                for (int y = 0; y < grid.GetLength(1); y++) {
                    Node node = grid[x, y];
                    if (node != null) {
                        node.isBed = false;
                    }
                }
            }
        }

        foreach (JObject serializedGridEntry in serializedGrid) {
            if (serializedGridEntry != null) {
                int x = serializedGridEntry["x"].Value<int>();
                int y = serializedGridEntry["y"].Value<int>();
                float worldX = serializedGridEntry["world.x"].Value<float>();
                float worldY = serializedGridEntry["world.y"].Value<float>();
                int buildingIndex = serializedGridEntry["building"].Value<int>();
                if (buildingIndex < 0) {
                    string buildingName = serializedGridEntry["building_name"]?.Value<string>();
                    if (!string.IsNullOrEmpty(buildingName)) {
                        for (int i = 0; i < buildings.Length; i++) {
                            if (buildings[i] != null && buildings[i].buildingName == buildingName) {
                                buildingIndex = i;
                                break;
                            }
                        }
                    }
                }
                bool isBed = serializedGridEntry["isbed"].Value<bool>();
                if (x < 0 || x >= grid.GetLength(0) || y < 0 || y >= grid.GetLength(1)) {
                    throw new Exception("Grid entry out of bounds: " + x + ", " + y);
                }
                Node node = grid[x, y];
                if (node == null) {
                    throw new Exception("Node not found at: " + x + ", " + y);
                }
                if (isBed || gridOverridesBed) {
                    node.isBed = isBed;
                }
                if (!isBed && buildingIndex >= 0 && buildingIndex < buildings.Length) {
                    BuildingScriptableObject building = buildings[buildingIndex];
                    if (building != null) {
                        if (overrideExisting) {
                            if (node.building != null) {
                                Destroy(node.building);
                            }
                            node.building = Instantiate(building.buildingPrefab, new Vector3(worldX, worldY, 0), Quaternion.identity);
                        } else {
                            if (node.building == null) {
                                node.building = Instantiate(building.buildingPrefab, new Vector3(worldX, worldY, 0), Quaternion.identity);
                            }
                        }

                        // If node.building is not null now set currentHealth to building_health
                        if (node.building != null) {
                            BuildingHealth buildingHealth = node.building.GetComponent<BuildingHealth>();
                            if (buildingHealth != null) {
                                buildingHealth.currentHealth = serializedGridEntry["building_health"].Value<int>();
                            }
                        }
                    }
                }
            }
        }
    }
}