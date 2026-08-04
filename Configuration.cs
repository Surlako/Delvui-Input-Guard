using System;
using Dalamud.Configuration;

namespace DelvUIInputGuard;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;
    public bool Enabled { get; set; } = true;
    public bool CapturePopups { get; set; } = true;
    public bool LogPublishedWindows { get; set; } = false;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
