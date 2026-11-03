using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MonitorDimmer.Core;

namespace MonitorDimmer.UI;

/// <summary>Editable list of program names (vlc.exe, discord.exe...).</summary>
public partial class AppListEditor : UserControl
{
    ObservableCollection<string>? items;

    public AppListEditor()
    {
        InitializeComponent();
    }

    /// <summary>Title shown in the picker dialog.</summary>
    public string PickerTitle { get; set; } = "Add apps";

    public ObservableCollection<string>? Items
    {
        get => items;
        set
        {
            if (items != null) items.CollectionChanged -= OnItemsChanged;
            items = value;
            if (items != null) items.CollectionChanged += OnItemsChanged;
            List.ItemsSource = items;
            UpdateEmpty();
        }
    }

    void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateEmpty();

    void UpdateEmpty() =>
        EmptyText.Visibility = items == null || items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    void AddName(string name)
    {
        name = WindowScanner.NormalizeExe(name);
        if (items == null || name.Length == 0) return;
        if (items.Any(i => WindowScanner.NormalizeExe(i) == name)) return;
        items.Add(name);
    }

    void Pick_Click(object sender, RoutedEventArgs e)
    {
        var picker = new AppPickerWindow(PickerTitle, items ?? Enumerable.Empty<string>())
        {
            Owner = Window.GetWindow(this),
        };
        if (picker.ShowDialog() != true) return;
        foreach (string name in picker.SelectedNames) AddName(name);
    }

    void Add_Click(object sender, RoutedEventArgs e)
    {
        AddName(NameBox.Text);
        NameBox.Clear();
        NameBox.Focus();
    }

    void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Add_Click(sender, e);
        e.Handled = true;
    }

    void Remove_Click(object sender, RoutedEventArgs e) => RemoveSelected();

    void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) RemoveSelected();
    }

    void RemoveSelected()
    {
        if (items == null) return;
        foreach (string name in List.SelectedItems.Cast<string>().ToList()) items.Remove(name);
    }
}
