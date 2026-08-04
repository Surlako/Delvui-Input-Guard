using System;
using Dalamud.Configuration;

namespace DelvUIInputGuard;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public bool LogStateChanges { get; set; } = false;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
