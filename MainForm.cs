using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SnippetDock;

public sealed class Snippet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class Slot
{
    public int Number { get; set; }
    public Guid? SnippetId { get; set; }
    public uint Modifiers { get; set; } = Native.MOD_CONTROL | Native.MOD_ALT;
    public Keys Key { get; set; }
}

public sealed class AppData
{
    public List<Snippet> Snippets { get; set; } = new();
    public List<Slot> Slots { get; set; } = Enumerable.Range(1, 10)
        .Select(n => new Slot { Number = n, Key = n == 10 ? Keys.D0 : (Keys)((int)Keys.D0 + n) })
        .ToList();
}

internal static class Native
{
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const int WM_HOTKEY = 0x0312;
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    public const int KEYEVENTF_KEYUP = 0x0002;
}

public sealed class MainForm : Form
{
    private readonly string dataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnippetDock");
    private string DataPath => Path.Combine(dataDir, "snippets.json");
    private AppData data = new();
    private readonly ListBox list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly TextBox search = new() { Dock = DockStyle.Top, PlaceholderText = "Search snippets..." };
    private readonly TextBox nameBox = new() { Dock = DockStyle.Top, PlaceholderText = "Snippet name" };
    private readonly TextBox textBox = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly ComboBox slotBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly ComboBox snippetBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly ComboBox modifierBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly ComboBox keyBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
    private readonly Label status = new() { Dock = DockStyle.Bottom, AutoSize = false, Height = 28, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Dictionary<int, string> hotkeyErrors = new();
    private Guid? editingId;
    private bool refreshing;

    public MainForm()
    {
        Text = "SnippetDock";
        Width = 1000;
        Height = 650;
        MinimumSize = new Size(780, 500);
        StartPosition = FormStartPosition.CenterScreen;
        LoadData();
        BuildUi();
        RefreshList();
        RefreshSlotControls();
        Shown += (_, _) => RegisterAllHotkeys();
        FormClosing += (_, _) => UnregisterAllHotkeys();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(10) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        Controls.Add(root);

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        left.Controls.Add(search, 0, 0);
        left.Controls.Add(list, 0, 1);
        var leftButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        leftButtons.Controls.Add(Button("New", (_, _) => ClearEditor()));
        leftButtons.Controls.Add(Button("Delete", (_, _) => DeleteSelected()));
        left.Controls.Add(leftButtons, 0, 2);
        root.Controls.Add(left, 0, 0);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1 };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        right.Controls.Add(nameBox, 0, 0);
        right.Controls.Add(textBox, 0, 1);
        right.Controls.Add(Button("Save snippet", (_, _) => SaveSnippet()), 0, 2);

        var assignPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true };
        slotBox.Items.AddRange(Enumerable.Range(1, 10).Cast<object>().ToArray());
        modifierBox.Items.AddRange(new object[] { "Ctrl + Alt", "Ctrl + Shift", "Alt + Shift", "Ctrl", "Alt", "Shift", "Win + Alt" });
        keyBox.Items.AddRange(new object[] { "1","2","3","4","5","6","7","8","9","0","F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12","Q","W","E","R","T","Y","U","I","O","P","A","S","D","F","G","H","J","K","L","Z","X","C","V","B","N","M" });
        assignPanel.Controls.Add(new Label { Text = "Slot", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        assignPanel.Controls.Add(slotBox);
        assignPanel.Controls.Add(new Label { Text = "Snippet", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        assignPanel.Controls.Add(snippetBox);
        assignPanel.Controls.Add(new Label { Text = "Shortcut", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        assignPanel.Controls.Add(modifierBox);
        assignPanel.Controls.Add(keyBox);
        assignPanel.Controls.Add(Button("Assign slot", (_, _) => AssignSlot()));
        right.Controls.Add(assignPanel, 0, 3);

        var help = new Label
        {
            Dock = DockStyle.Fill,
            Text = "How to use: save a snippet, assign it to a slot, then focus another app and press the shortcut.\r\nDefault shortcuts: Ctrl+Alt+1–9 and Ctrl+Alt+0 (slot 10). Data stays in your Windows user profile.",
            Padding = new Padding(3, 10, 3, 3),
            AutoSize = false
        };
        right.Controls.Add(help, 0, 4);
        root.Controls.Add(right, 1, 0);
        Controls.Add(status);
        search.TextChanged += (_, _) => RefreshList();
        list.SelectedIndexChanged += (_, _) => LoadSelectedIntoEditor();
        slotBox.SelectedIndexChanged += (_, _) => LoadSlotIntoControls();
        list.DisplayMember = nameof(Snippet.Name);
        slotBox.SelectedIndex = 0;
        modifierBox.SelectedIndex = 0;
        keyBox.SelectedIndex = 0;
    }

    private static Button Button(string text, EventHandler click)
    {
        var b = new Button { Text = text, AutoSize = true, Padding = new Padding(10, 4, 10, 4) };
        b.Click += click;
        return b;
    }

    private void LoadData()
    {
        Directory.CreateDirectory(dataDir);
        try
        {
            if (File.Exists(DataPath))
            {
                data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(DataPath)) ?? new AppData();
                data.Snippets ??= new();
                data.Slots ??= new();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not read saved snippets. A new empty library will be used.\n\n{ex.Message}", "SnippetDock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            data = new AppData();
        }
        for (int i = 1; i <= 10; i++)
            if (!data.Slots.Any(s => s.Number == i))
                data.Slots.Add(new Slot { Number = i, Key = i == 10 ? Keys.D0 : (Keys)((int)Keys.D0 + i) });
    }

    private void SaveData()
    {
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(DataPath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void RefreshList()
    {
        var q = search.Text.Trim();
        var selected = editingId;
        refreshing = true;
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var s in data.Snippets.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
                     .Where(s => q.Length == 0 || s.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || s.Text.Contains(q, StringComparison.CurrentCultureIgnoreCase)))
            list.Items.Add(s);
        list.EndUpdate();
        if (selected.HasValue)
            for (int i = 0; i < list.Items.Count; i++)
                if (((Snippet)list.Items[i]!).Id == selected.Value) { list.SelectedIndex = i; break; }
        refreshing = false;
        RefreshSnippetCombo();
    }

    private void RefreshSnippetCombo()
    {
        var old = snippetBox.SelectedItem as Snippet;
        snippetBox.BeginUpdate();
        snippetBox.Items.Clear();
        foreach (var s in data.Snippets.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)) snippetBox.Items.Add(s);
        snippetBox.DisplayMember = nameof(Snippet.Name);
        if (old != null) snippetBox.SelectedItem = data.Snippets.FirstOrDefault(s => s.Id == old.Id);
        snippetBox.EndUpdate();
    }

    private void LoadSelectedIntoEditor()
    {
        if (refreshing || list.SelectedItem is not Snippet s) return;
        editingId = s.Id;
        nameBox.Text = s.Name;
        textBox.Text = s.Text;
    }

    private void ClearEditor()
    {
        editingId = null;
        list.ClearSelected();
        nameBox.Clear();
        textBox.Clear();
        nameBox.Focus();
    }

    private void SaveSnippet()
    {
        var name = nameBox.Text.Trim();
        if (name.Length == 0) { MessageBox.Show("Enter a snippet name."); nameBox.Focus(); return; }
        if (textBox.Text.Length == 0) { MessageBox.Show("Enter the snippet text."); textBox.Focus(); return; }
        var existing = editingId.HasValue ? data.Snippets.FirstOrDefault(s => s.Id == editingId.Value) : null;
        if (existing == null)
        {
            existing = new Snippet();
            data.Snippets.Add(existing);
            editingId = existing.Id;
        }
        existing.Name = name;
        existing.Text = textBox.Text;
        SaveData();
        RefreshList();
        RefreshSlotControls();
        status.Text = $"Saved “{name}” · {data.Snippets.Count} snippets · {DataPath}";
    }

    private void DeleteSelected()
    {
        if (list.SelectedItem is not Snippet s) return;
        if (MessageBox.Show($"Delete “{s.Name}”?", "Confirm delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        data.Snippets.RemoveAll(x => x.Id == s.Id);
        foreach (var slot in data.Slots.Where(x => x.SnippetId == s.Id)) slot.SnippetId = null;
        editingId = null;
        SaveData();
        RefreshList();
        RefreshSlotControls();
        ClearEditor();
    }

    private void RefreshSlotControls()
    {
        RefreshSnippetCombo();
        LoadSlotIntoControls();
    }

    private void LoadSlotIntoControls()
    {
        if (slotBox.SelectedItem == null) return;
        int n = Convert.ToInt32(slotBox.SelectedItem);
        var slot = data.Slots.First(s => s.Number == n);
        var snippet = data.Snippets.FirstOrDefault(s => s.Id == slot.SnippetId);
        snippetBox.SelectedItem = snippet;
        modifierBox.SelectedItem = ModifierLabel(slot.Modifiers);
        keyBox.SelectedItem = KeyLabel(slot.Key);
    }

    private void AssignSlot()
    {
        if (slotBox.SelectedItem == null || snippetBox.SelectedItem is not Snippet selected)
        {
            MessageBox.Show("Choose a slot and a snippet first.");
            return;
        }
        var slot = data.Slots.First(s => s.Number == Convert.ToInt32(slotBox.SelectedItem));
        slot.SnippetId = selected.Id;
        slot.Modifiers = ModifierValue(modifierBox.SelectedItem?.ToString() ?? "Ctrl + Alt");
        slot.Key = KeyValue(keyBox.SelectedItem?.ToString() ?? "1");
        SaveData();
        RegisterAllHotkeys();
        status.Text = $"Slot {slot.Number} → {selected.Name} ({ModifierLabel(slot.Modifiers)}+{KeyLabel(slot.Key)})";
    }

    private static uint ModifierValue(string label) => label switch
    {
        "Ctrl + Alt" => Native.MOD_CONTROL | Native.MOD_ALT,
        "Ctrl + Shift" => Native.MOD_CONTROL | Native.MOD_SHIFT,
        "Alt + Shift" => Native.MOD_ALT | Native.MOD_SHIFT,
        "Ctrl" => Native.MOD_CONTROL,
        "Alt" => Native.MOD_ALT,
        "Shift" => Native.MOD_SHIFT,
        "Win + Alt" => Native.MOD_WIN | Native.MOD_ALT,
        _ => Native.MOD_CONTROL | Native.MOD_ALT
    };
    private static string ModifierLabel(uint m) => m switch
    {
        3 => "Ctrl + Alt", 6 => "Ctrl + Shift", 5 => "Alt + Shift", 2 => "Ctrl", 1 => "Alt", 4 => "Shift", 9 => "Win + Alt", _ => "Ctrl + Alt"
    };
    private static string KeyLabel(Keys k) => k == Keys.D0 ? "0" : k >= Keys.D1 && k <= Keys.D9 ? ((int)k - (int)Keys.D0).ToString() : k.ToString();
    private static Keys KeyValue(string s) => s switch
    {
        "0" => Keys.D0, "1" => Keys.D1, "2" => Keys.D2, "3" => Keys.D3, "4" => Keys.D4,
        "5" => Keys.D5, "6" => Keys.D6, "7" => Keys.D7, "8" => Keys.D8, "9" => Keys.D9,
        _ => Enum.TryParse<Keys>(s, out var k) ? k : Keys.D1
    };

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY)
        {
            int id = m.WParam.ToInt32();
            var slot = data.Slots.FirstOrDefault(s => s.Number == id);
            if (slot != null) PasteSlot(slot);
        }
        base.WndProc(ref m);
    }

    private void RegisterAllHotkeys()
    {
        UnregisterAllHotkeys();
        hotkeyErrors.Clear();
        var seen = new HashSet<string>();
        foreach (var slot in data.Slots.Where(s => s.SnippetId.HasValue))
        {
            string signature = $"{slot.Modifiers}:{(uint)slot.Key}";
            if (!seen.Add(signature) || !Native.RegisterHotKey(Handle, slot.Number, slot.Modifiers, (uint)slot.Key))
                hotkeyErrors[slot.Number] = "Shortcut unavailable (duplicate or used by Windows/another app)";
        }
        if (hotkeyErrors.Count > 0)
            status.Text = "Some shortcuts could not be registered. Try a different key/modifier combination.";
    }

    private void UnregisterAllHotkeys()
    {
        if (!IsHandleCreated) return;
        for (int i = 1; i <= 10; i++) Native.UnregisterHotKey(Handle, i);
    }

    private async void PasteSlot(Slot slot)
    {
        var snippet = data.Snippets.FirstOrDefault(s => s.Id == slot.SnippetId);
        if (snippet == null) { status.Text = $"Slot {slot.Number} has no snippet assigned."; return; }
        try
        {
            Clipboard.SetText(snippet.Text);
            await Task.Delay(100);
            Native.keybd_event(0x11, 0, 0, UIntPtr.Zero);
            Native.keybd_event((byte)Keys.V, 0, 0, UIntPtr.Zero);
            Native.keybd_event((byte)Keys.V, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
            Native.keybd_event(0x11, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
            status.Text = $"Pasted slot {slot.Number}: {snippet.Name}";
        }
        catch (Exception ex)
        {
            status.Text = $"Paste failed: {ex.Message}";
        }
    }
}
