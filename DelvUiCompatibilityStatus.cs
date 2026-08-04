using System;
using System.Linq;
using System.Reflection;

namespace DelvUIInputGuard;

internal sealed class DelvUiCompatibilityStatus
{
    private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public bool IsInstalled { get; private set; }
    public bool IsLoaded { get; private set; }
    public string Version { get; private set; } = "Not detected";
    public bool? WindowClippingEnabled { get; private set; }
    public bool? ThirdPartyClippingEnabled { get; private set; }
    public string LastError { get; private set; } = string.Empty;

    public void Refresh()
    {
        try
        {
            var plugin = Plugin.PluginInterface.InstalledPlugins.FirstOrDefault(candidate =>
                string.Equals(candidate.InternalName, "DelvUI", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.Name, "DelvUI", StringComparison.OrdinalIgnoreCase));

            IsInstalled = plugin is not null;
            IsLoaded = plugin?.IsLoaded == true;
            Version = plugin?.Version.ToString() ?? "Not detected";
            WindowClippingEnabled = null;
            ThirdPartyClippingEnabled = null;
            LastError = string.Empty;

            if (!IsLoaded)
                return;

            ReadRuntimeClippingConfiguration();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    public bool OpenDelvUiConfig()
    {
        try
        {
            var plugin = Plugin.PluginInterface.InstalledPlugins.FirstOrDefault(candidate =>
                string.Equals(candidate.InternalName, "DelvUI", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.Name, "DelvUI", StringComparison.OrdinalIgnoreCase));

            if (plugin?.IsLoaded == true && plugin.HasConfigUi)
            {
                plugin.OpenConfigUi();
                return true;
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }

        return false;
    }

    private void ReadRuntimeClippingConfiguration()
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(candidate =>
            string.Equals(candidate.GetName().Name, "DelvUI", StringComparison.OrdinalIgnoreCase));
        if (assembly is null)
            return;

        var helperType = assembly.GetType("DelvUI.Helpers.ClipRectsHelper", throwOnError: false);
        if (helperType is null)
            return;

        var instance = helperType.GetProperty("Instance", AnyStatic)?.GetValue(null) ??
                       helperType.GetField("Instance", AnyStatic)?.GetValue(null) ??
                       helperType.GetField("_instance", AnyStatic)?.GetValue(null);
        if (instance is null)
            return;

        var config = FindField(instance.GetType(), "_config")?.GetValue(instance) ??
                     instance.GetType().GetProperty("Config", AnyInstance)?.GetValue(instance);
        if (config is null)
            return;

        WindowClippingEnabled = ReadBoolean(config, "Enabled");
        ThirdPartyClippingEnabled = ReadBoolean(config, "ThirdPartyClipRectsEnabled");
    }

    private static FieldInfo? FindField(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(name, AnyInstance);
            if (field is not null)
                return field;
        }

        return null;
    }

    private static bool? ReadBoolean(object instance, string name)
    {
        for (var current = instance.GetType(); current is not null; current = current.BaseType)
        {
            var property = current.GetProperty(name, AnyInstance);
            if (property?.PropertyType == typeof(bool))
                return (bool?)property.GetValue(instance);

            var field = current.GetField(name, AnyInstance);
            if (field?.FieldType == typeof(bool))
                return (bool?)field.GetValue(instance);
        }

        return null;
    }
}
