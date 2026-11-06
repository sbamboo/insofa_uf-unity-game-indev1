using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// JSON for one Unity object, a list of them, or a sparse diff.
// Keys are dot paths: "Weapon.(Transform).localPosition".
// "@GameObject" and "@<res:Enemies/Orc>" create an object; the value is its props.
// "(Rigidbody)" selects a component. "Minions[0]" selects one match.
// Reference values are strings: "<scene:Player.Weapon>", "<res:Models/Sword>", "<Weapon.(Transform)>", "<#Boss>".
//
// EvaluateSelector finds what a selector names. One match returns that object.
// Several matches return object[] when multi is true, and the first match when multi is false.
// ApplyDiff writes only the keys present in the document onto a live target.
// Serialize writes one object, component, or list.
// Deserialize builds an object and returns it hidden and inactive. To place a GameObject:
//     foreach (Transform item in go.GetComponentsInChildren<Transform>(true))
//         item.gameObject.hideFlags = HideFlags.None;
//     go.SetActive(true);

public static class UNOR
{
    public static object EvaluateSelector(string selector, bool multi = true)
    {
        if (string.IsNullOrWhiteSpace(selector))
            return null;

        List<object> matches = ResolveSelector(selector.Trim(), null, false);
        return PackMatches(matches, multi);
    }

    public static void ApplyDiff(object target, string diffData, bool removeUnmentioned = false)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (diffData == null)
            throw new ArgumentNullException(nameof(diffData));

        if (target is string selector)
        {
            object resolved = EvaluateSelector(selector, true);
            if (resolved != null)
                ApplyDiff(resolved, diffData, removeUnmentioned);
            return;
        }

        if (target is IList list && !(target is JsonMap))
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null)
                    ApplyDiff(list[i], diffData, removeUnmentioned);
            }
            return;
        }

        string json = NormalizeDocument(diffData);
        JsonMap map = ParseDocument(json);
        bool outer = !trackFresh;
        if (outer)
            freshIds.Clear();
        trackFresh = true;
        try
        {
            using (Scope.Push(ScopeRootOf(target), false))
            {
                ApplyMap(target, map, Mode.Diff);
                Scope.Flush();
            }
            if (removeUnmentioned)
                PruneTo(target, MentionOf(map));
        }
            finally
            {
                if (outer)
                {
                    trackFresh = false;
                    freshIds.Clear();
                }
            }
    }

    public static string Serialize(object value)
    {
        var builder = new StringBuilder();
        var stack = new HashSet<object>(ReferenceComparer.Instance);
        if (value is GameObject go)
            WriteGameObject(builder, go, go, 0, stack);
        else if (value is Component component)
            WriteComponent(builder, component, component.gameObject, 0, stack);
        else
            WriteValue(builder, value, ScopeRootOf(value), 0, true, stack);
        return builder.ToString();
    }

    public static object Deserialize(string data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        object parsed = JsonParser.Parse(data);
        using (Scope.Push(null, true))
        {
            object created = BuildValue(parsed);
            Scope.Flush();
            HideOwned();
            return created;
        }
    }

    enum Mode
    {
        Diff,
        Build
    }

    struct Segment
    {
        public string Name;
        public int? Index;
        public bool Component;
        public bool Event;
        public bool Instantiate;
    }

    sealed class JsonMap
    {
        public readonly List<string> Keys = new List<string>();
        public readonly List<object> Values = new List<object>();

        public int Count
        {
            get { return Keys.Count; }
        }

        public void Add(string key, object value)
        {
            Keys.Add(key);
            Values.Add(value);
        }
    }

    sealed class Pending
    {
        public Action Apply;
    }

    sealed class Scope : IDisposable
    {
        static readonly Stack<Scope> stack = new Stack<Scope>();
        public static GameObject Root;
        public static bool Hide;
        public static List<Pending> PendingRefs = new List<Pending>();
        public static HashSet<GameObject> Owned = new HashSet<GameObject>();

        readonly GameObject previousRoot;
        readonly bool previousHide;
        readonly List<Pending> previousPending;
        readonly HashSet<GameObject> previousOwned;

        Scope(GameObject previousRoot, bool previousHide, List<Pending> previousPending, HashSet<GameObject> previousOwned)
        {
            this.previousRoot = previousRoot;
            this.previousHide = previousHide;
            this.previousPending = previousPending;
            this.previousOwned = previousOwned;
        }

        public static Scope Push(GameObject root, bool hide)
        {
            var scope = new Scope(Root, Hide, PendingRefs, Owned);
            stack.Push(scope);
            Root = root;
            Hide = hide;
            PendingRefs = new List<Pending>();
            Owned = new HashSet<GameObject>();
            return scope;
        }

        public static void Own(GameObject go)
        {
            if (Hide && go != null)
                Owned.Add(go);
        }

        public static void Flush()
        {
            List<Pending> batch = PendingRefs;
            PendingRefs = new List<Pending>();
            for (int i = 0; i < batch.Count; i++)
                batch[i].Apply();
        }

        public void Dispose()
        {
            if (stack.Count == 0 || stack.Peek() != this)
                return;
            stack.Pop();
            Root = previousRoot;
            Hide = previousHide;
            PendingRefs = previousPending;
            Owned = previousOwned;
        }
    }

    sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new ReferenceComparer();

        public new bool Equals(object x, object y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(object obj)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }

    static readonly Dictionary<string, Type> typeCache = new Dictionary<string, Type>();
    static readonly string[] ReservedGameObjectKeys = { "name", "tag", "layer", "active" };
    static readonly HashSet<int> freshIds = new HashSet<int>();
    static bool trackFresh;

    static JsonMap ParseDocument(string data)
    {
        object parsed = JsonParser.Parse(data);
        JsonMap map = parsed as JsonMap;
        if (map == null)
            throw new ArgumentException("U-NOR diff must be a JSON object.");
        return map;
    }

    static object BuildValue(object parsed)
    {
        if (parsed is JsonMap map)
            return BuildObject(map);
        if (parsed is List<object> list)
        {
            object[] built = new object[list.Count];
            for (int i = 0; i < list.Count; i++)
                built[i] = BuildValue(list[i]);
            return built;
        }
        if (parsed is string text && LooksLikeSelector(text))
        {
            bool instantiate = text[0] == '@';
            List<object> matches = ResolveSelector(text, Scope.Root, instantiate);
            return PackMatches(matches, true);
        }
        return parsed;
    }

    static object BuildObject(JsonMap map)
    {
        bool onlyCreates = map.Count > 0;
        for (int i = 0; i < map.Count; i++)
        {
            if (!ParseSegment(map.Keys[i]).Instantiate)
                onlyCreates = false;
        }

        if (onlyCreates)
        {
            var made = new List<object>();
            for (int i = 0; i < map.Count; i++)
                CollectCreated(null, ParseSegment(map.Keys[i]), map.Values[i], made);
            return PackMatches(made, true);
        }

        GameObject carrier = MakeGameObject("GameObject", null);
        if (Scope.Root == null)
            Scope.Root = carrier;
        ApplyMap(carrier, map, Mode.Build);
        return carrier;
    }

    static void HideOwned()
    {
        var roots = new List<GameObject>(Scope.Owned);
        for (int i = 0; i < roots.Count; i++)
        {
            GameObject go = roots[i];
            if (go == null || HasOwnedAncestor(go))
                continue;
            HideTree(go);
            go.SetActive(false);
        }
    }

    static bool HasOwnedAncestor(GameObject go)
    {
        Transform parent = go.transform.parent;
        while (parent != null)
        {
            if (Scope.Owned.Contains(parent.gameObject))
                return true;
            parent = parent.parent;
        }
        return false;
    }

    static void HideTree(GameObject go)
    {
        Transform[] transforms = go.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
            transforms[i].gameObject.hideFlags = HideFlags.HideAndDontSave;
    }

    static GameObject ScopeRootOf(object value)
    {
        GameObject go = value as GameObject;
        if (go != null)
            return go;
        Component component = value as Component;
        if (component != null)
            return component.gameObject;
        return null;
    }

    static void ApplyMap(object target, JsonMap map, Mode mode)
    {
        if (target == null || map == null)
            return;
        for (int i = 0; i < map.Count; i++)
            ApplyKey(target, map.Keys[i], map.Values[i], mode);
    }

    static void ApplyKey(object target, string key, object value, Mode mode)
    {
        List<string> parts = SplitPath(key);
        if (parts.Count == 0)
            return;

        object cursor = target;
        for (int i = 0; i < parts.Count - 1; i++)
        {
            cursor = Enter(cursor, ParseSegment(parts[i]), mode, true);
            if (cursor == null)
                return;
        }

        Segment last = ParseSegment(parts[parts.Count - 1]);
        if (last.Instantiate)
        {
            CreateInstances(cursor, last, value);
            return;
        }

        if (value is JsonMap childMap && CanEnter(cursor, last, mode, true))
        {
            object next = Enter(cursor, last, mode, true);
            if (next != null)
                ApplyMap(next, childMap, mode);
            return;
        }

        List<object> list = value as List<object>;
        if (list != null && CanEnter(cursor, last, mode, true))
        {
            ApplyList(cursor, last, list, mode);
            return;
        }

        object leaf = Enter(cursor, last, mode, false);
        if (last.Component || last.Event)
        {
            if (leaf == null)
                return;
            if (value == null)
                return;
            AssignMembers(leaf, value);
            return;
        }

        AssignMembers(cursor, last.Name, value);
    }

    static bool CanEnter(object cursor, Segment segment, Mode mode, bool structural)
    {
        if (segment.Component || segment.Instantiate)
            return true;
        GameObject go = cursor as GameObject;
        if (go == null)
            return false;
        if (IsReservedGameObjectKey(segment.Name))
            return false;
        if (mode == Mode.Build && structural)
            return true;
        return ChildAt(go, segment.Name, segment.Index ?? 0, false) != null;
    }

    static object Enter(object cursor, Segment segment, Mode mode, bool create)
    {
        if (cursor == null || segment.Instantiate)
            return null;

        if (segment.Component)
        {
            GameObject host = HostOf(cursor);
            if (host == null)
                return null;
            return ComponentAt(host, segment.Name, segment.Index ?? 0, create || mode == Mode.Build);
        }

        if (segment.Event)
            return cursor;

        GameObject go = cursor as GameObject;
        if (go != null && !IsReservedGameObjectKey(segment.Name))
        {
            GameObject child = ChildAt(go, segment.Name, segment.Index ?? 0, create && mode == Mode.Build);
            if (child != null)
                return child;
        }

        return ReadMember(cursor, segment.Name);
    }

    static void ApplyList(object cursor, Segment segment, List<object> list, Mode mode)
    {
        for (int i = 0; i < list.Count; i++)
        {
            Segment indexed = segment;
            indexed.Index = i;
            object item = list[i];
            if (item is JsonMap map)
            {
                object next = Enter(cursor, indexed, mode, true);
                if (next != null)
                    ApplyMap(next, map, mode);
            }
            else if (segment.Component)
            {
                object next = Enter(cursor, indexed, mode, true);
                if (next != null)
                    AssignMembers(next, item);
            }
        }
    }

    static void CreateInstances(object parent, Segment segment, object value)
    {
        var made = new List<object>();
        CollectCreated(parent, segment, value, made);
    }

    static void CollectCreated(object parent, Segment segment, object value, List<object> made)
    {
        List<object> list = value as List<object>;
        if (list != null)
        {
            for (int i = 0; i < list.Count; i++)
                CollectCreated(parent, segment, list[i], made);
            return;
        }

        object created = CreateOne(parent, segment);
        if (created == null)
            return;
        JsonMap props = value as JsonMap;
        if (props != null)
        {
            if (created is GameObject go && Scope.Root == null)
                Scope.Root = go;
            ApplyMap(created, props, Mode.Build);
        }
        made.Add(created);
    }

    static object CreateOne(object parent, Segment segment)
    {
        string name = segment.Name ?? "";
        if (name.StartsWith("<") || name.StartsWith("scene:") || name.StartsWith("res:") || name.StartsWith("asset_bundle:") || name.StartsWith("asset:") || name.StartsWith("#"))
        {
            List<object> sources = ResolveSelector(name, Scope.Root, false);
            object source = sources.Count > 0 ? sources[0] : null;
            GameObject prefab = source as GameObject;
            if (prefab == null && source is Component sourceComponent)
                prefab = sourceComponent.gameObject;
            if (prefab == null)
            {
                Debug.LogWarning("U-NOR could not instantiate '" + name + "'.");
                return null;
            }
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = prefab.name;
            NoteFresh(instance);
            Scope.Own(instance);
            ParentGameObject(instance, parent);
            if (Scope.Hide)
                HideTree(instance);
            return instance;
        }

        if (name == "GameObject" && !segment.Component)
        {
            GameObject created = MakeGameObject("GameObject", HostOf(parent));
            return created;
        }

        Type type = ResolveType(name);
        GameObject host = HostOf(parent);
        if (type == null || host == null || !typeof(Component).IsAssignableFrom(type))
        {
            Debug.LogWarning("U-NOR could not add component '" + name + "'.");
            return null;
        }
        return host.AddComponent(type);
    }

    static void ParentGameObject(GameObject child, object parent)
    {
        GameObject host = HostOf(parent);
        if (host != null)
            child.transform.SetParent(host.transform, false);
    }

    static GameObject MakeGameObject(string name, GameObject parent)
    {
        GameObject go = new GameObject(string.IsNullOrEmpty(name) ? "GameObject" : name);
        NoteFresh(go);
        Scope.Own(go);
        if (parent != null)
            go.transform.SetParent(parent.transform, false);
        if (Scope.Hide)
        {
            go.hideFlags = HideFlags.HideAndDontSave;
            go.SetActive(false);
        }
        return go;
    }

    static GameObject HostOf(object value)
    {
        GameObject go = value as GameObject;
        if (go != null)
            return go;
        Component component = value as Component;
        if (component != null)
            return component.gameObject;
        return null;
    }

    static GameObject ChildAt(GameObject go, string name, int index, bool create)
    {
        var matches = new List<GameObject>();
        Transform transform = go.transform;
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child.name == name)
                matches.Add(child.gameObject);
        }
        if (index < 0)
            index = matches.Count + index;
        if (index >= 0 && index < matches.Count)
            return matches[index];
        if (!create)
            return null;
        GameObject created = null;
        int next = matches.Count;
        if (index < 0)
            index = 0;
        while (next <= index)
        {
            created = MakeGameObject(name, go);
            next++;
        }
        return created;
    }

    static Component ComponentAt(GameObject go, string typeName, int index, bool create)
    {
        Type type = ResolveType(typeName);
        if (type == null || !typeof(Component).IsAssignableFrom(type))
        {
            Debug.LogWarning("U-NOR component '" + typeName + "' was not found.");
            return null;
        }
        Component[] found = go.GetComponents(type);
        if (index < 0)
            index = found.Length + index;
        if (index >= 0 && index < found.Length)
            return found[index];
        if (!create)
            return null;
        Component created = null;
        int count = found.Length;
        if (index < 0)
            index = 0;
        while (count <= index)
        {
            created = go.AddComponent(type);
            count++;
        }
        return created;
    }

    static void AssignMembers(object target, object value)
    {
        JsonMap map = value as JsonMap;
        if (map != null)
        {
            ApplyMap(target, map, Mode.Build);
            return;
        }
        Debug.LogWarning("U-NOR expected an object of props.");
    }

    static void AssignMembers(object target, string name, object value)
    {
        if (target == null)
            return;

        GameObject go = target as GameObject;
        if (go != null && AssignGameObjectKey(go, name, value))
            return;

        Type type = target.GetType();
        PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
        {
            SetConverted(target, property.PropertyType, value, delegate (object converted) { property.SetValue(target, converted, null); }, name);
            return;
        }

        FieldInfo field = FindField(type, name);
        if (field != null)
        {
            SetConverted(target, field.FieldType, value, delegate (object converted) { field.SetValue(target, converted); }, name);
            return;
        }

        Debug.LogWarning("U-NOR member '" + name + "' was not found on " + type.Name + ".");
    }

    static bool AssignGameObjectKey(GameObject go, string name, object value)
    {
        if (name == "name")
        {
            go.name = value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture);
            return true;
        }
        if (name == "tag")
        {
            if (value != null)
                go.tag = Convert.ToString(value, CultureInfo.InvariantCulture);
            return true;
        }
        if (name == "layer")
        {
            if (value is string layerName)
            {
                int layer = LayerMask.NameToLayer(layerName);
                if (layer >= 0)
                    go.layer = layer;
            }
            else if (value != null)
            {
                go.layer = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            return true;
        }
        if (name == "active")
        {
            if (value is bool active)
                go.SetActive(active);
            return true;
        }
        return false;
    }

    static void SetConverted(object target, Type type, object value, Action<object> write, string label)
    {
        try
        {
            object converted;
            if (!TryConvert(type, value, out converted))
            {
                if (value is string selector && LooksLikeSelector(selector) && typeof(UnityEngine.Object).IsAssignableFrom(type))
                {
                    string captured = selector;
                    Type capturedType = type;
                    Scope.PendingRefs.Add(new Pending
                    {
                        Apply = delegate
                        {
                            object later;
                            if (TryConvert(capturedType, captured, out later))
                                write(later);
                            else
                                Debug.LogWarning("U-NOR reference '" + captured + "' did not resolve.");
                        }
                    });
                    return;
                }
                Debug.LogWarning("U-NOR could not assign '" + label + "'.");
                return;
            }
            write(converted);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("U-NOR could not assign '" + label + "': " + exception.Message);
        }
    }

    static bool TryConvert(Type type, object value, out object converted)
    {
        converted = null;
        if (type == null)
            return false;
        if (value == null)
        {
            if (!type.IsValueType || Nullable.GetUnderlyingType(type) != null)
            {
                converted = null;
                return true;
            }
            return false;
        }

        Type nullable = Nullable.GetUnderlyingType(type);
        if (nullable != null)
            type = nullable;

        if (value is string hexText && (type == typeof(Color) || type == typeof(Color32)))
        {
            Color hex;
            if (TryParseHexColor(hexText, out hex))
            {
                converted = type == typeof(Color) ? (object)hex : (object)(Color32)hex;
                return true;
            }
        }

        if (value is string text && LooksLikeSelector(text) && typeof(UnityEngine.Object).IsAssignableFrom(type))
        {
            bool instantiate = text[0] == '@';
            List<object> matches = ResolveSelector(text, Scope.Root, instantiate);
            if (matches.Count == 0)
                return false;
            if (type.IsArray)
            {
                Type element = type.GetElementType();
                Array array = Array.CreateInstance(element, matches.Count);
                for (int i = 0; i < matches.Count; i++)
                {
                    object elementValue;
                    if (!TryConvert(element, matches[i], out elementValue))
                        return false;
                    array.SetValue(elementValue, i);
                }
                converted = array;
                return true;
            }
            value = matches[0];
        }

        if (type.IsInstanceOfType(value))
        {
            converted = value;
            return true;
        }

        if (typeof(UnityEngine.Object).IsAssignableFrom(type))
        {
            UnityEngine.Object obj = value as UnityEngine.Object;
            if (obj == null)
                return false;
            if (type.IsInstanceOfType(obj))
            {
                converted = obj;
                return true;
            }
            Component component = obj as Component;
            if (component != null && type.IsAssignableFrom(typeof(GameObject)) == false && typeof(Component).IsAssignableFrom(type))
            {
                Component found = component.GetComponent(type);
                if (found == null)
                    return false;
                converted = found;
                return true;
            }
            GameObject go = obj as GameObject;
            if (go == null && component != null)
                go = component.gameObject;
            if (go != null && typeof(Component).IsAssignableFrom(type))
            {
                Component found = go.GetComponent(type);
                if (found == null)
                    return false;
                converted = found;
                return true;
            }
            return false;
        }

        JsonMap map = value as JsonMap;
        if (map != null)
        {
            if (type == typeof(Vector2))
            {
                converted = new Vector2(MapFloat(map, "x"), MapFloat(map, "y"));
                return true;
            }
            if (type == typeof(Vector3))
            {
                converted = new Vector3(MapFloat(map, "x"), MapFloat(map, "y"), MapFloat(map, "z"));
                return true;
            }
            if (type == typeof(Vector4))
            {
                converted = new Vector4(MapFloat(map, "x"), MapFloat(map, "y"), MapFloat(map, "z"), MapFloat(map, "w"));
                return true;
            }
            if (type == typeof(Quaternion))
            {
                converted = new Quaternion(MapFloat(map, "x"), MapFloat(map, "y"), MapFloat(map, "z"), MapFloat(map, "w", 1f));
                return true;
            }
            if (type == typeof(Color))
            {
                converted = new Color(MapFloat(map, "r"), MapFloat(map, "g"), MapFloat(map, "b"), MapFloat(map, "a", 1f));
                return true;
            }
            if (type == typeof(Color32))
            {
                converted = new Color32(MapByte(map, "r"), MapByte(map, "g"), MapByte(map, "b"), MapByte(map, "a", 255));
                return true;
            }
            if (type == typeof(Vector2Int))
            {
                converted = new Vector2Int(MapInt(map, "x"), MapInt(map, "y"));
                return true;
            }
            if (type == typeof(Vector3Int))
            {
                converted = new Vector3Int(MapInt(map, "x"), MapInt(map, "y"), MapInt(map, "z"));
                return true;
            }
            if (type == typeof(Rect))
            {
                converted = new Rect(MapFloat(map, "x"), MapFloat(map, "y"), MapFloat(map, "width"), MapFloat(map, "height"));
                return true;
            }
            if (type == typeof(Bounds))
            {
                object center;
                object size;
                Vector3 centerValue = Vector3.zero;
                Vector3 sizeValue = Vector3.zero;
                if (TryMapValue(map, "center", out center) && TryConvert(typeof(Vector3), center, out center))
                    centerValue = (Vector3)center;
                if (TryMapValue(map, "size", out size) && TryConvert(typeof(Vector3), size, out size))
                    sizeValue = (Vector3)size;
                converted = new Bounds(centerValue, sizeValue);
                return true;
            }
            if (type == typeof(LayerMask))
            {
                object raw;
                if (TryMapValue(map, "value", out raw))
                {
                    converted = (LayerMask)MapInt(map, "value");
                    return true;
                }
            }
            if (!typeof(UnityEngine.Object).IsAssignableFrom(type) && !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null)
            {
                object instance = Activator.CreateInstance(type);
                ApplyMap(instance, map, Mode.Build);
                converted = instance;
                return true;
            }
        }

        if (value is string numberText && (type == typeof(float) || type == typeof(double)))
        {
            if (numberText == "Infinity")
            {
                converted = type == typeof(float) ? (object)float.PositiveInfinity : double.PositiveInfinity;
                return true;
            }
            if (numberText == "-Infinity")
            {
                converted = type == typeof(float) ? (object)float.NegativeInfinity : double.NegativeInfinity;
                return true;
            }
            if (numberText == "NaN")
            {
                converted = type == typeof(float) ? (object)float.NaN : double.NaN;
                return true;
            }
        }

        if (type.IsEnum)
        {
            if (value is string enumName)
            {
                converted = Enum.Parse(type, enumName, true);
                return true;
            }
            converted = Enum.ToObject(type, Convert.ToInt32(value, CultureInfo.InvariantCulture));
            return true;
        }

        if (type == typeof(LayerMask))
        {
            if (value is string layerName)
            {
                int layer = LayerMask.NameToLayer(layerName);
                converted = (LayerMask)Mathf.Max(0, layer);
                return layer >= 0;
            }
            converted = (LayerMask)Convert.ToInt32(value, CultureInfo.InvariantCulture);
            return true;
        }

        if (type.IsArray && value is List<object> items)
        {
            Type element = type.GetElementType();
            Array array = Array.CreateInstance(element, items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                object elementValue;
                if (!TryConvert(element, items[i], out elementValue))
                    return false;
                array.SetValue(elementValue, i);
            }
            converted = array;
            return true;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) && value is List<object> listItems)
        {
            Type element = type.GetGenericArguments()[0];
            IList list = (IList)Activator.CreateInstance(type);
            for (int i = 0; i < listItems.Count; i++)
            {
                object elementValue;
                if (!TryConvert(element, listItems[i], out elementValue))
                    return false;
                list.Add(elementValue);
            }
            converted = list;
            return true;
        }

        if (typeof(IList).IsAssignableFrom(type) && type != typeof(string))
            return false;

        converted = Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        return true;
    }

    static bool TryMapValue(JsonMap map, string key, out object value)
    {
        for (int i = 0; i < map.Count; i++)
        {
            if (map.Keys[i] == key)
            {
                value = map.Values[i];
                return true;
            }
        }
        value = null;
        return false;
    }

    static float MapFloat(JsonMap map, string key, float fallback = 0f)
    {
        object value;
        if (!TryMapValue(map, key, out value) || value == null)
            return fallback;
        return Convert.ToSingle(value, CultureInfo.InvariantCulture);
    }

    static int MapInt(JsonMap map, string key)
    {
        object value;
        if (!TryMapValue(map, key, out value) || value == null)
            return 0;
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    static byte MapByte(JsonMap map, string key, byte fallback = 0)
    {
        object value;
        if (!TryMapValue(map, key, out value) || value == null)
            return fallback;
        return Convert.ToByte(value, CultureInfo.InvariantCulture);
    }

    static object ReadMember(object target, string name)
    {
        if (target == null)
            return null;
        Type type = target.GetType();
        PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
        {
            try
            {
                return property.GetValue(target, null);
            }
            catch
            {
                return null;
            }
        }

        FieldInfo field = FindField(type, name);
        if (field == null)
            return null;
        return field.GetValue(target);
    }

    static FieldInfo FindField(Type type, string name)
    {
        while (type != null && type != typeof(object))
        {
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
            type = type.BaseType;
        }
        return null;
    }

    static bool IsReservedGameObjectKey(string name)
    {
        for (int i = 0; i < ReservedGameObjectKeys.Length; i++)
        {
            if (ReservedGameObjectKeys[i] == name)
                return true;
        }
        return false;
    }

    static List<object> ResolveSelector(string selector, GameObject scope, bool instantiate)
    {
        string text = selector.Trim();
        if (text.StartsWith("@"))
            text = text.Substring(1).Trim();
        if (text.Length >= 2 && text[0] == '<' && text[text.Length - 1] == '>')
            text = text.Substring(1, text.Length - 2);

        List<object> matches = ResolveAbsolute(text, scope);
        if (!instantiate || matches.Count == 0)
            return matches;

        var instances = new List<object>();
        for (int i = 0; i < matches.Count; i++)
        {
            GameObject prefab = matches[i] as GameObject;
            if (prefab == null && matches[i] is Component component)
                prefab = component.gameObject;
            if (prefab == null)
                continue;
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = prefab.name;
            NoteFresh(instance);
            Scope.Own(instance);
            if (Scope.Hide)
                HideTree(instance);
            instances.Add(instance);
        }
        return instances;
    }

    static List<object> ResolveAbsolute(string text, GameObject scope)
    {
        string memberPath;
        string resourcePath;
        if (TrySplitPrefix(text, "res:", out resourcePath, out memberPath))
            return Continue(LoadResources(resourcePath), memberPath);
        if (TrySplitPrefix(text, "asset_bundle:", out resourcePath, out memberPath))
            return Continue(LoadAssetBundle(resourcePath), memberPath);
        if (TrySplitPrefix(text, "asset:", out resourcePath, out memberPath, true))
            return Continue(LoadAssetPath(resourcePath), memberPath);
        if (text.StartsWith("scene:"))
            return Walk(text.Substring(6), null);
        if (text.StartsWith("#"))
            return Walk(text, null);
        if (scope != null)
            return Walk(text, scope);
        return Walk(text, null);
    }

    static bool TrySplitPrefix(string text, string prefix, out string head, out string tail, bool assetPath = false)
    {
        head = null;
        tail = null;
        if (!text.StartsWith(prefix))
            return false;
        string rest = text.Substring(prefix.Length);
        int dot = assetPath ? IndexOfAssetMemberDot(rest) : IndexOfMemberDot(rest);
        if (dot < 0)
        {
            head = rest;
            tail = "";
            return true;
        }
        head = rest.Substring(0, dot);
        tail = rest.Substring(dot + 1);
        return true;
    }

    static int IndexOfAssetMemberDot(string text)
    {
        int slash = text.LastIndexOf('/');
        int ext = text.IndexOf('.', slash + 1);
        if (ext < 0)
            return IndexOfMemberDot(text);
        int i = ext + 1;
        while (i < text.Length)
        {
            char c = text[i];
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                break;
            i++;
        }
        if (i < text.Length && text[i] == '.')
            return i;
        return -1;
    }

    static int IndexOfMemberDot(string text)
    {
        int paren = 0;
        int bracket = 0;
        int angle = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(') paren++;
            else if (c == ')') paren--;
            else if (c == '[') bracket++;
            else if (c == ']') bracket--;
            else if (c == '<') angle++;
            else if (c == '>') angle--;
            else if (c == '.' && paren == 0 && bracket == 0 && angle == 0)
                return i;
        }
        return -1;
    }

    static List<object> Continue(List<object> current, string path)
    {
        if (current.Count == 0 || string.IsNullOrEmpty(path))
            return current;
        return WalkFrom(current, SplitPath(path));
    }

    static List<object> Walk(string path, GameObject scope)
    {
        if (string.IsNullOrEmpty(path))
        {
            if (scope == null)
                return new List<object>();
            return new List<object> { scope };
        }
        List<string> parts = SplitPath(path);
        if (scope != null)
            return WalkFrom(new List<object> { scope }, parts);
        return WalkFrom(null, parts);
    }

    static List<object> WalkFrom(List<object> current, List<string> parts)
    {
        for (int i = 0; i < parts.Count; i++)
        {
            Segment segment = ParseSegment(parts[i]);
            if (current == null)
                current = StartSegment(segment);
            else
                current = Step(current, segment);
            if (segment.Index.HasValue)
                current = Pick(current, segment.Index.Value);
            if (current.Count == 0)
                break;
        }
        return current ?? new List<object>();
    }

    static List<object> StartSegment(Segment segment)
    {
        if (segment.Name.StartsWith("#"))
        {
            string tag = segment.Name.Substring(1);
            try
            {
                GameObject[] tagged = GameObject.FindGameObjectsWithTag(tag);
                return new List<object>(tagged);
            }
            catch
            {
                return new List<object>();
            }
        }
        return new List<object>(FindNamed(segment.Name));
    }

    static List<object> Step(List<object> current, Segment segment)
    {
        var next = new List<object>();
        for (int i = 0; i < current.Count; i++)
        {
            object item = current[i];
            if (segment.Component)
            {
                GameObject host = HostOf(item);
                if (host == null)
                    continue;
                Type type = ResolveType(segment.Name);
                if (type == null)
                    continue;
                Component[] found = host.GetComponents(type);
                for (int c = 0; c < found.Length; c++)
                    next.Add(found[c]);
                continue;
            }
            if (segment.Event)
            {
                object publisher = item;
                GameObject host = item as GameObject;
                if (host != null)
                    publisher = host.GetComponent<Component>();
                Delegate subscribers = EventSubscribers(publisher, segment.Name);
                if (subscribers != null)
                    next.Add(subscribers);
                continue;
            }

            GameObject go = item as GameObject;
            if (go != null)
            {
                int before = next.Count;
                Transform transform = go.transform;
                for (int c = 0; c < transform.childCount; c++)
                {
                    Transform child = transform.GetChild(c);
                    if (child.name == segment.Name)
                        next.Add(child.gameObject);
                }
                if (next.Count == before)
                {
                    object member = ReadMember(go, segment.Name);
                    if (member != null)
                        next.Add(member);
                }
                continue;
            }

            object value = ReadMember(item, segment.Name);
            if (value != null)
                next.Add(value);
        }
        return next;
    }

    static List<object> Pick(List<object> matches, int index)
    {
        if (matches.Count == 0)
            return matches;
        int resolved = index < 0 ? matches.Count + index : index;
        if (resolved < 0 || resolved >= matches.Count)
            resolved = 0;
        return new List<object> { matches[resolved] };
    }

    static object PackMatches(List<object> matches, bool multi)
    {
        if (matches == null || matches.Count == 0)
            return null;
        if (!multi || matches.Count == 1)
            return matches[0];
        return matches.ToArray();
    }

    static GameObject[] FindNamed(string name)
    {
        GameObject[] all = SceneObjects();
        var found = new List<GameObject>();
        for (int i = 0; i < all.Length; i++)
        {
            GameObject go = all[i];
            if (go == null || go.name != name)
                continue;
            if (!go.scene.IsValid() || !go.scene.isLoaded)
                continue;
            if ((go.hideFlags & HideFlags.HideAndDontSave) != 0)
                continue;
            if (!go.activeInHierarchy)
                continue;
            found.Add(go);
        }
        return found.ToArray();
    }

    static GameObject[] SceneObjects()
    {
#if UNITY_2022_2_OR_NEWER
        return UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        return UnityEngine.Object.FindObjectsOfType<GameObject>();
#endif
    }

    static List<object> LoadResources(string path)
    {
        var found = new List<object>();
        if (path.EndsWith("/*"))
        {
            UnityEngine.Object[] all = Resources.LoadAll(path.Substring(0, path.Length - 2));
            for (int i = 0; i < all.Length; i++)
                found.Add(all[i]);
            return found;
        }
        UnityEngine.Object asset = Resources.Load(path);
        if (asset != null)
            found.Add(asset);
        return found;
    }

    static List<object> LoadAssetBundle(string path)
    {
        var found = new List<object>();
        string[] parts = path.Split('/');
        if (parts.Length < 2)
            return found;
        string assetName = parts[parts.Length - 1];
        string bundlePath = string.Join("/", parts, 0, parts.Length - 1);
        AssetBundle bundle = null;
        bool owned = false;
        foreach (AssetBundle loaded in AssetBundle.GetAllLoadedAssetBundles())
        {
            if (loaded.name == bundlePath)
            {
                bundle = loaded;
                break;
            }
        }
        if (bundle == null)
        {
            bundle = AssetBundle.LoadFromFile(Path.Combine(Application.streamingAssetsPath, bundlePath));
            owned = bundle != null;
        }
        if (bundle == null)
            return found;
        UnityEngine.Object asset = bundle.LoadAsset(assetName);
        if (owned)
            bundle.Unload(false);
        if (asset != null)
            found.Add(asset);
        return found;
    }

    static List<object> LoadAssetPath(string path)
    {
        var found = new List<object>();
#if UNITY_EDITOR
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
        {
            string spriteName = Path.GetFileNameWithoutExtension(path);
            UnityEngine.Object[] reps = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
            for (int i = 0; i < reps.Length; i++)
            {
                Sprite sub = reps[i] as Sprite;
                if (sub == null)
                    continue;
                sprite = sub;
                if (sub.name == spriteName)
                    break;
            }
        }
        if (sprite != null)
        {
            found.Add(sprite);
            return found;
        }
        UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
        if (asset != null)
            found.Add(asset);
#else
        Debug.LogWarning("U-NOR asset path '" + path + "' is available in the editor.");
#endif
        return found;
    }

    static Delegate EventSubscribers(object publisher, string eventName)
    {
        if (publisher == null)
            return null;
        EventInfo info = publisher.GetType().GetEvent(eventName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (info == null)
            return null;
        FieldInfo field = publisher.GetType().GetField(info.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null)
            return null;
        return field.GetValue(publisher) as Delegate;
    }

    static Type ResolveType(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        Type cached;
        if (typeCache.TryGetValue(name, out cached))
            return cached;

        Type resolved = Type.GetType(name);
        if (resolved == null)
            resolved = Type.GetType("UnityEngine." + name + ", UnityEngine.CoreModule");
        if (resolved == null)
            resolved = Type.GetType("UnityEngine." + name + ", UnityEngine");
        if (resolved == null)
            resolved = Type.GetType("UnityEngine.UI." + name + ", UnityEngine.UI");
        if (resolved == null)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length && resolved == null; i++)
            {
                Type[] types;
                try
                {
                    types = assemblies[i].GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    types = exception.Types;
                }
                if (types == null)
                    continue;
                for (int t = 0; t < types.Length; t++)
                {
                    Type candidate = types[t];
                    if (candidate != null && (candidate.FullName == name || candidate.Name == name))
                    {
                        resolved = candidate;
                        break;
                    }
                }
            }
        }

        typeCache[name] = resolved;
        return resolved;
    }

    static List<string> SplitPath(string path)
    {
        var parts = new List<string>();
        int start = 0;
        int paren = 0;
        int bracket = 0;
        int angle = 0;
        for (int i = 0; i < path.Length; i++)
        {
            char c = path[i];
            if (c == '(') paren++;
            else if (c == ')') paren--;
            else if (c == '[') bracket++;
            else if (c == ']') bracket--;
            else if (c == '<') angle++;
            else if (c == '>') angle--;
            else if (c == '.' && paren == 0 && bracket == 0 && angle == 0)
            {
                parts.Add(path.Substring(start, i - start));
                start = i + 1;
            }
        }
        if (start < path.Length)
            parts.Add(path.Substring(start));
        return parts;
    }

    static Segment ParseSegment(string text)
    {
        Segment segment = new Segment();
        string body = text ?? "";
        if (body.StartsWith("@"))
        {
            segment.Instantiate = true;
            body = body.Substring(1);
        }
        if (body.StartsWith("("))
        {
            int end = body.IndexOf(')');
            if (end >= 1)
            {
                segment.Component = true;
                string inner = body.Substring(1, end - 1);
                int tilde = inner.LastIndexOf('~');
                if (tilde > 0)
                {
                    segment.Name = inner.Substring(0, tilde);
                    segment.Index = int.Parse(inner.Substring(tilde + 1), CultureInfo.InvariantCulture);
                }
                else
                {
                    segment.Name = inner;
                }
                string rest = body.Substring(end + 1);
                if (rest.StartsWith("[") && rest.EndsWith("]") && rest.Length > 2)
                    segment.Index = int.Parse(rest.Substring(1, rest.Length - 2), CultureInfo.InvariantCulture);
                return segment;
            }
        }
        if (body.StartsWith("[") && body.EndsWith("]") && body.Length > 2)
        {
            segment.Event = true;
            segment.Name = body.Substring(1, body.Length - 2);
            return segment;
        }
        if (body.EndsWith("]") && body.IndexOf('[') > 0)
        {
            int bracket = body.LastIndexOf('[');
            segment.Name = body.Substring(0, bracket);
            segment.Index = int.Parse(body.Substring(bracket + 1, body.Length - bracket - 2), CultureInfo.InvariantCulture);
            return segment;
        }
        int mark = body.LastIndexOf('~');
        if (mark > 0)
        {
            segment.Name = body.Substring(0, mark);
            segment.Index = int.Parse(body.Substring(mark + 1), CultureInfo.InvariantCulture);
            return segment;
        }
        segment.Name = body;
        return segment;
    }

    static bool LooksLikeSelector(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        if (text[0] == '@' || text[0] == '<' || text[0] == '#')
            return true;
        return text.StartsWith("scene:") || text.StartsWith("res:") || text.StartsWith("asset_bundle:") || text.StartsWith("asset:");
    }

    static void WriteValue(StringBuilder builder, object value, GameObject scope, int indent, bool expand, HashSet<object> stack)
    {
        if (value == null)
        {
            builder.Append("null");
            return;
        }
        if (value is string || value is bool || value is Enum || IsNumber(value))
        {
            WriteLiteral(builder, value);
            return;
        }
        if (value is Vector2 || value is Vector3 || value is Vector4 || value is Quaternion || value is Color || value is Color32 || value is Rect || value is Bounds || value is Vector2Int || value is Vector3Int)
        {
            WriteStruct(builder, value, indent);
            return;
        }
        if (value is LayerMask)
        {
            builder.Append(((LayerMask)value).value.ToString(CultureInfo.InvariantCulture));
            return;
        }
        if (expand)
        {
            GameObject go = value as GameObject;
            if (go != null)
            {
                WriteGameObject(builder, go, scope ?? go, indent, stack);
                return;
            }
            Component component = value as Component;
            if (component != null)
            {
                WriteComponent(builder, component, scope ?? component.gameObject, indent, stack);
                return;
            }
        }
        if (value is UnityEngine.Object unityObject)
        {
            if (unityObject == null)
            {
                builder.Append("null");
                return;
            }
            WriteString(builder, SelectorFor(unityObject, scope));
            return;
        }
        if (value is IList list && !(value is JsonMap))
        {
            WriteArray(builder, list, scope, indent, expand, stack);
            return;
        }
        if (stack.Contains(value))
        {
            builder.Append("null");
            return;
        }
        stack.Add(value);
        WriteReflectedObject(builder, value, scope, indent, stack);
        stack.Remove(value);
    }

    static void WriteGameObject(StringBuilder builder, GameObject go, GameObject scope, int indent, HashSet<object> stack)
    {
        var keys = new List<string>();
        var values = new List<object>();
        keys.Add("name");
        values.Add(go.name);
        keys.Add("tag");
        values.Add(go.tag);
        string layer = LayerMask.LayerToName(go.layer);
        keys.Add("layer");
        values.Add(string.IsNullOrEmpty(layer) ? (object)go.layer : layer);
        keys.Add("active");
        values.Add(go.activeSelf);

        Component[] components = go.GetComponents<Component>();
        var seen = new List<string>();
        var groups = new List<List<Component>>();
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null || component is Transform)
                continue;
            string key = "(" + component.GetType().Name + ")";
            int at = seen.IndexOf(key);
            if (at < 0)
            {
                seen.Add(key);
                groups.Add(new List<Component>());
                at = seen.Count - 1;
            }
            groups[at].Add(component);
        }

        Transform transform = go.transform;
        string transformKey = transform is RectTransform ? "(RectTransform)" : "(Transform)";
        keys.Add(transformKey);
        values.Add(transform);
        for (int i = 0; i < seen.Count; i++)
        {
            keys.Add(seen[i]);
            values.Add(groups[i].Count == 1 ? (object)groups[i][0] : groups[i]);
        }

        var childNames = new List<string>();
        var childGroups = new List<List<GameObject>>();
        for (int i = 0; i < transform.childCount; i++)
        {
            GameObject child = transform.GetChild(i).gameObject;
            int at = childNames.IndexOf(child.name);
            if (at < 0)
            {
                childNames.Add(child.name);
                childGroups.Add(new List<GameObject>());
                at = childNames.Count - 1;
            }
            childGroups[at].Add(child);
        }
        for (int i = 0; i < childNames.Count; i++)
        {
            keys.Add(childNames[i]);
            values.Add(childGroups[i].Count == 1 ? (object)childGroups[i][0] : childGroups[i]);
        }

        WriteObjectEntries(builder, keys, values, scope, indent, true, stack);
    }

    static void WriteComponent(StringBuilder builder, Component component, GameObject scope, int indent, HashSet<object> stack)
    {
        var keys = new List<string>();
        var values = new List<object>();
        if (component is Transform transform)
        {
            AddTransform(keys, values, transform);
        }
        else if (component is MonoBehaviour)
        {
            if (component is Behaviour behaviour)
            {
                keys.Add("enabled");
                values.Add(behaviour.enabled);
            }
            AddFields(keys, values, component);
        }
        else
        {
            AddProperties(keys, values, component);
        }
        WriteObjectEntries(builder, keys, values, scope, indent, false, stack);
    }

    static void AddTransform(List<string> keys, List<object> values, Transform transform)
    {
        RectTransform rect = transform as RectTransform;
        if (rect != null)
        {
            keys.Add("anchoredPosition");
            values.Add(rect.anchoredPosition);
            keys.Add("anchorMin");
            values.Add(rect.anchorMin);
            keys.Add("anchorMax");
            values.Add(rect.anchorMax);
            keys.Add("pivot");
            values.Add(rect.pivot);
            keys.Add("sizeDelta");
            values.Add(rect.sizeDelta);
            keys.Add("localRotation");
            values.Add(rect.localRotation);
            keys.Add("localScale");
            values.Add(rect.localScale);
            return;
        }
        keys.Add("localPosition");
        values.Add(transform.localPosition);
        keys.Add("localRotation");
        values.Add(transform.localRotation);
        keys.Add("localScale");
        values.Add(transform.localScale);
    }

    static void AddFields(List<string> keys, List<object> values, Component component)
    {
        Type type = component.GetType();
        while (type != null && type != typeof(MonoBehaviour) && type != typeof(Behaviour) && type != typeof(Component) && type != typeof(UnityEngine.Object))
        {
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field.IsStatic || field.IsNotSerialized || field.Name.Contains("k__BackingField"))
                    continue;
                if (field.IsDefined(typeof(NonSerializedAttribute), false))
                    continue;
                if (!field.IsPublic && !field.IsDefined(typeof(SerializeField), false))
                    continue;
                if (!CanRepresent(field.FieldType, 0))
                    continue;
                object fieldValue;
                try
                {
                    fieldValue = field.GetValue(component);
                }
                catch
                {
                    continue;
                }
                keys.Add(field.Name);
                values.Add(fieldValue);
            }
            type = type.BaseType;
        }
    }

    static void AddProperties(List<string> keys, List<object> values, Component component)
    {
        PropertyInfo[] properties = component.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
        for (int i = 0; i < properties.Length; i++)
        {
            PropertyInfo property = properties[i];
            if (!IncludeProperty(property))
                continue;
            object propertyValue;
            try
            {
                propertyValue = property.GetValue(component, null);
            }
            catch
            {
                continue;
            }
            keys.Add(property.Name);
            values.Add(propertyValue);
        }
    }

    static bool IncludeProperty(PropertyInfo property)
    {
        if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length > 0)
            return false;
        if (property.GetMethod.IsStatic || property.IsDefined(typeof(ObsoleteAttribute), true))
            return false;
        Type declared = property.DeclaringType;
        if (declared == typeof(UnityEngine.Object) || declared == typeof(Component) || declared == typeof(MonoBehaviour))
            return false;
        if (declared == typeof(Behaviour))
            return property.Name == "enabled";
        string name = property.Name;
        if (name == "parent" || name == "mesh" || name == "material" || name == "materials" || name == "hasChanged")
            return false;
        return CanRepresent(property.PropertyType, 0);
    }

    static void WriteReflectedObject(StringBuilder builder, object value, GameObject scope, int indent, HashSet<object> stack)
    {
        var keys = new List<string>();
        var values = new List<object>();
        FieldInfo[] fields = value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public);
        for (int i = 0; i < fields.Length; i++)
        {
            if (!CanRepresent(fields[i].FieldType, 0))
                continue;
            keys.Add(fields[i].Name);
            values.Add(fields[i].GetValue(value));
        }
        WriteObjectEntries(builder, keys, values, scope, indent, false, stack);
    }

    static bool CanRepresent(Type type, int depth)
    {
        if (type == null || depth > 6)
            return false;
        if (type == typeof(string) || type == typeof(bool) || IsNumericType(type) || type.IsEnum)
            return true;
        if (type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) || type == typeof(Quaternion) || type == typeof(Color) || type == typeof(Color32) || type == typeof(Rect) || type == typeof(Bounds) || type == typeof(Vector2Int) || type == typeof(Vector3Int) || type == typeof(LayerMask))
            return true;
        if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            return true;
        if (typeof(Delegate).IsAssignableFrom(type))
            return false;
        if (type.IsArray)
            return CanRepresent(type.GetElementType(), depth + 1);
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            return CanRepresent(type.GetGenericArguments()[0], depth + 1);
        if (type.IsValueType)
            return true;
        return type.GetConstructor(Type.EmptyTypes) != null;
    }

    static void WriteObjectEntries(StringBuilder builder, List<string> keys, List<object> values, GameObject scope, int indent, bool expand, HashSet<object> stack)
    {
        builder.Append('{');
        if (keys.Count == 0)
        {
            builder.Append('}');
            return;
        }
        builder.Append('\n');
        for (int i = 0; i < keys.Count; i++)
        {
            Indent(builder, indent + 1);
            WriteString(builder, keys[i]);
            builder.Append(": ");
            WriteValue(builder, values[i], scope, indent + 1, expand, stack);
            if (i + 1 < keys.Count)
                builder.Append(',');
            builder.Append('\n');
        }
        Indent(builder, indent);
        builder.Append('}');
    }

    static void WriteArray(StringBuilder builder, IList list, GameObject scope, int indent, bool expand, HashSet<object> stack)
    {
        builder.Append('[');
        if (list.Count == 0)
        {
            builder.Append(']');
            return;
        }
        builder.Append('\n');
        for (int i = 0; i < list.Count; i++)
        {
            Indent(builder, indent + 1);
            WriteValue(builder, list[i], scope, indent + 1, expand, stack);
            if (i + 1 < list.Count)
                builder.Append(',');
            builder.Append('\n');
        }
        Indent(builder, indent);
        builder.Append(']');
    }

    static void WriteStruct(StringBuilder builder, object value, int indent)
    {
        var keys = new List<string>();
        var values = new List<object>();
        if (value is Vector2 v2)
        {
            AddPair(keys, values, "x", v2.x);
            AddPair(keys, values, "y", v2.y);
        }
        else if (value is Vector3 v3)
        {
            AddPair(keys, values, "x", v3.x);
            AddPair(keys, values, "y", v3.y);
            AddPair(keys, values, "z", v3.z);
        }
        else if (value is Vector4 v4)
        {
            AddPair(keys, values, "x", v4.x);
            AddPair(keys, values, "y", v4.y);
            AddPair(keys, values, "z", v4.z);
            AddPair(keys, values, "w", v4.w);
        }
        else if (value is Quaternion quaternion)
        {
            AddPair(keys, values, "x", quaternion.x);
            AddPair(keys, values, "y", quaternion.y);
            AddPair(keys, values, "z", quaternion.z);
            AddPair(keys, values, "w", quaternion.w);
        }
        else if (value is Color color)
        {
            AddPair(keys, values, "r", color.r);
            AddPair(keys, values, "g", color.g);
            AddPair(keys, values, "b", color.b);
            AddPair(keys, values, "a", color.a);
        }
        else if (value is Color32 color32)
        {
            AddPair(keys, values, "r", (int)color32.r);
            AddPair(keys, values, "g", (int)color32.g);
            AddPair(keys, values, "b", (int)color32.b);
            AddPair(keys, values, "a", (int)color32.a);
        }
        else if (value is Rect rect)
        {
            AddPair(keys, values, "x", rect.x);
            AddPair(keys, values, "y", rect.y);
            AddPair(keys, values, "width", rect.width);
            AddPair(keys, values, "height", rect.height);
        }
        else if (value is Bounds bounds)
        {
            AddPair(keys, values, "center", bounds.center);
            AddPair(keys, values, "size", bounds.size);
        }
        else if (value is Vector2Int v2i)
        {
            AddPair(keys, values, "x", v2i.x);
            AddPair(keys, values, "y", v2i.y);
        }
        else if (value is Vector3Int v3i)
        {
            AddPair(keys, values, "x", v3i.x);
            AddPair(keys, values, "y", v3i.y);
            AddPair(keys, values, "z", v3i.z);
        }
        builder.Append('{');
        for (int i = 0; i < keys.Count; i++)
        {
            if (i == 0)
                builder.Append(' ');
            WriteString(builder, keys[i]);
            builder.Append(": ");
            if (values[i] is Vector3 nested)
                WriteStruct(builder, nested, indent);
            else
                WriteLiteral(builder, values[i]);
            if (i + 1 < keys.Count)
                builder.Append(", ");
        }
        builder.Append(" }");
    }

    static void AddPair(List<string> keys, List<object> values, string key, object value)
    {
        keys.Add(key);
        values.Add(value);
    }

    static void WriteLiteral(StringBuilder builder, object value)
    {
        if (value == null)
        {
            builder.Append("null");
            return;
        }
        if (value is string text)
        {
            WriteString(builder, text);
            return;
        }
        if (value is bool flag)
        {
            builder.Append(flag ? "true" : "false");
            return;
        }
        if (value is Enum)
        {
            WriteString(builder, value.ToString());
            return;
        }
        if (value is float number)
        {
            WriteFloat(builder, number);
            return;
        }
        if (value is double precise)
        {
            WriteFloat(builder, precise);
            return;
        }
        if (IsNumber(value))
        {
            builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            return;
        }
        WriteString(builder, Convert.ToString(value, CultureInfo.InvariantCulture));
    }

    static void WriteFloat(StringBuilder builder, double number)
    {
        if (double.IsPositiveInfinity(number))
        {
            WriteString(builder, "Infinity");
            return;
        }
        if (double.IsNegativeInfinity(number))
        {
            WriteString(builder, "-Infinity");
            return;
        }
        if (double.IsNaN(number))
        {
            WriteString(builder, "NaN");
            return;
        }
        builder.Append(number.ToString("R", CultureInfo.InvariantCulture));
    }

    static void WriteString(StringBuilder builder, string text)
    {
        builder.Append('"');
        if (text != null)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 32)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }
        }
        builder.Append('"');
    }

    static void Indent(StringBuilder builder, int level)
    {
        for (int i = 0; i < level; i++)
            builder.Append("    ");
    }

    static bool IsNumber(object value)
    {
        return value is byte || value is sbyte || value is short || value is ushort || value is int || value is uint || value is long || value is ulong || value is float || value is double || value is decimal;
    }

    static bool IsNumericType(Type type)
    {
        return type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) || type == typeof(float) || type == typeof(double) || type == typeof(decimal);
    }

    static string SelectorFor(UnityEngine.Object target, GameObject scope)
    {
        if (target == null)
            return "";
        string relative = RelativeSelector(target, scope);
        if (relative != null)
            return relative;
        string asset = AssetSelector(target);
        if (asset != null)
            return asset;
        GameObject go = target as GameObject;
        Component component = target as Component;
        if (go == null && component != null)
            go = component.gameObject;
        if (go == null || !go.scene.IsValid())
            return "<scene:" + target.name + ">";
        string path = HierarchyPath(null, go.transform);
        if (component != null)
        {
            string piece = ComponentPiece(go, component);
            path = path.Length == 0 ? piece : path + "." + piece;
        }
        return "<scene:" + path + ">";
    }

    static string RelativeSelector(UnityEngine.Object target, GameObject scope)
    {
        if (scope == null)
            return null;
        GameObject go = target as GameObject;
        Component component = target as Component;
        if (go == null && component != null)
            go = component.gameObject;
        if (go == null || !IsUnder(scope.transform, go.transform))
            return null;
        string path = HierarchyPath(scope.transform, go.transform);
        if (component != null)
        {
            string piece = ComponentPiece(go, component);
            path = path.Length == 0 ? piece : path + "." + piece;
        }
        return "<" + path + ">";
    }

    static string ComponentPiece(GameObject go, Component component)
    {
        Component[] all = go.GetComponents(component.GetType());
        int index = 0;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == component)
            {
                index = i;
                break;
            }
        }
        string name = component.GetType().Name;
        if (all.Length > 1)
            return "(" + name + ")[" + index.ToString(CultureInfo.InvariantCulture) + "]";
        return "(" + name + ")";
    }

    static string HierarchyPath(Transform root, Transform target)
    {
        var names = new List<string>();
        Transform cursor = target;
        while (cursor != null && cursor != root)
        {
            names.Add(NamePiece(cursor));
            cursor = cursor.parent;
        }
        names.Reverse();
        return string.Join(".", names.ToArray());
    }

    static string NamePiece(Transform transform)
    {
        int index = 0;
        int count = 0;
        Transform parent = transform.parent;
        if (parent == null)
        {
            GameObject[] roots = transform.gameObject.scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name != transform.name)
                    continue;
                if (roots[i] == transform.gameObject)
                    index = count;
                count++;
            }
        }
        else
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name != transform.name)
                    continue;
                if (child == transform)
                    index = count;
                count++;
            }
        }
        if (count <= 1)
            return transform.name;
        return transform.name + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
    }

    static bool IsUnder(Transform root, Transform target)
    {
        Transform cursor = target;
        while (cursor != null)
        {
            if (cursor == root)
                return true;
            cursor = cursor.parent;
        }
        return false;
    }

    static string AssetSelector(UnityEngine.Object target)
    {
#if UNITY_EDITOR
        string path = AssetDatabase.GetAssetPath(target);
        if (string.IsNullOrEmpty(path))
            return null;
        const string marker = "/Resources/";
        int at = path.IndexOf(marker, StringComparison.Ordinal);
        if (at >= 0)
        {
            string relative = path.Substring(at + marker.Length);
            int dot = relative.LastIndexOf('.');
            if (dot > 0)
                relative = relative.Substring(0, dot);
            return "<res:" + relative + ">";
        }
        return "<asset:" + path + ">";
#else
        return null;
#endif
    }

    sealed class Mention
    {
        public readonly Dictionary<string, Mention> Kids = new Dictionary<string, Mention>();
    }

    static void NoteFresh(GameObject go)
    {
        if (trackFresh && go != null)
            freshIds.Add(go.GetInstanceID());
    }

    static string NormalizeDocument(string text)
    {
        string trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("U-NOR diff is empty.");
        if (trimmed[0] == '{' || trimmed[0] == '[')
            return trimmed;

        List<string> parts = SplitTop(trimmed);
        var builder = new StringBuilder();
        builder.Append('{');
        int written = 0;
        for (int i = 0; i < parts.Count; i++)
        {
            string part = parts[i].Trim();
            if (part.Length == 0)
                continue;
            int eq = TopEquals(part);
            if (eq <= 0)
                throw new ArgumentException("U-NOR diff assignment is missing '=': " + part);
            string key = part.Substring(0, eq).Trim();
            string raw = part.Substring(eq + 1).Trim();
            if (key.Length == 0 || raw.Length == 0)
                throw new ArgumentException("U-NOR diff assignment is missing a key or a value.");
            if (written > 0)
                builder.Append(',');
            WriteString(builder, key);
            builder.Append(':');
            builder.Append(EncodeAssignmentValue(raw));
            written++;
        }
        if (written == 0)
            throw new ArgumentException("U-NOR diff is empty.");
        builder.Append('}');
        return builder.ToString();
    }

    static string EncodeAssignmentValue(string raw)
    {
        Color hex;
        if (TryParseHexColor(raw, out hex))
        {
            var builder = new StringBuilder();
            WriteString(builder, raw[0] == '#' ? raw : "#" + raw);
            return builder.ToString();
        }
        try
        {
            JsonParser.Parse(raw);
            return raw;
        }
        catch (Exception)
        {
            var builder = new StringBuilder();
            WriteString(builder, raw);
            return builder.ToString();
        }
    }

    static List<string> SplitTop(string text)
    {
        var parts = new List<string>();
        int start = 0;
        int paren = 0;
        int bracket = 0;
        int brace = 0;
        int angle = 0;
        bool quote = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quote)
            {
                if (c == '\\' && i + 1 < text.Length)
                {
                    i++;
                    continue;
                }
                if (c == '"')
                    quote = false;
                continue;
            }
            if (c == '"')
            {
                quote = true;
                continue;
            }
            if (c == '(') paren++;
            else if (c == ')') paren--;
            else if (c == '[') bracket++;
            else if (c == ']') bracket--;
            else if (c == '{') brace++;
            else if (c == '}') brace--;
            else if (c == '<') angle++;
            else if (c == '>') angle--;
            else if ((c == ',' || c == ';' || c == '\n') && paren == 0 && bracket == 0 && brace == 0 && angle == 0)
            {
                parts.Add(text.Substring(start, i - start));
                start = i + 1;
            }
        }
        parts.Add(text.Substring(start));
        return parts;
    }

    static int TopEquals(string text)
    {
        int paren = 0;
        int bracket = 0;
        int brace = 0;
        int angle = 0;
        bool quote = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quote)
            {
                if (c == '\\' && i + 1 < text.Length)
                {
                    i++;
                    continue;
                }
                if (c == '"')
                    quote = false;
                continue;
            }
            if (c == '"')
            {
                quote = true;
                continue;
            }
            if (c == '(') paren++;
            else if (c == ')') paren--;
            else if (c == '[') bracket++;
            else if (c == ']') bracket--;
            else if (c == '{') brace++;
            else if (c == '}') brace--;
            else if (c == '<') angle++;
            else if (c == '>') angle--;
            else if (c == '=' && paren == 0 && bracket == 0 && brace == 0 && angle == 0)
                return i;
        }
        return -1;
    }

    static bool TryParseHexColor(string text, out Color color)
    {
        color = Color.white;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        text = text.Trim();
        if (text[0] == '#')
            text = text.Substring(1);
        if (text.Length != 3 && text.Length != 4 && text.Length != 6 && text.Length != 8)
            return false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!hex)
                return false;
        }
        if (text.Length == 3 || text.Length == 4)
        {
            var expanded = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                expanded.Append(text[i]);
                expanded.Append(text[i]);
            }
            text = expanded.ToString();
        }
        return ColorUtility.TryParseHtmlString("#" + text, out color);
    }

    static Mention MentionOf(JsonMap map)
    {
        var root = new Mention();
        if (map == null)
            return root;
        for (int i = 0; i < map.Count; i++)
            AddMention(root, map.Keys[i], map.Values[i]);
        return root;
    }

    static void AddMention(Mention root, string key, object value)
    {
        List<string> parts = SplitPath(key);
        Mention cursor = root;
        for (int i = 0; i < parts.Count; i++)
        {
            string id = MentionId(ParseSegment(parts[i]));
            Mention next;
            if (!cursor.Kids.TryGetValue(id, out next))
            {
                next = new Mention();
                cursor.Kids[id] = next;
            }
            cursor = next;
        }
        JsonMap nested = value as JsonMap;
        if (nested == null)
            return;
        for (int i = 0; i < nested.Count; i++)
            AddMention(cursor, nested.Keys[i], nested.Values[i]);
    }

    static string MentionId(Segment segment)
    {
        if (segment.Component)
            return "(" + segment.Name + ")";
        if (segment.Event)
            return "[" + segment.Name + "]";
        if (segment.Instantiate)
            return "@" + segment.Name;
        return segment.Name ?? "";
    }

    static void PruneTo(object target, Mention mention)
    {
        if (target == null || mention == null)
            return;
        GameObject go = target as GameObject;
        if (go != null)
        {
            PruneGameObject(go, mention);
            return;
        }
        if (target is Component)
            PruneMembers(target, mention);
    }

    static void PruneGameObject(GameObject go, Mention mention)
    {
        Component[] components = go.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null || component is Transform)
                continue;
            string id = "(" + component.GetType().Name + ")";
            Mention child;
            if (!mention.Kids.TryGetValue(id, out child))
            {
                UnityEngine.Object.Destroy(component);
                continue;
            }
            PruneMembers(component, child);
        }

        Mention transformMention;
        string transformId = go.transform is RectTransform ? "(RectTransform)" : "(Transform)";
        if (mention.Kids.TryGetValue(transformId, out transformMention))
            PruneMembers(go.transform, transformMention);

        var doomed = new List<GameObject>();
        for (int i = 0; i < go.transform.childCount; i++)
        {
            GameObject child = go.transform.GetChild(i).gameObject;
            if (freshIds.Contains(child.GetInstanceID()))
                continue;
            if (!mention.Kids.ContainsKey(child.name))
                doomed.Add(child);
        }
        for (int i = 0; i < go.transform.childCount; i++)
        {
            GameObject child = go.transform.GetChild(i).gameObject;
            if (doomed.Contains(child))
                continue;
            Mention childMention;
            if (mention.Kids.TryGetValue(child.name, out childMention))
                PruneGameObject(child, childMention);
        }
        for (int i = 0; i < doomed.Count; i++)
            UnityEngine.Object.Destroy(doomed[i]);
    }

    static void PruneMembers(object target, Mention mention)
    {
        if (target == null || mention == null || mention.Kids.Count == 0)
            return;
        if (target is MonoBehaviour)
        {
            Type type = target.GetType();
            while (type != null && type != typeof(MonoBehaviour) && type != typeof(Behaviour) && type != typeof(Component) && type != typeof(UnityEngine.Object))
            {
                FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    if (field.IsStatic || field.IsNotSerialized || field.Name.Contains("k__BackingField"))
                        continue;
                    if (field.IsDefined(typeof(NonSerializedAttribute), false))
                        continue;
                    if (!field.IsPublic && !field.IsDefined(typeof(SerializeField), false))
                        continue;
                    if (mention.Kids.ContainsKey(field.Name))
                        continue;
                    ResetField(target, field);
                }
                type = type.BaseType;
            }
        }
        else
        {
            PropertyInfo[] properties = target.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
            for (int i = 0; i < properties.Length; i++)
            {
                PropertyInfo property = properties[i];
                if (!IncludeProperty(property) || mention.Kids.ContainsKey(property.Name))
                    continue;
                try
                {
                    object blank = property.PropertyType.IsValueType ? Activator.CreateInstance(property.PropertyType) : null;
                    property.SetValue(target, blank, null);
                }
                catch (Exception)
                {
                }
            }
        }

        foreach (KeyValuePair<string, Mention> pair in mention.Kids)
        {
            if (pair.Value.Kids.Count == 0)
                continue;
            object child = ReadMember(target, pair.Key);
            if (child != null && !(child is string))
                PruneMembers(child, pair.Value);
        }
    }

    static void ResetField(object target, FieldInfo field)
    {
        try
        {
            object blank = field.FieldType.IsValueType ? Activator.CreateInstance(field.FieldType) : null;
            field.SetValue(target, blank);
        }
        catch (Exception)
        {
        }
    }

    sealed class JsonParser
    {
        readonly string input;
        int at;

        JsonParser(string input)
        {
            this.input = input ?? "";
            if (this.input.Length > 0 && this.input[0] == '\uFEFF')
                at = 1;
        }

        public static object Parse(string input)
        {
            var parser = new JsonParser(input);
            parser.Skip();
            object value = parser.ReadValue();
            parser.Skip();
            if (parser.at < parser.input.Length)
                throw new ArgumentException("U-NOR JSON has trailing data at " + parser.at + ".");
            return value;
        }

        object ReadValue()
        {
            Skip();
            if (at >= input.Length)
                throw new ArgumentException("U-NOR JSON ended early.");
            char c = input[at];
            if (c == '{')
                return ReadObject();
            if (c == '[')
                return ReadArray();
            if (c == '"')
                return ReadString();
            if (c == 't' || c == 'f')
                return ReadKeyword();
            if (c == 'n')
                return ReadNull();
            return ReadNumber();
        }

        JsonMap ReadObject()
        {
            Expect('{');
            var map = new JsonMap();
            Skip();
            if (Peek('}'))
            {
                at++;
                return map;
            }
            while (true)
            {
                Skip();
                string key = ReadString();
                Skip();
                Expect(':');
                object value = ReadValue();
                map.Add(key, value);
                Skip();
                if (Peek(','))
                {
                    at++;
                    continue;
                }
                Expect('}');
                break;
            }
            return map;
        }

        List<object> ReadArray()
        {
            Expect('[');
            var list = new List<object>();
            Skip();
            if (Peek(']'))
            {
                at++;
                return list;
            }
            while (true)
            {
                list.Add(ReadValue());
                Skip();
                if (Peek(','))
                {
                    at++;
                    continue;
                }
                Expect(']');
                break;
            }
            return list;
        }

        string ReadString()
        {
            Expect('"');
            var builder = new StringBuilder();
            while (at < input.Length)
            {
                char c = input[at++];
                if (c == '"')
                    return builder.ToString();
                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }
                if (at >= input.Length)
                    break;
                char escaped = input[at++];
                switch (escaped)
                {
                    case '"':
                    case '\\':
                    case '/':
                        builder.Append(escaped);
                        break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (at + 4 > input.Length)
                            throw new ArgumentException("U-NOR JSON has a broken unicode escape.");
                        int code = int.Parse(input.Substring(at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        builder.Append((char)code);
                        at += 4;
                        break;
                    default:
                        throw new ArgumentException("U-NOR JSON has an unknown escape.");
                }
            }
            throw new ArgumentException("U-NOR JSON string did not end.");
        }

        object ReadKeyword()
        {
            if (Match("true"))
                return true;
            if (Match("false"))
                return false;
            throw new ArgumentException("U-NOR JSON has an invalid literal.");
        }

        object ReadNull()
        {
            if (!Match("null"))
                throw new ArgumentException("U-NOR JSON has an invalid literal.");
            return null;
        }

        bool Match(string word)
        {
            if (at + word.Length > input.Length)
                return false;
            if (string.CompareOrdinal(input, at, word, 0, word.Length) != 0)
                return false;
            at += word.Length;
            return true;
        }

        object ReadNumber()
        {
            int start = at;
            if (Peek('-'))
                at++;
            while (at < input.Length && (char.IsDigit(input[at]) || input[at] == '.' || input[at] == 'e' || input[at] == 'E' || input[at] == '+'))
                at++;
            string token = input.Substring(start, at - start);
            double number;
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                throw new ArgumentException("U-NOR JSON has an invalid number.");
            return number;
        }

        void Skip()
        {
            while (at < input.Length && char.IsWhiteSpace(input[at]))
                at++;
        }

        bool Peek(char expected)
        {
            return at < input.Length && input[at] == expected;
        }

        void Expect(char expected)
        {
            Skip();
            if (at >= input.Length || input[at] != expected)
                throw new ArgumentException("U-NOR JSON expected '" + expected + "'.");
            at++;
        }
    }
}

static class RemoraUnorCommands
{
    static readonly string[] Verbs = { "select", "debug", "inspect", "type", "modify", "create", "delete", "exists", "deser", "seri", "spawn" };
    static readonly Dictionary<string, object> handles = new Dictionary<string, object>();
    static int nextHandle = 1;
    static MethodInfo sendText;
    static MethodInfo manInvoke;
    static bool registered;
    static bool announced;
    static bool announceScheduled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void RegisterRuntime()
    {
        Register();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AnnounceRuntime()
    {
        Announce();
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

        Type remora = FindType("RemoraDev");
        if (remora == null)
            return;
        Type console = remora.GetNestedType("Console", BindingFlags.Public);
        if (console == null)
            return;
        Type cmds = console.GetNestedType("Cmds", BindingFlags.Public);
        if (cmds == null)
            return;

        sendText = console.GetMethod("SendTextIn", BindingFlags.Public | BindingFlags.Static);
        manInvoke = cmds.GetMethod("ManInvoke", BindingFlags.Public | BindingFlags.Static);
        MethodInfo register = null;
        MethodInfo[] methods = cmds.GetMethods(BindingFlags.Public | BindingFlags.Static);
        for (int i = 0; i < methods.Length; i++)
        {
            if (methods[i].Name != "Register")
                continue;
            ParameterInfo[] parameters = methods[i].GetParameters();
            if (parameters.Length == 4 && parameters[1].ParameterType == typeof(Delegate))
            {
                register = methods[i];
                break;
            }
        }
        if (register == null)
            return;

        Type helpType = cmds.GetNestedType("Help", BindingFlags.Public);
        Type argType = cmds.GetNestedType("Arg", BindingFlags.Public);
        if (helpType == null || argType == null)
            return;

        object help = Activator.CreateInstance(helpType);
        helpType.GetField("Description").SetValue(help, "Inspect and edit Unity objects with U-NOR. select, debug, inspect, type, modify, create, delete, exists, deser, seri, and spawn.");
        Array args = Array.CreateInstance(argType, 6);
        args.SetValue(Arg(argType, "action", "select|debug|inspect|type|modify|create|delete|exists|deser|seri|spawn", "Subcommand.", false, true), 0);
        args.SetValue(Arg(argType, "args", "args", "Subcommand arguments. Quote values that contain spaces.", true, true), 1);
        args.SetValue(Arg(argType, "single", "--single", "select: return the first match.", false, true), 2);
        args.SetValue(Arg(argType, "handle", "--handle", "select and create: return a unor:# handle.", false, true), 3);
        args.SetValue(Arg(argType, "rem", "--rem", "modify: remove anything the diff does not set.", false, true), 4);
        args.SetValue(Arg(argType, "spawn", "--spawn", "deser: place the result in the scene.", false, true), 5);
        helpType.GetField("Args").SetValue(help, args);

        Delegate handler = new Func<string, string[], bool, bool, bool, bool, object>(Unor);
        try
        {
            register.Invoke(null, new object[] { "unor", handler, help, true });
            registered = true;
            Announce();
        }
        catch (Exception exception)
        {
            Debug.LogWarning("U-NOR console commands were not registered: " + exception.Message);
        }
    }

    static void Announce()
    {
        if (!registered || announced || manInvoke == null || !Application.isPlaying)
            return;
        if (!ConsoleReady())
        {
            if (announceScheduled)
                return;
            announceScheduled = true;
            var marker = new GameObject("Remora U-NOR");
            marker.hideFlags = HideFlags.HideAndDontSave;
            marker.AddComponent<RemoraUnorAnnounce>();
            return;
        }

        try
        {
            manInvoke.Invoke(null, new object[] { "print", new object[] { "\u001b[32mLoaded U-NOR (v.0.1)\u001b[m" } });
            announced = true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("U-NOR load line was not printed: " + exception.Message);
        }
    }

    static bool ConsoleReady()
    {
        Type type = FindType("RemoraDevConsole");
        if (type == null)
            return false;
        FieldInfo instance = type.GetField("instance", BindingFlags.NonPublic | BindingFlags.Static);
        object console = instance != null ? instance.GetValue(null) : null;
        if (console == null)
            return false;
        FieldInfo log = type.GetField("logText", BindingFlags.NonPublic | BindingFlags.Instance);
        return log != null && log.GetValue(console) != null;
    }

    static object Arg(Type argType, string name, string type, string description, bool variadic, bool optional)
    {
        object arg = Activator.CreateInstance(argType);
        argType.GetField("Name").SetValue(arg, name);
        argType.GetField("Type").SetValue(arg, type);
        argType.GetField("Description").SetValue(arg, description);
        argType.GetField("Variadic").SetValue(arg, variadic);
        argType.GetField("Optional").SetValue(arg, optional);
        return arg;
    }

    static object Unor(string action = null, string[] args = null, bool single = false, bool handle = false, bool rem = false, bool spawn = false)
    {
        var tokens = new List<string>();
        if (!string.IsNullOrEmpty(action))
            tokens.Add(action);
        if (args != null)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.IsNullOrEmpty(args[i]))
                    tokens.Add(args[i]);
            }
        }
        if (tokens.Count == 0)
            throw new InvalidOperationException(Usage());

        int verbAt = -1;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (IsVerb(tokens[i]))
            {
                verbAt = i;
                break;
            }
        }
        if (verbAt < 0)
            throw new InvalidOperationException("unknown action \"" + tokens[0] + "\"\n" + Usage());

        string verb = tokens[verbAt].ToLowerInvariant();
        string piped = null;
        if (verbAt > 0)
        {
            var leading = new string[verbAt];
            for (int i = 0; i < verbAt; i++)
                leading[i] = tokens[i];
            piped = string.Join("\n", leading);
        }

        var rest = new List<string>();
        for (int i = verbAt + 1; i < tokens.Count; i++)
            rest.Add(tokens[i]);
        var positional = new List<string>();
        if (!string.IsNullOrEmpty(piped))
            positional.Add(piped);
        positional.AddRange(rest);
        return Dispatch(verb, positional, single, handle, rem, spawn);
    }

    static object Dispatch(string verb, List<string> args, bool single, bool handle, bool rem, bool spawn)
    {
        if (verb != "select" && single)
            throw new InvalidOperationException(verb + ": --single is not valid");
        if (verb != "select" && verb != "create" && handle)
            throw new InvalidOperationException(verb + ": --handle is not valid");
        if (verb != "modify" && rem)
            throw new InvalidOperationException(verb + ": --rem is not valid");
        if (verb != "deser" && spawn)
            throw new InvalidOperationException(verb + ": --spawn is not valid");

        switch (verb)
        {
            case "select": return Select(args, single, handle);
            case "debug": return DebugSelector(args);
            case "inspect": return InspectCommand(args);
            case "type": return TypeCommand(args);
            case "modify": return Modify(args, rem);
            case "create": return Create(args, handle);
            case "delete": return Delete(args);
            case "exists": return Exists(args);
            case "deser": return Deser(args, spawn);
            case "seri": return Seri(args);
            case "spawn": return Spawn(args);
            default:
                throw new InvalidOperationException("unknown action \"" + verb + "\"\n" + Usage());
        }
    }

    static object Select(List<string> args, bool single, bool handle)
    {
        string selector = Required(args, 0, "selector");
        NoExtra(args, 1, "select");
        List<object> matches = ResolveMany(selector, !single);
        if (handle)
        {
            string id = Keep(matches.Count == 1 ? matches[0] : (object)matches.ToArray());
            return Say(id);
        }

        var lines = new StringBuilder();
        for (int i = 0; i < matches.Count; i++)
        {
            if (i > 0)
                lines.Append('\n');
            lines.Append(SceneSelector(matches[i]));
        }
        return Say(lines.ToString());
    }

    static object DebugSelector(List<string> args)
    {
        if (args.Count == 0 || !string.Equals(args[0], "selector", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("debug: usage is unor debug selector <selector>");
        string selector = Required(args, 1, "selector");
        NoExtra(args, 2, "debug");
        List<object> matches = ResolveMany(selector, true);
        var text = new StringBuilder();
        text.Append("selector ").Append(selector);
        text.Append("\nmatches ").Append(matches.Count.ToString(CultureInfo.InvariantCulture));
        for (int i = 0; i < matches.Count; i++)
        {
            object item = matches[i];
            text.Append("\n[").Append(i.ToString(CultureInfo.InvariantCulture)).Append("] ");
            text.Append(TypeName(item)).Append(' ').Append(SceneSelector(item));
            GameObject go = Host(item);
            if (go != null)
            {
                text.Append(" active=").Append(go.activeSelf ? "true" : "false");
                if (go.scene.IsValid())
                    text.Append(" scene=").Append(go.scene.name);
            }
        }
        return Say(text.ToString());
    }

    static object InspectCommand(List<string> args)
    {
        string target = Required(args, 0, "selector|handle");
        int depth = 1;
        if (args.Count > 1)
        {
            if (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out depth) || depth < 0)
                throw new InvalidOperationException("inspect: depth must be a whole number");
        }
        NoExtra(args, args.Count > 1 ? 2 : 1, "inspect");
        List<object> matches = ResolveMany(target, true);
        if (matches.Count != 1)
            throw new InvalidOperationException("inspect: selector matched " + matches.Count.ToString(CultureInfo.InvariantCulture) + " objects");
        var text = new StringBuilder();
        WriteInspect(text, matches[0], depth, 0);
        return Say(text.ToString());
    }

    static object TypeCommand(List<string> args)
    {
        string target = Required(args, 0, "selector|handle");
        NoExtra(args, 1, "type");
        List<object> matches = ResolveMany(target, true);
        var text = new StringBuilder();
        for (int i = 0; i < matches.Count; i++)
        {
            if (i > 0)
                text.Append('\n');
            text.Append(TypeName(matches[i]));
        }
        return Say(text.ToString());
    }

    static object Modify(List<string> args, bool rem)
    {
        string target = Required(args, 0, "selector|handle");
        string data = Required(args, 1, "diffdata|diffdata-file");
        NoExtra(args, 2, "modify");
        List<object> matches = ResolveMany(target, true);
        string diff = LoadData(data);
        for (int i = 0; i < matches.Count; i++)
        {
            if (!(matches[i] is GameObject) && !(matches[i] is Component))
                throw new InvalidOperationException("modify: target is " + TypeName(matches[i]) + " and cannot take a diff");
            try
            {
                UNOR.ApplyDiff(matches[i], diff, rem);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("modify: " + exception.Message);
            }
        }
        return Say("modified " + matches.Count.ToString(CultureInfo.InvariantCulture));
    }

    static object Create(List<string> args, bool handle)
    {
        string data = Required(args, 0, "data|data-file");
        NoExtra(args, 1, "create");
        object created = Build(LoadData(data));
        object placed = PlaceAll(created, null);
        if (handle)
            return Say(Keep(placed));
        return Say(DescribePlaced(placed));
    }

    static object Delete(List<string> args)
    {
        string target = Required(args, 0, "selector|handle");
        NoExtra(args, 1, "delete");
        bool fromHandle = target.Trim().StartsWith("unor:#");
        List<object> matches = ResolveMany(target, true);
        for (int i = 0; i < matches.Count; i++)
        {
            Component component = matches[i] as Component;
            GameObject go = matches[i] as GameObject;
            if (go == null && component != null && !(component is Transform))
            {
                Forget(component);
                UnityEngine.Object.Destroy(component);
                continue;
            }
            if (go == null && component != null)
                go = component.gameObject;
            if (go == null)
                throw new InvalidOperationException("delete: target is " + TypeName(matches[i]) + " and cannot be deleted");
            Forget(go);
            UnityEngine.Object.Destroy(go);
        }
        if (fromHandle)
            handles.Remove(target.Trim());
        return Say("deleted " + matches.Count.ToString(CultureInfo.InvariantCulture));
    }

    static object Exists(List<string> args)
    {
        string target = Required(args, 0, "selector|handle");
        NoExtra(args, 1, "exists");
        string token = target.Trim();
        if (token.IndexOf('@') >= 0)
            throw new InvalidOperationException("selector does not allow @ \"" + token + "\"");
        if (token.StartsWith("unor:#"))
        {
            object stored;
            if (!handles.TryGetValue(token, out stored) || Dead(stored))
                throw new InvalidOperationException("unknown handle \"" + token + "\"");
            return Say("true");
        }
        object found = UNOR.EvaluateSelector(token, true);
        return Say(found == null ? "false" : "true");
    }

    static object Deser(List<string> args, bool spawn)
    {
        string data = Required(args, 0, "data|data-file");
        NoExtra(args, 1, "deser");
        object created = Build(LoadData(data));
        if (spawn)
            created = PlaceAll(created, null);
        if (created is GameObject || created is Component || created is object[])
            return Say(Keep(created));
        return Say(UNOR.Serialize(created));
    }

    static object Seri(List<string> args)
    {
        string target = Required(args, 0, "object");
        NoExtra(args, 1, "seri");
        List<object> matches = ResolveMany(target, true);
        object value = matches.Count == 1 ? matches[0] : (object)matches.ToArray();
        try
        {
            return Say(UNOR.Serialize(value));
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("seri: " + exception.Message);
        }
    }

    static object Spawn(List<string> args)
    {
        string target = Required(args, 0, "object");
        string where = args.Count > 1 ? args[1] : null;
        NoExtra(args, string.IsNullOrEmpty(where) ? 1 : 2, "spawn");
        List<object> matches = ResolveMany(target, true);
        if (matches.Count != 1)
            throw new InvalidOperationException("spawn: selector matched " + matches.Count.ToString(CultureInfo.InvariantCulture) + " objects");
        GameObject parent = null;
        if (!string.IsNullOrEmpty(where))
        {
            List<object> parents = ResolveMany(where, true);
            if (parents.Count != 1)
                throw new InvalidOperationException("spawn: where-selector matched " + parents.Count.ToString(CultureInfo.InvariantCulture) + " objects");
            parent = Host(parents[0]);
            if (parent == null)
                throw new InvalidOperationException("spawn: where-selector is not a GameObject");
        }
        object placed = PlaceAll(matches[0], parent);
        return Say(DescribePlaced(placed));
    }

    static object Build(string data)
    {
        try
        {
            object created = UNOR.Deserialize(data);
            if (created == null)
                throw new InvalidOperationException("data did not build an object");
            return created;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(exception.Message);
        }
    }

    static object PlaceAll(object created, GameObject parent)
    {
        object[] many = created as object[];
        if (many != null)
        {
            var placed = new object[many.Length];
            for (int i = 0; i < many.Length; i++)
                placed[i] = PlaceOne(many[i], parent);
            return placed.Length == 1 ? placed[0] : (object)placed;
        }
        return PlaceOne(created, parent);
    }

    static object PlaceOne(object value, GameObject parent)
    {
        GameObject go = Host(value);
        if (go == null)
            throw new InvalidOperationException("data did not build a GameObject");
        if (!go.scene.IsValid())
        {
            GameObject clone = UnityEngine.Object.Instantiate(go);
            clone.name = go.name;
            go = clone;
        }
        if (parent != null)
            go.transform.SetParent(parent.transform, false);
        Transform[] tree = go.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < tree.Length; i++)
        {
            if ((tree[i].gameObject.hideFlags & HideFlags.HideAndDontSave) != 0)
                tree[i].gameObject.hideFlags = HideFlags.None;
        }
        go.SetActive(true);
        return value is Component && Host(value) == go ? value : go;
    }

    static string DescribePlaced(object value)
    {
        object[] many = value as object[];
        if (many == null)
            return SceneSelector(value);
        var text = new StringBuilder();
        for (int i = 0; i < many.Length; i++)
        {
            if (i > 0)
                text.Append('\n');
            text.Append(SceneSelector(many[i]));
        }
        return text.ToString();
    }

    static void WriteInspect(StringBuilder text, object value, int depth, int indent)
    {
        Pad(text, indent);
        text.Append(TypeName(value));
        text.Append(' ');
        text.Append(LabelOf(value));
        if (depth <= 0)
            return;

        GameObject go = value as GameObject;
        if (go != null)
        {
            Line(text, indent + 1, "name = " + go.name);
            Line(text, indent + 1, "tag = " + go.tag);
            string layer = LayerMask.LayerToName(go.layer);
            Line(text, indent + 1, "layer = " + (string.IsNullOrEmpty(layer) ? go.layer.ToString(CultureInfo.InvariantCulture) : layer));
            Line(text, indent + 1, "active = " + (go.activeSelf ? "true" : "false"));
            Component[] components = go.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null)
                    continue;
                if (depth <= 1)
                    Line(text, indent + 1, "(" + components[i].GetType().Name + ")");
                else
                    WriteInspect(text, components[i], depth - 1, indent + 1);
            }
            for (int i = 0; i < go.transform.childCount; i++)
            {
                GameObject child = go.transform.GetChild(i).gameObject;
                if (depth <= 1)
                    Line(text, indent + 1, child.name);
                else
                    WriteInspect(text, child, depth - 1, indent + 1);
            }
            return;
        }

        if (value is Component || (value != null && !(value is string) && !(value is UnityEngine.Object)))
            WriteMembers(text, value, depth, indent);
    }

    static void WriteMembers(StringBuilder text, object value, int depth, int indent)
    {
        if (value == null || depth <= 0)
            return;
        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        PropertyInfo[] properties = value.GetType().GetProperties(flags);
        for (int i = 0; i < properties.Length; i++)
        {
            PropertyInfo property = properties[i];
            if (!property.CanRead || property.GetIndexParameters().Length > 0 || property.GetMethod.IsStatic)
                continue;
            if (property.DeclaringType == typeof(UnityEngine.Object) || property.DeclaringType == typeof(Component))
                continue;
            object member;
            try
            {
                member = property.GetValue(value, null);
            }
            catch (Exception)
            {
                continue;
            }
            WriteMember(text, property.Name, member, depth, indent);
        }
        FieldInfo[] fields = value.GetType().GetFields(flags);
        for (int i = 0; i < fields.Length; i++)
        {
            if (fields[i].IsStatic)
                continue;
            WriteMember(text, fields[i].Name, fields[i].GetValue(value), depth, indent);
        }
    }

    static void WriteMember(StringBuilder text, string name, object member, int depth, int indent)
    {
        if (depth > 1 && member != null && !(member is string) && !(member is UnityEngine.Object) && !member.GetType().IsPrimitive && !member.GetType().IsEnum)
        {
            Line(text, indent + 1, name);
            WriteInspect(text, member, depth - 1, indent + 1);
            return;
        }
        Line(text, indent + 1, name + " = " + Literal(member));
    }

    static string Literal(object value)
    {
        if (value == null)
            return "null";
        if (value is string)
            return "\"" + value + "\"";
        if (value is bool)
            return (bool)value ? "true" : "false";
        if (value is float || value is double)
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        if (value is UnityEngine.Object)
            return SceneSelector(value);
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? value.GetType().Name;
    }

    static void Line(StringBuilder text, int indent, string line)
    {
        text.Append('\n');
        Pad(text, indent);
        text.Append(line);
    }

    static void Pad(StringBuilder text, int indent)
    {
        for (int i = 0; i < indent; i++)
            text.Append("  ");
    }

    static List<object> ResolveMany(string token, bool multi)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("missing <selector>");
        token = token.Trim();
        if (token.StartsWith("unor:#"))
        {
            object stored;
            if (!handles.TryGetValue(token, out stored) || Dead(stored))
                throw new InvalidOperationException("unknown handle \"" + token + "\"");
            List<object> storedMatches = Expand(stored);
            if (!multi && storedMatches.Count > 1)
                return new List<object> { storedMatches[0] };
            return storedMatches;
        }
        if (token.IndexOf('@') >= 0)
            throw new InvalidOperationException("selector does not allow @ \"" + token + "\"");
        object found;
        try
        {
            found = UNOR.EvaluateSelector(token, multi);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("selector \"" + token + "\": " + exception.Message);
        }
        List<object> matches = Expand(found);
        if (matches.Count == 0)
            throw new InvalidOperationException("selector matched nothing \"" + token + "\"");
        return matches;
    }

    static List<object> Expand(object value)
    {
        var list = new List<object>();
        object[] many = value as object[];
        if (many != null)
        {
            for (int i = 0; i < many.Length; i++)
            {
                if (many[i] != null && !Dead(many[i]))
                    list.Add(many[i]);
            }
            return list;
        }
        if (value != null && !Dead(value))
            list.Add(value);
        return list;
    }

    static string Keep(object value)
    {
        string id = "unor:#" + nextHandle.ToString(CultureInfo.InvariantCulture);
        nextHandle++;
        handles[id] = value;
        return id;
    }

    static void Forget(object value)
    {
        var remove = new List<string>();
        foreach (KeyValuePair<string, object> pair in handles)
        {
            if (ReferenceEquals(pair.Value, value))
                remove.Add(pair.Key);
        }
        for (int i = 0; i < remove.Count; i++)
            handles.Remove(remove[i]);
    }

    static bool Dead(object value)
    {
        if (!(value is UnityEngine.Object))
            return false;
        return (UnityEngine.Object)value == null;
    }

    static GameObject Host(object value)
    {
        GameObject go = value as GameObject;
        if (go != null)
            return go;
        Component component = value as Component;
        if (component != null)
            return component.gameObject;
        return null;
    }

    static string TypeName(object value)
    {
        if (value == null)
            return "null";
        return value.GetType().FullName ?? value.GetType().Name;
    }

    static string LabelOf(object value)
    {
        GameObject go = value as GameObject;
        if (go != null)
            return go.name;
        Component component = value as Component;
        if (component != null)
            return component.GetType().Name;
        return Literal(value);
    }

    static string SceneSelector(object value)
    {
        Component component = value as Component;
        GameObject go = value as GameObject;
        if (go == null && component != null)
            go = component.gameObject;
        if (go == null || !go.scene.IsValid())
            return TypeName(value);
        var names = new List<string>();
        Transform cursor = go.transform;
        while (cursor != null)
        {
            names.Add(cursor.name);
            cursor = cursor.parent;
        }
        names.Reverse();
        string path = string.Join(".", names.ToArray());
        if (component != null)
            path += ".(" + component.GetType().Name + ")";
        return "<scene:" + path + ">";
    }

    static string LoadData(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("missing <data>");
        string text = token.Trim();
        if (text[0] == '{' || text[0] == '[' || text.IndexOf('=') >= 0)
            return text;
        if (!IsFileToken(text))
            return text;
        return ReadUnorFile(text);
    }

    static bool IsFileToken(string text)
    {
        if (text.IndexOf(' ') >= 0 || text.IndexOf('\n') >= 0 || text.IndexOf('=') >= 0 || text.IndexOf('{') >= 0)
            return false;
        if (text.EndsWith(".unor", StringComparison.OrdinalIgnoreCase) || text.EndsWith(".unor.json", StringComparison.OrdinalIgnoreCase))
            return true;
        return text.IndexOf('/') >= 0 || text.IndexOf('\\') >= 0;
    }

    static string ReadUnorFile(string file)
    {
        string relative = CleanRelative(file);
        string[] names = FileCandidates(relative);
        string[] folders = RemoraFolders();
        for (int f = 0; f < folders.Length; f++)
        {
            if (string.IsNullOrEmpty(folders[f]) || !Directory.Exists(folders[f]))
                continue;
            for (int n = 0; n < names.Length; n++)
            {
                string path = folders[f];
                string[] parts = names[n].Split('/');
                for (int p = 0; p < parts.Length; p++)
                    path = Path.Combine(path, parts[p]);
                if (File.Exists(path))
                    return File.ReadAllText(path);
            }
        }
        for (int n = 0; n < names.Length; n++)
        {
            string resourceName = StripDataExtension(names[n]);
            TextAsset asset = Resources.Load<TextAsset>("remora/" + resourceName);
            if (asset == null)
                asset = Resources.Load<TextAsset>("Remora/" + resourceName);
            if (asset != null)
                return asset.text ?? "";
        }
        throw new InvalidOperationException("\"" + relative + "\" was not found in the remora folder");
    }

    static string[] FileCandidates(string relative)
    {
        if (relative.EndsWith(".unor.json", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".unor", StringComparison.OrdinalIgnoreCase))
            return new[] { relative };
        return new[] { relative + ".unor", relative + ".unor.json" };
    }

    static string StripDataExtension(string relative)
    {
        if (relative.EndsWith(".unor.json", StringComparison.OrdinalIgnoreCase))
            return relative.Substring(0, relative.Length - 10);
        if (relative.EndsWith(".unor", StringComparison.OrdinalIgnoreCase))
            return relative.Substring(0, relative.Length - 5);
        return relative;
    }

    static string CleanRelative(string file)
    {
        if (string.IsNullOrWhiteSpace(file))
            throw new InvalidOperationException("missing <data-file>");
        file = file.Trim().Replace('\\', '/');
        if (file.Length == 0 || file[0] == '/' || file.IndexOf(':') >= 0)
            throw new InvalidOperationException("\"" + file + "\" is not a path inside the remora folder");
        string[] parts = file.Split('/');
        var clean = new List<string>();
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0 || parts[i] == ".")
                continue;
            if (parts[i] == "..")
                throw new InvalidOperationException("\"" + file + "\" is not a path inside the remora folder");
            clean.Add(parts[i]);
        }
        if (clean.Count == 0)
            throw new InvalidOperationException("missing <data-file>");
        return string.Join("/", clean.ToArray());
    }

    static string[] RemoraFolders()
    {
        string root = Path.GetDirectoryName(Application.dataPath);
        string streaming = Application.streamingAssetsPath;
        return new[]
        {
            string.IsNullOrEmpty(root) ? null : Path.Combine(root, "remora"),
            string.IsNullOrEmpty(root) ? null : Path.Combine(root, "Remora"),
            string.IsNullOrEmpty(streaming) ? null : Path.Combine(streaming, "remora"),
            string.IsNullOrEmpty(streaming) ? null : Path.Combine(streaming, "Remora")
        };
    }

    static string Required(List<string> args, int index, string name)
    {
        if (index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
            throw new InvalidOperationException("missing <" + name + ">");
        return args[index];
    }

    static void NoExtra(List<string> args, int count, string verb)
    {
        if (args.Count > count)
            throw new InvalidOperationException(verb + ": unexpected \"" + args[count] + "\"");
    }

    static bool IsVerb(string token)
    {
        for (int i = 0; i < Verbs.Length; i++)
        {
            if (string.Equals(Verbs[i], token, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    static string Say(string text)
    {
        if (sendText != null)
            sendText.Invoke(null, new object[] { text ?? "", true });
        else
            Debug.Log(text ?? "");
        return text ?? "";
    }

    static string Usage()
    {
        return "usage: unor <select|debug selector|inspect|type|modify|create|delete|exists|deser|seri|spawn>";
    }

    static Type FindType(string name)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type type = assemblies[i].GetType(name, false);
            if (type != null)
                return type;
        }
        return null;
    }

    internal static void ContinueAnnounce()
    {
        announceScheduled = false;
        Announce();
    }
}

public sealed class RemoraUnorAnnounce : MonoBehaviour
{
    void Start()
    {
        RemoraUnorCommands.ContinueAnnounce();
        Destroy(gameObject);
    }
}
