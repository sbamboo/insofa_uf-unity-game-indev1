using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AssetImporters;
#endif

public class RemoraDevConfig
{
    public static readonly (string Name, object Default)[] Fields =
    {
        ("Format", 1),
        ("Uuid", ""),
        ("Versions.Remora", "v0.0"),
        ("DevConsole.Hidden", true),
        ("DevConsole.Keybinds.ToggleHidden", "ctrl+shift+d"),
        ("DevConsole.Config.PanelWidth", 460f),
        ("DevConsole.Config.PanelHeight", 280f),
        ("DevConsole.Config.Margin", 12f),
        ("DevConsole.Config.Padding", 8f),
        ("DevConsole.Config.InputHeight", 28f),
        ("DevConsole.Config.ScrollbarWidth", 10f),
        ("DevConsole.Config.MaxLines", 400),
        ("DevConsole.Config.FontSize", 14),
        ("DevConsole.Config.ResizeStep", 24f),
        ("DevConsole.Config.Dock", "bottom-left"),
        ("DevConsole.Config.ReferenceWidth", 1920f),
        ("DevConsole.Config.ReferenceHeight", 1080f),
        ("DevConsole.Config.ScaleMatch", 0.5f),
        ("DevConsole.Config.StartupCommand", ""),
        ("DevConsole.Config.ShowInternalCommands", false),
        ("DevConsole.Config.PreferMacros", true),
        ("DevConsole.Config.ClickableLinks", true),
        ("DevConsole.Config.LogDlog", false),
        ("DevConsole.Config.PropogageKeys", false)
    };

    private static readonly Dictionary<string, object> values = new Dictionary<string, object>();
    private static readonly Dictionary<string, bool> defaulted = new Dictionary<string, bool>();

    static RemoraDevConfig()
    {
        ApplyDefaults();
    }

    public static T Get<T>(string name)
    {
        object value;
        if (!values.TryGetValue(name, out value))
            throw new KeyNotFoundException("Missing remora config value: " + name);

        return (T)value;
    }

    public static void TryApplyFile()
    {
        ApplyDefaults();

        string json;
        if (!RemoraDevConsole.TryReadConfig(out json))
            return;

        try
        {
            ApplyJson(json);
            object format = values["Format"];
            if (!(format is int) || (int)format != 1)
                ApplyDefaults();
        }
        catch (Exception exception)
        {
            ApplyDefaults();
            Debug.LogWarning("Remora config.json was not applied: " + exception.Message);
        }
    }

    public static string ToJson()
    {
        var builder = new StringBuilder();
        var stack = new List<string>();
        builder.Append("{\n");
        for (int i = 0; i < Fields.Length; i++)
        {
            string[] parts = Fields[i].Name.Split('.');
            int common = 0;
            while (common < stack.Count && stack[common] == parts[common])
                common++;

            for (int depth = stack.Count; depth > common; depth--)
            {
                builder.Append('\n');
                builder.Append(' ', depth * 4);
                builder.Append('}');
                stack.RemoveAt(stack.Count - 1);
            }

            if (i > 0)
                builder.Append(",\n");

            for (int depth = common; depth < parts.Length; depth++)
            {
                builder.Append(' ', (depth + 1) * 4);
                builder.Append('"');
                // Convert to kebab case
                for (int c = 0; c < parts[depth].Length; c++)
                {
                    if (char.IsUpper(parts[depth][c]) && c > 0)
                        builder.Append('-');
                    builder.Append(char.ToLowerInvariant(parts[depth][c]));
                }

                if (depth < parts.Length - 1)
                {
                    builder.Append("\": {\n");
                    stack.Add(parts[depth]);
                    continue;
                }

                builder.Append("\": ");
                object value = values[Fields[i].Name];
                if (value == null)
                {
                    builder.Append("null");
                }
                else if (value is string)
                {
                    builder.Append('"');
                    // Escape JSON
                    builder.Append(((string)value).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t"));
                    builder.Append('"');
                }
                else if (value is bool)
                {
                    builder.Append((bool)value ? "true" : "false");
                }
                else if (value is float)
                {
                    float number = (float)value;
                    float rounded = Mathf.Round(number);
                    if (Mathf.Abs(number - rounded) < 0.0001f)
                        builder.Append(((int)rounded).ToString(CultureInfo.InvariantCulture));
                    else
                        builder.Append(number.ToString("0.###", CultureInfo.InvariantCulture));
                }
                else
                {
                    builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                }
            }
        }

        for (int depth = stack.Count; depth >= 1; depth--)
        {
            builder.Append('\n');
            builder.Append(' ', depth * 4);
            builder.Append('}');
        }

        builder.Append("\n}\n");
        return builder.ToString();
    }

    private static void ApplyDefaults()
    {
        values.Clear();
        defaulted.Clear();
        for (int i = 0; i < Fields.Length; i++)
        {
            if (Fields[i].Name == "Uuid")
                values[Fields[i].Name] = Guid.NewGuid().ToString("D");
            else
                values[Fields[i].Name] = Fields[i].Default;
            defaulted[Fields[i].Name] = true;
        }
    }

    private static void ApplyJson(string text)
    {
        string json = text ?? "";
        int at = json.Length > 0 && json[0] == '\uFEFF' ? 1 : 0;
        var lookup = new Dictionary<string, string>();
        for (int i = 0; i < Fields.Length; i++)
        {
            string[] parts = Fields[i].Name.Split('.');
            var path = new StringBuilder();
            for (int p = 0; p < parts.Length; p++)
            {
                if (p > 0)
                    path.Append('.');

                // Convert to kebab case
                for (int c = 0; c < parts[p].Length; c++)
                {
                    if (char.IsUpper(parts[p][c]) && c > 0)
                        path.Append('-');
                    path.Append(char.ToLowerInvariant(parts[p][c]));
                }
            }

            lookup[path.ToString()] = Fields[i].Name;
        }

        void Skip()
        {
            while (at < json.Length && (json[at] == ' ' || json[at] == '\n' || json[at] == '\r' || json[at] == '\t'))
                at++;
        }

        string ReadString()
        {
            if (at >= json.Length || json[at] != '"')
                throw new FormatException("Expected string in config.json.");

            at++;
            var builder = new StringBuilder();
            while (at < json.Length)
            {
                char character = json[at++];
                if (character == '"')
                    return builder.ToString();
                if (character != '\\')
                {
                    builder.Append(character);
                    continue;
                }

                if (at >= json.Length)
                    throw new FormatException("Unfinished escape in config.json.");

                char escaped = json[at++];
                if (escaped == 'n')
                    builder.Append('\n');
                else if (escaped == 'r')
                    builder.Append('\r');
                else if (escaped == 't')
                    builder.Append('\t');
                else
                    builder.Append(escaped);
            }

            throw new FormatException("Unclosed string in config.json.");
        }

        double ReadNumber()
        {
            int start = at;
            if (at < json.Length && json[at] == '-')
                at++;
            while (at < json.Length && ((json[at] >= '0' && json[at] <= '9') || json[at] == '.' || json[at] == 'e' || json[at] == 'E' || json[at] == '+'))
                at++;

            double number;
            if (!double.TryParse(json.Substring(start, at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                throw new FormatException("Invalid number in config.json.");

            return number;
        }

        void SkipValue()
        {
            if (at >= json.Length)
                throw new FormatException("Unexpected end of config.json.");

            if (json[at] == '"')
            {
                ReadString();
                return;
            }

            if (json[at] == '{' || json[at] == '[')
            {
                char open = json[at];
                char close = open == '{' ? '}' : ']';
                int depth = 0;
                while (at < json.Length)
                {
                    if (json[at] == '"')
                    {
                        ReadString();
                        continue;
                    }

                    char character = json[at++];
                    if (character == open)
                        depth++;
                    else if (character == close && --depth == 0)
                        return;
                }

                throw new FormatException("Unclosed value in config.json.");
            }

            if (json[at] == '-' || (json[at] >= '0' && json[at] <= '9'))
            {
                ReadNumber();
                return;
            }

            while (at < json.Length && char.IsLetter(json[at]))
                at++;
        }

        void ReadObject(string path)
        {
            if (at >= json.Length || json[at] != '{')
                throw new FormatException("Expected object in config.json.");

            at++;
            Skip();
            if (at < json.Length && json[at] == '}')
            {
                at++;
                return;
            }

            while (at < json.Length)
            {
                string key = ReadString();
                Skip();
                if (at >= json.Length || json[at] != ':')
                    throw new FormatException("Expected ':' in config.json.");

                at++;
                Skip();
                string child = path.Length == 0 ? key : path + "." + key;
                if (at < json.Length && json[at] == '{')
                {
                    ReadObject(child);
                }
                else
                {
                    string fieldName;
                    if (!lookup.TryGetValue(child, out fieldName))
                    {
                        SkipValue();
                    }
                    else if (at < json.Length && json[at] == '"')
                    {
                        if (!(FieldDefault(fieldName) is string))
                            throw new FormatException("Expected a number in config.json.");

                        values[fieldName] = ReadString();
                        defaulted[fieldName] = false;
                    }
                    else if (at < json.Length && char.IsLetter(json[at]))
                    {
                        int start = at;
                        while (at < json.Length && char.IsLetter(json[at]))
                            at++;
                        string word = json.Substring(start, at - start);
                        object fallback = FieldDefault(fieldName);
                        if (word == "null")
                        {
                            if (!(fallback is string))
                                throw new FormatException("Expected a value in config.json.");
                            values[fieldName] = null;
                            defaulted[fieldName] = false;
                        }
                        else if (word == "true" || word == "false")
                        {
                            if (!(fallback is bool))
                                throw new FormatException("Expected a value in config.json.");
                            values[fieldName] = word == "true";
                            defaulted[fieldName] = false;
                        }
                        else
                        {
                            throw new FormatException("Expected a value in config.json.");
                        }
                    }
                    else
                    {
                        if (FieldDefault(fieldName) is string)
                            throw new FormatException("Expected a string in config.json.");

                        values[fieldName] = Convert.ChangeType(ReadNumber(), FieldDefault(fieldName).GetType(), CultureInfo.InvariantCulture);
                        defaulted[fieldName] = false;
                    }
                }

                Skip();
                if (at < json.Length && json[at] == ',')
                {
                    at++;
                    Skip();
                    if (at < json.Length && json[at] == '}')
                    {
                        at++;
                        return;
                    }

                    continue;
                }

                if (at < json.Length && json[at] == '}')
                {
                    at++;
                    return;
                }

                throw new FormatException("Expected ',' or '}' in config.json.");
            }

            throw new FormatException("Unclosed object in config.json.");
        }

        Skip();
        ReadObject("");
        Skip();
        if (at < json.Length)
            throw new FormatException("Unexpected trailing data in config.json.");
    }

    public static bool Exists(string keypath)
    {
        bool parent;
        string field;
        string path;
        string error;
        if (!TryHit(keypath, out path, out parent, out field, out error))
            return false;

        if (!parent)
            return values[field] != null;

        var names = new List<string>();
        Collect(path, names);
        for (int i = 0; i < names.Count; i++)
        {
            if (values[names[i]] != null)
                return true;
        }

        return false;
    }

    public static bool TryGetText(string keypath, out string text, out string error)
    {
        text = null;
        bool parent;
        string field;
        string path;
        if (!TryHit(keypath, out path, out parent, out field, out error))
            return false;

        if (!parent)
        {
            object value = values[field];
            string plain = value as string;
            if (value == null)
                text = "null";
            else if (plain != null || value is string)
                text = plain ?? "";
            else
                text = JsonScalar(value);
            return true;
        }

        var builder = new StringBuilder();
        WriteNode(builder, path, 0);
        text = builder.ToString();
        return true;
    }

    public static bool TryTypeName(string keypath, out string text, out string error)
    {
        text = null;
        bool parent;
        string field;
        string path;
        if (!TryHit(keypath, out path, out parent, out field, out error))
            return false;

        text = parent ? "object" : TypeName(FieldDefault(field).GetType());
        return true;
    }

    public static bool TryIsDefault(string keypath, out bool value, out string error)
    {
        value = false;
        bool parent;
        string field;
        string path;
        if (!TryHit(keypath, out path, out parent, out field, out error))
            return false;

        if (!parent)
        {
            value = defaulted[field];
            return true;
        }

        var names = new List<string>();
        Collect(path, names);
        value = true;
        for (int i = 0; i < names.Count; i++)
        {
            if (!defaulted[names[i]])
            {
                value = false;
                break;
            }
        }

        return true;
    }

    public static bool TryIsNull(string keypath, out bool value, out string error)
    {
        value = false;
        bool parent;
        string field;
        string path;
        if (!TryHit(keypath, out path, out parent, out field, out error))
            return false;

        if (!parent)
        {
            value = values[field] == null;
            return true;
        }

        var names = new List<string>();
        Collect(path, names);
        value = names.Count > 0;
        for (int i = 0; i < names.Count; i++)
        {
            if (values[names[i]] != null)
            {
                value = false;
                break;
            }
        }

        return true;
    }

    public static bool TrySet(string keypath, string raw, bool force, out string error)
    {
        error = null;
        bool parent;
        string field;
        string path;
        if (!TryHit(keypath, out path, out parent, out field, out error))
            return false;

        JsonValue node;
        string parseError;
        bool parsed = TryParseJson(raw ?? "", out node, out parseError);
        if (!parent && FieldDefault(field) is string)
        {
            if (parsed && node.Kind == JsonKind.Null)
                AssignField(field, null);
            else if (parsed && node.Kind == JsonKind.String)
                AssignField(field, node.Text);
            else if (parsed && (node.Kind == JsonKind.Object || node.Kind == JsonKind.Array))
            {
                error = "wrong type for \"" + path + "\"";
                return false;
            }
            else
                AssignField(field, raw ?? "");
            return true;
        }

        if (!parsed)
        {
            error = string.IsNullOrEmpty(parseError) ? "invalid JSON" : parseError;
            return false;
        }

        if (parent && node.Kind != JsonKind.Object)
        {
            error = "expected an object for \"" + path + "\"";
            return false;
        }

        var sets = new Dictionary<string, object>();
        var unsets = new List<string>();
        if (!parent)
        {
            object coerced;
            if (!TryCoerce(node, field, path, out coerced, out error))
                return false;
            sets[field] = coerced;
        }
        else if (!Plan(path, node, force, sets, unsets, out error))
        {
            return false;
        }

        for (int i = 0; i < unsets.Count; i++)
        {
            if (!sets.ContainsKey(unsets[i]))
                UnsetField(unsets[i]);
        }

        foreach (KeyValuePair<string, object> pair in sets)
            AssignField(pair.Key, pair.Value);
        return true;
    }

    public static bool TryUnset(string keypath, out string error)
    {
        return MutateEach(keypath, false, out error);
    }

    public static bool TryDefault(string keypath, out string error)
    {
        return MutateEach(keypath, true, out error);
    }

    static bool MutateEach(string keypath, bool toDefault, out string error)
    {
        bool parent;
        string field;
        string path;
        if (!TryHit(keypath, out path, out parent, out field, out error))
            return false;

        if (!parent)
        {
            if (toDefault)
                DefaultField(field);
            else
                UnsetField(field);
            return true;
        }

        var names = new List<string>();
        Collect(path, names);
        for (int i = 0; i < names.Count; i++)
        {
            if (toDefault)
                DefaultField(names[i]);
            else
                UnsetField(names[i]);
        }

        return true;
    }

    static bool TryHit(string keypath, out string path, out bool parent, out string field, out string error)
    {
        path = NormalizeKey(keypath);
        parent = false;
        field = null;
        error = null;
        if (path == null)
        {
            error = "invalid key \"" + (keypath ?? "") + "\"";
            return false;
        }

        if (!TryMatch(path, out parent, out field, out error))
            return false;
        return true;
    }

    static bool TryMatch(string path, out bool parent, out string field, out string error)
    {
        parent = false;
        field = null;
        error = null;
        for (int i = 0; i < Fields.Length; i++)
        {
            string kebab = KebabPath(Fields[i].Name);
            if (kebab == path)
                field = Fields[i].Name;
            else if (kebab.StartsWith(path + ".", StringComparison.Ordinal))
                parent = true;
        }

        if (field != null)
        {
            parent = false;
            return true;
        }

        if (parent)
            return true;

        error = "unknown key \"" + path + "\"";
        return false;
    }

    static void Collect(string path, List<string> names)
    {
        string prefix = path + ".";
        for (int i = 0; i < Fields.Length; i++)
        {
            string kebab = KebabPath(Fields[i].Name);
            if (kebab.StartsWith(prefix, StringComparison.Ordinal))
                names.Add(Fields[i].Name);
        }
    }

    static string NormalizeKey(string keypath)
    {
        if (string.IsNullOrWhiteSpace(keypath))
            return null;

        string[] parts = keypath.Trim().Split('.');
        var builder = new StringBuilder();
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0)
                return null;
            if (i > 0)
                builder.Append('.');
            builder.Append(Kebab(parts[i]));
        }

        return builder.ToString();
    }

    static string KebabPath(string name)
    {
        string[] parts = name.Split('.');
        var builder = new StringBuilder();
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0)
                builder.Append('.');
            builder.Append(Kebab(parts[i]));
        }

        return builder.ToString();
    }

    static string Kebab(string part)
    {
        var builder = new StringBuilder();
        for (int c = 0; c < part.Length; c++)
        {
            if (char.IsUpper(part[c]) && c > 0)
                builder.Append('-');
            builder.Append(char.ToLowerInvariant(part[c]));
        }

        return builder.ToString();
    }

    static object FieldDefault(string name)
    {
        for (int i = 0; i < Fields.Length; i++)
        {
            if (Fields[i].Name == name)
                return Fields[i].Default;
        }

        return null;
    }

    static void AssignField(string name, object value)
    {
        values[name] = value;
        if (name == "Uuid")
            defaulted[name] = false;
        else
            defaulted[name] = SameValue(value, FieldDefault(name));
    }

    static void UnsetField(string name)
    {
        object fallback = FieldDefault(name);
        if (fallback is string)
        {
            values[name] = null;
            defaulted[name] = false;
        }
        else
        {
            values[name] = fallback;
            defaulted[name] = true;
        }
    }

    static void DefaultField(string name)
    {
        if (name == "Uuid")
            values[name] = Guid.NewGuid().ToString("D");
        else
            values[name] = FieldDefault(name);
        defaulted[name] = true;
    }

    static bool SameValue(object left, object right)
    {
        if (left == null || right == null)
            return left == null && right == null;
        if (left is float && right is float)
            return Mathf.Abs((float)left - (float)right) < 0.0001f;
        return left.Equals(right);
    }

    static string TypeName(Type type)
    {
        if (type == typeof(string))
            return "string";
        if (type == typeof(int))
            return "int";
        if (type == typeof(float))
            return "float";
        if (type == typeof(bool))
            return "bool";
        return type.Name;
    }

    static string JsonScalar(object value)
    {
        if (value == null)
            return "null";
        if (value is bool)
            return (bool)value ? "true" : "false";
        if (value is int)
            return ((int)value).ToString(CultureInfo.InvariantCulture);
        if (value is float)
            return FormatFloat((float)value);
        if (value is string)
            return "\"" + EscapeJson((string)value) + "\"";
        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    static string FormatFloat(float number)
    {
        float rounded = Mathf.Round(number);
        if (Mathf.Abs(number - rounded) < 0.0001f)
            return ((int)rounded).ToString(CultureInfo.InvariantCulture);
        return number.ToString("0.###", CultureInfo.InvariantCulture);
    }

    static string EscapeJson(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        return text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }

    static void WriteNode(StringBuilder builder, string path, int indent)
    {
        bool parent;
        string field;
        string error;
        TryMatch(path, out parent, out field, out error);
        if (!parent)
        {
            builder.Append(JsonScalar(values[field]));
            return;
        }

        var names = new List<string>();
        Collect(path, names);
        var segments = new List<string>();
        for (int i = 0; i < names.Count; i++)
        {
            string segment = NextSegment(KebabPath(names[i]), path);
            if (!segments.Contains(segment))
                segments.Add(segment);
        }

        builder.Append("{\n");
        for (int i = 0; i < segments.Count; i++)
        {
            builder.Append(' ', (indent + 1) * 4);
            builder.Append('"');
            builder.Append(EscapeJson(segments[i]));
            builder.Append("\": ");
            string child = path.Length == 0 ? segments[i] : path + "." + segments[i];
            WriteNode(builder, child, indent + 1);
            if (i + 1 < segments.Count)
                builder.Append(',');
            builder.Append('\n');
        }

        if (indent > 0)
            builder.Append(' ', indent * 4);
        builder.Append('}');
    }

    static string NextSegment(string full, string parent)
    {
        string rest = parent.Length == 0 ? full : full.Substring(parent.Length + 1);
        int dot = rest.IndexOf('.');
        return dot < 0 ? rest : rest.Substring(0, dot);
    }

    static bool Plan(string path, JsonValue node, bool force, Dictionary<string, object> sets, List<string> unsets, out string error)
    {
        error = null;
        bool parent;
        string field;
        if (!TryMatch(path, out parent, out field, out error))
            return false;

        if (!parent)
        {
            if (FieldDefault(field) is string)
            {
                if (node.Kind == JsonKind.Null)
                {
                    sets[field] = null;
                    return true;
                }

                if (node.Kind == JsonKind.String)
                {
                    sets[field] = node.Text;
                    return true;
                }

                error = "wrong type for \"" + path + "\"";
                return false;
            }

            object coerced;
            if (!TryCoerce(node, field, path, out coerced, out error))
                return false;
            sets[field] = coerced;
            return true;
        }

        if (node.Kind != JsonKind.Object)
        {
            error = "expected an object for \"" + path + "\"";
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < node.Members.Count; i++)
        {
            string segment = Kebab(node.Members[i].Key);
            string child = path.Length == 0 ? segment : path + "." + segment;
            if (!seen.Add(segment))
            {
                error = "duplicate key \"" + child + "\"";
                return false;
            }

            if (!Plan(child, node.Members[i].Value, force, sets, unsets, out error))
                return false;
        }

        if (!force)
            return true;

        var names = new List<string>();
        Collect(path, names);
        for (int i = 0; i < names.Count; i++)
        {
            string segment = NextSegment(KebabPath(names[i]), path);
            if (!seen.Contains(segment))
                unsets.Add(names[i]);
        }

        return true;
    }

    static bool TryCoerce(JsonValue node, string field, string path, out object value, out string error)
    {
        value = null;
        error = null;
        Type type = FieldDefault(field).GetType();
        if (node.Kind != JsonKind.Bool && node.Kind != JsonKind.Number)
        {
            error = "wrong type for \"" + path + "\"";
            return false;
        }

        if (type == typeof(bool))
        {
            if (node.Kind != JsonKind.Bool)
            {
                error = "wrong type for \"" + path + "\"";
                return false;
            }

            value = node.Bool;
            return true;
        }

        if (node.Kind != JsonKind.Number)
        {
            error = "wrong type for \"" + path + "\"";
            return false;
        }

        if (type == typeof(int))
        {
            if (node.Number < int.MinValue || node.Number > int.MaxValue || Math.Abs(node.Number - Math.Round(node.Number)) > 0.0000001d)
            {
                error = "wrong type for \"" + path + "\"";
                return false;
            }

            value = (int)Math.Round(node.Number);
            return true;
        }

        value = (float)node.Number;
        return true;
    }

    enum JsonKind
    {
        Null,
        Bool,
        Number,
        String,
        Object,
        Array
    }

    sealed class JsonValue
    {
        public JsonKind Kind;
        public bool Bool;
        public double Number;
        public string Text;
        public List<JsonMember> Members;
    }

    sealed class JsonMember
    {
        public string Key;
        public JsonValue Value;
    }

    static bool TryParseJson(string text, out JsonValue node, out string error)
    {
        node = null;
        error = null;
        int at = 0;
        try
        {
            node = ReadJson(text, ref at);
            SkipJson(text, ref at);
            if (at != text.Length)
            {
                node = null;
                error = "invalid JSON";
                return false;
            }

            return true;
        }
        catch (FormatException)
        {
            node = null;
            error = "invalid JSON";
            return false;
        }
    }

    static JsonValue ReadJson(string text, ref int at)
    {
        SkipJson(text, ref at);
        if (at >= text.Length)
            throw new FormatException("invalid JSON");

        char character = text[at];
        if (character == '"')
            return new JsonValue { Kind = JsonKind.String, Text = ReadJsonString(text, ref at) };
        if (character == '{')
            return ReadJsonObject(text, ref at);
        if (character == '[')
        {
            SkipJsonBlock(text, ref at, '[', ']');
            return new JsonValue { Kind = JsonKind.Array };
        }

        if (char.IsLetter(character))
        {
            int start = at;
            while (at < text.Length && char.IsLetter(text[at]))
                at++;
            string word = text.Substring(start, at - start);
            if (word == "null")
                return new JsonValue { Kind = JsonKind.Null };
            if (word == "true" || word == "false")
                return new JsonValue { Kind = JsonKind.Bool, Bool = word == "true" };
            throw new FormatException("invalid JSON");
        }

        return new JsonValue { Kind = JsonKind.Number, Number = ReadJsonNumber(text, ref at) };
    }

    static JsonValue ReadJsonObject(string text, ref int at)
    {
        if (at >= text.Length || text[at] != '{')
            throw new FormatException("invalid JSON");

        at++;
        var node = new JsonValue { Kind = JsonKind.Object, Members = new List<JsonMember>() };
        SkipJson(text, ref at);
        if (at < text.Length && text[at] == '}')
        {
            at++;
            return node;
        }

        while (at < text.Length)
        {
            string key = ReadJsonString(text, ref at);
            SkipJson(text, ref at);
            if (at >= text.Length || text[at] != ':')
                throw new FormatException("invalid JSON");
            at++;
            node.Members.Add(new JsonMember { Key = key, Value = ReadJson(text, ref at) });
            SkipJson(text, ref at);
            if (at < text.Length && text[at] == ',')
            {
                at++;
                SkipJson(text, ref at);
                continue;
            }

            if (at < text.Length && text[at] == '}')
            {
                at++;
                return node;
            }

            throw new FormatException("invalid JSON");
        }

        throw new FormatException("invalid JSON");
    }

    static string ReadJsonString(string text, ref int at)
    {
        if (at >= text.Length || text[at] != '"')
            throw new FormatException("invalid JSON");

        at++;
        var builder = new StringBuilder();
        while (at < text.Length)
        {
            char character = text[at++];
            if (character == '"')
                return builder.ToString();
            if (character != '\\')
            {
                builder.Append(character);
                continue;
            }

            if (at >= text.Length)
                throw new FormatException("invalid JSON");
            char escaped = text[at++];
            if (escaped == 'n')
                builder.Append('\n');
            else if (escaped == 'r')
                builder.Append('\r');
            else if (escaped == 't')
                builder.Append('\t');
            else
                builder.Append(escaped);
        }

        throw new FormatException("invalid JSON");
    }

    static double ReadJsonNumber(string text, ref int at)
    {
        int start = at;
        if (at < text.Length && text[at] == '-')
            at++;
        while (at < text.Length && ((text[at] >= '0' && text[at] <= '9') || text[at] == '.' || text[at] == 'e' || text[at] == 'E' || text[at] == '+'))
            at++;

        double number;
        if (start == at || !double.TryParse(text.Substring(start, at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            throw new FormatException("invalid JSON");
        return number;
    }

    static void SkipJsonBlock(string text, ref int at, char open, char close)
    {
        int depth = 0;
        while (at < text.Length)
        {
            if (text[at] == '"')
            {
                ReadJsonString(text, ref at);
                continue;
            }

            char character = text[at++];
            if (character == open)
                depth++;
            else if (character == close && --depth == 0)
                return;
        }

        throw new FormatException("invalid JSON");
    }

    static void SkipJson(string text, ref int at)
    {
        while (at < text.Length && (text[at] == ' ' || text[at] == '\n' || text[at] == '\r' || text[at] == '\t'))
            at++;
    }
}

public static class RemoraDev
{
    public static class ScreenShot
    {
        public struct Result
        {
            public string FilePath;
            public int Width;
            public int Height;

            public Result(string filePath, int width, int height)
            {
                FilePath = filePath;
                Width = width;
                Height = height;
            }
        }

        const int MaxSide = 16384;

        public static bool TryParseResolution(string text, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (string.IsNullOrEmpty(text))
                return false;
            if (string.Equals(text, "480p", StringComparison.OrdinalIgnoreCase))
            {
                width = 640;
                height = 480;
                return true;
            }
            if (string.Equals(text, "720p", StringComparison.OrdinalIgnoreCase))
            {
                width = 1280;
                height = 720;
                return true;
            }
            if (string.Equals(text, "1080p", StringComparison.OrdinalIgnoreCase))
            {
                width = 1920;
                height = 1080;
                return true;
            }
            if (string.Equals(text, "1440p", StringComparison.OrdinalIgnoreCase))
            {
                width = 2560;
                height = 1440;
                return true;
            }

            int split = text.IndexOf('x');
            if (split < 0)
                split = text.IndexOf('X');
            if (split <= 0 || split >= text.Length - 1)
                return false;
            string left = text.Substring(0, split);
            string right = text.Substring(split + 1);
            if (right.IndexOf('x') >= 0 || right.IndexOf('X') >= 0)
                return false;
            int parsedWidth;
            int parsedHeight;
            if (!int.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedWidth))
                return false;
            if (!int.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedHeight))
                return false;
            if (parsedWidth <= 0 || parsedHeight <= 0)
                return false;
            width = parsedWidth;
            height = parsedHeight;
            return true;
        }

        public static Result Capture(Camera cam, string res = null, string file = null, bool visible = false, object targets = null)
        {
            if (cam == null)
                throw new InvalidOperationException("no camera");

            int targetWidth = Mathf.Max(1, Screen.width);
            int targetHeight = Mathf.Max(1, Screen.height);
            if (!string.IsNullOrEmpty(res))
            {
                if (!TryParseResolution(res, out targetWidth, out targetHeight))
                    throw new InvalidOperationException("invalid resolution \"" + res + "\"");
            }

            bool framed = targets != null || !visible;
            if (!framed)
                return Render(cam, false, cam.transform.position, 0f, 0f, targetWidth, targetHeight, targetWidth, targetHeight, file);

            List<Bounds> boxes = new List<Bounds>();
            if (targets != null)
                CollectTargets(targets, boxes);
            else
                CollectWorld(boxes);
            if (boxes.Count == 0)
                throw new InvalidOperationException(targets != null ? "selection has nothing to capture" : "nothing to capture");

            Vector3 right = cam.transform.right;
            Vector3 up = cam.transform.up;
            float minRight = 0f;
            float maxRight = 0f;
            float minUp = 0f;
            float maxUp = 0f;
            Project(boxes, right, up, out minRight, out maxRight, out minUp, out maxUp);
            if (visible)
            {
                float viewMinRight;
                float viewMaxRight;
                float viewMinUp;
                float viewMaxUp;
                VisibleSpan(cam, boxes, right, up, out viewMinRight, out viewMaxRight, out viewMinUp, out viewMaxUp);
                minRight = Mathf.Max(minRight, viewMinRight);
                maxRight = Mathf.Min(maxRight, viewMaxRight);
                minUp = Mathf.Max(minUp, viewMinUp);
                maxUp = Mathf.Min(maxUp, viewMaxUp);
                if (maxRight - minRight <= 0.0001f || maxUp - minUp <= 0.0001f)
                    throw new InvalidOperationException("nothing visible");
            }

            float width = maxRight - minRight;
            float height = maxUp - minUp;
            if (width <= 0.0001f || height <= 0.0001f)
                throw new InvalidOperationException("nothing to capture");

            Vector3 origin = cam.transform.position;
            Vector3 center = origin
                + right * (((minRight + maxRight) * 0.5f) - Vector3.Dot(origin, right))
                + up * (((minUp + maxUp) * 0.5f) - Vector3.Dot(origin, up));
            int renderWidth;
            int renderHeight;
            Scale(cam, center, width, height, targetWidth, targetHeight, out renderWidth, out renderHeight);
            return Render(cam, true, center, width, height, renderWidth, renderHeight, targetWidth, targetHeight, file);
        }

        static void CollectWorld(List<Bounds> boxes)
        {
            Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;
                if (!Useful(renderer.bounds))
                    continue;
                boxes.Add(renderer.bounds);
            }
        }

        static void CollectTargets(object value, List<Bounds> boxes)
        {
            if (value == null)
                return;
            object[] many = value as object[];
            if (many != null)
            {
                for (int i = 0; i < many.Length; i++)
                    CollectTargets(many[i], boxes);
                return;
            }

            IList list = value as IList;
            if (list != null && !(value is string) && !(value is UnityEngine.Object))
            {
                for (int i = 0; i < list.Count; i++)
                    CollectTargets(list[i], boxes);
                return;
            }

            Renderer renderer = value as Renderer;
            if (renderer != null)
            {
                if (Useful(renderer.bounds))
                    boxes.Add(renderer.bounds);
                return;
            }

            Collider volume = value as Collider;
            if (volume != null)
            {
                if (Useful(volume.bounds))
                    boxes.Add(volume.bounds);
                return;
            }

            Collider2D flat = value as Collider2D;
            if (flat != null)
            {
                if (Useful(flat.bounds))
                    boxes.Add(flat.bounds);
                return;
            }

            Component component = value as Component;
            GameObject host = value as GameObject;
            if (host == null && component != null)
                host = component.gameObject;
            if (host == null)
                return;

            bool any = false;
            Renderer[] renderers = host.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!Useful(renderers[i].bounds))
                    continue;
                boxes.Add(renderers[i].bounds);
                any = true;
            }
            if (any)
                return;

            Collider[] volumes = host.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < volumes.Length; i++)
            {
                if (!Useful(volumes[i].bounds))
                    continue;
                boxes.Add(volumes[i].bounds);
                any = true;
            }
            Collider2D[] flats = host.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < flats.Length; i++)
            {
                if (!Useful(flats[i].bounds))
                    continue;
                boxes.Add(flats[i].bounds);
                any = true;
            }
            if (!any)
                boxes.Add(new Bounds(host.transform.position, Vector3.one));
        }

        static bool Useful(Bounds bounds)
        {
            Vector3 size = bounds.size;
            if (float.IsNaN(size.x) || float.IsNaN(size.y) || float.IsNaN(size.z))
                return false;
            if (float.IsInfinity(size.x) || float.IsInfinity(size.y) || float.IsInfinity(size.z))
                return false;
            return true;
        }

        static void Project(List<Bounds> boxes, Vector3 right, Vector3 up, out float minRight, out float maxRight, out float minUp, out float maxUp)
        {
            minRight = float.PositiveInfinity;
            maxRight = float.NegativeInfinity;
            minUp = float.PositiveInfinity;
            maxUp = float.NegativeInfinity;
            for (int i = 0; i < boxes.Count; i++)
            {
                Vector3 center = boxes[i].center;
                Vector3 extents = boxes[i].extents;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = center + new Vector3(
                        (corner & 1) == 0 ? -extents.x : extents.x,
                        (corner & 2) == 0 ? -extents.y : extents.y,
                        (corner & 4) == 0 ? -extents.z : extents.z);
                    float rightDot = Vector3.Dot(point, right);
                    float upDot = Vector3.Dot(point, up);
                    if (rightDot < minRight)
                        minRight = rightDot;
                    if (rightDot > maxRight)
                        maxRight = rightDot;
                    if (upDot < minUp)
                        minUp = upDot;
                    if (upDot > maxUp)
                        maxUp = upDot;
                }
            }
        }

        static void VisibleSpan(Camera cam, List<Bounds> boxes, Vector3 right, Vector3 up, out float minRight, out float maxRight, out float minUp, out float maxUp)
        {
            Vector3 origin = cam.transform.position;
            float halfWidth;
            float halfHeight;
            if (cam.orthographic)
            {
                halfHeight = cam.orthographicSize;
                halfWidth = halfHeight * cam.aspect;
            }
            else
            {
                float distance = 0f;
                int used = 0;
                float near = Mathf.Max(0.01f, cam.nearClipPlane);
                for (int i = 0; i < boxes.Count; i++)
                {
                    float depth = Vector3.Dot(boxes[i].center - origin, cam.transform.forward);
                    if (depth <= near)
                        continue;
                    distance += depth;
                    used++;
                }
                if (used == 0)
                    throw new InvalidOperationException("nothing visible");
                distance /= used;
                halfHeight = distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                halfWidth = halfHeight * cam.aspect;
            }

            float originRight = Vector3.Dot(origin, right);
            float originUp = Vector3.Dot(origin, up);
            minRight = originRight - halfWidth;
            maxRight = originRight + halfWidth;
            minUp = originUp - halfHeight;
            maxUp = originUp + halfHeight;
        }

        static void Scale(Camera cam, Vector3 center, float width, float height, int targetWidth, int targetHeight, out int renderWidth, out int renderHeight)
        {
            float unitWidth;
            float unitHeight;
            if (cam.orthographic)
            {
                unitHeight = cam.orthographicSize * 2f;
                unitWidth = unitHeight * cam.aspect;
            }
            else
            {
                float distance = Mathf.Abs(Vector3.Dot(center - cam.transform.position, cam.transform.forward));
                if (distance < 0.001f)
                    distance = Mathf.Max(0.01f, cam.nearClipPlane);
                unitHeight = 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                unitWidth = unitHeight * cam.aspect;
            }

            if (unitWidth < 0.0001f || unitHeight < 0.0001f)
                throw new InvalidOperationException("camera view is empty");

            renderWidth = Mathf.Max(1, Mathf.RoundToInt(width * (targetWidth / unitWidth)));
            renderHeight = Mathf.Max(1, Mathf.RoundToInt(height * (targetHeight / unitHeight)));
            if (renderWidth > MaxSide || renderHeight > MaxSide)
                throw new InvalidOperationException("image is " + renderWidth.ToString(CultureInfo.InvariantCulture) + "x" + renderHeight.ToString(CultureInfo.InvariantCulture) + ", which is too large");
        }

        static Result Render(Camera cam, bool frame, Vector3 center, float width, float height, int renderWidth, int renderHeight, int targetWidth, int targetHeight, string file)
        {
            if (renderWidth <= 0 || renderHeight <= 0 || renderWidth > MaxSide || renderHeight > MaxSide)
                throw new InvalidOperationException("image is " + renderWidth.ToString(CultureInfo.InvariantCulture) + "x" + renderHeight.ToString(CultureInfo.InvariantCulture) + ", which is too large");

            Vector3 position = cam.transform.position;
            Quaternion rotation = cam.transform.rotation;
            bool orthographic = cam.orthographic;
            float orthographicSize = cam.orthographicSize;
            RenderTexture previous = cam.targetTexture;
            bool forced = cam.forceIntoRenderTexture;
            RenderTexture renderTexture = null;
            Texture2D screenshot = null;
            try
            {
                renderTexture = new RenderTexture(renderWidth, renderHeight, 24);
                renderTexture.Create();
                cam.forceIntoRenderTexture = true;
                cam.targetTexture = renderTexture;
                cam.ResetAspect();
                if (frame)
                {
                    cam.transform.position = center;
                    cam.transform.rotation = rotation;
                    cam.orthographic = true;
                    cam.orthographicSize = height * 0.5f;
                    cam.aspect = width / Mathf.Max(0.0001f, height);
                }

                Paint(cam, renderTexture);
                RenderTexture.active = renderTexture;
                screenshot = new Texture2D(renderWidth, renderHeight, TextureFormat.RGB24, false);
                screenshot.ReadPixels(new Rect(0, 0, renderWidth, renderHeight), 0, 0);
                screenshot.Apply();
                RenderTexture.active = null;

                string path = FinishPath(file, renderWidth, renderHeight, targetWidth, targetHeight);
                string folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                    Directory.CreateDirectory(folder);
                File.WriteAllBytes(path, screenshot.EncodeToPNG());
                return new Result(path, renderWidth, renderHeight);
            }
            finally
            {
                RenderTexture.active = null;
                cam.transform.position = position;
                cam.transform.rotation = rotation;
                cam.orthographic = orthographic;
                cam.orthographicSize = orthographicSize;
                cam.targetTexture = previous;
                cam.forceIntoRenderTexture = forced;
                cam.ResetAspect();
                if (screenshot != null)
                    UnityEngine.Object.Destroy(screenshot);
                if (renderTexture != null)
                {
                    renderTexture.Release();
                    UnityEngine.Object.Destroy(renderTexture);
                }
            }
        }

        static void Paint(Camera cam, RenderTexture target)
        {
            try
            {
                if (TrySubmit(cam, target))
                    return;
            }
            catch (TargetInvocationException exception)
            {
                if (exception.InnerException != null)
                    throw exception.InnerException;
                throw;
            }

            cam.Render();
        }

        static bool TrySubmit(Camera cam, RenderTexture target)
        {
            Type pipeline = Type.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipeline, Unity.RenderPipelines.Universal.Runtime");
            if (pipeline == null)
                return false;
            Type requestType = pipeline.GetNestedType("SingleCameraRequest");
            if (requestType == null)
                return false;
            object request = Activator.CreateInstance(requestType);
            FieldInfo destination = requestType.GetField("destination");
            if (destination == null)
                return false;
            destination.SetValue(request, target);

            MethodInfo supports = null;
            MethodInfo submit = null;
            MethodInfo[] methods = typeof(RenderPipeline).GetMethods(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
            {
                if (!methods[i].IsGenericMethodDefinition)
                    continue;
                if (methods[i].Name == "SupportsRenderRequest")
                    supports = methods[i];
                else if (methods[i].Name == "SubmitRenderRequest")
                    submit = methods[i];
            }
            if (supports == null || submit == null)
                return false;

            object supported = supports.MakeGenericMethod(requestType).Invoke(null, new object[] { cam, request });
            if (!(supported is bool) || !(bool)supported)
                return false;
            submit.MakeGenericMethod(requestType).Invoke(null, new object[] { cam, request });
            return true;
        }

        static string FinishPath(string file, int width, int height, int targetWidth, int targetHeight)
        {
            string name = string.IsNullOrEmpty(file) ? "%timestamp%_%width%x%height%.png" : file;
            name = name.Replace("%timestamp%", DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss"));
            name = name.Replace("%width%", width.ToString(CultureInfo.InvariantCulture));
            name = name.Replace("%height%", height.ToString(CultureInfo.InvariantCulture));
            name = name.Replace("%targetWidth%", targetWidth.ToString(CultureInfo.InvariantCulture));
            name = name.Replace("%targetHeight%", targetHeight.ToString(CultureInfo.InvariantCulture));
            if (!Path.IsPathRooted(name))
                name = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures")), name);
            return name;
        }
    }

    public static class Console
    {
        public enum Format
        {
            NONE,
            FORMATTED,
            ANSI
        }

        public static void SendTextIn(string content, bool raw = true)
        {
            RemoraDevConsole.ApiSend(content, raw);
        }

        public static void SetBuffer(string content)
        {
            RemoraDevConsole.ApiSetBuffer(content);
        }

        public static void SetLine(int lineNr, string content)
        {
            RemoraDevConsole.ApiSetLine(lineNr, content);
        }

        public static string GetBuffer(Format format = Format.NONE)
        {
            return RemoraDevConsole.ApiGetBuffer(format);
        }

        public static string GetLastLine(Format format = Format.NONE)
        {
            return RemoraDevConsole.ApiGetLastLine(format);
        }

        public static class Cmds
        {
            public struct Arg
            {
                public string Name;
                public string Type;
                public string Description;
                public bool Variadic;
                public bool Optional;
            }

            public struct Help
            {
                public string Description;
                public Arg[] Args;
            }

            public sealed class Parameter
            {
                public string Name;
                public Type Type;
                public string TypeName;
                public string Description;
                public bool Optional;
                public bool Params;
                public bool HasDefault;
                public object Default;
            }

            public sealed class Command
            {
                public string Id;
                public Parameter[] Parameters;
                public Parameter[] Shown;
                public Help Help;
                public Delegate Handler;
            }

            static readonly Dictionary<string, Command> registry = new Dictionary<string, Command>(StringComparer.Ordinal);
            static readonly Stack<StringBuilder> capturing = new Stack<StringBuilder>();
            static int logBypass;
            static int scriptDepth;
            static int macroDepth;
            static string userPrefix = "> ";
            static readonly Dictionary<string, MacroDef> macros = new Dictionary<string, MacroDef>(StringComparer.Ordinal);
            static readonly Dictionary<string, string> variables = new Dictionary<string, string>(StringComparer.Ordinal);
            static readonly HashSet<string> hiddenInBuild = new HashSet<string>(StringComparer.Ordinal);

            static Cmds()
            {
                Register("script", new Action<string>(ScriptCommand), new Help
                {
                    Description = "Run a .rem file from the remora folder. Each line is a command and can use ; and |.",
                    Args = new[]
                    {
                        new Arg
                        {
                            Name = "file",
                            Type = "file.rem",
                            Description = "File name or relative path inside remora or Remora. Example: test.rem."
                        }
                    }
                });
                Register("clear", new Action(ClearCommand), new Help
                {
                    Description = "Clear the console buffer."
                });
                Register("help", new Action<string>(HelpCommand), new Help
                {
                    Description = "List every command, or show one command in detail.",
                    Args = new[]
                    {
                        new Arg
                        {
                            Name = "command",
                            Description = "Command id. Omit this to print the short list.",
                            Optional = true
                        }
                    }
                });
                Register("print", new Action<string[]>(PrintCommand), new Help
                {
                    Description = "Print text. \\u001b, \\x1b and \\033 in the text become ESC so ANSI codes are parsed.",
                    Args = new[]
                    {
                        new Arg
                        {
                            Name = "text",
                            Description = "One or more strings. Wrap a value in double quotes to keep spaces.",
                            Variadic = true
                        }
                    }
                });
                Register("log", new Action<string, string[]>(LogCommand), new Help
                {
                    Description = "Print text. warn is yellow, error is red, debug is gray, and msg is left unformatted. A reset returns to that level.",
                    Args = new[]
                    {
                        new Arg
                        {
                            Name = "level",
                            Type = "msg|warn|warning|err|error|debug",
                            Description = "warn and warning match, and so do err and error. msg does not force a color."
                        },
                        new Arg
                        {
                            Name = "text",
                            Description = "One or more strings, with the same quoting and escapes as print.",
                            Variadic = true
                        }
                    }
                });
                Register("dlog", new Action<string, string[]>(DlogCommand), new Help
                {
                    Description = "Send text through Debug.Log. warn uses LogWarning and error uses LogError. Nothing is written in this console.",
                    Args = new[]
                    {
                        new Arg
                        {
                            Name = "level",
                            Type = "msg|warn|warning|err|error|debug",
                            Description = "warn and warning match, and so do err and error. msg does not force a color."
                        },
                        new Arg
                        {
                            Name = "text",
                            Description = "One or more strings, with the same quoting and escapes as print.",
                            Variadic = true
                        }
                    }
                });
                HideInBuild("dlog");
                Register("quit", new Action(QuitCommand), new Help
                {
                    Description = "Stop play mode in the editor, or close a built player."
                });
                Register("scene", new Func<string, string, object>(SceneCommand), new Help
                {
                    Description = "Read or change the active scene. Names and indexes come from the scenes included in the build.",
                    Args = new[]
                    {
                        new Arg
                        {
                            Name = "action",
                            Type = "query|queryidx|list|set|setidx",
                            Description = "query prints the current scene name. queryidx prints its build index. list prints every scene. set loads a scene by name. setidx loads a scene by build index."
                        },
                        new Arg
                        {
                            Name = "value",
                            Type = "scene",
                            Description = "Required for set (scene name) and setidx (build index).",
                            Optional = true
                        }
                    }
                });
                Register("var", new Func<string, string, string, object>(VarCommand), new Help
                {
                    Description = "Store text in named variables. save takes the value first and the key second, so a pipe can fill the value.",
                    Args = new[]
                    {
                        new Arg
                        {
                            Name = "action",
                            Type = "save|get|exists|unset",
                            Description = "save stores a value. get prints it. exists prints true or false. unset removes it."
                        },
                        new Arg
                        {
                            Name = "value",
                            Type = "value",
                            Description = "save: the text to store. get, exists, and unset: the variable key.",
                            Optional = true
                        },
                        new Arg
                        {
                            Name = "key",
                            Type = "key",
                            Description = "save: the variable name. Quote a value that contains spaces.",
                            Optional = true
                        }
                    }
                });
                Register("if", new Action<string, string, string, string>(IfCommand), new Help
                {
                    Description = "Run a .rem script when the comparison is true. A value that starts with $ is read from that variable.",
                    Args = new[]
                    {
                        new Arg { Name = "left", Type = "value|$key", Description = "Left side. $key reads the variable named key." },
                        new Arg { Name = "op", Type = "==|!=|>|<|>=|=<", Description = "Comparison. =< is less than or equal." },
                        new Arg { Name = "right", Type = "value|$key", Description = "Right side. $key reads the variable named key." },
                        new Arg { Name = "script", Type = "file.rem", Description = "Script to run when the comparison succeeds." }
                    }
                });
                Register("while", new Action<string, string, string, string>(WhileCommand), new Help
                {
                    Description = "Run a .rem script again while the comparison stays true. $key is read again each pass. Stops after 10000 passes.",
                    Args = new[]
                    {
                        new Arg { Name = "left", Type = "value|$key", Description = "Left side. $key reads the variable named key." },
                        new Arg { Name = "op", Type = "==|!=|>|<|>=|=<", Description = "Comparison. =< is less than or equal." },
                        new Arg { Name = "right", Type = "value|$key", Description = "Right side. $key reads the variable named key." },
                        new Arg { Name = "script", Type = "file.rem", Description = "Script to run while the comparison succeeds." }
                    }
                });
                Register("for", new Action<string, string>(ForCommand), new Help
                {
                    Description = "Run a .rem script a set number of times. The count is a number or $key. Stops after 10000 passes.",
                    Args = new[]
                    {
                        new Arg { Name = "count", Type = "value|$key", Description = "How many times to run. $key reads the variable named key." },
                        new Arg { Name = "script", Type = "file.rem", Description = "Script to run on each pass." }
                    }
                });
                Register("macro", new Func<string, string, string, object>(MacroCommand), new Help
                {
                    Description = "Define a command alias. The macro is stored as m:id and called without the m: prefix. prefer-macros chooses it over a command with the same name.",
                    Args = new[]
                    {
                        new Arg
                        {
                            Name = "action",
                            Type = "add|plate|list|unset|get",
                            Description = "add and plate define a macro. list prints them. get prints one definition. unset removes one."
                        },
                        new Arg
                        {
                            Name = "pattern",
                            Type = "pattern|id",
                            Description = "add/plate: the pattern, first word is the id. unset/get: the id, with or without m:.",
                            Optional = true
                        },
                        new Arg
                        {
                            Name = "body",
                            Type = "body",
                            Description = "add/plate: the expansion. *name, *0, *, *..., and ...* insert arguments. ; and | run as usual.",
                            Optional = true
                        }
                    }
                });
                Register("term", new Func<string, string, string, string, object>(TermCommand), new Help
                {
                    Description = "Read or change the console font size, panel size, and dock corner. set and reset update the pane immediately.",
                    Args = new[]
                    {
                        new Arg
                        {
                            Name = "area",
                            Type = "textscale|size|dock",
                            Description = "textscale is the font size. size is the panel width and height. dock is the screen corner."
                        },
                        new Arg
                        {
                            Name = "action",
                            Type = "reset|ischanged|get|set",
                            Description = "reset restores the loaded config. ischanged returns true when the live value differs. get returns the live value. set applies a new one."
                        },
                        new Arg
                        {
                            Name = "value",
                            Type = "value",
                            Description = "set textscale: font size. set size: width. set dock: bottom-left, bottom-right, top-left, or top-right.",
                            Optional = true
                        },
                        new Arg
                        {
                            Name = "height",
                            Type = "height",
                            Description = "set size: panel height.",
                            Optional = true
                        }
                    }
                });
                Register("prefix", new Action<string, string>(PrefixCommand), new Help
                {
                    Description = "Prefix on lines you type. Command output is not prefixed. The default prefix is \"> \".",
                    Args = new[]
                    {
                        new Arg
                        {
                            Name = "action",
                            Type = "set|default|get",
                            Description = "set stores a prefix, default restores \"> \", get prints the current prefix."
                        },
                        new Arg
                        {
                            Name = "prefix",
                            Type = "string:prefix",
                            Description = "Required for set. Quote it to keep spaces.",
                            Optional = true
                        }
                    }
                });
                Register("remora", new Func<string, string, string[], bool, object>(RemoraCommand), new Help
                {
                    Description = "Read and change the loaded config. config reload reads the json files again. Other actions take a key path such as uuid or dev-console.config.panel-width.",
                    Args = new[]
                    {
                        new Arg
                        {
                            Name = "group",
                            Type = "config",
                            Description = "Config operations."
                        },
                        new Arg
                        {
                            Name = "action",
                            Type = "reload|get|typeof|set|exists|unset|default|isdefault|isnull",
                            Description = "reload loads config again. get prints the value or a JSON object. typeof prints the type name. set writes a value or merges a JSON object. exists, isdefault, and isnull print true or false. unset nulls a string or restores the default. default restores the default."
                        },
                        new Arg
                        {
                            Name = "keypath",
                            Type = "keypath",
                            Description = "Key path. set takes a value after it. Quote a JSON object that contains spaces.",
                            Optional = true,
                            Variadic = true
                        },
                        new Arg
                        {
                            Name = "force",
                            Type = "--force",
                            Description = "With set on a parent, drop child keys the new object does not include.",
                            Optional = true
                        }
                    }
                });
                Register("screenshot", new Func<string, string, string, bool, object>(ScreenshotCommand), new Help
                {
                    Description = "Save a PNG of the world. With no selector, capture every renderer. --visible captures the camera view, or only the visible part of a selection. Files land in Captures next to the project or the built executable.",
                    Args = new[]
                    {
                        new Arg
                        {
                            Name = "target",
                            Type = "selector|res|file",
                            Description = "A U-NOR selector, a resolution (480p, 720p, 1080p, 1440p, or WIDTHxHEIGHT), or a .png file name.",
                            Optional = true
                        },
                        new Arg
                        {
                            Name = "next",
                            Type = "res|file",
                            Description = "Resolution after a selector, or a file name after a resolution.",
                            Optional = true
                        },
                        new Arg
                        {
                            Name = "file",
                            Type = "file",
                            Description = "File name. Tokens: %timestamp%, %width%, %height%, %targetWidth%, %targetHeight%.",
                            Optional = true
                        },
                        new Arg
                        {
                            Name = "visible",
                            Type = "--visible",
                            Description = "Capture the current camera view, or clip a selection to that view.",
                            Optional = true
                        }
                    }
                });
            }

            internal static string PrefixUserLine(string line)
            {
                return userPrefix + (line ?? "");
            }

            static string FormatInvoke(string id, object[] args)
            {
                var builder = new StringBuilder();
                builder.Append(id ?? "");
                if (args == null)
                    return builder.ToString();

                for (int i = 0; i < args.Length; i++)
                {
                    builder.Append(' ');
                    object arg = args[i];
                    string text = arg as string;
                    if (text == null && arg != null)
                        text = Convert.ToString(arg, CultureInfo.InvariantCulture) ?? "";
                    text = text ?? "";
                    bool quote = text.Length == 0;
                    for (int c = 0; c < text.Length && !quote; c++)
                        quote = text[c] == ' ' || text[c] == '"' || text[c] == '\\';
                    if (!quote)
                    {
                        builder.Append(text);
                        continue;
                    }

                    builder.Append('"');
                    for (int c = 0; c < text.Length; c++)
                    {
                        if (text[c] == '\\' || text[c] == '"')
                            builder.Append('\\');
                        builder.Append(text[c]);
                    }

                    builder.Append('"');
                }

                return builder.ToString();
            }

            public static void Register(string id, Delegate handler, Help help = default(Help), bool force = false)
            {
                Add(id, handler, help, force);
            }

            public static void Register(string id, Action handler, Help help = default(Help), bool force = false)
            {
                Add(id, handler, help, force);
            }

            public static void Register<T1>(string id, Action<T1> handler, Help help = default(Help), bool force = false)
            {
                Add(id, handler, help, force);
            }

            public static void Register<T1, T2>(string id, Action<T1, T2> handler, Help help = default(Help), bool force = false)
            {
                Add(id, handler, help, force);
            }

            public static void Register<T1, T2, T3>(string id, Action<T1, T2, T3> handler, Help help = default(Help), bool force = false)
            {
                Add(id, handler, help, force);
            }

            public static void Register<T1, T2, T3, T4>(string id, Action<T1, T2, T3, T4> handler, Help help = default(Help), bool force = false)
            {
                Add(id, handler, help, force);
            }

            public static void Register<TResult>(string id, Func<TResult> handler, Help help = default(Help), bool force = false)
            {
                Add(id, handler, help, force);
            }

            public static void Register<T1, TResult>(string id, Func<T1, TResult> handler, Help help = default(Help), bool force = false)
            {
                Add(id, handler, help, force);
            }

            public static void Register<T1, T2, TResult>(string id, Func<T1, T2, TResult> handler, Help help = default(Help), bool force = false)
            {
                Add(id, handler, help, force);
            }

            public static void Register<T1, T2, T3, TResult>(string id, Func<T1, T2, T3, TResult> handler, Help help = default(Help), bool force = false)
            {
                Add(id, handler, help, force);
            }

            public static void Register<T1, T2, T3, T4, TResult>(string id, Func<T1, T2, T3, T4, TResult> handler, Help help = default(Help), bool force = false)
            {
                Add(id, handler, help, force);
            }

            public static void UnRegister(string id)
            {
                if (string.IsNullOrEmpty(id))
                    return;

                id = id.Trim();
                registry.Remove(id);
                macros.Remove(id);
            }

            public static void HideInBuild(string commandId)
            {
                if (string.IsNullOrWhiteSpace(commandId))
                    return;

                hiddenInBuild.Add(commandId.Trim());
            }

            public static object ManInvoke(string id, params object[] args)
            {
                Command command;
                if (!ResolveCall(id, out command))
                {
                    Fail("unknown command \"" + (id ?? "") + "\"");
                    return null;
                }

                object[] supplied = Expand(args, command);
                if (RemoraDevConfig.Get<bool>("DevConsole.Config.ShowInternalCommands"))
                    RemoraDevConsole.EchoLine(FormatInvoke(command.Id, supplied));

                object result;
                string error;
                if (!TryCall(command, supplied, false, out result, out error))
                {
                    Fail(command.Id + ": " + error);
                    return null;
                }

                return result;
            }

            public static Command[] ListRegistered()
            {
                var list = new List<Command>();
                foreach (Command command in registry.Values)
                {
                    if (HiddenInThisBuild(command.Id))
                        continue;
                    list.Add(command);
                }
                list.Sort(delegate(Command a, Command b) { return string.CompareOrdinal(a.Id, b.Id); });
                return list.ToArray();
            }

            internal static void ExecuteInput(string line)
            {
                List<string> statements = SplitOn(line, ';');
                for (int i = 0; i < statements.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(statements[i]))
                        ExecuteChain(statements[i].Trim());
                }
            }

            internal static bool Absorb(string content)
            {
                if (logBypass > 0 || capturing.Count == 0)
                    return false;

                StringBuilder buffer = capturing.Peek();
                if (buffer.Length > 0)
                    buffer.Append('\n');
                buffer.Append(content ?? "");
                return true;
            }

            static void ExecuteChain(string line)
            {
                List<string> parts = SplitOn(line, '|');
                if (parts.Count <= 1)
                {
                    Execute(parts.Count == 0 ? "" : parts[0].Trim());
                    return;
                }

                string piped = null;
                bool hasPiped = false;
                for (int i = 0; i < parts.Count; i++)
                {
                    string part = parts[i].Trim();
                    if (part.Length == 0)
                    {
                        Fail("empty command in pipe");
                        return;
                    }

                    string output;
                    if (!RunPiped(part, hasPiped, piped, i + 1 < parts.Count, out output))
                        return;
                    piped = output;
                    hasPiped = true;
                }
            }

            static bool RunPiped(string line, bool hasPiped, string piped, bool capture, out string output)
            {
                output = "";
                List<string> words;
                string error;
                if (!Split(line, out words, out error))
                {
                    Command named;
                    if (words.Count > 0 && ResolveCall(words[0], out named))
                        Fail(named.Id + ": " + error);
                    else
                        Fail(string.IsNullOrEmpty(error) ? "empty command in pipe" : error);
                    return false;
                }

                Command resolved;
                if (words.Count == 0 || !ResolveCall(words[0], out resolved))
                {
                    Fail("unknown command \"" + (words.Count == 0 ? "" : words[0]) + "\"");
                    return false;
                }

                var supplied = new List<object>();
                if (hasPiped)
                    supplied.Add(piped ?? "");
                for (int i = 1; i < words.Count; i++)
                    supplied.Add(words[i]);

                Command command = resolved;
                if (capture)
                    capturing.Push(new StringBuilder());
                object result = null;
                bool ok = false;
                try
                {
                    ok = TryCall(command, supplied.ToArray(), true, out result, out error);
                }
                finally
                {
                    if (capture && capturing.Count > 0)
                    {
                        output = capturing.Pop().ToString();
                    }
                }

                if (!ok)
                {
                    Fail(command.Id + ": " + error);
                    return false;
                }

                if (!capture)
                    return true;

                if (output.Length == 0 && result != null)
                    output = result as string ?? Convert.ToString(result, CultureInfo.InvariantCulture) ?? "";
                return true;
            }

            static List<string> SplitOn(string line, char separator)
            {
                var parts = new List<string>();
                var current = new StringBuilder();
                bool inQuote = false;
                if (line == null)
                    line = "";

                for (int i = 0; i < line.Length; i++)
                {
                    char currentChar = line[i];
                    if (currentChar == '\\' && i + 1 < line.Length)
                    {
                        char next = line[i + 1];
                        if (next == '\\' || next == '"')
                        {
                            current.Append(currentChar);
                            current.Append(next);
                            i++;
                            continue;
                        }

                        if (next == separator)
                        {
                            current.Append(separator);
                            i++;
                            continue;
                        }
                    }

                    if (currentChar == '"')
                    {
                        inQuote = !inQuote;
                        current.Append(currentChar);
                        continue;
                    }

                    if (!inQuote && currentChar == separator)
                    {
                        parts.Add(current.ToString());
                        current.Length = 0;
                        continue;
                    }

                    current.Append(currentChar);
                }

                parts.Add(current.ToString());
                return parts;
            }

            internal static void Execute(string line)
            {
                List<string> words;
                string error;
                if (!Split(line, out words, out error))
                {
                    Command named;
                    if (words.Count > 0 && ResolveCall(words[0], out named))
                        Fail(named.Id + ": " + error);
                    return;
                }

                Command command;
                if (words.Count == 0 || !ResolveCall(words[0], out command))
                    return;

                var supplied = new object[words.Count - 1];
                for (int i = 1; i < words.Count; i++)
                    supplied[i - 1] = words[i];
                object result;
                if (!TryCall(command, supplied, true, out result, out error))
                    Fail(command.Id + ": " + error);
            }

            static void Add(string id, Delegate handler, Help help, bool force)
            {
                if (string.IsNullOrEmpty(id) || handler == null)
                    return;

                id = id.Trim();
                if (!force && (registry.ContainsKey(id) || (!id.StartsWith("m:", StringComparison.Ordinal) && registry.ContainsKey("m:" + id))))
                {
                    Fail("register: \"" + id + "\" is already registered");
                    return;
                }

                ParameterInfo[] reflected = handler.Method.GetParameters();
                var parameters = new Parameter[reflected.Length];
                for (int i = 0; i < reflected.Length; i++)
                {
                    ParameterInfo item = reflected[i];
                    string name = string.IsNullOrEmpty(item.Name) ? "arg" + i : item.Name;
                    string typeOverride = null;
                    string description = null;
                    bool variadic = item.IsDefined(typeof(ParamArrayAttribute), false);
                    bool forceOptional = false;
                    if (help.Args != null && i < help.Args.Length)
                    {
                        if (!string.IsNullOrEmpty(help.Args[i].Name))
                            name = help.Args[i].Name;
                        if (!string.IsNullOrEmpty(help.Args[i].Type))
                            typeOverride = help.Args[i].Type;
                        description = help.Args[i].Description;
                        if (help.Args[i].Variadic)
                            variadic = true;
                        if (help.Args[i].Optional)
                            forceOptional = true;
                    }

                    bool optional = forceOptional || (!variadic && (item.HasDefaultValue || Nullable.GetUnderlyingType(item.ParameterType) != null || IsNullableReference(item)));
                    parameters[i] = new Parameter
                    {
                        Name = name,
                        Type = item.ParameterType,
                        TypeName = string.IsNullOrEmpty(typeOverride) ? Label(item.ParameterType, variadic) : typeOverride,
                        Description = description,
                        Optional = optional,
                        Params = variadic,
                        HasDefault = item.HasDefaultValue,
                        Default = item.HasDefaultValue ? item.DefaultValue : null
                    };
                }

                registry[id] = new Command
                {
                    Id = id,
                    Parameters = parameters,
                    Help = help,
                    Handler = handler
                };
            }

            static bool TryGet(string id, out Command command)
            {
                command = null;
                if (string.IsNullOrEmpty(id))
                    return false;
                return registry.TryGetValue(id.Trim(), out command);
            }

            static bool ResolveCall(string typed, out Command command)
            {
                command = null;
                if (string.IsNullOrEmpty(typed))
                    return false;

                typed = typed.Trim();
                if (typed.StartsWith("m:", StringComparison.Ordinal))
                    return !HiddenInThisBuild(typed) && registry.TryGetValue(typed, out command);

                string macroId = "m:" + typed;
                bool prefer = RemoraDevConfig.Get<bool>("DevConsole.Config.PreferMacros");
                if (prefer && !HiddenInThisBuild(macroId) && macros.ContainsKey(macroId) && registry.TryGetValue(macroId, out command))
                    return true;
                if (!HiddenInThisBuild(typed) && registry.TryGetValue(typed, out command))
                    return true;
                command = null;
                return !HiddenInThisBuild(macroId) && macros.ContainsKey(macroId) && registry.TryGetValue(macroId, out command);
            }

            static bool HiddenInThisBuild(string id)
            {
                return !Application.isEditor && !string.IsNullOrEmpty(id) && hiddenInBuild.Contains(id);
            }

            static object[] Expand(object[] args, Command command)
            {
                if (args == null)
                    return new object[0];
                if (args.Length != 1 || !(args[0] is IList list))
                    return args;
                if (command.Parameters.Length == 1 && !command.Parameters[0].Params && command.Parameters[0].Type.IsInstanceOfType(args[0]))
                    return args;

                var expanded = new object[list.Count];
                for (int i = 0; i < list.Count; i++)
                    expanded[i] = list[i];
                return expanded;
            }

            static bool TryCall(Command command, object[] args, bool parseStrings, out object result, out string error)
            {
                result = null;
                error = null;
                Parameter[] parameters = command.Parameters;
                var final = new object[parameters.Length];
                var set = new bool[parameters.Length];
                if (args == null)
                    args = new object[0];

                var positional = new List<object>();
                for (int n = 0; n < args.Length; n++)
                {
                    int flag = FlagIndex(parameters, args[n]);
                    if (flag < 0)
                    {
                        positional.Add(args[n]);
                        continue;
                    }

                    final[flag] = true;
                    set[flag] = true;
                }

                args = positional.ToArray();
                int index = 0;
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (set[i])
                        continue;

                    Parameter parameter = parameters[i];
                    if (parameter.Params)
                    {
                        int count = args.Length - index;
                        if (count < 1)
                        {
                            if (!parameter.Optional)
                            {
                                error = "missing " + Token(parameter);
                                return false;
                            }

                            Type emptyElement = parameter.Type.IsArray ? parameter.Type.GetElementType() : typeof(string);
                            final[i] = Array.CreateInstance(emptyElement, 0);
                            continue;
                        }

                        Type element = parameter.Type.IsArray ? parameter.Type.GetElementType() : typeof(string);
                        Array packed = Array.CreateInstance(element, count);
                        for (int n = 0; n < count; n++)
                        {
                            object value;
                            if (!TryConvert(args[index + n], element, parameter, parseStrings, out value, out error))
                                return false;
                            packed.SetValue(value, n);
                        }

                        final[i] = packed;
                        index = args.Length;
                        continue;
                    }

                    if (index >= args.Length)
                    {
                        if (!parameter.Optional)
                        {
                            error = "missing " + Token(parameter);
                            return false;
                        }

                        final[i] = DefaultOf(parameter);
                        continue;
                    }

                    object converted;
                    if (!TryConvert(args[index], parameter.Type, parameter, parseStrings, out converted, out error))
                        return false;
                    final[i] = converted;
                    index++;
                }

                if (index < args.Length)
                {
                    error = "unexpected \"" + args[index] + "\"";
                    return false;
                }

                try
                {
                    result = command.Handler.DynamicInvoke(final);
                    return true;
                }
                catch (TargetInvocationException ex)
                {
                    error = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                    return false;
                }
            }

            static bool TryConvert(object raw, Type type, Parameter parameter, bool parseStrings, out object value, out string error)
            {
                value = null;
                error = null;
                Type underlying = Nullable.GetUnderlyingType(type);
                Type target = underlying ?? type;
                if (raw == null || raw is DBNull)
                {
                    if (underlying != null || !target.IsValueType || parameter.Optional)
                    {
                        value = null;
                        return true;
                    }

                    error = "missing " + Token(parameter);
                    return false;
                }

                if (target.IsInstanceOfType(raw) && !(parseStrings && raw is string && target != typeof(string)))
                {
                    value = raw;
                    return true;
                }

                string text = raw as string;
                if (text == null)
                {
                    try
                    {
                        value = Convert.ChangeType(raw, target, CultureInfo.InvariantCulture);
                        return true;
                    }
                    catch (Exception)
                    {
                        error = "cannot use \"" + raw + "\" as " + Token(parameter);
                        return false;
                    }
                }

                if (target == typeof(string))
                {
                    value = text;
                    return true;
                }

                if (target == typeof(bool))
                {
                    bool parsed;
                    if (bool.TryParse(text, out parsed) || text == "1" || text == "0")
                    {
                        value = text == "0" ? false : (text == "1" ? true : parsed);
                        return true;
                    }

                    if (IsFlag(text, parameter.Name))
                    {
                        value = true;
                        return true;
                    }

                    error = "cannot use \"" + text + "\" as " + Token(parameter);
                    return false;
                }

                if (target == typeof(char))
                {
                    if (text.Length == 1)
                    {
                        value = text[0];
                        return true;
                    }

                    error = "cannot use \"" + text + "\" as " + Token(parameter);
                    return false;
                }

                if (target.IsEnum)
                {
                    try
                    {
                        value = Enum.Parse(target, text, true);
                        return true;
                    }
                    catch (Exception)
                    {
                        error = "cannot use \"" + text + "\" as " + Token(parameter);
                        return false;
                    }
                }

                try
                {
                    value = Convert.ChangeType(text, target, CultureInfo.InvariantCulture);
                    return true;
                }
                catch (Exception)
                {
                    error = "cannot use \"" + text + "\" as " + Token(parameter);
                    return false;
                }
            }

            static int FlagIndex(Parameter[] parameters, object raw)
            {
                string text = raw as string;
                if (!IsFlag(text, null))
                    return -1;

                string name = text.Substring(2);
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].Params || !IsBool(parameters[i].Type))
                        continue;
                    if (string.Equals(parameters[i].Name, name, StringComparison.OrdinalIgnoreCase))
                        return i;
                }

                return -1;
            }

            static bool IsFlag(string text, string name)
            {
                if (string.IsNullOrEmpty(text) || !text.StartsWith("--") || text.Length < 3)
                    return false;
                if (name == null)
                    return true;
                return string.Equals(text.Substring(2), name, StringComparison.OrdinalIgnoreCase);
            }

            static bool IsBool(Type type)
            {
                return (Nullable.GetUnderlyingType(type) ?? type) == typeof(bool);
            }

            static object DefaultOf(Parameter parameter)
            {
                if (parameter.HasDefault && parameter.Default != DBNull.Value && parameter.Default != Type.Missing)
                    return parameter.Default;
                if (Nullable.GetUnderlyingType(parameter.Type) != null || !parameter.Type.IsValueType)
                    return null;
                return Activator.CreateInstance(parameter.Type);
            }

            static bool Split(string line, out List<string> words, out string error)
            {
                words = new List<string>();
                error = null;
                var current = new StringBuilder();
                bool inQuote = false;
                bool any = false;
                if (line == null)
                    line = "";

                for (int i = 0; i < line.Length; i++)
                {
                    char currentChar = line[i];
                    if (currentChar == '\\' && i + 1 < line.Length && (line[i + 1] == '\\' || line[i + 1] == '"'))
                    {
                        current.Append(line[i + 1]);
                        any = true;
                        i++;
                        continue;
                    }

                    if (currentChar == '"')
                    {
                        inQuote = !inQuote;
                        any = true;
                        continue;
                    }

                    if (!inQuote && currentChar == ' ')
                    {
                        if (!any)
                            continue;
                        words.Add(current.ToString());
                        current.Length = 0;
                        any = false;
                        continue;
                    }

                    current.Append(currentChar);
                    any = true;
                }

                if (any)
                    words.Add(current.ToString());
                if (!inQuote)
                    return true;

                error = "unclosed quote";
                return false;
            }

            static object RemoraCommand(string group, string action, string[] extra, bool force)
            {
                if (!string.Equals(group, "config", StringComparison.OrdinalIgnoreCase))
                {
                    Fail("remora: unknown group \"" + (group ?? "") + "\"");
                    return null;
                }

                if (extra == null)
                    extra = new string[0];
                if (force && !string.Equals(action, "set", StringComparison.OrdinalIgnoreCase))
                {
                    Fail("remora: --force is only valid for set");
                    return null;
                }

                bool keepFont;
                bool keepSize;
                bool keepHidden;
                RemoraDevConsole.CaptureDrift(out keepFont, out keepSize, out keepHidden);
                if (string.Equals(action, "reload", StringComparison.OrdinalIgnoreCase))
                {
                    if (extra.Length != 0)
                    {
                        Fail("remora: unexpected \"" + extra[0] + "\"");
                        return null;
                    }

                    RemoraDevConfig.TryApplyFile();
                    RemoraDevConsole.SyncAfterConfig(keepFont, keepSize, keepHidden);
                    return null;
                }

                if (!IsConfigAction(action))
                {
                    Fail("remora: unknown action \"" + (action ?? "") + "\"");
                    return null;
                }

                if (extra.Length < 1)
                {
                    Fail("remora: missing <keypath>");
                    return null;
                }

                bool isSet = string.Equals(action, "set", StringComparison.OrdinalIgnoreCase);
                if (!isSet && extra.Length > 1)
                {
                    Fail("remora: unexpected \"" + extra[1] + "\"");
                    return null;
                }

                if (isSet && extra.Length < 2)
                {
                    Fail("remora: missing <value>");
                    return null;
                }

                string key = extra[0];
                string error;
                if (string.Equals(action, "get", StringComparison.OrdinalIgnoreCase))
                {
                    string text;
                    if (!RemoraDevConfig.TryGetText(key, out text, out error))
                    {
                        Fail("remora: " + error);
                        return null;
                    }

                    return Report(text);
                }

                if (string.Equals(action, "typeof", StringComparison.OrdinalIgnoreCase))
                {
                    string text;
                    if (!RemoraDevConfig.TryTypeName(key, out text, out error))
                    {
                        Fail("remora: " + error);
                        return null;
                    }

                    return Report(text);
                }

                if (string.Equals(action, "exists", StringComparison.OrdinalIgnoreCase))
                    return Report(RemoraDevConfig.Exists(key));

                if (string.Equals(action, "isdefault", StringComparison.OrdinalIgnoreCase))
                {
                    bool value;
                    if (!RemoraDevConfig.TryIsDefault(key, out value, out error))
                    {
                        Fail("remora: " + error);
                        return null;
                    }

                    return Report(value);
                }

                if (string.Equals(action, "isnull", StringComparison.OrdinalIgnoreCase))
                {
                    bool value;
                    if (!RemoraDevConfig.TryIsNull(key, out value, out error))
                    {
                        Fail("remora: " + error);
                        return null;
                    }

                    return Report(value);
                }

                bool ok;
                if (isSet)
                {
                    string raw = extra.Length == 2 ? extra[1] : string.Join(" ", extra, 1, extra.Length - 1);
                    ok = RemoraDevConfig.TrySet(key, raw, force, out error);
                }
                else if (string.Equals(action, "unset", StringComparison.OrdinalIgnoreCase))
                {
                    ok = RemoraDevConfig.TryUnset(key, out error);
                }
                else
                {
                    ok = RemoraDevConfig.TryDefault(key, out error);
                }

                if (!ok)
                {
                    Fail("remora: " + error);
                    return null;
                }

                RemoraDevConsole.SyncAfterConfig(keepFont, keepSize, keepHidden);
                return null;
            }

            static bool IsConfigAction(string action)
            {
                return string.Equals(action, "get", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(action, "typeof", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(action, "set", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(action, "exists", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(action, "unset", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(action, "default", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(action, "isdefault", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(action, "isnull", StringComparison.OrdinalIgnoreCase);
            }

            static void ScriptCommand(string file)
            {
                TryRunScript(file);
            }

            static bool TryRunScript(string file)
            {
                if (string.IsNullOrWhiteSpace(file))
                {
                    Fail("script: missing <file.rem>");
                    return false;
                }

                if (scriptDepth >= 8)
                {
                    Fail("script: too many nested scripts");
                    return false;
                }

                string text;
                string error;
                if (!RemoraDevConsole.TryReadRem(file, out text, out error))
                {
                    Fail("script: " + error);
                    return false;
                }

                scriptDepth++;
                try
                {
                    string normalized = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
                    int start = 0;
                    for (int i = 0; i <= normalized.Length; i++)
                    {
                        if (i != normalized.Length && normalized[i] != '\n')
                            continue;

                        string line = normalized.Substring(start, i - start).Trim();
                        start = i + 1;
                        if (line.Length > 0)
                            ExecuteInput(line);
                    }
                }
                finally
                {
                    scriptDepth--;
                }

                return true;
            }

            static void IfCommand(string left, string op, string right, string script)
            {
                bool pass;
                if (!TryCondition("if", left, op, right, out pass) || !pass)
                    return;
                TryRunScript(script);
            }

            static void WhileCommand(string left, string op, string right, string script)
            {
                for (int i = 0; i < 10000; i++)
                {
                    bool pass;
                    if (!TryCondition("while", left, op, right, out pass) || !pass)
                        return;
                    if (!TryRunScript(script))
                        return;
                }

                Fail("while: too many iterations");
            }

            static void ForCommand(string countText, string script)
            {
                string countValue;
                if (!TryOperand("for", countText, out countValue))
                    return;

                double number;
                if (!double.TryParse(countValue, NumberStyles.Float, CultureInfo.InvariantCulture, out number) || number < 0d || Math.Abs(number - Math.Round(number)) > 0.0000001d)
                {
                    Fail("for: \"" + countValue + "\" is not a count");
                    return;
                }

                int count = (int)Math.Round(number);
                if (count > 10000)
                {
                    Fail("for: too many iterations");
                    return;
                }

                for (int i = 0; i < count; i++)
                {
                    if (!TryRunScript(script))
                        return;
                }
            }

            static bool TryCondition(string id, string leftText, string op, string rightText, out bool pass)
            {
                pass = false;
                string left;
                string right;
                if (!TryOperand(id, leftText, out left) || !TryOperand(id, rightText, out right))
                    return false;
                if (!TryCompare(id, left, op, right, out pass))
                    return false;
                return true;
            }

            static bool TryOperand(string id, string text, out string value)
            {
                value = null;
                if (text != null && text.Length > 1 && text[0] == '$')
                {
                    string key = text.Substring(1);
                    if (!variables.TryGetValue(key, out value))
                    {
                        Fail(id + ": unknown variable \"" + key + "\"");
                        return false;
                    }

                    return true;
                }

                value = text ?? "";
                return true;
            }

            static bool TryCompare(string id, string left, string op, string right, out bool pass)
            {
                pass = false;
                double leftNumber = 0d;
                double rightNumber = 0d;
                bool numbers = double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out leftNumber)
                    && double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out rightNumber);
                int order;
                if (numbers && Math.Abs(leftNumber - rightNumber) <= 0.0000001d)
                    order = 0;
                else
                    order = numbers ? leftNumber.CompareTo(rightNumber) : string.CompareOrdinal(left, right);

                if (op == "==")
                    pass = order == 0;
                else if (op == "!=")
                    pass = order != 0;
                else if (op == ">")
                    pass = order > 0;
                else if (op == "<")
                    pass = order < 0;
                else if (op == ">=")
                    pass = order >= 0;
                else if (op == "=<" || op == "<=")
                    pass = order <= 0;
                else
                {
                    Fail(id + ": unknown operator \"" + op + "\"");
                    return false;
                }

                return true;
            }

            static object MacroCommand(string action, string pattern = null, string body = null)
            {
                if (string.Equals(action, "list", StringComparison.OrdinalIgnoreCase))
                {
                    if (pattern != null)
                    {
                        Fail("macro: unexpected \"" + pattern + "\"");
                        return null;
                    }

                    var ids = new List<string>(macros.Keys);
                    ids.Sort(StringComparer.Ordinal);
                    var builder = new StringBuilder();
                    for (int i = 0; i < ids.Count; i++)
                    {
                        if (i > 0)
                            builder.Append('\n');
                        builder.Append(ids[i]);
                    }

                    return Report(builder.ToString());
                }

                if (string.Equals(action, "unset", StringComparison.OrdinalIgnoreCase))
                    return MacroUnset(pattern, body);
                if (string.Equals(action, "get", StringComparison.OrdinalIgnoreCase))
                    return MacroGet(pattern, body);
                if (string.Equals(action, "add", StringComparison.OrdinalIgnoreCase) || string.Equals(action, "plate", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(pattern))
                    {
                        Fail("macro: missing <pattern>");
                        return null;
                    }

                    if (body == null)
                    {
                        Fail("macro: missing <body>");
                        return null;
                    }

                    string error;
                    if (!TryDefineMacro(pattern, body, string.Equals(action, "plate", StringComparison.OrdinalIgnoreCase), out error))
                    {
                        Fail("macro: " + error);
                        return null;
                    }

                    return null;
                }

                Fail("macro: unknown action \"" + (action ?? "") + "\"");
                return null;
            }

            static object MacroUnset(string id, string extra)
            {
                if (extra != null)
                {
                    Fail("macro: unexpected \"" + extra + "\"");
                    return null;
                }

                string key;
                string error;
                if (!TryMacroKey(id, out key, out error))
                {
                    Fail("macro: " + error);
                    return null;
                }

                if (!macros.ContainsKey(key))
                {
                    Fail("macro: unknown macro \"" + key.Substring(2) + "\"");
                    return null;
                }

                UnRegister(key);
                return null;
            }

            static object MacroGet(string id, string extra)
            {
                if (extra != null)
                {
                    Fail("macro: unexpected \"" + extra + "\"");
                    return null;
                }

                string key;
                string error;
                if (!TryMacroKey(id, out key, out error))
                {
                    Fail("macro: " + error);
                    return null;
                }

                MacroDef macro;
                if (!macros.TryGetValue(key, out macro))
                {
                    Fail("macro: unknown macro \"" + key.Substring(2) + "\"");
                    return null;
                }

                return Report(macro.Pattern + " => " + macro.Body);
            }

            static bool TryMacroKey(string id, out string key, out string error)
            {
                key = null;
                string bare = BareMacroId(id, out error);
                if (bare == null)
                    return false;
                key = "m:" + bare;
                return true;
            }

            static string BareMacroId(string word, out string error)
            {
                error = null;
                if (string.IsNullOrWhiteSpace(word))
                {
                    error = "missing <id>";
                    return null;
                }

                word = word.Trim();
                if (word.StartsWith("m:", StringComparison.Ordinal))
                    word = word.Substring(2);
                if (word.Length == 0 || word.IndexOf(':') >= 0 || word.IndexOf(' ') >= 0)
                {
                    error = "invalid id \"" + word + "\"";
                    return null;
                }

                return word;
            }

            static bool TryDefineMacro(string pattern, string body, bool plate, out string error)
            {
                error = null;
                List<string> words;
                if (!Split(pattern, out words, out error) || words.Count == 0)
                {
                    error = string.IsNullOrEmpty(error) ? "invalid pattern" : error;
                    return false;
                }

                string bare = BareMacroId(words[0], out error);
                if (bare == null)
                    return false;

                string registered = "m:" + bare;
                if (registry.ContainsKey(registered) || macros.ContainsKey(registered))
                {
                    error = "\"" + bare + "\" is already registered";
                    return false;
                }

                var slots = new List<MacroSlot>();
                for (int i = 1; i < words.Count; i++)
                {
                    MacroSlot slot;
                    if (plate ? !TryParsePlateSlot(words[i], out slot, out error) : !TryParseAddSlot(words[i], out slot, out error))
                        return false;
                    slots.Add(slot);
                }

                if (!AssignMacroSlots(slots, out error))
                    return false;

                var macro = new MacroDef();
                macro.Id = bare;
                macro.Registered = registered;
                macro.Pattern = pattern;
                macro.Body = body ?? "";
                macro.Slots = slots;
                string captured = registered;
                Register(registered, new Action<string[]>(delegate(string[] args) { RunMacro(captured, args); }), new Help
                {
                    Description = pattern + " => " + (body ?? ""),
                    Args = new[]
                    {
                        new Arg { Name = "args", Type = "args", Variadic = true, Optional = true }
                    }
                }, true);
                Command command;
                if (!registry.TryGetValue(registered, out command))
                {
                    error = "\"" + bare + "\" was not registered";
                    return false;
                }

                command.Shown = ShownSlots(slots);
                macros[registered] = macro;
                return true;
            }

            static void RunMacro(string registered, string[] args)
            {
                MacroDef macro;
                if (!macros.TryGetValue(registered, out macro))
                {
                    Fail("macro: unknown macro \"" + registered + "\"");
                    return;
                }

                if (macroDepth >= 8)
                {
                    Fail("macro: too many nested macros");
                    return;
                }

                Dictionary<string, string> named;
                List<string> splat;
                string error;
                if (!TryBindMacro(macro, args ?? new string[0], out named, out splat, out error))
                {
                    Fail(registered + ": " + error);
                    return;
                }

                string line;
                if (!TryExpandMacro(macro.Body, named, splat, macro.SplatName, out line, out error))
                {
                    Fail(registered + ": " + error);
                    return;
                }

                macroDepth++;
                try
                {
                    ExecuteInput(line);
                }
                finally
                {
                    macroDepth--;
                }
            }

            static bool TryParseAddSlot(string word, out MacroSlot slot, out string error)
            {
                slot = null;
                error = null;
                bool optional = false;
                if (word.Length >= 2 && word[0] == '[' && word[word.Length - 1] == ']')
                {
                    optional = true;
                    word = word.Substring(1, word.Length - 2);
                }

                string type = "";
                string star = word;
                bool nullable = false;
                int marker = word.IndexOf(":*", StringComparison.Ordinal);
                if (marker >= 0)
                {
                    type = word.Substring(0, marker);
                    star = word.Substring(marker + 1);
                }

                if (type.EndsWith("?", StringComparison.Ordinal))
                {
                    nullable = true;
                    type = type.Substring(0, type.Length - 1);
                }

                if (!star.StartsWith("*", StringComparison.Ordinal))
                {
                    error = "invalid placeholder \"" + word + "\"";
                    return false;
                }

                string rest = star.Substring(1);
                bool splat = rest.Length == 0 || rest == "...";
                return FinishStar(rest, type, splat || optional, nullable, splat, out slot, out error);
            }

            static bool TryParsePlateSlot(string word, out MacroSlot slot, out string error)
            {
                slot = null;
                error = null;
                bool optional = false;
                if (word.Length >= 2 && word[0] == '[' && word[word.Length - 1] == ']')
                    optional = true;
                else if (!(word.Length >= 2 && word[0] == '<' && word[word.Length - 1] == '>'))
                {
                    error = "invalid plate \"" + word + "\"";
                    return false;
                }

                string inner = word.Substring(1, word.Length - 2).Trim();
                bool variadic = false;
                if (inner.StartsWith("...", StringComparison.Ordinal))
                {
                    variadic = true;
                    inner = inner.Substring(3);
                }

                string type = "";
                string name = inner;
                int colon = inner.LastIndexOf(':');
                if (colon >= 0)
                {
                    type = inner.Substring(0, colon);
                    name = inner.Substring(colon + 1);
                }
                else if (variadic)
                {
                    type = inner;
                    name = "";
                }

                bool nullable = false;
                if (type.EndsWith("?", StringComparison.Ordinal))
                {
                    nullable = true;
                    type = type.Substring(0, type.Length - 1);
                }

                if (name.StartsWith("*", StringComparison.Ordinal))
                    name = name.Substring(1);
                if (variadic)
                    return FinishStar(name, type, optional, nullable, true, out slot, out error);
                if (name.Length == 0)
                {
                    error = "invalid plate \"" + word + "\"";
                    return false;
                }

                return FinishStar(name, type, optional, nullable, false, out slot, out error);
            }

            static bool FinishStar(string name, string type, bool optional, bool nullable, bool variadic, out MacroSlot slot, out string error)
            {
                slot = new MacroSlot();
                error = null;
                slot.TypeName = string.IsNullOrEmpty(type) ? "string" : type;
                slot.Optional = optional;
                slot.Nullable = nullable;
                slot.Index = -1;
                if (variadic || name.Length == 0 || name == "...")
                {
                    slot.Variadic = true;
                    slot.Optional = optional;
                    slot.Name = name.Length == 0 || name == "..." ? "..." : name;
                    return true;
                }

                bool numbered = true;
                for (int i = 0; i < name.Length; i++)
                {
                    if (name[i] < '0' || name[i] > '9')
                    {
                        numbered = false;
                        break;
                    }
                }

                if (numbered)
                {
                    int index;
                    if (!int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
                    {
                        error = "invalid placeholder \"*" + name + "\"";
                        return false;
                    }

                    slot.Numbered = true;
                    slot.Index = index;
                    slot.Name = name;
                    return true;
                }

                slot.Name = name;
                return true;
            }

            static bool AssignMacroSlots(List<MacroSlot> slots, out string error)
            {
                error = null;
                var taken = new Dictionary<int, MacroSlot>();
                int cursor = 0;
                bool sawVariadic = false;
                var names = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < slots.Count; i++)
                {
                    MacroSlot slot = slots[i];
                    if (slot.Variadic)
                    {
                        if (sawVariadic || i != slots.Count - 1)
                        {
                            error = "variadic placeholder must be last";
                            return false;
                        }

                        sawVariadic = true;
                        continue;
                    }

                    int index = slot.Index;
                    if (!slot.Numbered)
                    {
                        while (taken.ContainsKey(cursor))
                            cursor++;
                        index = cursor;
                        slot.Index = index;
                        cursor++;
                    }

                    if (taken.ContainsKey(index))
                    {
                        error = "duplicate argument *" + slot.Name;
                        return false;
                    }

                    taken[index] = slot;
                    if (!names.Add(slot.Name))
                    {
                        error = "duplicate argument *" + slot.Name;
                        return false;
                    }
                }

                return true;
            }

            static Parameter[] ShownSlots(List<MacroSlot> slots)
            {
                var shown = new Parameter[slots.Count];
                for (int i = 0; i < slots.Count; i++)
                {
                    MacroSlot slot = slots[i];
                    string typeName = slot.TypeName;
                    if (slot.Nullable && !typeName.EndsWith("?", StringComparison.Ordinal))
                        typeName += "?";
                    shown[i] = new Parameter
                    {
                        Name = string.IsNullOrEmpty(slot.Name) || slot.Name == "..." ? "args" : slot.Name,
                        TypeName = typeName,
                        Optional = slot.Optional,
                        Params = slot.Variadic,
                        Type = slot.Variadic ? typeof(string[]) : typeof(string)
                    };
                }

                return shown;
            }

            static bool TryBindMacro(MacroDef macro, string[] args, out Dictionary<string, string> named, out List<string> splat, out string error)
            {
                named = new Dictionary<string, string>(StringComparer.Ordinal);
                splat = new List<string>();
                error = null;
                int max = -1;
                int need = 0;
                MacroSlot variadic = null;
                for (int i = 0; i < macro.Slots.Count; i++)
                {
                    MacroSlot slot = macro.Slots[i];
                    if (slot.Variadic)
                    {
                        variadic = slot;
                        continue;
                    }

                    if (slot.Index > max)
                        max = slot.Index;
                    if (!slot.Optional && slot.Index + 1 > need)
                        need = slot.Index + 1;
                }

                if (args.Length < need)
                {
                    error = "missing <" + MissingSlot(macro, args.Length) + ">";
                    return false;
                }

                if (variadic == null && max >= 0 && args.Length > max + 1)
                {
                    error = "unexpected \"" + args[max + 1] + "\"";
                    return false;
                }

                if (variadic == null && max < 0 && args.Length > 0)
                {
                    error = "unexpected \"" + args[0] + "\"";
                    return false;
                }

                for (int i = 0; i < macro.Slots.Count; i++)
                {
                    MacroSlot slot = macro.Slots[i];
                    if (slot.Variadic)
                        continue;
                    if (slot.Index >= args.Length)
                    {
                        named[slot.Name] = "";
                        continue;
                    }

                    if (!TryAcceptSlot(slot, args[slot.Index], out error))
                        return false;
                    named[slot.Name] = args[slot.Index];
                }

                int from = variadic == null ? 0 : max + 1;
                if (variadic != null && !variadic.Optional && from >= args.Length)
                {
                    error = "missing <..." + variadic.TypeName + ">";
                    return false;
                }

                for (int i = from; i < args.Length; i++)
                {
                    if (variadic != null && !TryAcceptSlot(variadic, args[i], out error))
                        return false;
                    splat.Add(args[i]);
                }

                if (variadic == null)
                {
                    splat.Clear();
                    for (int i = 0; i < args.Length; i++)
                        splat.Add(args[i]);
                }

                macro.SplatName = variadic == null ? null : variadic.Name;
                return true;
            }

            static string MissingSlot(MacroDef macro, int have)
            {
                for (int i = 0; i < macro.Slots.Count; i++)
                {
                    MacroSlot slot = macro.Slots[i];
                    if (!slot.Variadic && !slot.Optional && slot.Index >= have)
                        return slot.Name;
                }

                return "arg";
            }

            static bool TryAcceptSlot(MacroSlot slot, string value, out string error)
            {
                error = null;
                if (slot.Nullable && (string.IsNullOrEmpty(value) || value == "null"))
                    return true;

                string type = string.IsNullOrEmpty(slot.TypeName) ? "any" : slot.TypeName;
                string[] parts = type.Split('|');
                for (int i = 0; i < parts.Length; i++)
                {
                    if (MatchesMacroType(parts[i].Trim(), value))
                        return true;
                }

                error = "cannot use \"" + value + "\" as <" + type + ">";
                return false;
            }

            static bool MatchesMacroType(string type, string value)
            {
                if (type.EndsWith("?", StringComparison.Ordinal))
                    type = type.Substring(0, type.Length - 1);
                if (type.Length == 0 || type == "any" || type == "value" || type == "string")
                    return true;
                if (type == "int" || type == "integer")
                {
                    int number;
                    return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
                }

                if (type == "float" || type == "double" || type == "number")
                {
                    double number;
                    return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
                }

                if (type == "bool" || type == "boolean")
                {
                    bool parsed;
                    return bool.TryParse(value, out parsed) || value == "0" || value == "1";
                }

                return true;
            }

            static bool TryExpandMacro(string body, Dictionary<string, string> named, List<string> splat, string splatName, out string line, out string error)
            {
                line = null;
                error = null;
                var builder = new StringBuilder();
                string text = body ?? "";
                for (int i = 0; i < text.Length; i++)
                {
                    if (text[i] == '.' && i + 3 < text.Length && text[i + 1] == '.' && text[i + 2] == '.' && text[i + 3] == '*')
                    {
                        AppendSplat(builder, splat, true);
                        i += 3;
                        continue;
                    }

                    if (text[i] != '*')
                    {
                        builder.Append(text[i]);
                        continue;
                    }

                    if (i + 3 < text.Length && text[i + 1] == '.' && text[i + 2] == '.' && text[i + 3] == '.')
                    {
                        AppendSplat(builder, splat, false);
                        i += 3;
                        continue;
                    }

                    int end = i + 1;
                    while (end < text.Length && IsMacroName(text[end]))
                        end++;
                    if (end == i + 1)
                    {
                        AppendSplat(builder, splat, false);
                        continue;
                    }

                    string name = text.Substring(i + 1, end - i - 1);
                    if (splatName != null && name == splatName)
                    {
                        AppendSplat(builder, splat, false);
                        i = end - 1;
                        continue;
                    }

                    string value;
                    if (!named.TryGetValue(name, out value))
                    {
                        error = "unknown placeholder \"*" + name + "\"";
                        return false;
                    }

                    builder.Append(QuoteMacro(value));
                    i = end - 1;
                }

                line = builder.ToString();
                return true;
            }

            static void AppendSplat(StringBuilder builder, List<string> splat, bool reverse)
            {
                if (splat == null || splat.Count == 0)
                    return;

                if (!reverse)
                {
                    for (int i = 0; i < splat.Count; i++)
                    {
                        if (i > 0)
                            builder.Append(' ');
                        builder.Append(QuoteMacro(splat[i]));
                    }

                    return;
                }

                for (int i = splat.Count - 1; i >= 0; i--)
                {
                    if (i < splat.Count - 1)
                        builder.Append(' ');
                    builder.Append(QuoteMacro(splat[i]));
                }
            }

            static string QuoteMacro(string text)
            {
                if (text == null)
                    text = "";
                bool quote = text.Length == 0;
                for (int i = 0; i < text.Length && !quote; i++)
                {
                    char current = text[i];
                    quote = current == ' ' || current == '"' || current == '\\' || current == ';' || current == '|' || current == '\n' || current == '\t';
                }

                if (!quote)
                    return text;

                var builder = new StringBuilder();
                builder.Append('"');
                for (int i = 0; i < text.Length; i++)
                {
                    char current = text[i];
                    if (current == '\\' || current == '"')
                        builder.Append('\\');
                    builder.Append(current);
                }

                builder.Append('"');
                return builder.ToString();
            }

            static bool IsMacroName(char current)
            {
                return (current >= 'a' && current <= 'z') || (current >= 'A' && current <= 'Z') || (current >= '0' && current <= '9') || current == '_' || current == '-';
            }

            sealed class MacroSlot
            {
                public string Name;
                public string TypeName;
                public bool Optional;
                public bool Nullable;
                public bool Variadic;
                public bool Numbered;
                public int Index;
            }

            sealed class MacroDef
            {
                public string Id;
                public string Registered;
                public string Pattern;
                public string Body;
                public string SplatName;
                public List<MacroSlot> Slots;
            }

            static object ScreenshotCommand(string first = null, string second = null, string third = null, bool visible = false)
            {
                var args = new List<string>();
                if (first != null)
                    args.Add(first);
                if (second != null)
                    args.Add(second);
                if (third != null)
                    args.Add(third);

                string selector = null;
                string res = null;
                string file = null;
                if (args.Count == 3)
                {
                    selector = args[0];
                    res = args[1];
                    file = args[2];
                }
                else if (args.Count == 2)
                {
                    int ignoredWidth;
                    int ignoredHeight;
                    if (ScreenShot.TryParseResolution(args[0], out ignoredWidth, out ignoredHeight))
                    {
                        res = args[0];
                        file = args[1];
                    }
                    else
                    {
                        selector = args[0];
                        if (ScreenShot.TryParseResolution(args[1], out ignoredWidth, out ignoredHeight))
                            res = args[1];
                        else
                            file = args[1];
                    }
                }
                else if (args.Count == 1)
                {
                    int ignoredWidth;
                    int ignoredHeight;
                    if (ScreenShot.TryParseResolution(args[0], out ignoredWidth, out ignoredHeight))
                        res = args[0];
                    else if (LooksLikeShotFile(args[0]))
                        file = args[0];
                    else
                        selector = args[0];
                }

                if (res != null)
                {
                    int ignoredWidth;
                    int ignoredHeight;
                    if (!ScreenShot.TryParseResolution(res, out ignoredWidth, out ignoredHeight))
                    {
                        Fail("screenshot: invalid resolution \"" + res + "\". valid resolutions are 480p, 720p, 1080p, 1440p, or WIDTHxHEIGHT");
                        return null;
                    }
                }

                object targets = null;
                if (selector != null)
                {
                    object selected = ManInvoke("unor", "select", new string[] { selector });
                    if (selected == null)
                        return null;
                    targets = EvaluateShot(selector);
                    if (targets == null)
                        return null;
                }

                Camera cam = Camera.main;
                if (cam == null)
                {
                    Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                    for (int i = 0; i < cameras.Length; i++)
                    {
                        if (cameras[i] != null && cameras[i].enabled)
                        {
                            cam = cameras[i];
                            break;
                        }
                    }
                }
                if (cam == null)
                {
                    Fail("screenshot: no camera");
                    return null;
                }

                try
                {
                    ScreenShot.Result shot = ScreenShot.Capture(cam, res, file, visible, targets);
                    return Report("Captured world to \"" + shot.FilePath + "\" (" + shot.Width.ToString(CultureInfo.InvariantCulture) + "x" + shot.Height.ToString(CultureInfo.InvariantCulture) + " px)");
                }
                catch (Exception exception)
                {
                    Fail("screenshot: " + exception.Message);
                    return null;
                }
            }

            static bool LooksLikeShotFile(string text)
            {
                if (string.IsNullOrEmpty(text))
                    return false;
                if (text.IndexOf('%') >= 0 || text.IndexOf('/') >= 0 || text.IndexOf('\\') >= 0)
                    return true;
                return text.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
            }

            static object EvaluateShot(string selector)
            {
                Type type = null;
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    type = assemblies[i].GetType("UNOR");
                    if (type != null)
                        break;
                }
                if (type == null)
                {
                    Fail("screenshot: unor returned no object");
                    return null;
                }

                MethodInfo method = type.GetMethod("EvaluateSelector", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(string), typeof(bool) }, null);
                if (method == null)
                {
                    Fail("screenshot: unor returned no object");
                    return null;
                }

                try
                {
                    return method.Invoke(null, new object[] { selector, true });
                }
                catch (TargetInvocationException exception)
                {
                    string message = exception.InnerException != null ? exception.InnerException.Message : exception.Message;
                    Fail("screenshot: " + message);
                    return null;
                }
            }

            static void ClearCommand()
            {
                SetBuffer("");
            }

            static void HelpCommand(string command = null)
            {
                if (string.IsNullOrEmpty(command))
                {
                    Command[] all = ListRegistered();
                    for (int i = 0; i < all.Length; i++)
                        SendTextIn(Signature(all[i]));
                    return;
                }

                Command found;
                if (!ResolveCall(command, out found))
                {
                    Fail("help: unknown command \"" + command + "\"");
                    return;
                }

                SendTextIn(Signature(found));
                if (!string.IsNullOrEmpty(found.Help.Description))
                    SendTextIn("\u001b[38;2;176;176;176m" + found.Help.Description + "\u001b[m");
                Parameter[] shown = Visible(found);
                for (int i = 0; i < shown.Length; i++)
                {
                    Parameter parameter = shown[i];
                    string line = "\u001b[1;38;2;140;209;255m" + parameter.Name + ":\u001b[m " + TokenColor(parameter) + Token(parameter) + "\u001b[m";
                    if (!string.IsNullOrEmpty(parameter.Description))
                        line += "\u001b[38;2;176;176;176m  " + parameter.Description + "\u001b[m";
                    SendTextIn(line);
                }
            }

            static void PrintCommand(string[] text)
            {
                if (text == null || text.Length == 0)
                {
                    Fail("print: missing <...string>");
                    return;
                }

                SendTextIn(JoinInterpreted(text));
            }

            static void LogCommand(string level, string[] text)
            {
                WriteLog("log", level, text, false);
            }

            static void DlogCommand(string level, string[] text)
            {
                WriteLog("dlog", level, text, true);
            }

            static void WriteLog(string id, string level, string[] text, bool debugLog)
            {
                bool plain;
                byte red;
                byte green;
                byte blue;
                if (!TryLevel(level, out plain, out red, out green, out blue))
                {
                    Fail(id + ": unknown level \"" + level + "\"");
                    return;
                }

                if (text == null || text.Length == 0)
                {
                    Fail(id + ": missing <...string>");
                    return;
                }

                string message = JoinInterpreted(text);
                if (debugLog)
                {
                    EmitUnityLog(level, message);
                    return;
                }

                message = LevelTag(level) + message;
                if (plain)
                    SendTextIn(message);
                else
                    SendTextIn(WithLevelColor(message, red, green, blue));
            }

            static string LevelTag(string level)
            {
                if (string.Equals(level, "warn", StringComparison.OrdinalIgnoreCase) || string.Equals(level, "warning", StringComparison.OrdinalIgnoreCase))
                    return "[warn] ";
                if (string.Equals(level, "err", StringComparison.OrdinalIgnoreCase) || string.Equals(level, "error", StringComparison.OrdinalIgnoreCase))
                    return "[err] ";
                if (string.Equals(level, "debug", StringComparison.OrdinalIgnoreCase))
                    return "[debug] ";
                return "[msg] ";
            }

            static void EmitUnityLog(string level, string message)
            {
                RemoraDevConsole.PushIgnoreUnityLog();
                try
                {
                    if (string.Equals(level, "warn", StringComparison.OrdinalIgnoreCase) || string.Equals(level, "warning", StringComparison.OrdinalIgnoreCase))
                        Debug.LogWarning(message);
                    else if (string.Equals(level, "err", StringComparison.OrdinalIgnoreCase) || string.Equals(level, "error", StringComparison.OrdinalIgnoreCase))
                        Debug.LogError(message);
                    else
                        Debug.Log(message);
                }
                finally
                {
                    RemoraDevConsole.PopIgnoreUnityLog();
                }
            }

            static object VarCommand(string action, string first = null, string second = null)
            {
                // A pipe is inserted as the first argument, so `cmd | var save key` arrives as value, save, key.
                if (string.Equals(first, "save", StringComparison.OrdinalIgnoreCase) && second != null && !string.Equals(action, "save", StringComparison.OrdinalIgnoreCase))
                    return SaveVariable(action, second);

                if (string.Equals(action, "save", StringComparison.OrdinalIgnoreCase))
                    return SaveVariable(first, second);

                if (second != null)
                {
                    Fail("var: unexpected \"" + second + "\"");
                    return null;
                }

                if (string.Equals(action, "get", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(first))
                    {
                        Fail("var: missing <key>");
                        return null;
                    }

                    string value;
                    if (!variables.TryGetValue(first, out value))
                    {
                        Fail("var: unknown variable \"" + first + "\"");
                        return null;
                    }

                    return Report(value);
                }

                if (string.Equals(action, "exists", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(first))
                    {
                        Fail("var: missing <key>");
                        return null;
                    }

                    return Report(variables.ContainsKey(first));
                }

                if (string.Equals(action, "unset", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(first))
                    {
                        Fail("var: missing <key>");
                        return null;
                    }

                    variables.Remove(first);
                    return null;
                }

                Fail("var: unknown action \"" + (action ?? "") + "\"");
                return null;
            }

            static object SaveVariable(string value, string key)
            {
                if (value == null)
                {
                    Fail("var: missing <value>");
                    return null;
                }

                if (string.IsNullOrEmpty(key))
                {
                    Fail("var: missing <key>");
                    return null;
                }

                variables[key] = value;
                return value;
            }

            static void QuitCommand()
            {
#if UNITY_EDITOR
                EditorApplication.isPlaying = false;
#endif
                Application.Quit();
            }

            static object SceneCommand(string action, string value = null)
            {
                if (string.Equals(action, "query", StringComparison.OrdinalIgnoreCase))
                {
                    if (value != null)
                    {
                        Fail("scene: unexpected \"" + value + "\"");
                        return null;
                    }

                    return Report(SceneManager.GetActiveScene().name);
                }

                if (string.Equals(action, "queryidx", StringComparison.OrdinalIgnoreCase))
                {
                    if (value != null)
                    {
                        Fail("scene: unexpected \"" + value + "\"");
                        return null;
                    }

                    return Report(SceneManager.GetActiveScene().buildIndex);
                }

                if (string.Equals(action, "list", StringComparison.OrdinalIgnoreCase))
                {
                    if (value != null)
                    {
                        Fail("scene: unexpected \"" + value + "\"");
                        return null;
                    }

                    var lines = new StringBuilder();
                    int count = SceneManager.sceneCountInBuildSettings;
                    for (int i = 0; i < count; i++)
                    {
                        string name = SceneNameAt(i);
                        if (name == null)
                            continue;
                        if (lines.Length > 0)
                            lines.Append('\n');
                        lines.Append(i.ToString(CultureInfo.InvariantCulture));
                        lines.Append(' ');
                        lines.Append(name);
                    }

                    return Report(lines.ToString());
                }

                if (string.Equals(action, "set", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        Fail("scene: missing <scene>");
                        return null;
                    }

                    int index = FindScene(value.Trim());
                    if (index < 0)
                    {
                        Fail("scene: unknown scene \"" + value.Trim() + "\"");
                        return null;
                    }

                    SceneManager.LoadScene(index);
                    return Report(SceneManager.GetActiveScene().name);
                }

                if (string.Equals(action, "setidx", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        Fail("scene: missing <scene>");
                        return null;
                    }

                    int index;
                    if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out index) || SceneNameAt(index) == null)
                    {
                        Fail("scene: unknown index \"" + value.Trim() + "\"");
                        return null;
                    }

                    SceneManager.LoadScene(index);
                    return Report(index);
                }

                Fail("scene: unknown action \"" + (action ?? "") + "\"");
                return null;
            }

            static int FindScene(string name)
            {
                int count = SceneManager.sceneCountInBuildSettings;
                int ignoreCase = -1;
                for (int i = 0; i < count; i++)
                {
                    string scene = SceneNameAt(i);
                    if (scene == null)
                        continue;
                    if (string.Equals(scene, name, StringComparison.Ordinal))
                        return i;
                    if (ignoreCase < 0 && string.Equals(scene, name, StringComparison.OrdinalIgnoreCase))
                        ignoreCase = i;
                }

                return ignoreCase;
            }

            static string SceneNameAt(int index)
            {
                if (index < 0 || index >= SceneManager.sceneCountInBuildSettings)
                    return null;

                string path = SceneUtility.GetScenePathByBuildIndex(index);
                if (string.IsNullOrEmpty(path))
                    return null;

                return Path.GetFileNameWithoutExtension(path);
            }

            static object TermCommand(string area, string action, string value = null, string height = null)
            {
                if (!RemoraDevConsole.IsOpen)
                {
                    Fail("term: console is not ready");
                    return null;
                }

                if (string.Equals(area, "textscale", StringComparison.OrdinalIgnoreCase))
                    return TermTextScale(action, value, height);
                if (string.Equals(area, "size", StringComparison.OrdinalIgnoreCase))
                    return TermSize(action, value, height);
                if (string.Equals(area, "dock", StringComparison.OrdinalIgnoreCase))
                    return TermDock(action, value, height);

                Fail("term: unknown area \"" + area + "\"");
                return null;
            }

            static object TermTextScale(string action, string value, string height)
            {
                if (IsSet(action))
                {
                    if (value == null)
                    {
                        Fail("term: missing <value>");
                        return null;
                    }

                    if (height != null)
                    {
                        Fail("term: unexpected \"" + height + "\"");
                        return null;
                    }

                    int size;
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out size))
                    {
                        Fail("term: \"" + value + "\" is not a font size");
                        return null;
                    }

                    RemoraDevConsole.SetCurrentFontSize(size);
                    return null;
                }

                if (RejectValue(action, value, height))
                    return null;
                if (IsReset(action))
                {
                    RemoraDevConsole.ResetFontSize();
                    return null;
                }

                if (IsChanged(action))
                    return Report(RemoraDevConsole.FontSizeChanged());
                if (IsGet(action))
                    return Report(RemoraDevConsole.CurrentFontSize());

                Fail("term: unknown action \"" + action + "\"");
                return null;
            }

            static object TermSize(string action, string value, string height)
            {
                if (IsSet(action))
                {
                    if (value == null)
                    {
                        Fail("term: missing <value>");
                        return null;
                    }

                    if (height == null)
                    {
                        Fail("term: missing <height>");
                        return null;
                    }

                    float width;
                    float tall;
                    if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out width) || !float.TryParse(height, NumberStyles.Float, CultureInfo.InvariantCulture, out tall))
                    {
                        Fail("term: width and height must be numbers");
                        return null;
                    }

                    RemoraDevConsole.SetPanelSize(width, tall);
                    return null;
                }

                if (RejectValue(action, value, height))
                    return null;
                if (IsReset(action))
                {
                    RemoraDevConsole.ResetPanelSize();
                    return null;
                }

                if (IsChanged(action))
                    return Report(RemoraDevConsole.PanelSizeChanged());
                if (IsGet(action))
                    return Report(RemoraDevConsole.PanelSizeText());

                Fail("term: unknown action \"" + action + "\"");
                return null;
            }

            static object TermDock(string action, string value, string height)
            {
                if (IsSet(action))
                {
                    if (value == null)
                    {
                        Fail("term: missing <value>");
                        return null;
                    }

                    if (height != null)
                    {
                        Fail("term: unexpected \"" + height + "\"");
                        return null;
                    }

                    if (!RemoraDevConsole.TrySetDock(value))
                    {
                        Fail("term: unknown dock \"" + value + "\"");
                        return null;
                    }

                    return null;
                }

                if (RejectValue(action, value, height))
                    return null;
                if (IsReset(action))
                {
                    RemoraDevConsole.ResetDock();
                    return null;
                }

                if (IsChanged(action))
                    return Report(RemoraDevConsole.DockChanged());
                if (IsGet(action))
                    return Report(RemoraDevConsole.CurrentDock());

                Fail("term: unknown action \"" + action + "\"");
                return null;
            }

            static bool IsSet(string action)
            {
                return string.Equals(action, "set", StringComparison.OrdinalIgnoreCase);
            }

            static bool IsReset(string action)
            {
                return string.Equals(action, "reset", StringComparison.OrdinalIgnoreCase);
            }

            static bool IsGet(string action)
            {
                return string.Equals(action, "get", StringComparison.OrdinalIgnoreCase);
            }

            static bool IsChanged(string action)
            {
                return string.Equals(action, "ischanged", StringComparison.OrdinalIgnoreCase);
            }

            static bool RejectValue(string action, string value, string height)
            {
                if (value != null)
                {
                    Fail("term: unexpected \"" + value + "\"");
                    return true;
                }

                if (height != null)
                {
                    Fail("term: unexpected \"" + height + "\"");
                    return true;
                }

                return false;
            }

            static object Report(object value)
            {
                string text;
                if (value is bool)
                    text = (bool)value ? "true" : "false";
                else
                    text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                SendTextIn(text);
                return value;
            }

            static void PrefixCommand(string action, string prefix = null)
            {
                if (string.Equals(action, "get", StringComparison.OrdinalIgnoreCase))
                {
                    SendTextIn(QuotedPrefix());
                    return;
                }

                if (string.Equals(action, "default", StringComparison.OrdinalIgnoreCase))
                {
                    userPrefix = "> ";
                    SendTextIn(QuotedPrefix());
                    return;
                }

                if (string.Equals(action, "set", StringComparison.OrdinalIgnoreCase))
                {
                    if (prefix == null)
                    {
                        Fail("prefix: missing <string:prefix>");
                        return;
                    }

                    userPrefix = prefix;
                    SendTextIn(QuotedPrefix());
                    return;
                }

                Fail("prefix: unknown action \"" + action + "\"");
            }

            static string QuotedPrefix()
            {
                var builder = new StringBuilder();
                builder.Append("\u001b[38;2;140;209;255m\"");
                for (int i = 0; i < userPrefix.Length; i++)
                {
                    char current = userPrefix[i];
                    if (current == '\\' || current == '"')
                        builder.Append('\\');
                    builder.Append(current);
                }

                builder.Append("\"\u001b[m");
                return builder.ToString();
            }

            static bool TryLevel(string level, out bool plain, out byte red, out byte green, out byte blue)
            {
                plain = false;
                red = 0;
                green = 0;
                blue = 0;
                if (string.Equals(level, "msg", StringComparison.OrdinalIgnoreCase))
                {
                    plain = true;
                    return true;
                }

                if (string.Equals(level, "warn", StringComparison.OrdinalIgnoreCase) || string.Equals(level, "warning", StringComparison.OrdinalIgnoreCase))
                {
                    red = 255;
                    green = 204;
                    blue = 102;
                    return true;
                }

                if (string.Equals(level, "err", StringComparison.OrdinalIgnoreCase) || string.Equals(level, "error", StringComparison.OrdinalIgnoreCase))
                {
                    red = 255;
                    green = 119;
                    blue = 119;
                    return true;
                }

                if (string.Equals(level, "debug", StringComparison.OrdinalIgnoreCase))
                {
                    red = 160;
                    green = 160;
                    blue = 160;
                    return true;
                }

                return false;
            }

            static string JoinInterpreted(string[] text)
            {
                var builder = new StringBuilder();
                for (int i = 0; i < text.Length; i++)
                {
                    if (i > 0)
                        builder.Append(' ');
                    builder.Append(Interpret(text[i] ?? ""));
                }

                return builder.ToString();
            }

            static string WithLevelColor(string text, byte red, byte green, byte blue)
            {
                string color = "38;2;" + red + ";" + green + ";" + blue;
                var builder = new StringBuilder();
                builder.Append('\u001b');
                builder.Append('[');
                builder.Append(color);
                builder.Append('m');
                for (int i = 0; i < text.Length; i++)
                {
                    int end;
                    if (!TrySgr(text, i, out end))
                    {
                        builder.Append(text[i]);
                        continue;
                    }

                    builder.Append(RewriteReset(text, i, end, color));
                    i = end - 1;
                }

                return builder.ToString();
            }

            static bool TrySgr(string text, int start, out int end)
            {
                end = start;
                if (start + 2 >= text.Length || text[start] != '\u001b' || text[start + 1] != '[')
                    return false;

                int p = start + 2;
                while (p < text.Length)
                {
                    char current = text[p];
                    if (current == 'm')
                    {
                        end = p + 1;
                        return true;
                    }

                    if ((current >= '0' && current <= '9') || current == ';' || current == ':')
                    {
                        p++;
                        continue;
                    }

                    return false;
                }

                return false;
            }

            static string RewriteReset(string text, int start, int end, string color)
            {
                var parts = new List<string>();
                int from = start + 2;
                for (int i = from; i < end; i++)
                {
                    if (text[i] != ';')
                        continue;
                    parts.Add(text.Substring(from, i - from));
                    from = i + 1;
                }

                parts.Add(text.Substring(from, Math.Max(0, end - 1 - from)));

                bool sawReset = false;
                bool colorAfterReset = false;
                var rebuilt = new StringBuilder();
                rebuilt.Append('\u001b');
                rebuilt.Append('[');
                for (int i = 0; i < parts.Count; i++)
                {
                    if (i > 0)
                        rebuilt.Append(';');

                    string part = parts[i];
                    int colorSpan = ColorSpan(parts, i);
                    if (colorSpan > 0)
                    {
                        for (int n = 0; n < colorSpan && i + n < parts.Count; n++)
                        {
                            if (n > 0)
                                rebuilt.Append(';');
                            rebuilt.Append(parts[i + n]);
                        }

                        if (part == "38" || part.StartsWith("38:"))
                            colorAfterReset = true;
                        i += colorSpan - 1;
                        continue;
                    }

                    if (part.Length == 0 || part == "0")
                    {
                        rebuilt.Append(part);
                        sawReset = true;
                        colorAfterReset = false;
                        continue;
                    }

                    if (part == "39")
                    {
                        rebuilt.Append(color);
                        colorAfterReset = true;
                        continue;
                    }

                    rebuilt.Append(part);
                    if (IsClassicForeground(part))
                        colorAfterReset = true;
                }

                rebuilt.Append('m');
                if (sawReset && !colorAfterReset)
                {
                    rebuilt.Append('\u001b');
                    rebuilt.Append('[');
                    rebuilt.Append(color);
                    rebuilt.Append('m');
                }

                return rebuilt.ToString();
            }

            static int ColorSpan(List<string> parts, int index)
            {
                string part = parts[index];
                bool extended = part == "38" || part == "48";
                if (!extended && !part.StartsWith("38:") && !part.StartsWith("48:"))
                    return 0;
                if (!extended)
                    return 1;

                if (index + 1 >= parts.Count)
                    return 1;
                if (parts[index + 1] == "5")
                    return Math.Min(3, parts.Count - index);
                if (parts[index + 1] != "2")
                    return 1;

                int rest = parts.Count - (index + 2);
                if (rest >= 4)
                    return Math.Min(6, parts.Count - index);
                return Math.Min(5, parts.Count - index);
            }

            static bool IsClassicForeground(string part)
            {
                int code;
                if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out code))
                    return false;
                return (code >= 30 && code <= 37) || (code >= 90 && code <= 97);
            }

            static string Interpret(string text)
            {
                var builder = new StringBuilder();
                for (int i = 0; i < text.Length; i++)
                {
                    if (text[i] != '\\' || i + 1 >= text.Length)
                    {
                        builder.Append(text[i]);
                        continue;
                    }

                    char next = text[i + 1];
                    if (next == '\\')
                    {
                        builder.Append('\\');
                        i++;
                        continue;
                    }

                    if (next == 'e' || next == 'E')
                    {
                        builder.Append('\u001b');
                        i++;
                        continue;
                    }

                    if (next == 'u' || next == 'U')
                    {
                        int count = next == 'u' ? 4 : 8;
                        int code;
                        if (TryHex(text, i + 2, count, out code))
                        {
                            builder.Append((char)code);
                            i += 1 + count;
                            continue;
                        }
                    }
                    else if (next == 'x' || next == 'X')
                    {
                        int code;
                        int used;
                        if (TryHexRun(text, i + 2, 2, out code, out used))
                        {
                            builder.Append((char)code);
                            i += 1 + used;
                            continue;
                        }
                    }
                    else if (next >= '0' && next <= '7')
                    {
                        int code;
                        int used;
                        if (TryOctal(text, i + 1, out code, out used))
                        {
                            builder.Append((char)code);
                            i += used;
                            continue;
                        }
                    }

                    builder.Append('\\');
                }

                return builder.ToString();
            }

            static bool TryHex(string text, int start, int count, out int code)
            {
                code = 0;
                if (start + count > text.Length)
                    return false;
                for (int i = 0; i < count; i++)
                {
                    int digit = Hex(text[start + i]);
                    if (digit < 0)
                        return false;
                    code = (code << 4) + digit;
                }

                return true;
            }

            static bool TryHexRun(string text, int start, int max, out int code, out int used)
            {
                code = 0;
                used = 0;
                while (used < max && start + used < text.Length)
                {
                    int digit = Hex(text[start + used]);
                    if (digit < 0)
                        break;
                    code = (code << 4) + digit;
                    used++;
                }

                return used > 0;
            }

            static bool TryOctal(string text, int start, out int code, out int used)
            {
                code = 0;
                used = 0;
                while (used < 3 && start + used < text.Length)
                {
                    char current = text[start + used];
                    if (current < '0' || current > '7')
                        break;
                    code = (code << 3) + (current - '0');
                    used++;
                }

                return used > 0;
            }

            static int Hex(char current)
            {
                if (current >= '0' && current <= '9')
                    return current - '0';
                if (current >= 'a' && current <= 'f')
                    return current - 'a' + 10;
                if (current >= 'A' && current <= 'F')
                    return current - 'A' + 10;
                return -1;
            }

            static Parameter[] Visible(Command command)
            {
                if (command.Shown != null)
                    return command.Shown;
                return command.Parameters;
            }

            static string Signature(Command command)
            {
                var builder = new StringBuilder();
                builder.Append("\u001b[1;38;2;140;209;255m");
                builder.Append(command.Id);
                builder.Append("\u001b[m");
                Parameter[] shown = Visible(command);
                for (int i = 0; i < shown.Length; i++)
                {
                    Parameter parameter = shown[i];
                    builder.Append(' ');
                    builder.Append(TokenColor(parameter));
                    builder.Append(Token(parameter));
                    builder.Append("\u001b[m");
                }

                return builder.ToString();
            }

            static string TokenColor(Parameter parameter)
            {
                if (parameter.Optional)
                    return "\u001b[38;2;160;170;180m";
                return "\u001b[38;2;255;204;102m";
            }

            static string Token(Parameter parameter)
            {
                if (parameter.Params && parameter.Optional)
                    return "[..." + parameter.TypeName + "]";
                if (parameter.Params)
                    return "<..." + parameter.TypeName + ">";
                if (parameter.Optional)
                    return "[" + parameter.TypeName + "]";
                return "<" + parameter.TypeName + ">";
            }

            static string Label(Type type, bool variadic)
            {
                if (variadic && type.IsArray)
                    type = type.GetElementType();
                Type underlying = Nullable.GetUnderlyingType(type);
                if (underlying != null)
                    return Label(underlying, false) + "?";
                if (type == typeof(string))
                    return "string";
                if (type == typeof(int))
                    return "int";
                if (type == typeof(bool))
                    return "bool";
                if (type == typeof(float))
                    return "float";
                if (type == typeof(double))
                    return "double";
                if (type == typeof(long))
                    return "long";
                if (type == typeof(short))
                    return "short";
                if (type == typeof(byte))
                    return "byte";
                if (type == typeof(char))
                    return "char";
                if (type == typeof(object))
                    return "object";
                if (type.IsArray)
                    return Label(type.GetElementType(), false) + "[]";
                return type.Name;
            }

            static bool IsNullableReference(ParameterInfo parameter)
            {
                object[] attributes;
                try
                {
                    attributes = parameter.GetCustomAttributes(true);
                }
                catch (Exception)
                {
                    return false;
                }

                for (int i = 0; i < attributes.Length; i++)
                {
                    object attribute = attributes[i];
                    if (attribute == null || attribute.GetType().Name != "NullableAttribute")
                        continue;
                    FieldInfo flags = attribute.GetType().GetField("NullableFlags");
                    if (flags == null)
                        continue;
                    var bytes = flags.GetValue(attribute) as byte[];
                    if (bytes != null && bytes.Length > 0 && bytes[0] == 2)
                        return true;
                }

                return false;
            }

            static void Fail(string message)
            {
                logBypass++;
                SendTextIn("\u001b[38;2;255;119;119m" + message);
                logBypass--;
            }
        }
    }
}

static class RemoraAnsi
{
    public struct Style
    {
        public bool Bold;
        public bool Italic;
        public bool Underline;
        public bool Strike;
        public byte Script;
        public bool HasFg;
        public bool HasBg;
        public Color32 Fg;
        public Color32 Bg;

        public bool IsDefault
        {
            get
            {
                return !Bold && !Italic && !Underline && !Strike && Script == 0 && !HasFg && !HasBg;
            }
        }
    }

    static readonly Color32[] Basic =
    {
        new Color32(0, 0, 0, 255),
        new Color32(205, 0, 0, 255),
        new Color32(0, 205, 0, 255),
        new Color32(205, 205, 0, 255),
        new Color32(0, 0, 238, 255),
        new Color32(205, 0, 205, 255),
        new Color32(0, 205, 205, 255),
        new Color32(229, 229, 229, 255),
        new Color32(127, 127, 127, 255),
        new Color32(255, 0, 0, 255),
        new Color32(0, 255, 0, 255),
        new Color32(255, 255, 0, 255),
        new Color32(92, 92, 255, 255),
        new Color32(255, 0, 255, 255),
        new Color32(0, 255, 255, 255),
        new Color32(255, 255, 255, 255)
    };

    public static List<string> SplitLines(string content, Style style)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(content))
            return lines;

        content = content.Replace("\r\n", "\n").Replace('\r', '\n');
        if (content[content.Length - 1] == '\n')
            content = content.Substring(0, content.Length - 1);
        if (content.Length == 0)
        {
            lines.Add(Canonical("", ref style));
            return lines;
        }

        int start = 0;
        for (int i = 0; i <= content.Length; i++)
        {
            if (i != content.Length && content[i] != '\n')
                continue;

            string segment = content.Substring(start, i - start);
            lines.Add(Canonical(segment, ref style));
            start = i + 1;
        }

        return lines;
    }

    public static string Present(string ansi, RemoraDev.Console.Format format)
    {
        if (string.IsNullOrEmpty(ansi))
            return "";

        ansi = ansi.Replace("\r\n", "\n").Replace('\r', '\n');
        if (format == RemoraDev.Console.Format.ANSI)
            return ansi;

        var builder = new StringBuilder();
        var style = default(Style);
        Walk(ansi, ref style, true, delegate(string text, Style run)
        {
            if (text == "\n")
            {
                builder.Append('\n');
                return;
            }

            if (format == RemoraDev.Console.Format.NONE)
            {
                builder.Append(text);
                return;
            }

            AppendFormatted(builder, run, text);
        });
        return builder.ToString();
    }

    public static string Plain(string ansi)
    {
        return Present(ansi, RemoraDev.Console.Format.NONE);
    }

    public static string Collapse(string text, List<Style> styles, List<bool> hidden)
    {
        styles.Clear();
        hidden.Clear();
        if (string.IsNullOrEmpty(text))
            return "";

        var chars = text.ToCharArray();
        var style = default(Style);
        for (int i = 0; i < chars.Length;)
        {
            int end;
            if (Match(text, i, out end))
            {
                Apply(text, i + 2, end - 1, ref style);
                for (int k = i; k < end; k++)
                {
                    chars[k] = '\u200b';
                    styles.Add(style);
                    hidden.Add(true);
                }

                i = end;
                continue;
            }

            styles.Add(style);
            hidden.Add(false);
            if (chars[i] == '\n')
                style = default(Style);
            i++;
        }

        return new string(chars);
    }

    public static void FillPlain(string ansi, List<Style> styles, out string plain)
    {
        styles.Clear();
        var builder = new StringBuilder();
        if (string.IsNullOrEmpty(ansi))
        {
            plain = "";
            return;
        }

        int start = 0;
        for (int i = 0; i <= ansi.Length; i++)
        {
            if (i != ansi.Length && ansi[i] != '\n')
                continue;

            var style = default(Style);
            string segment = ansi.Substring(start, i - start);
            Walk(segment, ref style, false, delegate(string text, Style run)
            {
                builder.Append(text);
                for (int n = 0; n < text.Length; n++)
                    styles.Add(run);
            });
            if (i < ansi.Length)
            {
                builder.Append('\n');
                styles.Add(default(Style));
            }

            start = i + 1;
        }

        plain = builder.ToString();
    }

    public static bool TrySkip(string text, int caret, int direction, out int target)
    {
        target = caret;
        if (string.IsNullOrEmpty(text) || direction == 0)
            return false;

        int start;
        int end;
        if (direction < 0)
        {
            if (SequenceEnding(text, caret, out start))
            {
                target = start;
                return true;
            }

            if (caret > 0 && SequenceContaining(text, caret - 1, out start, out end))
            {
                target = start;
                return true;
            }

            return false;
        }

        if (caret < text.Length && Match(text, caret, out end))
        {
            target = end;
            return true;
        }

        if (SequenceContaining(text, caret, out start, out end))
        {
            target = end;
            return true;
        }

        return false;
    }

    public static bool TryDelete(string text, int caret, bool backspace, out int start, out int end)
    {
        start = 0;
        end = 0;
        if (string.IsNullOrEmpty(text))
            return false;

        if (backspace)
        {
            if (SequenceEnding(text, caret, out start))
            {
                end = caret;
                return true;
            }

            return caret > 0 && SequenceContaining(text, caret - 1, out start, out end) || SequenceContaining(text, caret, out start, out end);
        }

        if (caret < text.Length && Match(text, caret, out end))
        {
            start = caret;
            return true;
        }

        return SequenceContaining(text, caret, out start, out end);
    }

    static string Canonical(string segment, ref Style style)
    {
        var builder = new StringBuilder();
        var emitted = default(Style);
        bool pending = true;
        Walk(segment, ref style, false, delegate(string text, Style run)
        {
            if (text.Length == 0 || text == "\n")
                return;

            if (pending || !Same(emitted, run))
            {
                if (!(pending && run.IsDefault))
                    AppendAnsi(builder, run);
                emitted = run;
                pending = false;
            }

            builder.Append(text);
        });
        if (!style.IsDefault)
            builder.Append("\u001b[0m");
        return builder.ToString();
    }

    static void Walk(string ansi, ref Style style, bool resetOnNewline, Action<string, Style> emit)
    {
        if (string.IsNullOrEmpty(ansi))
            return;

        var chunk = new StringBuilder();
        var chunkStyle = style;
        bool open = false;

        for (int i = 0; i < ansi.Length;)
        {
            int end;
            if (Match(ansi, i, out end))
            {
                Flush(chunk, ref open, chunkStyle, emit);
                Apply(ansi, i + 2, end - 1, ref style);
                i = end;
                continue;
            }

            char current = ansi[i];
            if (current == '\n' && resetOnNewline)
            {
                Flush(chunk, ref open, chunkStyle, emit);
                emit("\n", default(Style));
                style = default(Style);
                chunkStyle = default(Style);
                i++;
                continue;
            }

            if (!open || !Same(chunkStyle, style))
            {
                Flush(chunk, ref open, chunkStyle, emit);
                chunkStyle = style;
                open = true;
            }

            chunk.Append(current);
            i++;
        }

        Flush(chunk, ref open, chunkStyle, emit);
    }

    static void Flush(StringBuilder chunk, ref bool open, Style chunkStyle, Action<string, Style> emit)
    {
        if (!open)
            return;

        if (chunk.Length > 0)
            emit(chunk.ToString(), chunkStyle);
        chunk.Length = 0;
        open = false;
    }

    static void AppendAnsi(StringBuilder builder, Style style)
    {
        builder.Append('\u001b');
        builder.Append('[');
        if (style.IsDefault)
        {
            builder.Append('m');
            return;
        }

        builder.Append('0');
        if (style.Bold)
            builder.Append(";1");
        if (style.Italic)
            builder.Append(";3");
        if (style.Underline)
            builder.Append(";4");
        if (style.Strike)
            builder.Append(";9");
        if (style.Script == 1)
            builder.Append(";73");
        else if (style.Script == 2)
            builder.Append(";74");
        if (style.HasFg)
        {
            builder.Append(";38;2;");
            builder.Append(style.Fg.r);
            builder.Append(';');
            builder.Append(style.Fg.g);
            builder.Append(';');
            builder.Append(style.Fg.b);
        }

        if (style.HasBg)
        {
            builder.Append(";48;2;");
            builder.Append(style.Bg.r);
            builder.Append(';');
            builder.Append(style.Bg.g);
            builder.Append(';');
            builder.Append(style.Bg.b);
        }

        builder.Append('m');
    }

    static void AppendFormatted(StringBuilder builder, Style style, string text)
    {
        if (style.Bold)
            builder.Append("<b>");
        if (style.Italic)
            builder.Append("<i>");
        if (style.Underline)
            builder.Append("<u>");
        if (style.Strike)
            builder.Append("<s>");
        if (style.Script == 1)
            builder.Append("<sup>");
        else if (style.Script == 2)
            builder.Append("<sub>");
        if (style.HasFg)
        {
            builder.Append("<color=#");
            builder.Append(style.Fg.r.ToString("x2"));
            builder.Append(style.Fg.g.ToString("x2"));
            builder.Append(style.Fg.b.ToString("x2"));
            builder.Append('>');
        }

        if (style.HasBg)
        {
            builder.Append("<bg=#");
            builder.Append(style.Bg.r.ToString("x2"));
            builder.Append(style.Bg.g.ToString("x2"));
            builder.Append(style.Bg.b.ToString("x2"));
            builder.Append('>');
        }

        for (int i = 0; i < text.Length; i++)
        {
            char current = text[i];
            if (current == '&')
                builder.Append("&amp;");
            else if (current == '<')
                builder.Append("&lt;");
            else if (current == '>')
                builder.Append("&gt;");
            else
                builder.Append(current);
        }

        if (style.HasBg)
            builder.Append("</bg>");
        if (style.HasFg)
            builder.Append("</color>");
        if (style.Script == 2)
            builder.Append("</sub>");
        else if (style.Script == 1)
            builder.Append("</sup>");
        if (style.Strike)
            builder.Append("</s>");
        if (style.Underline)
            builder.Append("</u>");
        if (style.Italic)
            builder.Append("</i>");
        if (style.Bold)
            builder.Append("</b>");
    }

    static bool Match(string text, int i, out int end)
    {
        end = i;
        if (string.IsNullOrEmpty(text) || i + 1 >= text.Length || text[i] != '\u001b' || text[i + 1] != '[')
            return false;

        int p = i + 2;
        while (p < text.Length)
        {
            char current = text[p];
            if (current == 'm')
            {
                end = p + 1;
                return true;
            }

            if ((current >= '0' && current <= '9') || current == ';' || current == ':')
            {
                p++;
                continue;
            }

            return false;
        }

        return false;
    }

    static bool SequenceContaining(string text, int index, out int start, out int end)
    {
        start = 0;
        end = 0;
        if (index < 0 || index >= text.Length)
            return false;

        for (int i = 0; i < text.Length; i++)
        {
            if (!Match(text, i, out end))
                continue;

            if (index >= i && index < end)
            {
                start = i;
                return true;
            }

            i = end - 1;
        }

        return false;
    }

    static bool SequenceEnding(string text, int caret, out int start)
    {
        start = 0;
        if (caret <= 0)
            return false;

        for (int i = 0; i < text.Length; i++)
        {
            int end;
            if (!Match(text, i, out end))
                continue;

            if (end == caret)
            {
                start = i;
                return true;
            }

            i = end - 1;
        }

        return false;
    }

    static void Apply(string text, int from, int to, ref Style style)
    {
        if (from >= to)
        {
            style = default(Style);
            return;
        }

        int i = from;
        while (i < to)
        {
            int code = ReadInt(text, ref i, to);
            if (i < to && text[i] == ':')
            {
                i++;
                if (code == 38 || code == 48)
                    ReadColor(text, ref i, to, code == 48, ref style);
                else
                    while (i < to && text[i] != ';')
                        i++;
            }
            else if (code == 38 || code == 48)
            {
                ReadColor(text, ref i, to, code == 48, ref style);
            }
            else
            {
                ApplyOne(code, ref style);
            }

            if (i < to && text[i] == ';')
                i++;
        }
    }

    static void ReadColor(string text, ref int i, int to, bool background, ref Style style)
    {
        ConsumeSep(text, ref i, to);
        int mode = ReadInt(text, ref i, to);
        if (mode == 5)
        {
            ConsumeSep(text, ref i, to);
            SetColor(ref style, background, Palette(ReadInt(text, ref i, to)));
            return;
        }

        if (mode != 2)
            return;

        int separators = 0;
        int look = i;
        while (look < to && (text[look] == ';' || text[look] == ':'))
        {
            separators++;
            look++;
        }

        if (separators >= 2)
            i = look;
        else if (CountNumbers(text, i, to) >= 4)
        {
            ConsumeSep(text, ref i, to);
            ReadInt(text, ref i, to);
        }

        byte red = NextByte(text, ref i, to);
        byte green = NextByte(text, ref i, to);
        byte blue = NextByte(text, ref i, to);
        SetColor(ref style, background, new Color32(red, green, blue, 255));
    }

    static void ApplyOne(int code, ref Style style)
    {
        if (code == 0)
        {
            style = default(Style);
            return;
        }

        if (code == 1)
            style.Bold = true;
        else if (code == 3)
            style.Italic = true;
        else if (code == 4)
            style.Underline = true;
        else if (code == 9)
            style.Strike = true;
        else if (code == 22)
            style.Bold = false;
        else if (code == 23)
            style.Italic = false;
        else if (code == 24)
            style.Underline = false;
        else if (code == 29)
            style.Strike = false;
        else if (code == 39)
            style.HasFg = false;
        else if (code == 49)
            style.HasBg = false;
        else if (code == 73)
            style.Script = 1;
        else if (code == 74)
            style.Script = 2;
        else if (code == 75)
            style.Script = 0;
        else if (code >= 30 && code <= 37)
            SetColor(ref style, false, Palette(code - 30));
        else if (code >= 90 && code <= 97)
            SetColor(ref style, false, Palette(code - 90 + 8));
        else if (code >= 40 && code <= 47)
            SetColor(ref style, true, Palette(code - 40));
        else if (code >= 100 && code <= 107)
            SetColor(ref style, true, Palette(code - 100 + 8));
    }

    static Color32 Palette(int index)
    {
        if (index < 0)
            index = 0;
        if (index > 255)
            index = 255;
        if (index < 16)
            return Basic[index];
        if (index >= 232)
        {
            byte gray = (byte)(8 + (index - 232) * 10);
            return new Color32(gray, gray, gray, 255);
        }

        int cube = index - 16;
        return new Color32(Cube(cube / 36), Cube((cube / 6) % 6), Cube(cube % 6), 255);
    }

    static byte Cube(int value)
    {
        if (value <= 0)
            return 0;
        return (byte)(55 + value * 40);
    }

    static void SetColor(ref Style style, bool background, Color32 color)
    {
        if (background)
        {
            style.HasBg = true;
            style.Bg = color;
        }
        else
        {
            style.HasFg = true;
            style.Fg = color;
        }
    }

    static int ReadInt(string text, ref int i, int to)
    {
        int value = 0;
        while (i < to && text[i] >= '0' && text[i] <= '9')
        {
            value = value * 10 + (text[i] - '0');
            i++;
        }

        return value;
    }

    static byte NextByte(string text, ref int i, int to)
    {
        ConsumeSep(text, ref i, to);
        return (byte)Mathf.Clamp(ReadInt(text, ref i, to), 0, 255);
    }

    static void ConsumeSep(string text, ref int i, int to)
    {
        if (i < to && (text[i] == ';' || text[i] == ':'))
            i++;
    }

    static int CountNumbers(string text, int i, int to)
    {
        int count = 0;
        bool inNumber = false;
        for (int p = i; p < to; p++)
        {
            char current = text[p];
            if (current >= '0' && current <= '9')
            {
                if (!inNumber)
                {
                    count++;
                    inNumber = true;
                }
            }
            else
            {
                inNumber = false;
                if (current != ';' && current != ':')
                    break;
            }
        }

        return count;
    }

    static bool Same(Style a, Style b)
    {
        if (a.Bold != b.Bold || a.Italic != b.Italic || a.Underline != b.Underline || a.Strike != b.Strike)
            return false;
        if (a.Script != b.Script || a.HasFg != b.HasFg || a.HasBg != b.HasBg)
            return false;
        if (a.HasFg && (a.Fg.r != b.Fg.r || a.Fg.g != b.Fg.g || a.Fg.b != b.Fg.b))
            return false;
        if (a.HasBg && (a.Bg.r != b.Bg.r || a.Bg.g != b.Bg.g || a.Bg.b != b.Bg.b))
            return false;
        return true;
    }
}

public class RemoraDevConsole : MonoBehaviour
{
    private static RemoraDevConsole instance;
    private static int ignoreUnityLog;
    private bool mirrorDebugLog;
    private static Sprite whiteSprite;
    private static Font builtinFont;

    private readonly List<string> lines = new List<string>();
    private readonly TextGenerator logMeasure = new TextGenerator();

    private ScrollRect scrollRect;
    private RectTransform panelRect;
    private RectTransform logView;
    private RectTransform contentRect;
    private RectTransform inputRect;
    private AnsiText logText;
    private LayoutElement logLayout;
    private AnsiText inputText;
    private Text placeholderText;
    private InputField inputField;
    private GameObject canvasObject;
    private bool consoleHidden;
    private CanvasScaler canvasScaler;
    private RectTransform viewportRect;
    private RectTransform scrollbarRect;
    private int fontSize;
    private float panelWidth;
    private float panelHeight;
    private bool dockRight;
    private bool dockTop;
    private bool bothCtrlsWereDown;
    private GameObject ownedEventSystem;
    private bool followBottom = true;
    private bool scrollToBottom;
    private bool layoutDirty;
    private bool adjustingScroll;
    private bool refocusInput;
    private float appliedViewHeight = -1f;
    private readonly List<string> typedHistory = new List<string>();
    private int historyIndex = -1;
    private string historyDraft = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (instance != null)
            return;

        var root = new GameObject("Remora Dev Console");
        root.AddComponent<RemoraDevConsole>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        LoadConfig();
        Build();
    }

    private void LoadConfig()
    {
        RemoraDevConfig.TryApplyFile();
        fontSize = Setting<int>("FontSize");
        panelWidth = Setting<float>("PanelWidth");
        panelHeight = Setting<float>("PanelHeight");
        ReadDock(Setting<string>("Dock"), out dockRight, out dockTop);
        mirrorDebugLog = ReadLogDlog();
    }

    private static T Setting<T>(string name)
    {
        return RemoraDevConfig.Get<T>("DevConsole.Config." + name);
    }

    private static void ReadDock(string dock, out bool right, out bool top)
    {
        switch (dock)
        {
            case "bottom-right":
                right = true;
                top = false;
                break;
            case "top-left":
                right = false;
                top = true;
                break;
            case "top-right":
                right = true;
                top = true;
                break;
            default:
                right = false;
                top = false;
                break;
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        Application.logMessageReceived += OnLogMessage;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Application.logMessageReceived -= OnLogMessage;
    }

    private void Start()
    {
        EnsureEventSystem();
        string startup = Setting<string>("StartupCommand");
        if (!string.IsNullOrWhiteSpace(startup))
        {
            string version = RemoraDevConfig.Get<string>("Versions.Remora") ?? "";
            version = version.Replace("\\", "\\\\").Replace("\"", "\\\"");
            startup = startup.Replace("{versions.remora}", version);
            SubmitInput(startup, Setting<bool>("ShowInternalCommands"));
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    internal static void ApiSend(string content, bool raw)
    {
        if (raw && RemoraDev.Console.Cmds.Absorb(content))
            return;

        if (instance == null)
            return;

        if (raw)
        {
            instance.AddContent(content, default(RemoraAnsi.Style));
            return;
        }

        instance.SubmitInput(content, true);
    }

    internal static void EchoLine(string line)
    {
        if (instance == null || string.IsNullOrEmpty(line))
            return;

        instance.AddContent(RemoraDev.Console.Cmds.PrefixUserLine(line), default(RemoraAnsi.Style));
    }

    internal static void ApiSetBuffer(string content)
    {
        if (instance == null)
            return;

        instance.lines.Clear();
        if (!string.IsNullOrEmpty(content))
            instance.lines.AddRange(RemoraAnsi.SplitLines(content, default(RemoraAnsi.Style)));
        instance.ShowLines();
    }

    internal static void ApiSetLine(int lineNr, string content)
    {
        if (instance == null || lineNr < 0)
            return;

        var added = RemoraAnsi.SplitLines(content ?? "", default(RemoraAnsi.Style));
        if (added.Count == 0)
            added.Add("");

        while (instance.lines.Count < lineNr)
            instance.lines.Add("");

        if (lineNr == instance.lines.Count)
        {
            instance.lines.AddRange(added);
        }
        else
        {
            instance.lines.RemoveAt(lineNr);
            instance.lines.InsertRange(lineNr, added);
        }

        instance.ShowLines();
    }

    internal static string ApiGetBuffer(RemoraDev.Console.Format format)
    {
        if (instance == null || instance.lines.Count == 0)
            return "";

        return RemoraAnsi.Present(string.Join("\n", instance.lines), format);
    }

    internal static string ApiGetLastLine(RemoraDev.Console.Format format)
    {
        if (instance == null || instance.lines.Count == 0)
            return "";

        return RemoraAnsi.Present(instance.lines[instance.lines.Count - 1], format);
    }

    private void Update()
    {
        if (ToggleHiddenPressed())
            SetConsoleHidden(!consoleHidden, true);

        if (consoleHidden || inputField == null || !inputField.isFocused)
            return;

        bool bothCtrls = BothCtrlsHeld();
        if (bothCtrls && !bothCtrlsWereDown)
            ResetLayout();
        bothCtrlsWereDown = bothCtrls;

        if (!CtrlHeld() || AltHeld())
            return;

        if (PressedMinus())
            SetFontSize(fontSize - 1);
        else if (PressedPlus())
            SetFontSize(fontSize + 1);
        else if (PressedDot())
            SetFontSize(Setting<int>("FontSize"));
    }

    private void LateUpdate()
    {
        if (refocusInput && inputField != null)
        {
            refocusInput = false;
            inputField.ActivateInputField();
        }

        if (scrollRect == null || scrollRect.viewport == null)
            return;

        float viewHeight = scrollRect.viewport.rect.height;
        bool viewChanged = !Mathf.Approximately(viewHeight, appliedViewHeight);
        if (!layoutDirty && !scrollToBottom && !viewChanged)
            return;

        bool pin = scrollToBottom && followBottom;
        layoutDirty = false;
        scrollToBottom = false;
        appliedViewHeight = viewHeight;

        Canvas.ForceUpdateCanvases();
        FitLogHeight();
        var layout = contentRect.GetComponent<VerticalLayoutGroup>();
        float preferred = logLayout.preferredHeight + (layout != null ? layout.padding.vertical : 0);
        contentRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(preferred, viewHeight));
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);

        if (!pin)
            return;

        adjustingScroll = true;
        scrollRect.velocity = Vector2.zero;
        scrollRect.verticalNormalizedPosition = 0f;
        adjustingScroll = false;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureEventSystem();
    }

    public static bool GameplayInputBlocked()
    {
        if (instance == null || instance.consoleHidden || instance.inputField == null || !instance.inputField.isFocused)
            return false;

        try
        {
            return !RemoraDevConfig.Get<bool>("DevConsole.Config.PropogageKeys");
        }
        catch (Exception)
        {
            return true;
        }
    }

    internal static void PushIgnoreUnityLog()
    {
        ignoreUnityLog++;
    }

    internal static void PopIgnoreUnityLog()
    {
        if (ignoreUnityLog > 0)
            ignoreUnityLog--;
    }

    private static bool ReadLogDlog()
    {
        try
        {
            object value = RemoraDevConfig.Get<object>("DevConsole.Config.LogDlog");
            return value is bool && (bool)value;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsUnityDebugLog(LogType type, string message, string stackTrace)
    {
        if (type == LogType.Log || type == LogType.Exception)
            return true;

        string blob = (message ?? "") + "\n" + (stackTrace ?? "");
        if (blob.IndexOf("MonoBehaviour:print", StringComparison.Ordinal) >= 0)
            return true;
        if (blob.IndexOf("Debug:Log (", StringComparison.Ordinal) < 0)
            return false;
        if (blob.IndexOf("Debug:LogWarning", StringComparison.Ordinal) >= 0)
            return false;
        if (blob.IndexOf("Debug:LogError", StringComparison.Ordinal) >= 0)
            return false;
        if (blob.IndexOf("Debug:LogException", StringComparison.Ordinal) >= 0)
            return false;
        return true;
    }

    private void OnLogMessage(string message, string stackTrace, LogType type)
    {
        if (ignoreUnityLog > 0 || logText == null || string.IsNullOrEmpty(message))
            return;

        if (!mirrorDebugLog && IsUnityDebugLog(type, message, stackTrace))
            return;

        var style = default(RemoraAnsi.Style);
        style.HasFg = true;
        switch (type)
        {
            case LogType.Warning:
                style.Fg = new Color32(255, 204, 102, 255);
                break;
            case LogType.Error:
            case LogType.Assert:
            case LogType.Exception:
                style.Fg = new Color32(255, 119, 119, 255);
                break;
            default:
                style.Fg = new Color32(230, 230, 230, 255);
                break;
        }

        AddContent(message, style);
    }

    private void OnSubmit(string text)
    {
        inputField.text = string.Empty;
        refocusInput = true;
        historyIndex = -1;
        historyDraft = "";

        if (string.IsNullOrWhiteSpace(text))
            return;

        typedHistory.Add(text.Trim());
        RemoraDev.Console.SendTextIn(text, false);
    }

    internal bool RecallTyped(int direction)
    {
        if (inputField == null)
            return false;
        if (typedHistory.Count == 0 || direction == 0)
            return true;

        if (direction < 0)
        {
            if (historyIndex == -1)
            {
                historyDraft = inputField.text ?? "";
                historyIndex = typedHistory.Count - 1;
            }
            else if (historyIndex > 0)
                historyIndex--;

            ShowRecalled(typedHistory[historyIndex]);
            return true;
        }

        if (historyIndex == -1)
            return true;
        if (historyIndex < typedHistory.Count - 1)
        {
            historyIndex++;
            ShowRecalled(typedHistory[historyIndex]);
            return true;
        }

        historyIndex = -1;
        ShowRecalled(historyDraft);
        return true;
    }

    private void ShowRecalled(string value)
    {
        inputField.text = value ?? "";
        inputField.MoveTextEnd(false);
    }

    private void SubmitInput(string text, bool echo)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        string submitted = text.Trim();
        if (echo)
            AddContent(RemoraDev.Console.Cmds.PrefixUserLine(submitted), default(RemoraAnsi.Style));
        RemoraDev.Console.Cmds.ExecuteInput(submitted);
    }

    private void OnScroll(Vector2 position)
    {
        if (adjustingScroll)
            return;

        followBottom = IsPinnedToBottom();
    }

    private void AddContent(string content, RemoraAnsi.Style style)
    {
        if (string.IsNullOrEmpty(content))
            return;

        lines.AddRange(RemoraAnsi.SplitLines(content, style));
        ShowLines();
    }

    private void ShowLines()
    {
        int max = Setting<int>("MaxLines");
        if (max < 1)
            max = 1;
        if (lines.Count > max)
            lines.RemoveRange(0, lines.Count - max);

        logText.text = string.Join("\n", lines);
        MarkLayoutDirty();
    }

    private void FitLogHeight()
    {
        if (logText == null || logLayout == null)
            return;

        float width = logText.rectTransform.rect.width;
        if (scrollRect != null && scrollRect.viewport != null)
        {
            var layout = contentRect.GetComponent<VerticalLayoutGroup>();
            float inner = scrollRect.viewport.rect.width - (layout != null ? layout.padding.horizontal : 0f);
            if (inner > 1f && (width < 1f || inner < width))
                width = inner;
        }

        if (width < 1f)
            return;

        string plain = RemoraAnsi.Plain(logText.text);
        var settings = logText.GetGenerationSettings(new Vector2(width, 0f));
        settings.richText = false;
        settings.horizontalOverflow = HorizontalWrapMode.Wrap;
        settings.verticalOverflow = VerticalWrapMode.Overflow;
        settings.generateOutOfBounds = true;
        settings.updateBounds = true;
        logMeasure.Populate(plain, settings);

        float units = Mathf.Max(0.01f, logText.pixelsPerUnit);
        float height = logMeasure.rectExtents.height / units;
        int rowCount = logMeasure.lineCount;
        if (rowCount > 0)
        {
            var rows = logMeasure.lines;
            UILineInfo last = rows[rowCount - 1];
            float block = (rows[0].topY - (last.topY - last.height)) / units;
            if (block > height)
                height = block;
        }

        // Glyph bounds stop at the ink, so the last line was shorter than the
        // cell the mesh actually occupies and the scroller still thought it fit.
        logLayout.preferredHeight = Mathf.Ceil(height);
    }

    internal static bool IsOpen
    {
        get { return instance != null; }
    }

    internal static int CurrentFontSize()
    {
        return instance.fontSize;
    }

    internal static bool FontSizeChanged()
    {
        return instance.fontSize != Setting<int>("FontSize");
    }

    internal static void CaptureDrift(out bool fontChanged, out bool sizeChanged, out bool hiddenChanged)
    {
        if (instance == null)
        {
            fontChanged = false;
            sizeChanged = false;
            hiddenChanged = false;
            return;
        }

        fontChanged = instance.fontSize != Setting<int>("FontSize");
        sizeChanged = Mathf.Abs(instance.panelWidth - Setting<float>("PanelWidth")) > 0.01f
            || Mathf.Abs(instance.panelHeight - Setting<float>("PanelHeight")) > 0.01f;
        hiddenChanged = instance.consoleHidden != RemoraDevConfig.Get<bool>("DevConsole.Hidden");
    }

    internal static void SyncAfterConfig(bool keepFont, bool keepSize, bool keepHidden)
    {
        if (instance == null)
            return;

        instance.ApplyConfig(keepFont, keepSize, keepHidden);
    }

    private void ApplyConfig(bool keepFont, bool keepSize, bool keepHidden)
    {
        if (!keepSize)
        {
            panelWidth = Setting<float>("PanelWidth");
            panelHeight = Setting<float>("PanelHeight");
        }

        ReadDock(Setting<string>("Dock"), out dockRight, out dockTop);
        if (canvasScaler != null)
        {
            canvasScaler.referenceResolution = new Vector2(Setting<float>("ReferenceWidth"), Setting<float>("ReferenceHeight"));
            canvasScaler.matchWidthOrHeight = Setting<float>("ScaleMatch");
        }

        ApplyPanel();
        if (viewportRect != null)
            Stretch(viewportRect, 0f, 0f, Setting<float>("ScrollbarWidth"), 0f);
        if (scrollbarRect != null)
        {
            scrollbarRect.offsetMin = new Vector2(-Setting<float>("ScrollbarWidth"), 0f);
            scrollbarRect.offsetMax = new Vector2(0f, 0f);
        }

        SetFontSize(keepFont ? fontSize : Setting<int>("FontSize"), true);
        mirrorDebugLog = ReadLogDlog();
        if (!keepHidden)
            SetConsoleHidden(RemoraDevConfig.Get<bool>("DevConsole.Hidden"), false);
        if (logText != null)
            ShowLines();
    }

    private void SetConsoleHidden(bool hidden, bool focus)
    {
        consoleHidden = hidden;
        if (canvasObject != null)
            canvasObject.SetActive(!hidden);
        if (hidden)
        {
            refocusInput = false;
            return;
        }

        if (focus && inputField != null)
            inputField.ActivateInputField();
    }

    private bool ToggleHiddenPressed()
    {
        List<string> keys;
        if (!TryParseKeybind(RemoraDevConfig.Get<string>("DevConsole.Keybinds.ToggleHidden"), out keys))
            return false;

        bool all = true;
        bool edge = false;
        for (int i = 0; i < keys.Count; i++)
        {
            bool pressed;
            bool down;
            if (!TryKeyState(keys[i], out pressed, out down))
                return false;
            if (!pressed)
                all = false;
            if (down)
                edge = true;
        }

        return all && edge;
    }

    // '+' separates keys. A key that is plus itself is written '+', so "ctrl++" is ctrl and plus, while "ctrl+" is unfinished.
    private static bool TryParseKeybind(string text, out List<string> keys)
    {
        keys = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parsed = new List<string>();
        int i = 0;
        if (!ReadKeybindKey(text, ref i, parsed))
            return false;
        while (i < text.Length)
        {
            SkipKeybindSpace(text, ref i);
            if (i >= text.Length || text[i] != '+')
                return false;
            i++;
            if (!ReadKeybindKey(text, ref i, parsed))
                return false;
        }

        keys = parsed;
        return true;
    }

    private static bool ReadKeybindKey(string text, ref int i, List<string> keys)
    {
        SkipKeybindSpace(text, ref i);
        if (i >= text.Length)
            return false;
        if (text[i] == '+')
        {
            keys.Add("+");
            i++;
            return true;
        }

        int start = i;
        while (i < text.Length && text[i] != '+' && !char.IsWhiteSpace(text[i]))
            i++;
        if (start == i)
            return false;

        keys.Add(text.Substring(start, i - start));
        SkipKeybindSpace(text, ref i);
        return true;
    }

    private static void SkipKeybindSpace(string text, ref int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i]))
            i++;
    }

    private static bool TryKeyState(string token, out bool pressed, out bool down)
    {
        pressed = false;
        down = false;
        if (string.IsNullOrEmpty(token))
            return false;

#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard == null)
            return false;

        string name = token.ToLowerInvariant();
        if (name == "ctrl" || name == "control")
            return ButtonState(keyboard.ctrlKey, out pressed, out down);
        if (name == "shift")
            return ButtonState(keyboard.shiftKey, out pressed, out down);
        if (name == "alt")
            return ButtonState(keyboard.altKey, out pressed, out down);
        if (name == "leftctrl" || name == "lctrl")
            return ButtonState(keyboard.leftCtrlKey, out pressed, out down);
        if (name == "rightctrl" || name == "rctrl")
            return ButtonState(keyboard.rightCtrlKey, out pressed, out down);
        if (name == "leftshift" || name == "lshift")
            return ButtonState(keyboard.leftShiftKey, out pressed, out down);
        if (name == "rightshift" || name == "rshift")
            return ButtonState(keyboard.rightShiftKey, out pressed, out down);
        if (name == "leftalt" || name == "lalt")
            return ButtonState(keyboard.leftAltKey, out pressed, out down);
        if (name == "rightalt" || name == "ralt")
            return ButtonState(keyboard.rightAltKey, out pressed, out down);
        if (name == "+" || name == "plus")
        {
            bool padPressed;
            bool padDown;
            bool equalsPressed;
            bool equalsDown;
            bool shownPressed;
            bool shownDown;
            bool pad = ButtonState(keyboard.numpadPlusKey, out padPressed, out padDown);
            bool equals = ButtonState(keyboard.equalsKey, out equalsPressed, out equalsDown);
            bool shown = DisplayKeyState(keyboard, "+", out shownPressed, out shownDown);
            if (!pad && !equals && !shown)
                return false;

            pressed = padPressed || equalsPressed || shownPressed;
            down = padDown || equalsDown || shownDown;
            return true;
        }

        var keys = keyboard.allKeys;
        for (int i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            if (string.Equals(key.name, token, StringComparison.OrdinalIgnoreCase) || string.Equals(key.displayName, token, StringComparison.OrdinalIgnoreCase))
                return ButtonState(key, out pressed, out down);
        }

        return false;
#else
        return false;
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private static bool ButtonState(UnityEngine.InputSystem.Controls.ButtonControl button, out bool pressed, out bool down)
    {
        pressed = button != null && button.isPressed;
        down = button != null && button.wasPressedThisFrame;
        return button != null;
    }

    private static bool DisplayKeyState(Keyboard keyboard, string displayName, out bool pressed, out bool down)
    {
        pressed = false;
        down = false;
        bool found = false;
        var keys = keyboard.allKeys;
        for (int i = 0; i < keys.Count; i++)
        {
            if (!string.Equals(keys[i].displayName, displayName, StringComparison.OrdinalIgnoreCase))
                continue;
            found = true;
            pressed = pressed || keys[i].isPressed;
            down = down || keys[i].wasPressedThisFrame;
        }

        return found;
    }
#endif

    internal static void SetCurrentFontSize(int size)
    {
        instance.SetFontSize(size, true);
    }

    internal static void ResetFontSize()
    {
        instance.SetFontSize(Setting<int>("FontSize"), true);
    }

    internal static void SetPanelSize(float width, float height)
    {
        instance.panelWidth = width;
        instance.panelHeight = height;
        instance.ApplyPanel();
        instance.MarkLayoutDirty();
    }

    internal static void ResetPanelSize()
    {
        SetPanelSize(Setting<float>("PanelWidth"), Setting<float>("PanelHeight"));
    }

    internal static bool PanelSizeChanged()
    {
        return Mathf.Abs(instance.panelWidth - Setting<float>("PanelWidth")) > 0.01f || Mathf.Abs(instance.panelHeight - Setting<float>("PanelHeight")) > 0.01f;
    }

    internal static string PanelSizeText()
    {
        return FormatPanel(instance.panelWidth) + " " + FormatPanel(instance.panelHeight);
    }

    internal static string CurrentDock()
    {
        return DockLabel(instance.dockRight, instance.dockTop);
    }

    internal static bool DockChanged()
    {
        bool right;
        bool top;
        ReadDock(Setting<string>("Dock"), out right, out top);
        return instance.dockRight != right || instance.dockTop != top;
    }

    internal static bool TrySetDock(string name)
    {
        bool right;
        bool top;
        if (!TryReadDock(name, out right, out top))
            return false;

        instance.dockRight = right;
        instance.dockTop = top;
        instance.ApplyPanel();
        return true;
    }

    internal static void ResetDock()
    {
        bool right;
        bool top;
        ReadDock(Setting<string>("Dock"), out right, out top);
        instance.dockRight = right;
        instance.dockTop = top;
        instance.ApplyPanel();
    }

    private static string DockLabel(bool right, bool top)
    {
        if (top)
            return right ? "top-right" : "top-left";
        return right ? "bottom-right" : "bottom-left";
    }

    private static bool TryReadDock(string dock, out bool right, out bool top)
    {
        if (string.Equals(dock, "bottom-left", StringComparison.OrdinalIgnoreCase))
        {
            right = false;
            top = false;
            return true;
        }

        if (string.Equals(dock, "bottom-right", StringComparison.OrdinalIgnoreCase))
        {
            right = true;
            top = false;
            return true;
        }

        if (string.Equals(dock, "top-left", StringComparison.OrdinalIgnoreCase))
        {
            right = false;
            top = true;
            return true;
        }

        if (string.Equals(dock, "top-right", StringComparison.OrdinalIgnoreCase))
        {
            right = true;
            top = true;
            return true;
        }

        right = false;
        top = false;
        return false;
    }

    private static string FormatPanel(float value)
    {
        float rounded = Mathf.Round(value);
        if (Mathf.Abs(value - rounded) <= 0.01f)
            return ((int)rounded).ToString(CultureInfo.InvariantCulture);
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private void SetFontSize(int size, bool force = false)
    {
        if (!force && size == fontSize)
            return;

        fontSize = size;
        logText.fontSize = size;
        inputText.fontSize = size;
        placeholderText.fontSize = size;
        inputField.ForceLabelUpdate();

        float rowHeight = Mathf.Max(Setting<float>("InputHeight"), size + 14f);
        inputRect.sizeDelta = new Vector2(-(Setting<float>("Padding") * 2f), rowHeight);
        Stretch(logView, Setting<float>("Padding"), Setting<float>("Padding") + rowHeight + 6f, Setting<float>("Padding"), Setting<float>("Padding"));
        MarkLayoutDirty();
    }

    private void MarkLayoutDirty()
    {
        layoutDirty = true;
        if (followBottom)
            scrollToBottom = true;
    }

    private char ValidateInput(string text, int index, char added)
    {
        if (CtrlHeld() && !AltHeld() && (added == '+' || added == '=' || added == '-' || added == '.'))
            return '\0';

        return added;
    }

    private static bool CtrlHeld()
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard != null)
            return keyboard.ctrlKey.isPressed;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
#else
        return false;
#endif
    }

    private static bool AltHeld()
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard != null)
            return keyboard.altKey.isPressed;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
#else
        return false;
#endif
    }

    private static bool ShiftHeld()
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard != null)
            return keyboard.shiftKey.isPressed;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#else
        return false;
#endif
    }

    private static bool PressedPlus()
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.numpadPlusKey.wasPressedThisFrame || PressedDisplayName("+") || PressedDisplayName("=")))
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus);
#else
        return false;
#endif
    }

    private static bool PressedMinus()
    {
        if (ShiftHeld())
            return false;

#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.numpadMinusKey.wasPressedThisFrame || PressedDisplayName("-")))
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus);
#else
        return false;
#endif
    }

    private static bool PressedDot()
    {
        if (ShiftHeld())
            return false;

#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.periodKey.wasPressedThisFrame || keyboard.numpadPeriodKey.wasPressedThisFrame || PressedDisplayName(".")))
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.Period) || Input.GetKeyDown(KeyCode.KeypadPeriod);
#else
        return false;
#endif
    }

    private bool HandleChord(Event evt)
    {
        bool ctrl = (evt.modifiers & EventModifiers.Control) != 0;
        bool shift = (evt.modifiers & EventModifiers.Shift) != 0;
        bool alt = (evt.modifiers & EventModifiers.Alt) != 0;
        if (alt || ctrl == shift)
            return false;

        if (ctrl && (evt.keyCode == KeyCode.Period || evt.keyCode == KeyCode.KeypadPeriod || evt.character == '.'))
        {
            SetFontSize(Setting<int>("FontSize"));
            return true;
        }

        int horizontal = 0;
        int vertical = 0;
        switch (evt.keyCode)
        {
            case KeyCode.LeftArrow:
                horizontal = -1;
                break;
            case KeyCode.RightArrow:
                horizontal = 1;
                break;
            case KeyCode.UpArrow:
                vertical = 1;
                break;
            case KeyCode.DownArrow:
                vertical = -1;
                break;
            default:
                return false;
        }

        if (ctrl)
            Resize(horizontal, vertical);
        else
            Dock(horizontal, vertical);

        return true;
    }

    private void Resize(int horizontal, int vertical)
    {
        if (horizontal != 0)
        {
            float direction = dockRight ? -horizontal : horizontal;
            panelWidth += direction * Setting<float>("ResizeStep");
        }

        if (vertical != 0)
        {
            float direction = dockTop ? -vertical : vertical;
            panelHeight += direction * Setting<float>("ResizeStep");
        }

        ApplyPanel();
        MarkLayoutDirty();
    }

    private void Dock(int horizontal, int vertical)
    {
        if (horizontal < 0)
            dockRight = false;
        else if (horizontal > 0)
            dockRight = true;

        if (vertical < 0)
            dockTop = false;
        else if (vertical > 0)
            dockTop = true;

        ApplyPanel();
    }

    private void ResetLayout()
    {
        bool homeRight;
        bool homeTop;
        ReadDock(Setting<string>("Dock"), out homeRight, out homeTop);
        if (dockRight == homeRight && dockTop == homeTop && panelWidth == Setting<float>("PanelWidth") && panelHeight == Setting<float>("PanelHeight"))
            return;

        dockRight = homeRight;
        dockTop = homeTop;
        panelWidth = Setting<float>("PanelWidth");
        panelHeight = Setting<float>("PanelHeight");
        ApplyPanel();
        MarkLayoutDirty();
    }

    private static bool BothCtrlsHeld()
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard != null)
            return keyboard.leftCtrlKey.isPressed && keyboard.rightCtrlKey.isPressed;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(KeyCode.LeftControl) && Input.GetKey(KeyCode.RightControl);
#else
        return false;
#endif
    }

    public static string GetDynNamespaceDir()
    {
        string beside = DirectoryOf(ConfigBesideBinary());
        if (beside != null)
            return beside;

        string streaming = DirectoryOf(ConfigInStreamingAssets());
        if (streaming != null)
            return streaming;

        string source = DirectoryOf(ConfigInSource());
        if (source != null)
            return source;

        return null;
    }

    internal static bool TryReadConfig(out string json)
    {
        json = null;

        string beside = ConfigBesideBinary();
        if (beside != null)
            return ReadConfigFile(beside, out json);

        string streaming = ConfigInStreamingAssets();
        if (streaming != null)
            return ReadConfigFile(streaming, out json);

        TextAsset asset = Resources.Load<TextAsset>("remora/config");
        if (asset == null)
            asset = Resources.Load<TextAsset>("Remora/config");
        if (asset != null)
        {
            json = asset.text ?? "";
            return true;
        }

        string source = ConfigInSource();
        if (source != null)
            return ReadConfigFile(source, out json);

        return false;
    }

    internal static bool TryReadRem(string file, out string text, out string error)
    {
        text = null;
        error = null;
        string relative;
        if (!TryRemRelative(file, out relative))
        {
            error = "\"" + (file ?? "") + "\" is not a path inside the remora folder";
            return false;
        }

        if (TryReadRemFolder(ChildDirectoryExact(GetBinaryDirectory(), "remora"), relative, out text, out error))
            return error == null;
        if (error != null)
            return false;
        if (TryReadRemFolder(ChildDirectoryExact(GetBinaryDirectory(), "Remora"), relative, out text, out error))
            return error == null;
        if (error != null)
            return false;

        string streaming = Application.streamingAssetsPath;
        if (TryReadRemFolder(ChildDirectoryExact(streaming, "remora"), relative, out text, out error))
            return error == null;
        if (error != null)
            return false;
        if (TryReadRemFolder(ChildDirectoryExact(streaming, "Remora"), relative, out text, out error))
            return error == null;
        if (error != null)
            return false;

        TextAsset asset = Resources.Load<TextAsset>(RemResourcePath("remora", relative));
        if (asset == null)
            asset = Resources.Load<TextAsset>(RemResourcePath("Remora", relative));
        if (asset != null)
        {
            text = asset.text ?? "";
            return true;
        }

        string binaryDirectory = GetBinaryDirectory();
        string sourceRoot = GetSourceRoot(binaryDirectory);
        if (!string.IsNullOrEmpty(sourceRoot) && (binaryDirectory == null || !SamePath(sourceRoot, binaryDirectory)))
        {
            if (TryReadRemFolder(ChildDirectoryExact(sourceRoot, "remora"), relative, out text, out error))
                return error == null;
            if (error != null)
                return false;
            if (TryReadRemFolder(ChildDirectoryExact(sourceRoot, "Remora"), relative, out text, out error))
                return error == null;
            if (error != null)
                return false;
        }

        error = "\"" + relative + "\" was not found";
        return false;
    }

    static bool TryReadRemFolder(string folder, string relative, out string text, out string error)
    {
        text = null;
        error = null;
        if (string.IsNullOrEmpty(folder))
            return false;

        string path = folder;
        string[] parts = relative.Split('/');
        for (int i = 0; i < parts.Length; i++)
            path = Path.Combine(path, parts[i]);

        string full = Path.GetFullPath(path);
        string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!File.Exists(full))
            return false;

        try
        {
            text = File.ReadAllText(full);
            return true;
        }
        catch (Exception exception)
        {
            error = "\"" + relative + "\" could not be read (" + exception.Message + ")";
            return true;
        }
    }

    static bool TryRemRelative(string file, out string relative)
    {
        relative = null;
        if (string.IsNullOrWhiteSpace(file))
            return false;

        file = file.Trim().Replace('\\', '/');
        if (file.Length == 0 || file[0] == '/' || file.IndexOf(':') >= 0)
            return false;

        string[] parts = file.Split('/');
        var clean = new List<string>();
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (part.Length == 0 || part == ".")
                continue;
            if (part == "..")
                return false;
            clean.Add(part);
        }

        if (clean.Count == 0)
            return false;

        int last = clean.Count - 1;
        if (!clean[last].EndsWith(".rem", StringComparison.OrdinalIgnoreCase))
            clean[last] = clean[last] + ".rem";

        relative = string.Join("/", clean.ToArray());
        return true;
    }

    static string RemResourcePath(string folder, string relative)
    {
        string without = relative;
        if (without.EndsWith(".rem", StringComparison.OrdinalIgnoreCase))
            without = without.Substring(0, without.Length - 4);
        return folder + "/" + without;
    }

    static bool ReadConfigFile(string path, out string json)
    {
        try
        {
            json = File.ReadAllText(path);
            return true;
        }
        catch (Exception exception)
        {
            json = null;
            Debug.LogWarning("Remora config.json was not applied: " + exception.Message);
            return false;
        }
    }

    static string DirectoryOf(string file)
    {
        if (string.IsNullOrEmpty(file))
            return null;

        return Path.GetDirectoryName(file);
    }

    static string ConfigBesideBinary()
    {
        return ConfigFile(FindRemoraDirectory(GetBinaryDirectory()));
    }

    static string ConfigInStreamingAssets()
    {
        string root = Application.streamingAssetsPath;
        if (string.IsNullOrEmpty(root))
            return null;

        return ConfigFile(FindRemoraDirectory(root));
    }

    static string ConfigInSource()
    {
        string binaryDirectory = GetBinaryDirectory();
        string sourceRoot = GetSourceRoot(binaryDirectory);
        if (string.IsNullOrEmpty(sourceRoot))
            return null;

        if (binaryDirectory != null && SamePath(sourceRoot, binaryDirectory))
            return null;

        return ConfigFile(FindRemoraDirectory(sourceRoot));
    }

    static string ConfigFile(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return null;

        foreach (string file in Directory.EnumerateFiles(directory))
        {
            if (string.Equals(Path.GetFileName(file), "config.json", StringComparison.Ordinal))
                return file;
        }

        return null;
    }

    private static string GetBinaryDirectory()
    {
#if UNITY_EDITOR
        return null;
#elif UNITY_STANDALONE_OSX
        return Path.Combine(Application.dataPath, "MacOS");
#elif UNITY_STANDALONE
        return Path.GetDirectoryName(Application.dataPath);
#else
        return null;
#endif
    }

    private static string GetSourceRoot(string binaryDirectory)
    {
#if UNITY_EDITOR
        return Path.GetDirectoryName(Application.dataPath);
#else
        string current = binaryDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (ChildDirectoryExact(current, "Assets") != null && ChildDirectoryExact(current, "ProjectSettings") != null)
                return current;

            DirectoryInfo parent = Directory.GetParent(current);
            if (parent == null || SamePath(parent.FullName, current))
                break;

            current = parent.FullName;
        }

        return null;
#endif
    }

    private static string FindRemoraDirectory(string parent)
    {
        string lower = ChildDirectoryExact(parent, "remora");
        if (lower != null)
            return lower;

        return ChildDirectoryExact(parent, "Remora");
    }

    private static string ChildDirectoryExact(string parent, string name)
    {
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            return null;

        foreach (string directory in Directory.EnumerateDirectories(parent))
        {
            if (string.Equals(Path.GetFileName(directory), name, System.StringComparison.Ordinal))
                return directory;
        }

        return null;
    }

    private static bool SamePath(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            System.StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyPanel()
    {
        float anchorX = dockRight ? 1f : 0f;
        float anchorY = dockTop ? 1f : 0f;
        var corner = new Vector2(anchorX, anchorY);
        panelRect.anchorMin = corner;
        panelRect.anchorMax = corner;
        panelRect.pivot = corner;
        panelRect.anchoredPosition = new Vector2(dockRight ? -Setting<float>("Margin") : Setting<float>("Margin"), dockTop ? -Setting<float>("Margin") : Setting<float>("Margin"));
        panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);
    }

    private static bool PressedDisplayName(string name)
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard == null)
            return false;

        var keys = keyboard.allKeys;
        for (var i = 0; i < keys.Count; i++)
        {
            if (keys[i].wasPressedThisFrame && keys[i].displayName == name)
                return true;
        }
#endif
        return false;
    }

    private bool IsPinnedToBottom()
    {
        if (scrollRect.viewport == null)
            return true;

        if (contentRect.rect.height <= scrollRect.viewport.rect.height + 1f)
            return true;

        return scrollRect.verticalNormalizedPosition <= 0.02f;
    }

    private void EnsureEventSystem()
    {
        var systems = FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        EventSystem sceneSystem = null;
        foreach (var system in systems)
        {
            if (ownedEventSystem != null && system.gameObject == ownedEventSystem)
                continue;

            sceneSystem = system;
            break;
        }

        if (sceneSystem != null)
        {
            if (ownedEventSystem != null)
            {
                Destroy(ownedEventSystem);
                ownedEventSystem = null;
            }

            EnsureInputModule(sceneSystem.gameObject);
            return;
        }

        if (ownedEventSystem != null)
        {
            EnsureInputModule(ownedEventSystem);
            return;
        }

        ownedEventSystem = new GameObject("EventSystem");
        ownedEventSystem.AddComponent<EventSystem>();
        EnsureInputModule(ownedEventSystem);
        DontDestroyOnLoad(ownedEventSystem);
    }

    private static void EnsureInputModule(GameObject eventSystemObject)
    {
#if ENABLE_INPUT_SYSTEM
        if (eventSystemObject.GetComponent<InputSystemUIInputModule>() == null)
            eventSystemObject.AddComponent<InputSystemUIInputModule>();

#if !ENABLE_LEGACY_INPUT_MANAGER
        var legacy = eventSystemObject.GetComponent<StandaloneInputModule>();
        if (legacy != null)
            legacy.enabled = false;
#endif
#else
        if (eventSystemObject.GetComponent<StandaloneInputModule>() == null)
            eventSystemObject.AddComponent<StandaloneInputModule>();
#endif
    }

    private void Build()
    {
        var canvasRect = CreateRect("Canvas", transform);
        canvasObject = canvasRect.gameObject;
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;

        canvasScaler = canvasRect.gameObject.AddComponent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = new Vector2(Setting<float>("ReferenceWidth"), Setting<float>("ReferenceHeight"));
        canvasScaler.matchWidthOrHeight = Setting<float>("ScaleMatch");
        canvasRect.gameObject.AddComponent<GraphicRaycaster>();

        panelRect = CreateRect("Panel", canvasRect);
        ApplyPanel();
        var panel = panelRect;

        var panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.sprite = WhiteSprite();
        panelImage.color = new Color(0.05f, 0.06f, 0.08f, 0.9f);

        var scroll = CreateRect("Log", panel);
        logView = scroll;
        Stretch(scroll, Setting<float>("Padding"), Setting<float>("Padding") + Setting<float>("InputHeight") + 6f, Setting<float>("Padding"), Setting<float>("Padding"));

        var viewport = CreateRect("Viewport", scroll);
        viewportRect = viewport;
        Stretch(viewport, 0f, 0f, Setting<float>("ScrollbarWidth"), 0f);
        var viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.sprite = WhiteSprite();
        viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
        viewport.gameObject.AddComponent<RectMask2D>();

        contentRect = CreateRect("Content", viewport);
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0f, 0f);

        var layout = contentRect.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(6, 6, 4, 4);
        layout.childAlignment = TextAnchor.LowerLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        logText = (AnsiText)CreateText(contentRect, "Text", string.Empty, new Color(0.9f, 0.92f, 0.95f, 1f), TextAnchor.UpperLeft, true, false);
        logText.raycastTarget = true;
        logText.gameObject.AddComponent<LogLinks>().text = logText;
        logLayout = logText.gameObject.AddComponent<LayoutElement>();
        logLayout.layoutPriority = 1;

        scrollRect = scroll.gameObject.AddComponent<ScrollRect>();
        scrollRect.viewport = viewport;
        scrollRect.content = contentRect;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 24f;
        scrollRect.verticalScrollbar = CreateScrollbar(scroll);
        scrollbarRect = scrollRect.verticalScrollbar.GetComponent<RectTransform>();
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        scrollRect.onValueChanged.AddListener(OnScroll);

        inputField = CreateInput(panel);
        SetConsoleHidden(RemoraDevConfig.Get<bool>("DevConsole.Hidden"), false);
    }

    private InputField CreateInput(RectTransform parent)
    {
        var rect = CreateRect("Input", parent);
        inputRect = rect;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(-(Setting<float>("Padding") * 2f), Setting<float>("InputHeight"));
        rect.anchoredPosition = new Vector2(0f, Setting<float>("Padding"));

        var idle = new Color(0.12f, 0.14f, 0.18f, 1f);
        var focused = new Color(0.16f, 0.24f, 0.34f, 1f);
        var accentIdle = new Color(1f, 1f, 1f, 0.12f);
        var accentFocused = new Color(0.55f, 0.82f, 1f, 1f);

        var background = rect.gameObject.AddComponent<Image>();
        background.sprite = WhiteSprite();
        background.color = idle;

        var accent = CreateRect("Accent", rect);
        accent.anchorMin = new Vector2(0f, 0f);
        accent.anchorMax = new Vector2(0f, 1f);
        accent.pivot = new Vector2(0f, 0.5f);
        accent.sizeDelta = new Vector2(2f, 0f);
        accent.anchoredPosition = Vector2.zero;
        var accentImage = accent.gameObject.AddComponent<Image>();
        accentImage.sprite = WhiteSprite();
        accentImage.color = accentIdle;
        accentImage.raycastTarget = false;

        var chrome = rect.gameObject.AddComponent<InputChrome>();
        chrome.background = background;
        chrome.accent = accentImage;
        chrome.backgroundIdle = idle;
        chrome.backgroundFocused = focused;
        chrome.accentIdle = accentIdle;
        chrome.accentFocused = accentFocused;

        inputText = (AnsiText)CreateText(rect, "Text", string.Empty, Color.white, TextAnchor.MiddleLeft, true, true);
        Stretch(inputText.rectTransform, 10f, 4f, 8f, 4f);

        placeholderText = CreateText(rect, "Placeholder", "type a line", new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleLeft, false, false);
        placeholderText.fontStyle = FontStyle.Italic;
        Stretch(placeholderText.rectTransform, 10f, 4f, 8f, 4f);

        var field = rect.gameObject.AddComponent<ConsoleField>();
        field.console = this;
        field.textComponent = inputText;
        field.placeholder = placeholderText;
        field.onValidateInput = ValidateInput;
        field.lineType = InputField.LineType.SingleLine;
        field.customCaretColor = true;
        field.caretColor = Color.white;
        field.onSubmit.AddListener(OnSubmit);

        var navigation = field.navigation;
        navigation.mode = Navigation.Mode.None;
        field.navigation = navigation;
        return field;
    }

    private Scrollbar CreateScrollbar(RectTransform parent)
    {
        var bar = CreateRect("Scrollbar", parent);
        bar.anchorMin = new Vector2(1f, 0f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(1f, 0.5f);
        bar.offsetMin = new Vector2(-Setting<float>("ScrollbarWidth"), 0f);
        bar.offsetMax = new Vector2(0f, 0f);

        var background = bar.gameObject.AddComponent<Image>();
        background.sprite = WhiteSprite();
        background.color = new Color(1f, 1f, 1f, 0.08f);

        var sliding = CreateRect("Sliding Area", bar);
        Stretch(sliding, 0f, 0f, 0f, 0f);

        var handle = CreateRect("Handle", sliding);
        Stretch(handle, 1f, 1f, 1f, 1f);
        var handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.sprite = WhiteSprite();
        handleImage.color = new Color(1f, 1f, 1f, 0.45f);

        var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = handleImage;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        return scrollbar;
    }

    private Text CreateText(RectTransform parent, string name, string content, Color color, TextAnchor alignment, bool ansi, bool collapseCaret)
    {
        var rect = CreateRect(name, parent);
        Text text = ansi ? rect.gameObject.AddComponent<AnsiText>() : rect.gameObject.AddComponent<Text>();
        var ansiText = text as AnsiText;
        if (ansiText != null)
            ansiText.collapseCaret = collapseCaret;

        text.font = BuiltinFont();
        text.fontSize = Setting<int>("FontSize");
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.supportRichText = false;
        text.raycastTarget = false;
        text.text = content;
        return text;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        return rect;
    }

    private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static Sprite WhiteSprite()
    {
        if (whiteSprite != null)
            return whiteSprite;

        var texture = Texture2D.whiteTexture;
        whiteSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f);
        return whiteSprite;
    }

    private static Font BuiltinFont()
    {
        if (builtinFont == null)
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return builtinFont;
    }

    private sealed class LogLinks : MonoBehaviour, IPointerClickHandler
    {
        public AnsiText text;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (text != null)
                text.OpenLink(eventData);
        }
    }

    private sealed class AnsiText : Text
    {
        public bool collapseCaret;
        readonly List<RemoraAnsi.Style> styles = new List<RemoraAnsi.Style>();
        readonly List<RemoraAnsi.Style> visibleStyles = new List<RemoraAnsi.Style>();
        readonly List<bool> hidden = new List<bool>();
        readonly List<UIVertex> vertCopy = new List<UIVertex>();
        readonly TextGenerator visual = new TextGenerator();
        readonly List<LinkSpan> links = new List<LinkSpan>();
        string linkPlain = "";

        struct LinkSpan
        {
            public int Start;
            public int End;
            public string Url;
        }

        protected override void OnPopulateMesh(VertexHelper toFill)
        {
            if (font == null)
            {
                toFill.Clear();
                return;
            }

            m_DisableFontTextureRebuiltCallback = true;
            Vector2 solidUv;
            bool hasSolid = TrySolidUv(out solidUv);
            string source = text ?? "";
            var settings = GetGenerationSettings(rectTransform.rect.size);
            settings.generateOutOfBounds = true;
            settings.richText = false;

            TextGenerator drawn;
            string populated;
            List<RemoraAnsi.Style> drawnStyles;
            if (collapseCaret)
            {
                string collapsed = RemoraAnsi.Collapse(source, styles, hidden);
                var plain = new StringBuilder();
                visibleStyles.Clear();
                for (int i = 0; i < source.Length && i < hidden.Count; i++)
                {
                    if (hidden[i])
                        continue;

                    plain.Append(source[i]);
                    visibleStyles.Add(styles[i]);
                }

                populated = plain.ToString();
                drawnStyles = visibleStyles;
                visual.PopulateWithErrors(populated, settings, gameObject);
                cachedTextGenerator.PopulateWithErrors(collapsed, settings, gameObject);
                CopyCaret(collapsed, populated);
                drawn = visual;
                links.Clear();
                linkPlain = "";
            }
            else
            {
                RemoraAnsi.FillPlain(source, styles, out populated);
                cachedTextGenerator.PopulateWithErrors(populated, settings, gameObject);
                drawn = cachedTextGenerator;
                drawnStyles = styles;
                linkPlain = populated;
                CollectLinks(populated);
            }

            IList<UIVertex> liveVerts = drawn.verts;
            IList<UICharInfo> chars = drawn.characters;
            toFill.Clear();
            if (liveVerts == null || liveVerts.Count < 4 || chars == null || chars.Count == 0)
            {
                m_DisableFontTextureRebuiltCallback = false;
                return;
            }

            vertCopy.Clear();
            for (int v = 0; v < liveVerts.Count; v++)
                vertCopy.Add(liveVerts[v]);

            float units = 1f / Mathf.Max(0.01f, pixelsPerUnit);
            Vector2 rounding = new Vector2(vertCopy[0].position.x, vertCopy[0].position.y) * units;
            rounding = PixelAdjustPoint(rounding) - rounding;
            Paint(toFill, drawn, populated, drawnStyles, vertCopy, chars, units, rounding, hasSolid, solidUv, false);
            Paint(toFill, drawn, populated, drawnStyles, vertCopy, chars, units, rounding, hasSolid, solidUv, true);
            m_DisableFontTextureRebuiltCallback = false;
        }

        internal bool OpenLink(PointerEventData eventData)
        {
            if (collapseCaret || eventData == null || eventData.button != PointerEventData.InputButton.Left || eventData.dragging)
                return false;
            if (!RemoraDevConfig.Get<bool>("DevConsole.Config.ClickableLinks") || links.Count == 0)
                return false;

            Vector2 local;
            Camera camera = eventData.pressEventCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position, camera, out local))
                return false;

            int index = CharacterAt(local);
            if (index < 0)
                return false;
            for (int i = 0; i < links.Count; i++)
            {
                if (index < links[i].Start || index >= links[i].End)
                    continue;
                Application.OpenURL(links[i].Url);
                return true;
            }

            return false;
        }

        void CollectLinks(string plain)
        {
            links.Clear();
            if (!RemoraDevConfig.Get<bool>("DevConsole.Config.ClickableLinks") || string.IsNullOrEmpty(plain))
                return;

            for (int i = 0; i < plain.Length; i++)
            {
                int scheme;
                if (!UrlScheme(plain, i, out scheme))
                    continue;
                int end = scheme;
                while (end < plain.Length && !char.IsWhiteSpace(plain[end]))
                    end++;
                if (end <= scheme)
                    continue;
                links.Add(new LinkSpan { Start = i, End = end, Url = plain.Substring(i, end - i) });
                i = end - 1;
            }
        }

        static bool UrlScheme(string text, int index, out int schemeEnd)
        {
            schemeEnd = index;
            if (index + 8 <= text.Length && string.Compare(text, index, "https://", 0, 8, StringComparison.OrdinalIgnoreCase) == 0)
            {
                schemeEnd = index + 8;
                return true;
            }

            if (index + 7 <= text.Length && string.Compare(text, index, "http://", 0, 7, StringComparison.OrdinalIgnoreCase) == 0)
            {
                schemeEnd = index + 7;
                return true;
            }

            return false;
        }

        bool IsLink(int index)
        {
            if (links.Count == 0)
                return false;
            for (int i = 0; i < links.Count; i++)
            {
                if (index >= links[i].Start && index < links[i].End)
                    return true;
            }

            return false;
        }

        int CharacterAt(Vector2 local)
        {
            var chars = cachedTextGenerator.characters;
            var rows = cachedTextGenerator.lines;
            if (chars == null || rows == null || rows.Count == 0)
                return -1;

            float units = 1f / Mathf.Max(0.01f, pixelsPerUnit);
            int count = Mathf.Min(linkPlain.Length, chars.Count);
            for (int i = 0; i < count; i++)
            {
                char current = linkPlain[i];
                if (current == '\n' || current == '\r' || chars[i].charWidth <= 0f)
                    continue;

                int line = LineOf(cachedTextGenerator, i);
                if (line < 0 || line >= rows.Count)
                    continue;
                UILineInfo row = rows[line];
                float x0 = chars[i].cursorPos.x * units;
                float x1 = x0 + chars[i].charWidth * units;
                float y1 = row.topY * units;
                float y0 = y1 - row.height * units;
                if (local.x >= x0 && local.x <= x1 && local.y >= y0 && local.y <= y1)
                    return i;
            }

            return -1;
        }

        void CopyCaret(string collapsed, string plain)
        {
            var rawChars = cachedTextGenerator.characters;
            var plainChars = visual.characters;
            if (rawChars == null || plainChars == null)
                return;

            int plainIndex = 0;
            float endX = 0f;
            int rawCount = Mathf.Min(rawChars.Count, collapsed.Length);
            for (int i = 0; i < rawCount; i++)
            {
                if (i < hidden.Count && hidden[i])
                    continue;
                if (plainIndex >= plainChars.Count || plainIndex >= plain.Length)
                    break;

                UICharInfo info = rawChars[i];
                UICharInfo visible = plainChars[plainIndex];
                info.cursorPos = visible.cursorPos;
                info.charWidth = visible.charWidth;
                rawChars[i] = info;
                endX = visible.cursorPos.x + visible.charWidth;
                plainIndex++;
            }

            if (plainChars.Count > plain.Length)
                endX = plainChars[plain.Length].cursorPos.x;

            float nextX = endX;
            for (int i = rawCount - 1; i >= 0; i--)
            {
                UICharInfo info = rawChars[i];
                if (i < hidden.Count && hidden[i])
                {
                    info.cursorPos.x = nextX;
                    info.charWidth = 0f;
                    rawChars[i] = info;
                }
                else
                {
                    nextX = info.cursorPos.x;
                }
            }

            if (rawChars.Count > collapsed.Length)
            {
                UICharInfo end = rawChars[collapsed.Length];
                end.cursorPos.x = endX;
                end.charWidth = 0f;
                rawChars[collapsed.Length] = end;
            }
        }

        void Paint(VertexHelper toFill, TextGenerator generator, string populated, List<RemoraAnsi.Style> drawnStyles, IList<UIVertex> verts, IList<UICharInfo> chars, float units, Vector2 rounding, bool hasSolid, Vector2 solidUv, bool glyphs)
        {
            // Unity's text generator emits quads for visible glyphs only. Spaces and
            // newlines have cursor info and a width, but no vertices. Advancing the
            // vertex cursor for them paints each later glyph with an earlier style.
            int vert = 0;
            int count = Mathf.Min(populated.Length, chars.Count);
            for (int i = 0; i < count; i++)
            {
                char current = populated[i];
                if (current == '\n' || current == '\r')
                    continue;

                bool hasQuad = !char.IsWhiteSpace(current);
                if (hasQuad && vert + 3 >= verts.Count)
                    break;

                var style = i < drawnStyles.Count ? drawnStyles[i] : default(RemoraAnsi.Style);
                if (IsLink(i))
                {
                    style.HasFg = true;
                    style.Fg = new Color32(80, 160, 255, 255);
                    style.Underline = true;
                }
                int line = LineOf(generator, i);
                if (!glyphs)
                {
                    if (hasSolid && style.HasBg && chars[i].charWidth > 0f)
                        AddBar(toFill, generator, chars, i, line, units, rounding, solidUv, style.Bg, 0f, 1f);
                }
                else if (hasQuad)
                {
                    AddGlyph(toFill, generator, verts, vert, style, line, units, rounding);
                    if (hasSolid && chars[i].charWidth > 0f && (style.Underline || style.Strike))
                    {
                        Color32 ink = style.HasFg ? style.Fg : (Color32)color;
                        if (style.Underline)
                            AddBar(toFill, generator, chars, i, line, units, rounding, solidUv, ink, 0.08f, 0.16f);
                        if (style.Strike)
                            AddBar(toFill, generator, chars, i, line, units, rounding, solidUv, ink, 0.46f, 0.54f);
                    }
                }
                else if (hasSolid && chars[i].charWidth > 0f && (style.Underline || style.Strike))
                {
                    Color32 ink = style.HasFg ? style.Fg : (Color32)color;
                    if (style.Underline)
                        AddBar(toFill, generator, chars, i, line, units, rounding, solidUv, ink, 0.08f, 0.16f);
                    if (style.Strike)
                        AddBar(toFill, generator, chars, i, line, units, rounding, solidUv, ink, 0.46f, 0.54f);
                }

                if (hasQuad)
                    vert += 4;
            }
        }

        void AddGlyph(VertexHelper toFill, TextGenerator generator, IList<UIVertex> verts, int vert, RemoraAnsi.Style style, int line, float units, Vector2 rounding)
        {
            var quad = new UIVertex[4];
            float left = float.MaxValue;
            float bottom = float.MaxValue;
            for (int k = 0; k < 4; k++)
            {
                UIVertex vertex = verts[vert + k];
                vertex.position *= units;
                vertex.position.x += rounding.x;
                vertex.position.y += rounding.y;
                if (style.HasFg)
                    vertex.color = style.Fg;
                if (vertex.position.x < left)
                    left = vertex.position.x;
                if (vertex.position.y < bottom)
                    bottom = vertex.position.y;
                quad[k] = vertex;
            }

            // Superscript and subscript stay inside the line cell. 73 raises, 74 lowers, 75 clears both.
            if ((style.Script == 1 || style.Script == 2) && line < generator.lines.Count)
            {
                UILineInfo info = generator.lines[line];
                float top = info.topY * units + rounding.y;
                float low = top - info.height * units;
                float anchorY = style.Script == 1 ? top : low;
                const float scale = 0.62f;
                for (int k = 0; k < 4; k++)
                {
                    UIVertex vertex = quad[k];
                    vertex.position.x = left + (vertex.position.x - left) * scale;
                    vertex.position.y = anchorY + (vertex.position.y - anchorY) * scale;
                    quad[k] = vertex;
                }
            }

            if (style.Italic)
            {
                bottom = float.MaxValue;
                for (int k = 0; k < 4; k++)
                    if (quad[k].position.y < bottom)
                        bottom = quad[k].position.y;
                for (int k = 0; k < 4; k++)
                {
                    UIVertex vertex = quad[k];
                    vertex.position.x += (vertex.position.y - bottom) * 0.22f;
                    quad[k] = vertex;
                }
            }

            toFill.AddUIVertexQuad(quad);
            if (style.Bold)
            {
                for (int k = 0; k < 4; k++)
                {
                    UIVertex vertex = quad[k];
                    vertex.position.x += 1f;
                    quad[k] = vertex;
                }

                toFill.AddUIVertexQuad(quad);
            }
        }

        void AddBar(VertexHelper toFill, TextGenerator generator, IList<UICharInfo> chars, int index, int line, float units, Vector2 rounding, Vector2 uv, Color32 ink, float from, float to)
        {
            if (line >= generator.lines.Count)
                return;

            UICharInfo info = chars[index];
            UILineInfo row = generator.lines[line];
            float x0 = info.cursorPos.x * units + rounding.x;
            float x1 = x0 + info.charWidth * units;
            float y1 = row.topY * units + rounding.y;
            float y0 = y1 - row.height * units;
            float low = Mathf.Lerp(y0, y1, from);
            float high = Mathf.Lerp(y0, y1, to);
            var quad = new UIVertex[4];
            for (int k = 0; k < 4; k++)
            {
                quad[k] = UIVertex.simpleVert;
                quad[k].uv0 = uv;
                quad[k].color = ink;
            }

            quad[0].position = new Vector3(x0, low, 0f);
            quad[1].position = new Vector3(x0, high, 0f);
            quad[2].position = new Vector3(x1, high, 0f);
            quad[3].position = new Vector3(x1, low, 0f);
            toFill.AddUIVertexQuad(quad);
        }

        static int LineOf(TextGenerator generator, int charIndex)
        {
            var rows = generator.lines;
            int line = 0;
            for (int n = 0; n < rows.Count - 1; n++)
            {
                if (rows[n + 1].startCharIdx > charIndex)
                    break;
                line = n + 1;
            }

            return line;
        }

        bool TrySolidUv(out Vector2 uv)
        {
            uv = Vector2.zero;
            font.RequestCharactersInTexture("\u2588_", fontSize, FontStyle.Normal);
            CharacterInfo info;
            if (!font.GetCharacterInfo('\u2588', out info, fontSize, FontStyle.Normal) && !font.GetCharacterInfo('_', out info, fontSize, FontStyle.Normal))
                return false;

            uv = (info.uvBottomLeft + info.uvTopRight) * 0.5f;
            return true;
        }
    }

    private sealed class ConsoleField : InputField
    {
        public RemoraDevConsole console;
        private readonly Event inputEvent = new Event();

        public override void OnUpdateSelected(BaseEventData eventData)
        {
            if (!isFocused)
                return;

            bool consumedEvent = false;
            while (Event.PopEvent(inputEvent))
            {
                if (inputEvent.rawType == EventType.KeyDown)
                {
                    consumedEvent = true;
                    if (console != null && console.HandleChord(inputEvent))
                        continue;

                    if (HandleHistory(inputEvent))
                        continue;

                    if (HandleAnsiEdit(inputEvent))
                        continue;

                    if (KeyPressed(inputEvent) == EditState.Finish)
                    {
                        if (!wasCanceled)
                            SendOnSubmit();

                        DeactivateInputField();
                        continue;
                    }

                    UpdateLabel();
                }

                if (inputEvent.type == EventType.ExecuteCommand && inputEvent.commandName == "SelectAll")
                {
                    SelectAll();
                    consumedEvent = true;
                }
            }

            if (consumedEvent)
                UpdateLabel();

            eventData.Use();
        }

        bool HandleHistory(Event inputEvent)
        {
            if (inputEvent.keyCode != KeyCode.UpArrow && inputEvent.keyCode != KeyCode.DownArrow)
                return false;

            bool ctrl = (inputEvent.modifiers & EventModifiers.Control) != 0;
            bool shift = (inputEvent.modifiers & EventModifiers.Shift) != 0;
            if (ctrl || shift || console == null)
                return false;

            return console.RecallTyped(inputEvent.keyCode == KeyCode.UpArrow ? -1 : 1);
        }

        bool HandleAnsiEdit(Event inputEvent)
        {
            bool ctrl = (inputEvent.modifiers & EventModifiers.Control) != 0;
            bool alt = (inputEvent.modifiers & EventModifiers.Alt) != 0;
            if (ctrl || alt)
                return false;

            bool shift = (inputEvent.modifiers & EventModifiers.Shift) != 0;
            if (inputEvent.keyCode == KeyCode.LeftArrow || inputEvent.keyCode == KeyCode.RightArrow)
            {
                int direction = inputEvent.keyCode == KeyCode.LeftArrow ? -1 : 1;
                int target;
                if (!RemoraAnsi.TrySkip(text, caretPosition, direction, out target))
                    return false;

                if (shift)
                    selectionFocusPosition = target;
                else
                    caretPosition = target;
                UpdateLabel();
                return true;
            }

            if (inputEvent.keyCode != KeyCode.Backspace && inputEvent.keyCode != KeyCode.Delete)
                return false;
            if (caretPosition != selectionAnchorPosition)
                return false;

            int start;
            int end;
            bool backspace = inputEvent.keyCode == KeyCode.Backspace;
            if (!RemoraAnsi.TryDelete(text, caretPosition, backspace, out start, out end) || end <= start)
                return false;

            text = text.Remove(start, end - start);
            caretPosition = start;
            UpdateLabel();
            return true;
        }
    }

    private sealed class InputChrome : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        public Image background;
        public Image accent;
        public Color backgroundIdle;
        public Color backgroundFocused;
        public Color accentIdle;
        public Color accentFocused;

        public void OnSelect(BaseEventData eventData)
        {
            background.color = backgroundFocused;
            accent.color = accentFocused;
        }

        public void OnDeselect(BaseEventData eventData)
        {
            background.color = backgroundIdle;
            accent.color = accentIdle;
        }
    }
}

#if UNITY_EDITOR
[ScriptedImporter(1, "rem")]
public class RemoraRemImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext context)
    {
        var asset = new TextAsset(File.ReadAllText(context.assetPath));
        context.AddObjectToAsset("text", asset);
        context.SetMainObject(asset);
    }
}
#endif
