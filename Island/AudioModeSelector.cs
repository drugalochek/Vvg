using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using RadioButton = System.Windows.Controls.RadioButton;

namespace Island;

/// <summary>Shared production segmented selector, independently testable without MainWindow services.</summary>
internal sealed class AudioModeSelector : WrapPanel
{
    internal string SelectedMode { get; private set; }
    internal AudioModeSelector(string? mode, Action<string> selected, Style? buttonStyle = null)
    {
        SelectedMode = AudioLevelPolicy.NormalizeMode(mode, OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348));
        string group = "MokuAudioMode_" + Guid.NewGuid().ToString("N");
        foreach (var (id, text) in new[] { (AudioLevelPolicy.Windows11, "Windows 11"), (AudioLevelPolicy.Windows10, "Windows 10") })
        {
            var button = new RadioButton
            {
                Content = text, Tag = id, GroupName = group, IsChecked = SelectedMode == id,
                Margin = new Thickness(0, 0, 4, 4)
            };
            if (buttonStyle != null) button.Style = buttonStyle;
            AutomationProperties.SetName(button, text);
            button.Checked += (_, _) =>
            {
                if (SelectedMode == id) return;
                SelectedMode = id;
                selected(id);
            };
            Children.Add(button);
        }
    }
}
