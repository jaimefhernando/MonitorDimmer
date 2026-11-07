using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Shadeplay;

/// <summary>
/// A set of settings switched on together, such as "Late night". It sits between the normal
/// settings and a game's profile: the profile still wins where it sets something itself.
/// Switching back to Normal undoes all of it.
/// </summary>
public sealed class Scene : ObservableObject
{
    string id = Guid.NewGuid().ToString("N");
    string name = "New scene";
    bool customStrength;
    int strength = 80;
    ProfileDimMethod method = ProfileDimMethod.Default;
    ProfileLighting lighting = ProfileLighting.Default;
    string rgbColor = "#FFA020";
    int rgbBrightness = 20;
    ProfileSwitch mouseLock = ProfileSwitch.Default;
    ProfileSwitch night = ProfileSwitch.Default;
    ProfileSwitch sideDisplay = ProfileSwitch.Default;
    bool isActive;

    public Scene()
    {
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not (nameof(Summary) or nameof(IsActive))) OnPropertyChanged(nameof(Summary));
        };
    }

    public string Id { get => id; set => Set(ref id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value); }
    public string Name { get => name; set => Set(ref name, value ?? ""); }
    public bool CustomStrength { get => customStrength; set => Set(ref customStrength, value); }
    public int Strength { get => strength; set => Set(ref strength, Math.Clamp(value, 5, 100)); }
    public ProfileDimMethod Method { get => method; set => Set(ref method, value); }
    public ProfileLighting Lighting { get => lighting; set => Set(ref lighting, value); }
    public string RgbColor { get => rgbColor; set => Set(ref rgbColor, value ?? "#FFA020"); }
    public int RgbBrightness { get => rgbBrightness; set => Set(ref rgbBrightness, Math.Clamp(value, 1, 100)); }
    public ProfileSwitch MouseLock { get => mouseLock; set => Set(ref mouseLock, value); }
    /// <summary>Night tint on or off whatever the schedule says.</summary>
    public ProfileSwitch Night { get => night; set => Set(ref night, value); }
    public ProfileSwitch SideDisplay { get => sideDisplay; set => Set(ref sideDisplay, value); }

    /// <summary>This is the scene picked on Home (kept in step by the settings).</summary>
    [JsonIgnore] public bool IsActive { get => isActive; set => Set(ref isActive, value); }

    /// <summary>What it changes, e.g. "100 % · lights off · mouse lock on".</summary>
    [JsonIgnore]
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (CustomStrength) parts.Add($"{Strength} %");
            if (Method != ProfileDimMethod.Default) parts.Add(Method.ToString().ToLowerInvariant());
            switch (Lighting)
            {
                case ProfileLighting.Off: parts.Add("lights off"); break;
                case ProfileLighting.Color: parts.Add("own light color"); break;
                case ProfileLighting.Unchanged: parts.Add("lights left alone"); break;
            }
            if (MouseLock != ProfileSwitch.Default) parts.Add($"mouse lock {MouseLock.ToString().ToLowerInvariant()}");
            if (Night != ProfileSwitch.Default) parts.Add($"night tint {Night.ToString().ToLowerInvariant()}");
            if (SideDisplay != ProfileSwitch.Default) parts.Add($"side display {SideDisplay.ToString().ToLowerInvariant()}");
            return parts.Count == 0 ? "Changes nothing yet" : string.Join(" · ", parts);
        }
    }

    /// <summary>The scenes a new install starts with.</summary>
    public static ObservableCollection<Scene> Defaults() => new()
    {
        new Scene
        {
            Id = "competitive", Name = "Competitive", CustomStrength = true, Strength = 100,
            Lighting = ProfileLighting.Off, MouseLock = ProfileSwitch.On, SideDisplay = ProfileSwitch.Off,
        },
        new Scene
        {
            Id = "late-night", Name = "Late night", CustomStrength = true, Strength = 95,
            Lighting = ProfileLighting.Color, RgbColor = "#FF6A00", RgbBrightness = 8,
            Night = ProfileSwitch.On, SideDisplay = ProfileSwitch.On,
        },
        new Scene
        {
            Id = "streaming", Name = "Streaming", CustomStrength = true, Strength = 60,
            Lighting = ProfileLighting.Unchanged, MouseLock = ProfileSwitch.On, SideDisplay = ProfileSwitch.Off,
        },
    };
}
