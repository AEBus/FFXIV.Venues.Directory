using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Plugin;
using FFXIV.Venues.Directory.UI.Abstractions;

namespace FFXIV.Venues.Directory.UI;

internal sealed class SettingsWindow : Window
{
    private readonly Configuration _configuration;
    private readonly IDalamudPluginInterface _pluginInterface;

    public SettingsWindow(UiBuilder uiBuilder, Configuration configuration, IDalamudPluginInterface pluginInterface)
        : base(uiBuilder)
    {
        _configuration = configuration;
        _pluginInterface = pluginInterface;
        Title = "FFXIV Venues Directory Settings";
        InitialSize = new Vector2(520, 220);
        MinimumSize = new Vector2(420, 180);
        MaximumSize = new Vector2(900, 480);
    }

    public override void Render()
    {
        ImGui.TextWrapped("Configure FFXIV Venues Directory plugin options.");
        ImGui.Separator();

        var lifestreamEnabled = _configuration.EnableLifestreamIntegration;
        if (ImGui.Checkbox("Enable Lifestream integration", ref lifestreamEnabled))
        {
            _configuration.EnableLifestreamIntegration = lifestreamEnabled;
            _configuration.Save(_pluginInterface);
        }

        ImGui.TextDisabled("Shows or hides the Visit (Lifestream) button in venue details.");
        ImGui.Spacing();
        ImGui.TextDisabled("Commands:");
        ImGui.BulletText("/ffxivvenues - open the venue directory");
        ImGui.BulletText("/ffxivvenues settings - open this settings window");
    }
}

