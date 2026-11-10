//*******************************************************************************
//*<機能名>     : デジタル署名ツール
//*<ID>         : SettingsForm
//*<種別>       : フォームモジュール
//*<依存ﾓｼﾞｭｰﾙ> : SigningSettings / SettingsValidation / SettingsStore / InstallationGuideForm
//*------------------------------------------------------------------------------
//*<説明>       : 利用期間・接続情報の編集と次期更新を行います。
//*------------------------------------------------------------------------------
//*<必要な      : .NET 10 Desktop Runtime（Windows x64）
//*  参照設定>  : DigitalSigningTool.NET10.csproj
//*------------------------------------------------------------------------------
//*******************************************************************************



namespace DigitalSigningTool;

#region 設定画面

public sealed class SettingsForm : Form
{
    //==============================
    //1. 定義部
    //==============================
    #region ############### 変数定義 ###############
    public SigningSettings Settings { get; private set; }
    private readonly CheckBox limitPeriod = new() { Name = "LimitUsagePeriod", Text = "利用期間の制限を有効にする", AutoSize = true };
    private readonly DateTimePicker start = new() { Format = DateTimePickerFormat.Short, Width = 160 };
    private readonly DateTimePicker end = new() { Format = DateTimePickerFormat.Short, Width = 160 };
    private readonly TextBox certificate = new();
    private readonly TextBox vault = new();
    private readonly TextBox client = new();
    private readonly TextBox tenant = new();
    private readonly TextBox secret = new() { UseSystemPasswordChar = true, PlaceholderText = "保存済みの場合、空欄で現在の値を保持" };
    private readonly TextBox tool = new();
    private readonly TextBox timestamp = new();
    private readonly TextBox publisher = new();
    private readonly Label notice = new() { AutoSize = true, MaximumSize = new Size(680, 0), Margin = new Padding(8) };

    #endregion

    //==============================
    //2. 初期化・画面操作・共通処理
    //==============================
    #region ############### 処理定義 ###############

    // 設定画面の初期化
    //***************************************************************************
    //*<引数> settings : 現在の設定
    //*-------------------------------------------------------------------------
    //*<返値> なし
    //*-------------------------------------------------------------------------
    //*<説明> 編集用コピーを作成し、利用期間・接続設定の入力欄とイベントを初期化します。
    //***************************************************************************
    public SettingsForm(SigningSettings settings)
    {
        UiTheme.Apply(this, "設定 - デジタル署名ツール");
        Settings = settings.Copy();
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(940, 820); MinimumSize = new Size(900, 680);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Background, Padding = new Padding(24), ColumnCount = 1, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);
        notice.MaximumSize = new Size(800, 0);
        notice.Text = settings.IsExpired(DateTime.Today)
            ? "利用期限を過ぎています。利用期間を更新して保存してください。"
            : "各項目はそのまま編集できます。変更後に「保存」を押してください。";
        var heading = UiTheme.Heading("設定", notice); heading.Margin = new Padding(0, 0, 0, 20);
        layout.Controls.Add(heading, 0, 0);
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, AutoScroll = true, Padding = new Padding(16), Margin = Padding.Empty };
        var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(4) };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.Controls.Add(fields); layout.Controls.Add(card, 0, 1);
        var next = new Button { Name = "RenewPeriod", Text = "次期へ更新", AutoSize = true };
        UiTheme.StyleButton(next);
        AddSection(fields, "利用期間・証明書", next);
        AddRow(fields, "期間の制限", limitPeriod);
        limitPeriod.CheckedChanged += (_, _) => { start.Enabled = end.Enabled = next.Enabled = limitPeriod.Checked; };
        limitPeriod.Checked = Settings.LimitUsagePeriod;
        start.Enabled = end.Enabled = next.Enabled = limitPeriod.Checked;
        next.Click += (_, _) =>
        {
            try
            {
                var draft = Settings.Copy();
                draft.StartDate = start.Value.Date; draft.EndDate = end.Value.Date; draft.CertificateName = certificate.Text.Trim();
                var previousName = draft.CertificateName;
                draft.AdvanceYear();
                start.Value = draft.StartDate; end.Value = draft.EndDate; certificate.Text = draft.CertificateName;
                notice.Text = "利用期間を1年進めました。" + (draft.CertificateName != previousName ? "証明書名の年も更新しました。" : "") + "「保存」で確定します。";
            }
            catch (Exception ex) { ShowError(ex, "次期へ更新"); }
        };
        foreach (var picker in new[] { start, end }) { picker.Format = DateTimePickerFormat.Custom; picker.CustomFormat = "yyyy/MM/dd"; picker.MaximumSize = new Size(210, 0); }
        AddRow(fields, "利用開始日", start);
        AddRow(fields, "利用終了日（当日まで）", end);
        AddRow(fields, "証明書名", certificate);
        AddSection(fields, "接続情報");
        AddRow(fields, "Key Vault URL", vault);
        AddRow(fields, "クライアントID", client);
        AddRow(fields, "テナントID", tenant);
        AddRow(fields, "クライアントシークレット", secret);
        AddRow(fields, "自社証明書の発行元名", publisher);
        AddRow(fields, "タイムスタンプURL", timestamp);
        AddSection(fields, "署名ツール");
        var toolPicker = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Margin = Padding.Empty };
        toolPicker.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolPicker.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolPicker.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var browseTool = new Button { Text = "参照…", AutoSize = true };
        var setupTool = new Button { Text = "自動セットアップ", AutoSize = true };
        UiTheme.StyleButton(browseTool); UiTheme.StyleButton(setupTool);
        tool.BorderStyle = BorderStyle.FixedSingle; tool.Dock = DockStyle.Fill; tool.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        toolPicker.Controls.Add(tool, 0, 0); toolPicker.Controls.Add(browseTool, 1, 0); toolPicker.Controls.Add(setupTool, 2, 0);
        browseTool.Click += (_, _) =>
        {
            try
            {
                using var dialog = new OpenFileDialog { Title = "AzureSignTool.exeを選択", Filter = "AzureSignTool.exe|AzureSignTool.exe|EXEファイル|*.exe", CheckFileExists = true };
                if (dialog.ShowDialog(this) == DialogResult.OK) tool.Text = dialog.FileName;
            }
            catch (Exception ex) { ShowError(ex, "AzureSignToolの参照"); }
        };
        setupTool.Click += (_, _) => ShowInstallationGuide();
        AddRow(fields, "AzureSignToolの場所", toolPicker);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 16, 0, 0) };
        var save = new Button { Name = "SaveSettings", Text = "保存", AutoSize = true };
        var close = new Button { Name = "CloseSettings", Text = "キャンセル", AutoSize = true, DialogResult = DialogResult.Cancel };
        UiTheme.StyleButton(save, primary: true); UiTheme.StyleButton(close);
        buttons.Controls.AddRange([save, close]); layout.Controls.Add(buttons, 0, 2);
        CancelButton = close; AcceptButton = save;
        save.Click += (_, _) => SaveSettings();
        start.Value = Settings.StartDate; end.Value = Settings.EndDate;
        certificate.PlaceholderText = "署名に使用する証明書名"; vault.PlaceholderText = "https://your-vault.vault.azure.net/";
        client.PlaceholderText = "アプリケーション（クライアント）ID"; tenant.PlaceholderText = "ディレクトリ（テナント）ID";
        publisher.PlaceholderText = "証明書のCN（会社名）";
        certificate.Text = Settings.CertificateName; vault.Text = Settings.VaultUrl;
        client.Text = Settings.ClientId; tenant.Text = Settings.TenantId;
        tool.Text = InstallationGuideForm.FindExisting(Settings.ToolPath); timestamp.Text = Settings.TimestampUrl; publisher.Text = Settings.PublisherName;
        Shown += (_, _) => { if (!File.Exists(tool.Text)) ShowInstallationGuide(); };
    }

    // 設定項目の見出し追加
    //***************************************************************************
    //*<引数> layout : 配置先 / title : 見出し / action : 補助操作（省略可）
    //*-------------------------------------------------------------------------
    //*<返値> なし
    //*-------------------------------------------------------------------------
    //*<説明> 利用期間・接続情報・署名ツールを同じ見た目で区分します。
    //***************************************************************************
    private static void AddSection(TableLayoutPanel layout, string title, Control? action = null)
    {
        var row = layout.RowCount++;
        var label = new Label { Text = title, AutoSize = true, ForeColor = UiTheme.Text, Font = new Font("Yu Gothic UI", 11, FontStyle.Bold), Anchor = AnchorStyles.Left, Margin = new Padding(6, 16, 6, 12) };
        layout.Controls.Add(label, 0, row);
        if (action is not null) { action.Anchor = AnchorStyles.Left; action.Margin = new Padding(6, 8, 0, 8); layout.Controls.Add(action, 1, row); }
        else layout.SetColumnSpan(label, 2);
    }

    // 自動セットアップ画面の表示
    //***************************************************************************
    //*<引数> なし
    //*-------------------------------------------------------------------------
    //*<返値> true : パス反映済み / false : キャンセルまたは失敗
    //*-------------------------------------------------------------------------
    //*<説明> セットアップ画面を開き、成功した場合だけEXEの場所を反映します。
    //***************************************************************************
    private bool ShowInstallationGuide()
    {
        try
        {
            using var guide = new InstallationGuideForm(tool.Text.Trim().Trim('"'));
            if (guide.ShowDialog(this) != DialogResult.OK) return false;
            tool.Text = guide.SelectedPath;
            return true;
        }
        catch (Exception ex) { ShowError(ex, nameof(ShowInstallationGuide)); return false; }
    }

    // 設定項目の行追加
    //***************************************************************************
    //*<引数> layout : 配置先 / title : 項目名 / input : 入力コントロール
    //*-------------------------------------------------------------------------
    //*<返値> なし
    //*-------------------------------------------------------------------------
    //*<説明> 設定画面へ項目名と入力欄を1行追加します。
    //***************************************************************************
    private static void AddRow(TableLayoutPanel layout, string title, Control input)
    {
        var row = layout.RowCount;
        layout.RowCount = row + 1;
        layout.Controls.Add(new Label { Text = title, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(6, 8, 6, 8) }, 0, row);
        input.Dock = DockStyle.Fill; input.Margin = new Padding(6, 6, 6, 6);
        if (input is TextBox text) { text.BorderStyle = BorderStyle.FixedSingle; text.BackColor = Color.White; text.ForeColor = UiTheme.Text; }
        layout.Controls.Add(input, 1, row);
    }

    // 設定値の検査と保存
    //***************************************************************************
    //*<引数> なし
    //*-------------------------------------------------------------------------
    //*<返値> なし
    //*-------------------------------------------------------------------------
    //*<説明> 入力検査・認証情報の復号確認・保存の順に処理し、成功した場合だけ利用設定を置き換えます。
    //***************************************************************************
    private void SaveSettings()
    {
        try
        {
            var draft = Settings.Copy();
            draft.LimitUsagePeriod = limitPeriod.Checked;
            draft.StartDate = start.Value.Date; draft.EndDate = end.Value.Date;
            draft.CertificateName = certificate.Text.Trim(); draft.VaultUrl = vault.Text.Trim();
            draft.ClientId = client.Text.Trim(); draft.TenantId = tenant.Text.Trim();
            draft.ToolPath = tool.Text.Trim().Trim('"'); draft.TimestampUrl = timestamp.Text.Trim(); draft.PublisherName = publisher.Text.Trim();
            SettingsValidation.ValidateConnection(draft);
            if (draft.IsExpired(DateTime.Today)) throw new ArgumentException("利用終了日を今日以降に設定してください。");
            if (!File.Exists(draft.ToolPath))
            {
                if (!ShowInstallationGuide()) return;
                draft.ToolPath = tool.Text.Trim().Trim('"');
                SettingsValidation.ValidateConnection(draft);
                if (!File.Exists(draft.ToolPath)) throw new FileNotFoundException("AzureSignToolが見つかりません。");
            }
            if (!string.IsNullOrEmpty(secret.Text)) draft.ProtectedSecret = SecretProtection.Protect(secret.Text);
            if (!draft.IsConfigured) throw new ArgumentException("クライアントシークレットを入力してください。");
            _ = SecretProtection.Unprotect(draft.ProtectedSecret);
            SettingsStore.Save(draft); Settings = draft; secret.Clear();
            DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex) { ShowError(ex, nameof(SaveSettings)); }
    }

    // エラー内容の表示
    //***************************************************************************
    //*<引数> error : 例外 / procedure : 処理名
    //*-------------------------------------------------------------------------
    //*<返値> なし
    //*-------------------------------------------------------------------------
    //*<説明> 入力中のシークレットを伏せ、モジュール名と処理名を付けて表示します。
    //***************************************************************************
    private void ShowError(Exception error, string procedure) => MessageBox.Show(this,
        ErrorDetails.Format(error, typeof(SettingsForm), procedure, secret.Text), "設定の確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    #endregion

}

#endregion

#region 自動セットアップ画面

public sealed class InstallationGuideForm : Form
{
    //==============================
    //1. 定義部
    //==============================
    #region ############### 変数定義 ###############
    public string SelectedPath { get; private set; } = "";
    private bool installing;
    private readonly CancellationTokenSource cancellation = new();

    #endregion

    //==============================
    //2. 初期化・画面操作・共通処理
    //==============================
    #region ############### 処理定義 ###############

    // 導入済みEXEの検索
    //***************************************************************************
    //*<引数> configured : 設定済みEXEパス
    //*-------------------------------------------------------------------------
    //*<返値> 見つかったEXEパス。未発見時は設定値
    //*-------------------------------------------------------------------------
    //*<説明> 設定先、ユーザー配置先、標準の導入先の順でEXEを探します。起動は行いません。
    //***************************************************************************
    public static string FindExisting(string configured) => new[]
    {
        configured, AzureSignToolInstaller.InstallPath, @"C:\Program Files\AzureSignTool\AzureSignTool.exe",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools", "AzureSignTool.exe")
    }.FirstOrDefault(File.Exists) ?? configured;

    // セットアップ画面の初期化
    //***************************************************************************
    //*<引数> configured : 設定済みEXEパス
    //*-------------------------------------------------------------------------
    //*<返値> なし
    //*-------------------------------------------------------------------------
    //*<説明> インストール・中止・既存EXE選択の画面を作成し、完了時だけ結果を返します。
    //***************************************************************************
    public InstallationGuideForm(string configured)
    {
        UiTheme.Apply(this, "セットアップ - デジタル署名ツール");
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 400); MinimumSize = new Size(720, 420);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Background, Padding = new Padding(24), ColumnCount = 1, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 5; i++) layout.RowStyles.Add(new RowStyle(i == 1 ? SizeType.Percent : SizeType.AutoSize, i == 1 ? 100 : 0));
        Controls.Add(layout);
        var heading = UiTheme.Heading("署名ツールの準備", new Label { Text = "初回に必要なツールを、自動でインストールします。", AutoSize = true });
        heading.Margin = new Padding(0, 0, 0, 20); layout.Controls.Add(heading, 0, 0);
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(20), Margin = Padding.Empty };
        card.Controls.Add(new Label
        {
            Text = "「自動インストール」を押すと、ダウンロードから設定への反映まで完了します。\n\nすでに導入済みの場合は、EXEを選択してください。",
            Dock = DockStyle.Fill,
            AutoSize = true,
            MaximumSize = new Size(660, 0)
        });
        layout.Controls.Add(card, 0, 1);
        var status = new Label { Text = "インターネット接続が必要です。", ForeColor = UiTheme.Muted, AutoSize = true, MaximumSize = new Size(680, 0), Margin = new Padding(0, 12, 0, 8) };
        var progress = new ProgressBar { Dock = DockStyle.Fill, Height = 8, Style = ProgressBarStyle.Continuous };
        layout.Controls.Add(status, 0, 2); layout.Controls.Add(progress, 0, 3);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 16, 0, 0) };
        var install = new Button { Name = "InstallTool", Text = "自動インストール", AutoSize = true };
        var existing = new Button { Text = "導入済みのEXEを選ぶ", AutoSize = true };
        var close = new Button { Text = "閉じる", AutoSize = true };
        UiTheme.StyleButton(install, primary: true); UiTheme.StyleButton(existing); UiTheme.StyleButton(close);
        actions.Controls.AddRange([install, existing, close]); layout.Controls.Add(actions, 0, 4);
        close.Click += (_, _) => Close(); CancelButton = close;
        existing.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = "AzureSignTool.exeを選択", Filter = "AzureSignTool.exe|AzureSignTool.exe|EXEファイル|*.exe", CheckFileExists = true };
            var found = FindExisting(configured);
            if (File.Exists(found)) dialog.FileName = found;
            if (dialog.ShowDialog(this) == DialogResult.OK) { SelectedPath = dialog.FileName; DialogResult = DialogResult.OK; Close(); }
        };
        install.Click += async (_, _) =>
        {
            installing = true; install.Enabled = existing.Enabled = false; close.Text = "中止";
            UiTheme.StyleButton(close, danger: true); progress.Value = 0;
            status.ForeColor = UiTheme.Muted; status.Text = "公式サイトからダウンロードしています…";
            var reporter = new Progress<int>(value =>
            {
                if (IsDisposed || Disposing || !installing) return;
                progress.Value = value;
                status.Text = value >= 95 ? "ファイルを検証して配置しています…" : $"ダウンロード中… {value}%";
            });
            var completed = false;
            try
            {
                SelectedPath = await AzureSignToolInstaller.InstallAsync(reporter, cancellation.Token);
                completed = true;
            }
            catch (OperationCanceledException) { status.Text = "インストールを中止しました。"; }
            catch (Exception ex) { status.Text = "インストールできませんでした。\n" + ErrorDetails.Format(ex, typeof(AzureSignToolInstaller), nameof(AzureSignToolInstaller.InstallAsync)); status.ForeColor = Color.Firebrick; }
            finally { installing = false; install.Enabled = existing.Enabled = true; close.Text = "閉じる"; UiTheme.StyleButton(close); }
            if (completed) { DialogResult = DialogResult.OK; Close(); }
            else if (cancellation.IsCancellationRequested) Close();
        };
        FormClosing += (_, e) => { if (installing) { e.Cancel = true; cancellation.Cancel(); status.Text = "中止しています…"; } };
        FormClosed += (_, _) => cancellation.Dispose();
    }
    #endregion

}

#endregion
