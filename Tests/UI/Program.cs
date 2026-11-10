// Mainは呼び出さず、画面を表示せずに一覧の結合・操作・配置だけを確認する。
using DigitalSigningTool;
using System.ComponentModel;
using System.Reflection;

internal static class UiChecks
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var form = new MainForm(new SigningSettings());
        var grid = (DataGridView)form.Controls.Find("FileList", true).Single();
        var items = (BindingList<SigningItem>)grid.DataSource!;
        var add = typeof(MainForm).GetMethod("AddItems", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var setChecked = typeof(MainForm).GetMethod("SetChecked", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var paths = new[]
        {
            @"C:\納品\SampleApp\Bin\SampleApp.exe",
            @"C:\納品\SampleApp\Bin\Vendor.Library.dll",
            @"C:\納品\SampleApp\Setup.exe",
            @"C:\納品\SampleApp\Bin\Updater.exe",
            @"C:\開発\SampleApp\Bin\SampleApp.exe"
        };
        var added = (int)add.Invoke(form, new object[] { paths })!;
        if (added != 5 || (int)add.Invoke(form, new object[] { paths })! != 0) throw new Exception("UI duplicate handling failed");
        if (grid.Columns.Count != 5 || grid.Columns[0] is not DataGridViewCheckBoxColumn || grid.Columns[4] is not DataGridViewLinkColumn) throw new Exception("UI columns failed");
        if (grid.Columns[2].HeaderText != "ファイル名" || !grid.Columns[4].HeaderText.StartsWith("ファイル場所")) throw new Exception("UI column order failed");
        form.CreateControl(); form.PerformLayout();
        // BindingContextを明示し、非表示のままデータ結合を完了させる。
        grid.BindingContext = new BindingContext();
        grid.CreateControl();
        setChecked.Invoke(form, new object[] { false });
        if (items.Any(item => item.IsSelected)) throw new Exception("Unselect-all failed");
        var start = (Button)form.Controls.Find("StartSigning", true).Single();
        if (start.Enabled) throw new Exception("Start button enabled without checked rows");
        setChecked.Invoke(form, new object[] { true });
        if (items.Any(item => !item.IsSelected) || !start.Enabled) throw new Exception("Select-all failed");
        grid.CurrentCell = grid.Rows[0].Cells["Check"];
        grid.BeginEdit(false); grid.CurrentCell.Value = false;
        grid.CommitEdit(DataGridViewDataErrorContexts.Commit); grid.EndEdit();
        if (items[0].IsSelected) throw new Exception("Checkbox edit not reflected to signing selection");
        items[0].IsSelected = true;
        items[0].ApplyResult(new(paths[0], "成功", "自社署名とタイムスタンプを確認しました。"));
        items[1].ApplyResult(new(paths[1], "スキップ", "他社署名を保持：Example Software（Valid）"));
        items[2].ApplyResult(new(paths[2], "失敗", "対象ファイルは読み取り専用です。設定を確認して再実行してください。"));
        items[3].SetState("未処理（中止）", "中止操作のため、このファイルは処理していません。");
        items[4].IsSelected = false;
        var counter = (Label)form.Controls.Find("SelectionCount", true).Single();
        if (!counter.Text.Contains("5件") || !counter.Text.Contains("4件")) throw new Exception("Checkbox count update failed");
        if (grid.Rows.Count != 5 || grid.Rows[0].Cells["Result"].Value?.ToString() != "○") throw new Exception("Bound result display failed");
        var detail = (TextBox)form.Controls.Find("SelectedDetail", true).Single();
        if (!detail.Text.Contains("自社署名とタイムスタンプ")) throw new Exception("Selected detail was not refreshed with row result");
        var root = Path.Combine(Path.GetTempPath(), "DigitalSigningToolUiChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var preview = Path.Combine(root, "UI-preview.png");
        // 非表示Formは子の描画を省くため、内容パネルを切り離して描画する。
        using var content = form.Controls[0];
        form.Controls.Remove(content); content.Dock = DockStyle.None; content.Size = form.ClientSize;
        content.CreateControl(); content.PerformLayout();
        using (var image = new Bitmap(content.Width, content.Height))
        {
            content.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            image.Save(preview);
        }
        form.Size = form.MinimumSize; content.Size = form.ClientSize; content.PerformLayout();
        using (var image = new Bitmap(content.Width, content.Height))
        {
            content.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            image.Save(Path.Combine(root, "UI-preview-small.png"));
        }
        using var settingsForm = new SettingsForm(new SigningSettings());
        using var setupForm = new InstallationGuideForm("");
        if (FindControls(settingsForm).OfType<Button>().Any(button => button.Text == "手動設定")) throw new Exception("Redundant manual-settings button remains");
        if (settingsForm.BackColor != form.BackColor || setupForm.BackColor != form.BackColor || settingsForm.Font.Name != form.Font.Name || setupForm.Font.Size != form.Font.Size) throw new Exception("Window theme is not consistent");
        var limit = (CheckBox)settingsForm.Controls.Find("LimitUsagePeriod", true).Single();
        var renew = (Button)settingsForm.Controls.Find("RenewPeriod", true).Single();
        if (limit.Checked || renew.Enabled) throw new Exception("Usage period should be optional and disabled initially");
        limit.Checked = true;
        if (!renew.Enabled) throw new Exception("Period controls did not enable");
        limit.Checked = false;
        var save = (Button)settingsForm.Controls.Find("SaveSettings", true).Single();
        var install = (Button)setupForm.Controls.Find("InstallTool", true).Single();
        if (save.BackColor != start.BackColor || install.BackColor != start.BackColor || save.FlatStyle != start.FlatStyle) throw new Exception("Primary button style is not consistent");
        RenderForm(settingsForm, Path.Combine(root, "Settings-preview.png"));
        RenderForm(setupForm, Path.Combine(root, "Setup-preview.png"));
        Console.WriteLine("PASS: five columns, checkbox binding, deduplication, same names, select/unselect all, button state, result symbols, selection count");
        Console.WriteLine("PASS: no manual-settings button; consistent window and primary-button styling on all three forms");
        Console.WriteLine("PREVIEW: " + preview);
        Console.WriteLine("No application window, signing, Azure connection, or folder opening.");
    }

    private static IEnumerable<Control> FindControls(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in FindControls(child)) yield return descendant;
        }
    }

    private static void RenderForm(Form form, string path)
    {
        using var content = form.Controls[0];
        form.Controls.Remove(content); content.Dock = DockStyle.None; content.Size = form.ClientSize;
        content.CreateControl(); content.PerformLayout();
        using var image = new Bitmap(content.Width, content.Height);
        content.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        image.Save(path);
    }
}
