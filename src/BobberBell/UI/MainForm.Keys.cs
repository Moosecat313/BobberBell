using System;
using System.Linq;
using System.Windows.Forms;
using BobberBell.Core.Input;

namespace BobberBell.UI;

/// <summary>Key pickers (fishing, buff, rod), all backed by the single <see cref="KeyCatalog"/> allow-list.</summary>
internal sealed partial class MainForm
{
    private void LoadKeyOptions()
    {
        foreach (var (cb, def) in new[] { (comboBoxKey, KeyCatalog.DefaultFishingKey), (comboBoxBuffKey, KeyCatalog.DefaultBuffKey), (comboBoxRodKey, KeyCatalog.DefaultRodKey) })
        {
            cb.Items.Clear();
            cb.Items.AddRange(KeyCatalog.All.Cast<object>().ToArray());
            SelectKey(cb, def);
        }
    }

    private static bool SelectKey(ComboBox cb, string name)
    {
        for (int i = 0; i < cb.Items.Count; i++)
        {
            if (cb.Items[i] is KeyOption k && string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                cb.SelectedIndex = i;
                return true;
            }
        }

        return false;
    }

    private static KeyOption? SelectedKey(ComboBox cb) => cb.SelectedItem as KeyOption;
}
