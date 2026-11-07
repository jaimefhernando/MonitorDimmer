using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Shadeplay.Core;

namespace Shadeplay.UI;

/// <summary>The list of scenes and what the selected one changes.</summary>
public partial class ScenesView : UserControl
{
    AppSettings settings = null!;
    DimController controller = null!;

    public ScenesView()
    {
        InitializeComponent();
    }

    public void Init(AppSettings settings, DimController controller)
    {
        this.settings = settings;
        this.controller = controller;
        SceneList.ItemsSource = settings.Scenes;
        if (settings.Scenes.Count > 0) SceneList.SelectedIndex = 0;
        UpdateEditor();
    }

    void SceneList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateEditor();

    void UpdateEditor()
    {
        bool any = SceneList.SelectedItem != null;
        Editor.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
    }

    void Add_Click(object sender, RoutedEventArgs e)
    {
        var scene = new Scene { Name = "New scene" };
        settings.Scenes.Add(scene);
        SceneList.SelectedItem = scene;
    }

    void Use_Click(object sender, RoutedEventArgs e)
    {
        if (SceneList.SelectedItem is Scene scene) controller.SetScene(scene.IsActive ? "" : scene.Id);
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (SceneList.SelectedItem is not Scene scene) return;
        if (MessageBox.Show(Window.GetWindow(this)!, $"Remove the {scene.Name} scene?", "Remove scene",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        if (scene.IsActive) controller.SetScene("");
        int index = SceneList.SelectedIndex;
        settings.Scenes.Remove(scene);
        if (settings.Scenes.Count > 0) SceneList.SelectedIndex = Math.Min(index, settings.Scenes.Count - 1);
        UpdateEditor();
    }

    /// <summary>The list never scrolls itself, so the wheel scrolls the page.</summary>
    void SceneList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        ((UIElement)((FrameworkElement)sender).Parent).RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
        });
    }
}
