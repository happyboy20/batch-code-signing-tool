//*******************************************************************************
//*<機能名>     : デジタル署名ツール
//*<ID>         : MainForm
//*<種別>       : フォームモジュール
//*<依存ﾓｼﾞｭｰﾙ> : SettingsForm / TargetFiles / SigningItem / SigningService
//*------------------------------------------------------------------------------
//*<説明>       : チェックした複数ファイルを署名し、結果と保存場所を表示します。
//*------------------------------------------------------------------------------
//*<必要な      : .NET 10 Desktop Runtime（Windows x64）
//*  参照設定>  : DigitalSigningTool.NET10.csproj
//*------------------------------------------------------------------------------
//*******************************************************************************

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace DigitalSigningTool;

#region メイン画面

public sealed class MainForm : Form
{
    //==============================
    //1. 定義部
    //==============================
    #region ############### 変数定義 ###############
    private static readonly Color Navy = UiTheme.Text;
    private static readonly Color Blue = UiTheme.Primary;
    private static readonly Color Muted = UiTheme.Muted;
    private SigningSettings settings;
    private bool running;                               // 一覧読込・署名の操作中か
    private bool bulkSelection;                         // 全選択操作中の通知抑制
    private bool closeAfterStop;                        // 停止後に終了するか
    private CancellationTokenSource? workCancellation;
    private readonly BindingList<SigningItem> items = new();
    private readonly Button addFiles = new() { Name = "AddFiles", Text = "ファイルを追加…", AutoSize = true };
    private readonly Button addFolder = new() { Name = "AddFolder", Text = "フォルダーを追加…", AutoSize = true };
    private readonly Button selectAll = new() { Name = "SelectAll", Text = "全選択", AutoSize = true };
    private readonly Button unselectAll = new() { Name = "UnselectAll", Text = "全選択取消", AutoSize = true };
    private readonly Button clearList = new() { Name = "ClearList", Text = "一覧をクリア", AutoSize = true };
    private readonly Button sign = new() { Name = "StartSigning", Text = "チェックしたファイルを署名", AutoSize = true };
    private readonly Button stop = new() { Name = "StopSigning", Text = "中止（強制停止）", AutoSize = true, Enabled = false };
    private readonly Button configure = new() { Text = "設定", AutoSize = true };
    private readonly Label period = new() { AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 6, 0, 0) };
    private readonly Label selection = new() { Name = "SelectionCount", AutoSize = true, ForeColor = Navy, Anchor = AnchorStyles.Left };
    private readonly Label summary = new() { Text = "ファイルまたはフォルダーを追加してください。", Dock = DockStyle.Fill, AutoSize = true, ForeColor = Muted, Margin = new Padding(14, 8, 0, 4) };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Height = 8, Style = ProgressBarStyle.Continuous, Margin = new Padding(0, 12, 0, 12) };
    private readonly DataGridView results = new()
    {
        Name = "FileList",
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        BackgroundColor = Color.White,
        BorderStyle = BorderStyle.None,
        AutoGenerateColumns = false,
        EnableHeadersVisualStyles = false,
        EditMode = DataGridViewEditMode.EditOnEnter
    };
    private readonly TextBox detail = new()
    {
        Name = "SelectedDetail",
        Dock = DockStyle.Fill,
        ReadOnly = true,
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        BorderStyle = BorderStyle.None,
        BackColor = Color.White,
        ForeColor = Navy
    };
    #endregion

    //==============================
    //2. 初期化・画面操作・共通処理
    //==============================
    #region ############### 処理定義 ###############

    // メイン画面の初期化
    //***************************************************************************
    //*<引数> settings : 利用中の設定
    //*-------------------------------------------------------------------------
    //*<返値> なし
    //*-------------------------------------------------------------------------
    //*<説明> 標準部品だけで画面とイベントを作成します。起動時に接続情報の未設定で設定画面へ移動しません。
    //***************************************************************************
    public MainForm(SigningSettings settings)
    {
        this.settings = settings;
        UiTheme.Apply(this, "デジタル署名ツール");
        Size = new Size(1180, 820); MinimumSize = new Size(960, 680); StartPosition = FormStartPosition.CenterScreen;
        foreach (var button in new[] { addFiles, addFolder, selectAll, unselectAll, clearList, configure }) UiTheme.StyleButton(button);
        UiTheme.StyleButton(sign, primary: true); UiTheme.StyleButton(stop, danger: true);
        BuildLayout(); BuildGrid();
        items.ListChanged += (_, _) => { if (!bulkSelection) { UpdateSelection(); UpdateDetail(); } };
        addFiles.Click += async (_, _) => await AddFilesAsync();
        addFolder.Click += async (_, _) => await AddFolderAsync();
        selectAll.Click += (_, _) => SetChecked(true);
        unselectAll.Click += (_, _) => SetChecked(false);
        clearList.Click += (_, _) => { items.Clear(); detail.Clear(); summary.Text = "一覧をクリアしました。"; };
        configure.Click += (_, _) => EditSettings();
        sign.Click += async (_, _) => await SignAsync();
        stop.Click += (_, _) => RequestStop();
        FormClosing += (_, e) =>
        {
            if (!running) return;
            e.Cancel = true; closeAfterStop = true; RequestStop();
        };
        UpdatePeriod(); UpdateSelection();
    }

    // 画面レイアウトの構築
    //***************************************************************************
    //*<引数> なし
    //*-------------------------------------------------------------------------
    //*<返値> なし
    //*-------------------------------------------------------------------------
    //*<説明> 見出し、追加・選択操作、対象一覧、詳細、進捗、署名・中止操作を配置します。
    //***************************************************************************
    private void BuildLayout()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = BackColor, Padding = new Padding(24), ColumnCount = 1, RowCount = 8 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 8; row++) layout.RowStyles.Add(new RowStyle(row == 4 ? SizeType.Percent : row == 5 ? SizeType.Absolute : SizeType.AutoSize, row == 4 ? 100 : row == 5 ? 104 : 0));
        Controls.Add(layout);
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 20) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(UiTheme.Heading("デジタル署名", period), 0, 0);
        configure.Anchor = AnchorStyles.Top | AnchorStyles.Right; header.Controls.Add(configure, 1, 0); layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(new Label { Text = "ファイルを追加 → 対象にチェック → 署名を開始", AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 0, 0, 12) }, 0, 1);
        var toolbar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 14) };
        toolbar.Controls.AddRange([addFiles, addFolder, selectAll, unselectAll, clearList]); layout.Controls.Add(toolbar, 0, 2);
        var information = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 0, 0, 10) };
        information.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40)); information.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        information.Controls.Add(selection, 0, 0);
        information.Controls.Add(new Label { Text = "○ 成功　△ スキップ・中止　× 失敗", AutoSize = true, Anchor = AnchorStyles.Right, ForeColor = Muted }, 1, 0);
        layout.Controls.Add(information, 0, 3); layout.Controls.Add(results, 0, 4);
        var detailBox = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12), Margin = new Padding(0, 8, 0, 0) };
        detailBox.Controls.Add(detail); layout.Controls.Add(detailBox, 0, 5); layout.Controls.Add(progress, 0, 6);
        var footer = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.Controls.Add(sign, 0, 0); footer.Controls.Add(stop, 1, 0); footer.Controls.Add(summary, 2, 0); layout.Controls.Add(footer, 0, 7);
    }

    // ファイル一覧の構築
    //***************************************************************************
    //*<引数> なし
    //*-------------------------------------------------------------------------
    //*<返値> なし
    //*-------------------------------------------------------------------------
    //*<説明> チェック・結果記号・ファイル名・詳細・場所の順で列を構築します。チェック列だけ編集可能にします。
    //***************************************************************************
    private void BuildGrid()
    {
        results.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Navy, ForeColor = Color.White, SelectionBackColor = Navy, SelectionForeColor = Color.White, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(6) };
        results.ColumnHeadersHeight = 42; results.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        results.DefaultCellStyle = new DataGridViewCellStyle
        {
            Font = Font,
            ForeColor = Navy,
            BackColor = Color.White,
            SelectionBackColor = Color.FromArgb(225, 237, 252),
            SelectionForeColor = Navy,
            Padding = new Padding(5),
            NullValue = ""
        };
        results.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 253);
        results.GridColor = Color.FromArgb(228, 234, 242); results.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        results.RowTemplate.Height = 36;
        results.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Check", HeaderText = "選択", DataPropertyName = nameof(SigningItem.IsSelected), Width = 68, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, SortMode = DataGridViewColumnSortMode.NotSortable });
        results.Columns.Add(new DataGridViewTextBoxColumn { Name = "Result", HeaderText = "結果", DataPropertyName = nameof(SigningItem.Symbol), ReadOnly = true, Width = 62, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, SortMode = DataGridViewColumnSortMode.NotSortable });
        results.Columns.Add(new DataGridViewTextBoxColumn { Name = "FileName", HeaderText = "ファイル名", DataPropertyName = nameof(SigningItem.FileName), ReadOnly = true, FillWeight = 28, MinimumWidth = 150, SortMode = DataGridViewColumnSortMode.NotSortable });
        results.Columns.Add(new DataGridViewTextBoxColumn { Name = "Detail", HeaderText = "詳細", DataPropertyName = nameof(SigningItem.Detail), ReadOnly = true, FillWeight = 42, MinimumWidth = 180, SortMode = DataGridViewColumnSortMode.NotSortable });
        results.Columns.Add(new DataGridViewLinkColumn
        {
            Name = "Location",
            HeaderText = "ファイル場所（クリックで開く）",
            DataPropertyName = nameof(SigningItem.DirectoryPath),
            ReadOnly = true,
            FillWeight = 30,
            MinimumWidth = 170,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            LinkColor = Blue,
            ActiveLinkColor = Blue,
            VisitedLinkColor = Blue,
            LinkBehavior = LinkBehavior.HoverUnderline,
            TrackVisitedState = false
        });
        results.Columns["Result"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        results.Columns["Result"]!.DefaultCellStyle.Font = new Font("Segoe UI Symbol", 13, FontStyle.Bold);
        results.DataSource = items;
        results.CurrentCellDirtyStateChanged += (_, _) => { if (results.IsCurrentCellDirty) results.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        results.SelectionChanged += (_, _) => UpdateDetail();
        results.CellContentClick += (_, e) => { if (e.RowIndex >= 0 && results.Columns[e.ColumnIndex].Name == "Location") OpenLocation(e.RowIndex); };
        results.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || results.Columns[e.ColumnIndex].Name != "Result") return;
            e.CellStyle!.ForeColor = e.Value?.ToString() switch { "○" => Color.FromArgb(20, 128, 92), "△" => Color.FromArgb(170, 110, 15), "×" => Color.Firebrick, _ => Navy };
        };
        results.DataError += (_, e) => { e.ThrowException = false; ShowError(e.Exception ?? new InvalidOperationException("一覧の値を更新できません。"), "一覧の入力"); };
    }

    // 複数ファイルの追加
    //***************************************************************************
    //*<引数> なし
    //*-------------------------------------------------------------------------
    //*<返値> 追加完了を表すTask
    //*-------------------------------------------------------------------------
    //*<説明> 複数ファイルを選択して一覧に追加します。ダイアログの取消では一覧を変更しません。
    //***************************************************************************
    private async Task AddFilesAsync()
    {
        if (running) return;
        try
        {
            using var dialog = new OpenFileDialog { Title = "署名するファイルを選択（複数選択可）", Filter = "EXE・DLL|*.exe;*.dll|すべてのファイル|*.*", Multiselect = true, CheckFileExists = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) await LoadFilesAsync(token => TargetFiles.CollectMany(dialog.FileNames, token));
        }
        catch (Exception ex) { ShowError(ex, nameof(AddFilesAsync)); }
    }

    // フォルダー内ファイルの追加
    //***************************************************************************
    //*<引数> なし
    //*-------------------------------------------------------------------------
    //*<返値> 追加完了を表すTask
    //*-------------------------------------------------------------------------
    //*<説明> 指定フォルダー配下のEXE・DLLを一覧に追加します。まだ署名は実行しません。
    //***************************************************************************
    private async Task AddFolderAsync()
    {
        if (running) return;
        try
        {
            using var dialog = new FolderBrowserDialog { Description = "追加するフォルダー（配下のEXE・DLLを一覧に追加）", UseDescriptionForTitle = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) await LoadFilesAsync(token => TargetFiles.Collect(dialog.SelectedPath, false, token));
        }
        catch (Exception ex) { ShowError(ex, nameof(AddFolderAsync)); }
    }

    // 対象ファイルの読込
    //***************************************************************************
    //*<引数> collect : 中止要求付きの一覧取得処理
    //*-------------------------------------------------------------------------
    //*<返値> 取得・追加完了を表すTask
    //*-------------------------------------------------------------------------
    //*<説明> 画面操作を止めて対象を収集します。収集失敗・中止では既存一覧を保持します。
    //***************************************************************************
    private async Task LoadFilesAsync(Func<CancellationToken, string[]> collect)
    {
        using var cancellation = new CancellationTokenSource();
        workCancellation = cancellation; running = true; SetEnabled(false);
        summary.Text = "追加するファイルを確認しています…";
        try
        {
            var paths = await Task.Run(() => collect(cancellation.Token), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            var added = AddItems(paths);
            summary.Text = paths.Length == 0 ? "追加できるEXE・DLLがありません。" : $"{added}件追加しました。重複するファイルは追加していません。";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { summary.Text = "一覧への追加を中止しました。"; }
        catch (Exception ex) { summary.Text = "ファイルを追加できませんでした。"; ShowError(ex, nameof(LoadFilesAsync)); }
        finally { CompleteOperation(); }
    }

    // 一覧への重複排除付き追加
    //***************************************************************************
    //*<引数> paths : 追加する絶対パス一覧
    //*-------------------------------------------------------------------------
    //*<返値> 新たに追加した件数
    //*-------------------------------------------------------------------------
    //*<説明> 同じパスの重複を除き、チェック済みの行を追加します。同名でも場所が異なれば保持します。
    //***************************************************************************
    private int AddItems(IEnumerable<string> paths)
    {
        var known = new HashSet<string>(items.Select(item => item.FullPath), StringComparer.OrdinalIgnoreCase);
        var added = 0;
        bulkSelection = true; items.RaiseListChangedEvents = false;
        try
        {
            foreach (var path in paths)
            {
                var fullPath = Path.GetFullPath(path);
                if (known.Add(fullPath)) { items.Add(new SigningItem(fullPath)); added++; }
            }
        }
        finally { items.RaiseListChangedEvents = true; items.ResetBindings(); bulkSelection = false; UpdateSelection(); }
        return added;
    }

    // 全行のチェック状態変更
    //***************************************************************************
    //*<引数> value : 全行へ設定するチェック状態
    //*-------------------------------------------------------------------------
    //*<返値> なし
    //*-------------------------------------------------------------------------
    //*<説明> 全選択・全選択取消を適用し、チェック件数と署名ボタンの状態を更新します。
    //***************************************************************************
    private void SetChecked(bool value)
    {
        if (running) return;
        results.EndEdit(); bulkSelection = true; items.RaiseListChangedEvents = false;
        try { foreach (var item in items) item.IsSelected = value; }
        finally { items.RaiseListChangedEvents = true; items.ResetBindings(); bulkSelection = false; UpdateSelection(); }
    }

    // 選択件数と操作状態の更新
    //***************************************************************************
    //*<引数> なし
    //*-------------------------------------------------------------------------
    //*<返値> なし
    //*-------------------------------------------------------------------------
    //*<説明> 一覧件数・チェック件数を表示します。対象がゼロ、または操作中なら署名開始を無効にします。
    //***************************************************************************
    private void UpdateSelection()
    {
        selection.Text = $"一覧 {items.Count}件　／　チェック {items.Count(item => item.IsSelected)}件";
        sign.Enabled = !running && items.Any(item => item.IsSelected);
    }

    // 選択行の詳細表示
    //***************************************************************************
    //*<引数> なし
    //*-------------------------------------------------------------------------
    //*<返値> なし
    //*-------------------------------------------------------------------------
    //*<説明> 選択行のファイル名・状態・理由・フルパスを下部へ表示します。
    //***************************************************************************
    private void UpdateDetail()
    {
        if (results.CurrentRow?.DataBoundItem is not SigningItem item) { detail.Clear(); return; }
        detail.Text = $"{item.FileName}　｜　{item.Status}{Environment.NewLine}{item.Detail}{Environment.NewLine}{item.FullPath}";
    }

    // 保存先フォルダーを開く
    //***************************************************************************
    //*<引数> rowIndex : 場所リンクをクリックした行番号
    //*-------------------------------------------------------------------------
    //*<返値> なし
    //*-------------------------------------------------------------------------
    //*<説明> 対象ファイルの保存フォルダーを開きます。場所が存在しなければ理由を表示します。
    //***************************************************************************
    private void OpenLocation(int rowIndex)
    {
        try
        {
            if (results.Rows[rowIndex].DataBoundItem is not SigningItem item) return;
            if (!Directory.Exists(item.DirectoryPath)) throw new DirectoryNotFoundException("保存先フォルダーが見つかりません。");
            Process.Start(new ProcessStartInfo(item.DirectoryPath) { UseShellExecute = true });
        }
        catch (Exception ex) { ShowError(ex, nameof(OpenLocation)); }
    }

    // 利用期間の表示
    //***************************************************************************
    //*<引数> なし
    //*-------------------------------------------------------------------------
    //*<返値> なし
    //*-------------------------------------------------------------------------
    //*<説明> 現在の利用期間と証明書名を表示します。
    //***************************************************************************
    private void UpdatePeriod() => period.Text = (settings.LimitUsagePeriod ? $"利用期間 {settings.StartDate:yyyy/MM/dd} ～ {settings.EndDate:yyyy/MM/dd}" : "利用期間の制限なし") + "　｜　証明書 " + (string.IsNullOrWhiteSpace(settings.CertificateName) ? "未設定" : settings.CertificateName);

    // 設定画面の表示
    //***************************************************************************
    //*<引数> なし
    //*-------------------------------------------------------------------------
    //*<返値> true : 保存成功 / false : 取消または失敗
    //*-------------------------------------------------------------------------
    //*<説明> 設定を編集します。期限切れのまま閉じた場合はツールを終了します。
    //***************************************************************************
    private bool EditSettings()
    {
        try
        {
            using var form = new SettingsForm(settings);
            if (form.ShowDialog(this) == DialogResult.OK) { settings = form.Settings; UpdatePeriod(); return true; }
            if (settings.IsExpired(DateTime.Today)) Close();
            return false;
        }
        catch (Exception ex) { ShowError(ex, nameof(EditSettings)); if (settings.IsExpired(DateTime.Today)) Close(); return false; }
    }

    // チェック済みファイルの署名
    //***************************************************************************
    //*<引数> なし
    //*-------------------------------------------------------------------------
    //*<返値> 署名完了を表すTask
    //*-------------------------------------------------------------------------
    //*<説明> チェックした行を固定して署名します。未選択行は処理せず、結果は既存行の記号と詳細へ反映します。
    //***************************************************************************
    private async Task SignAsync()
    {
        if (running) return;
        if (settings.IsExpired(DateTime.Today)) { EditSettings(); return; }
        results.EndEdit();
        var selected = items.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0) { MessageBox.Show(this, "署名するファイルにチェックを付けてください。"); return; }
        string secret;
        try
        {
            SettingsValidation.ValidateConnection(settings);
            if (!File.Exists(settings.ToolPath)) throw new FileNotFoundException("AzureSignToolが見つかりません。設定画面の自動セットアップを使用してください。");
            secret = SecretProtection.Unprotect(settings.ProtectedSecret);
        }
        catch (Exception ex) { ShowError(ex, "署名前の設定確認"); EditSettings(); return; }
        using var cancellation = new CancellationTokenSource();
        workCancellation = cancellation; running = true; SetEnabled(false);
        progress.Value = 0; progress.Maximum = selected.Length;
        int succeeded = 0, failed = 0, skipped = 0, interrupted = 0;
        try
        {
            foreach (var item in selected) item.SetState("待機", "署名を待っています。");
            var snapshot = settings.Copy();
            foreach (var item in selected)
            {
                if (cancellation.IsCancellationRequested) break;
                item.SetState("処理中", "署名状態を確認しています…");
                summary.Text = $"処理中 {progress.Value + 1}/{selected.Length}：{item.FileName}";
                var result = await SigningService.SignAsync(item.FullPath, snapshot, secret, cancellation.Token);
                item.ApplyResult(result); UpdateDetail();
                switch (result.Result) { case "成功": succeeded++; break; case "スキップ": skipped++; break; case "中止": interrupted++; break; default: failed++; break; }
                progress.Value++;
                if (result.Result == "中止") break;
            }
            MarkRemaining(selected, cancellation.IsCancellationRequested);
            var remaining = selected.Length - succeeded - failed - skipped - interrupted;
            summary.Text = $"{(cancellation.IsCancellationRequested ? "中止" : "完了")}：成功 {succeeded} ／ 失敗 {failed} ／ スキップ {skipped} ／ 中止 {interrupted} ／ 未処理 {remaining}";
            if (!closeAfterStop) MessageBox.Show(this, summary.Text, "署名結果", MessageBoxButtons.OK, failed > 0 || interrupted > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MarkRemaining(selected, false);
            summary.Text = $"処理中断：成功 {succeeded} ／ 失敗 {failed} ／ スキップ {skipped}";
            ShowError(ex, nameof(SignAsync), secret);
        }
        finally { secret = ""; UpdateDetail(); CompleteOperation(); }
    }

    // 停止後の未処理行の表示
    //***************************************************************************
    //*<引数> selected : 今回の対象行 / cancelled : 中止要求による停止か
    //*-------------------------------------------------------------------------
    //*<返値> なし
    //*-------------------------------------------------------------------------
    //*<説明> 停止・中断で未実行になった行に、三角記号と理由を表示します。
    //***************************************************************************
    private static void MarkRemaining(IEnumerable<SigningItem> selected, bool cancelled)
    {
        foreach (var item in selected.Where(item => item.Status is "待機" or "処理中"))
            item.SetState(cancelled ? "未処理（中止）" : "未処理（中断）", cancelled ? "中止操作のため、このファイルは処理していません。" : "処理が中断されたため、結果を確認できませんでした。");
    }

    // 操作可否の切替
    //***************************************************************************
    //*<引数> enabled : 操作を許可するか
    //*-------------------------------------------------------------------------
    //*<返値> なし
    //*-------------------------------------------------------------------------
    //*<説明> 処理中の追加・選択変更・設定変更を防ぎ、中止操作を有効にします。場所リンクは維持します。
    //***************************************************************************
    private void SetEnabled(bool enabled)
    {
        foreach (Control control in new Control[] { addFiles, addFolder, selectAll, unselectAll, clearList, configure }) control.Enabled = enabled;
        results.Columns["Check"]!.ReadOnly = !enabled;
        stop.Enabled = !enabled && workCancellation?.IsCancellationRequested == false;
        UpdateSelection();
    }

    // 処理終了後の画面復帰
    //***************************************************************************
    //*<引数> なし
    //*-------------------------------------------------------------------------
    //*<返値> なし
    //*-------------------------------------------------------------------------
    //*<説明> 操作状態を復帰します。終了要求がある場合は停止完了後に閉じます。
    //***************************************************************************
    private void CompleteOperation()
    {
        running = false; workCancellation = null;
        if (IsDisposed) return;
        SetEnabled(true);
        if (closeAfterStop) Close();
        else if (settings.IsExpired(DateTime.Today)) EditSettings();
    }

    // 処理の強制停止要求
    //***************************************************************************
    //*<引数> なし
    //*-------------------------------------------------------------------------
    //*<返値> なし
    //*-------------------------------------------------------------------------
    //*<説明> 外部プロセスまで中止要求を伝え、後続ファイルの処理を防ぎます。
    //***************************************************************************
    private void RequestStop()
    {
        if (!running || workCancellation is null || workCancellation.IsCancellationRequested) return;
        stop.Enabled = false; summary.Text = "強制停止しています… 残りのファイルは処理しません。";
        workCancellation.Cancel();
    }

    // エラー内容の表示
    //***************************************************************************
    //*<引数> error : 例外 / procedure : 処理名 / secret : 非表示にする値
    //*-------------------------------------------------------------------------
    //*<返値> なし
    //*-------------------------------------------------------------------------
    //*<説明> 認証情報を伏せ、モジュール名・処理名を付記して表示します。ログは保存しません。
    //***************************************************************************
    private void ShowError(Exception error, string procedure, string secret = "") => MessageBox.Show(this,
        ErrorDetails.Format(error, typeof(MainForm), procedure, secret), "処理の確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    #endregion
}

#endregion

#region 一覧行のデータ

public sealed class SigningItem : INotifyPropertyChanged
{
    private bool isSelected = true; // 追加したファイルは初期状態でチェックする
    private string status = "未処理";
    private string detail = "未処理です。チェックしたファイルだけを署名します。";
    public string FullPath { get; }
    public string FileName => Path.GetFileName(FullPath);
    public string DirectoryPath => Path.GetDirectoryName(FullPath)!;
    public bool IsSelected { get => isSelected; set { if (isSelected != value) { isSelected = value; Notify(); } } }
    public string Status => status;
    public string Detail => detail;
    public string Symbol => status switch { "成功" => "○", "スキップ" or "中止" or "未処理（中止）" or "未処理（中断）" => "△", "失敗" => "×", _ => "" };
    public event PropertyChangedEventHandler? PropertyChanged;

    // 一覧行の初期化
    //***************************************************************************
    //*<引数> fullPath : 対象ファイルの絶対パス
    //*-------------------------------------------------------------------------
    //*<返値> なし
    //*-------------------------------------------------------------------------
    //*<説明> 存在確認は追加・署名の各時点で行い、行は絶対パスを保持します。
    //***************************************************************************
    public SigningItem(string fullPath) => FullPath = Path.GetFullPath(fullPath);

    // 対象行への結果反映
    //***************************************************************************
    //*<引数> result : 行の処理結果
    //*-------------------------------------------------------------------------
    //*<返値> なし
    //*-------------------------------------------------------------------------
    //*<説明> 記号と理由を通知します。別ファイルの結果を誤反映しないよう確認します。
    //***************************************************************************
    public void ApplyResult(FileResult result)
    {
        if (!string.Equals(Path.GetFullPath(result.Path), FullPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("対象ファイルと処理結果のパスが一致しません。");
        SetState(result.Result, result.Detail);
    }

    // 処理状態と理由の更新
    //***************************************************************************
    //*<引数> newStatus : 処理状態 / reason : 表示する理由
    //*-------------------------------------------------------------------------
    //*<返値> なし
    //*-------------------------------------------------------------------------
    //*<説明> 待機・中止による未処理も含め、表示用の変更を画面へ通知します。
    //***************************************************************************
    public void SetState(string newStatus, string reason)
    {
        status = newStatus; detail = reason;
        Notify(nameof(Status)); Notify(nameof(Detail)); Notify(nameof(Symbol));
    }

    // 一覧への変更通知
    //***************************************************************************
    //*<引数> property : 変更された項目名（省略時は呼出元名）
    //*-------------------------------------------------------------------------
    //*<返値> なし
    //*-------------------------------------------------------------------------
    //*<説明> Windows Formsの一覧へ変更を通知します。
    //***************************************************************************
    private void Notify([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

#endregion

#region 対象ファイルの取得

//==============================
//1. 定義部・共通処理
//==============================

public static class TargetFiles
{
    // 複数ファイルの検査と重複排除
    //***************************************************************************
    //*<引数> paths : 選択したファイル / cancellationToken : 中止要求
    //*-------------------------------------------------------------------------
    //*<返値> 絶対パスの一覧（同一パスは1件）
    //*-------------------------------------------------------------------------
    //*<説明> 全選択ファイルの検査成功後に一覧へ追加します。同名でも別の場所なら保持します。
    //***************************************************************************
    public static string[] CollectMany(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selected = new List<string>();
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Collect(path, true, cancellationToken)[0];
            if (unique.Add(fullPath)) selected.Add(fullPath);
        }
        return selected.ToArray();
    }

    // 対象ファイルの取得
    //***************************************************************************
    //*<引数> input : 対象のフルパス / singleFile : 単一ファイル指定か / cancellationToken : 中止要求
    //*-------------------------------------------------------------------------
    //*<返値> 対象ファイルの絶対パス一覧
    //*-------------------------------------------------------------------------
    //*<説明> フォルダー指定時は配下のEXE・DLLを再帰取得します。一覧取得失敗時は署名を開始しません。
    //***************************************************************************
    public static string[] Collect(string input, bool singleFile, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(input)) throw new ArgumentException("対象を指定してください。");
        if (!Path.IsPathFullyQualified(input)) throw new ArgumentException("対象はファイルまたはフォルダーのフルパスで指定してください。");
        var fullPath = Path.GetFullPath(input);
        if (singleFile)
        {
            if (!File.Exists(fullPath)) throw new FileNotFoundException("指定したファイルが見つかりません。", fullPath);
            return [fullPath];
        }
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException("指定したフォルダーが見つかりません。");
        // 一覧取得が失敗した場合は全体を中止。再解析ポイントの外へは移動しない。
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint };
        return Directory.EnumerateFiles(fullPath, "*", options)
            .Select(path => { cancellationToken.ThrowIfCancellationRequested(); return path; })
            .Where(path => Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

#endregion
