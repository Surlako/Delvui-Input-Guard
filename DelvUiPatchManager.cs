using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using Dalamud.Bindings.ImGui;
using HarmonyLib;

namespace DelvUIInputGuard;

internal sealed class DelvUiPatchManager : IDisposable
{
    private const string HarmonyId = "Surlako.DelvUIInputGuard";
    private static readonly Dictionary<MethodSignature, MethodInfo> ReplacementMethods = BuildReplacementMethods();
    private static readonly OpCode[] OneByteOpCodes = new OpCode[0x100];
    private static readonly OpCode[] TwoByteOpCodes = new OpCode[0x100];

    private readonly Harmony harmony = new(HarmonyId);
    private readonly object syncRoot = new();
    private readonly List<string> patchedMethods = new();

    private Assembly? delvUiAssembly;
    private DateTime nextAttachAttemptUtc = DateTime.MinValue;
    private bool disposed;

    public bool IsInstalled { get; private set; }
    public bool IsLoaded { get; private set; }
    public bool IsAttached => delvUiAssembly is not null;
    public string DetectedVersion { get; private set; } = "Not detected";
    public int PatchedMethodCount { get; private set; }
    public string LastError { get; private set; } = string.Empty;
    public IReadOnlyList<string> PatchedMethods => patchedMethods;

    static DelvUiPatchManager()
    {
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opCode)
                continue;

            var value = unchecked((ushort)opCode.Value);
            if (value < 0x100)
                OneByteOpCodes[value] = opCode;
            else if ((value & 0xff00) == 0xfe00)
                TwoByteOpCodes[value & 0xff] = opCode;
        }
    }

    public DelvUiPatchManager()
    {
        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
    }

    public void TryAttach(bool force = false)
    {
        if (disposed)
            return;

        RefreshPluginStatus();

        if (IsAttached)
            return;

        if (!force && DateTime.UtcNow < nextAttachAttemptUtc)
            return;

        nextAttachAttemptUtc = DateTime.UtcNow.AddSeconds(2);

        var assembly = AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(IsDelvUiAssembly);

        if (assembly is null)
        {
            LastError = IsInstalled
                ? "DelvUI is installed, but its runtime assembly is not loaded yet."
                : "Waiting for DelvUI to be installed and loaded.";
            return;
        }

        Attach(assembly);
    }

    private void RefreshPluginStatus()
    {
        try
        {
            var plugin = Plugin.PluginInterface.InstalledPlugins.FirstOrDefault(candidate =>
                string.Equals(candidate.InternalName, "DelvUI", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.Name, "DelvUI", StringComparison.OrdinalIgnoreCase));

            IsInstalled = plugin is not null;
            IsLoaded = plugin?.IsLoaded == true;
            if (plugin is not null)
                DetectedVersion = plugin.Version.ToString();
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "Could not read DelvUI from Dalamud's installed-plugin list.");
        }
    }

    private bool IsDelvUiAssembly(Assembly assembly)
    {
        try
        {
            var plugin = Plugin.PluginInterface.GetPlugin(assembly);
            if (plugin is not null &&
                (string.Equals(plugin.InternalName, "DelvUI", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(plugin.Name, "DelvUI", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        catch
        {
            // Fall through to assembly/type-name checks.
        }

        if (string.Equals(assembly.GetName().Name, "DelvUI", StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            return GetLoadableTypes(assembly).Any(type =>
                type.FullName?.StartsWith("DelvUI.", StringComparison.Ordinal) == true);
        }
        catch
        {
            return false;
        }
    }

    private void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        if (disposed || IsAttached)
            return;

        if (IsDelvUiAssembly(args.LoadedAssembly))
            Attach(args.LoadedAssembly);
    }

    private void Attach(Assembly assembly)
    {
        lock (syncRoot)
        {
            if (disposed || IsAttached)
                return;

            try
            {
                LastError = string.Empty;
                patchedMethods.Clear();

                var transpiler = new HarmonyMethod(typeof(DelvUiPatchManager), nameof(TranspileMouseQueries));
                var methods = GetLoadableTypes(assembly)
                    .Where(IsRuntimeInteractionType)
                    .SelectMany(GetPatchableMethods)
                    .Distinct(MethodBaseComparer.Instance)
                    .ToArray();

                foreach (var method in methods)
                {
                    if (!ReferencesGuardedMouseQuery(method))
                        continue;

                    try
                    {
                        harmony.Patch(method, transpiler: transpiler);
                        patchedMethods.Add($"{method.DeclaringType?.FullName}.{method.Name}");
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Warning(ex, "Could not patch DelvUI method {Method}", method);
                    }
                }

                if (patchedMethods.Count == 0)
                    throw new InvalidOperationException("DelvUI was found, but no compatible mouse-query methods were found.");

                delvUiAssembly = assembly;
                IsInstalled = true;
                IsLoaded = true;
                DetectedVersion = assembly.GetName().Version?.ToString() ?? DetectedVersion;
                PatchedMethodCount = patchedMethods.Count;
                Plugin.Log.Information(
                    "DelvUI Input Guard attached to DelvUI {Version}; patched {Count} methods.",
                    DetectedVersion,
                    PatchedMethodCount
                );
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Plugin.Log.Error(ex, "Failed to attach DelvUI Input Guard.");
                try
                {
                    harmony.UnpatchAll(HarmonyId);
                }
                catch
                {
                    // ignored
                }
            }
        }
    }


    private static bool IsRuntimeInteractionType(Type type)
    {
        var typeNamespace = type.Namespace ?? string.Empty;

        // DelvUI configuration windows are ordinary ImGui interfaces and must
        // remain interactive. The guard only patches runtime HUD/input code.
        return !typeNamespace.StartsWith("DelvUI.Config", StringComparison.Ordinal);
    }

    private static IEnumerable<MethodBase> GetPatchableMethods(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        foreach (var method in type.GetMethods(flags))
        {
            if (!method.IsAbstract && !method.ContainsGenericParameters && method.GetMethodBody() is not null)
                yield return method;
        }

        foreach (var constructor in type.GetConstructors(flags))
        {
            if (!constructor.ContainsGenericParameters && constructor.GetMethodBody() is not null)
                yield return constructor;
        }
    }

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null).Cast<Type>().ToArray();
        }
    }

    private static bool ReferencesGuardedMouseQuery(MethodBase method)
    {
        var body = method.GetMethodBody();
        var il = body?.GetILAsByteArray();
        if (il is null || il.Length == 0)
            return false;

        var position = 0;
        while (position < il.Length)
        {
            var opCode = ReadOpCode(il, ref position);
            if (opCode.Size == 0)
                return false;

            if (opCode.OperandType == OperandType.InlineMethod)
            {
                if (position + 4 > il.Length)
                    return false;

                var token = BitConverter.ToInt32(il, position);
                try
                {
                    var resolved = method.Module.ResolveMethod(
                        token,
                        method.DeclaringType?.GetGenericArguments(),
                        method.IsGenericMethod ? method.GetGenericArguments() : null
                    );

                    if (resolved is MethodInfo resolvedMethod &&
                        ReplacementMethods.ContainsKey(MethodSignature.From(resolvedMethod)))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Ignore unresolved metadata tokens and continue scanning.
                }
            }

            position += GetOperandSize(opCode.OperandType, il, position);
        }

        return false;
    }

    private static OpCode ReadOpCode(byte[] il, ref int position)
    {
        if (position >= il.Length)
            return default;

        var first = il[position++];
        if (first != 0xfe)
            return OneByteOpCodes[first];

        if (position >= il.Length)
            return default;

        return TwoByteOpCodes[il[position++]];
    }

    private static int GetOperandSize(OperandType operandType, byte[] il, int position)
    {
        return operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget => 1,
            OperandType.ShortInlineI => 1,
            OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI => 4,
            OperandType.InlineBrTarget => 4,
            OperandType.InlineField => 4,
            OperandType.InlineMethod => 4,
            OperandType.InlineSig => 4,
            OperandType.InlineString => 4,
            OperandType.InlineTok => 4,
            OperandType.InlineType => 4,
            OperandType.ShortInlineR => 4,
            OperandType.InlineI8 => 8,
            OperandType.InlineR => 8,
            OperandType.InlineSwitch => position + 4 <= il.Length
                ? 4 + Math.Max(0, BitConverter.ToInt32(il, position)) * 4
                : 0,
            _ => 0,
        };
    }

    public static IEnumerable<CodeInstruction> TranspileMouseQueries(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                instruction.operand is MethodInfo calledMethod &&
                ReplacementMethods.TryGetValue(MethodSignature.From(calledMethod), out var replacement))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
            }

            yield return instruction;
        }
    }

    private static Dictionary<MethodSignature, MethodInfo> BuildReplacementMethods()
    {
        var map = new Dictionary<MethodSignature, MethodInfo>();

        AddReplacement(map, nameof(ImGui.IsMouseHoveringRect), new[] { typeof(Vector2), typeof(Vector2) }, nameof(MouseQueryWrappers.IsMouseHoveringRect2));
        AddReplacement(map, nameof(ImGui.IsMouseHoveringRect), new[] { typeof(Vector2), typeof(Vector2), typeof(bool) }, nameof(MouseQueryWrappers.IsMouseHoveringRect3));
        AddReplacement(map, nameof(ImGui.IsItemHovered), Type.EmptyTypes, nameof(MouseQueryWrappers.IsItemHovered0));
        AddReplacement(map, nameof(ImGui.IsItemHovered), new[] { typeof(ImGuiHoveredFlags) }, nameof(MouseQueryWrappers.IsItemHovered1));
        AddReplacement(map, nameof(ImGui.IsWindowHovered), Type.EmptyTypes, nameof(MouseQueryWrappers.IsWindowHovered0));
        AddReplacement(map, nameof(ImGui.IsWindowHovered), new[] { typeof(ImGuiHoveredFlags) }, nameof(MouseQueryWrappers.IsWindowHovered1));
        AddReplacement(map, nameof(ImGui.IsAnyItemHovered), Type.EmptyTypes, nameof(MouseQueryWrappers.IsAnyItemHovered));
        AddReplacement(map, nameof(ImGui.IsItemClicked), Type.EmptyTypes, nameof(MouseQueryWrappers.IsItemClicked0));
        AddReplacement(map, nameof(ImGui.IsItemClicked), new[] { typeof(ImGuiMouseButton) }, nameof(MouseQueryWrappers.IsItemClicked1));
        AddReplacement(map, nameof(ImGui.IsMouseDown), new[] { typeof(ImGuiMouseButton) }, nameof(MouseQueryWrappers.IsMouseDown));
        AddReplacement(map, nameof(ImGui.IsMouseClicked), new[] { typeof(ImGuiMouseButton) }, nameof(MouseQueryWrappers.IsMouseClicked1));
        AddReplacement(map, nameof(ImGui.IsMouseClicked), new[] { typeof(ImGuiMouseButton), typeof(bool) }, nameof(MouseQueryWrappers.IsMouseClicked2));
        AddReplacement(map, nameof(ImGui.IsMouseReleased), new[] { typeof(ImGuiMouseButton) }, nameof(MouseQueryWrappers.IsMouseReleased));
        AddReplacement(map, nameof(ImGui.IsMouseDoubleClicked), new[] { typeof(ImGuiMouseButton) }, nameof(MouseQueryWrappers.IsMouseDoubleClicked));
        AddReplacement(map, nameof(ImGui.IsMouseDragging), new[] { typeof(ImGuiMouseButton) }, nameof(MouseQueryWrappers.IsMouseDragging1));
        AddReplacement(map, nameof(ImGui.IsMouseDragging), new[] { typeof(ImGuiMouseButton), typeof(float) }, nameof(MouseQueryWrappers.IsMouseDragging2));

        return map;
    }

    private static void AddReplacement(
        IDictionary<MethodSignature, MethodInfo> map,
        string targetName,
        Type[] targetParameters,
        string wrapperName)
    {
        var target = typeof(ImGui).GetMethod(
            targetName,
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: targetParameters,
            modifiers: null
        );

        var wrapper = typeof(MouseQueryWrappers).GetMethod(wrapperName, BindingFlags.Public | BindingFlags.Static);
        if (target is not null && wrapper is not null)
            map[MethodSignature.From(target)] = wrapper;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;

        try
        {
            harmony.UnpatchAll(HarmonyId);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Failed to remove one or more DelvUI Input Guard patches.");
        }

        delvUiAssembly = null;
        patchedMethods.Clear();
        PatchedMethodCount = 0;
    }

    private readonly record struct MethodSignature(string DeclaringType, string Name, string Parameters)
    {
        public static MethodSignature From(MethodInfo method)
        {
            var declaringType = method.DeclaringType?.FullName ?? string.Empty;
            var parameters = string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.FullName));
            return new MethodSignature(declaringType, method.Name, parameters);
        }
    }

    private sealed class MethodBaseComparer : IEqualityComparer<MethodBase>
    {
        public static MethodBaseComparer Instance { get; } = new();

        public bool Equals(MethodBase? x, MethodBase? y)
        {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;
            return x.Module == y.Module && x.MetadataToken == y.MetadataToken;
        }

        public int GetHashCode(MethodBase obj) => HashCode.Combine(obj.Module, obj.MetadataToken);
    }
}
