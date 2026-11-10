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
    private readonly ComboBox mode = new() { Name = "SigningMode", DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox usbStore = new() { Name = "UsbStore", DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox tokenCertificates = new() { Name = "UsbCertificates", DropDownStyle = ComboBoxStyle.DropDownList, DropDownWidth = 850 };
    private readonly TextBox usbTool = new() { Name = "SignToolPath" };
    private readonly TextBox thumbprint = new() { Name = "UsbThumbprint" };
    private readonly TextBox usbPublisher = new() { Name = "UsbPublisherName" };
    private Control[] azureRows = [];
    private Control[] usbRows = [];
    private readonly Label tokenNotice = new() { AutoSize = true, MaximumSize = new Size(570, 0), Text = "USBの公式ドライバーとWindows SDKが必要です。PINはドライバーの画面で入力します。" };
    private Button loadCertificates = null!;
    private Button browseSignTool = null!;
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
        Size = new Size(980, 900); MinimumSize = new Size(900, 680);
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
        mode.Items.AddRange(["Azure Key Vault", "USBトークン（Microsoft SignTool）"]);
        usbStore.Items.AddRange(["現在のユーザー：個人（My）", "ローカルコンピューター：個人（My）"]);
        mode.SelectedIndex = Settings.UseUsbToken ? 1 : 0;
        usbStore.SelectedIndex = Settings.UsbLocalMachine ? 1 : 0;
        AddRow(fields, "署名方式", mode);
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
                var draft = Settings.Copy(); draft.UseUsbToken = mode.SelectedIndex == 1;
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
        AddRow(fields, "タイムスタンプURL", timestamp);
        var azureFirstRow = fields.RowCount;
        AddRow(fields, "証明書名", certificate);
        AddSection(fields, "接続情報");
        AddRow(fields, "Key Vault URL", vault);
        AddRow(fields, "クライアントID", client);
        AddRow(fields, "テナントID", tenant);
        AddRow(fields, "クライアントシークレット", secret);
        AddRow(fields, "自社証明書の発行元名", publisher);
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
        var usbFirstRow = fields.RowCount;
        AddSection(fields, "USBトークン");
        AddRow(fields, "証明書ストア", usbStore);
        loadCertificates = new Button { Name = "LoadUsbCertificates", Text = "証明書を読込", AutoSize = true };
        UiTheme.StyleButton(loadCertificates);
        var certificatePicker = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        certificatePicker.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); certificatePicker.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tokenCertificates.Dock = DockStyle.Fill;
        certificatePicker.Controls.Add(tokenCertificates, 0, 0); certificatePicker.Controls.Add(loadCertificates, 1, 0);
        loadCertificates.Click += (_, _) => LoadUsbCertificates();
        tokenCertificates.SelectedIndexChanged += (_, _) =>
        {
            if (tokenCertificates.SelectedItem is UsbCertificate selected) { thumbprint.Text = selected.Thumbprint; usbPublisher.Text = selected.Publisher; }
        };
        usbStore.SelectedIndexChanged += (_, _) => { tokenCertificates.Items.Clear(); thumbprint.Clear(); tokenNotice.Text = "ストアを変更しました。「証明書を読込」から選択してください。"; };
        AddRow(fields, "コード署名証明書", certificatePicker);
        AddRow(fields, "証明書の拇印", thumbprint);
        AddRow(fields, "USB証明書の発行元名", usbPublisher);
        browseSignTool = new Button { Text = "参照…", AutoSize = true };
        var findSignTool = new Button { Name = "FindSignTool", Text = "自動検出", AutoSize = true };
        UiTheme.StyleButton(browseSignTool); UiTheme.StyleButton(findSignTool);
        var signToolPicker = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Margin = Padding.Empty };
        signToolPicker.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        signToolPicker.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); signToolPicker.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        usbTool.Dock = DockStyle.Fill; usbTool.BorderStyle = BorderStyle.FixedSingle;
        signToolPicker.Controls.Add(usbTool, 0, 0); signToolPicker.Controls.Add(browseSignTool, 1, 0); signToolPicker.Controls.Add(findSignTool, 2, 0);
        browseSignTool.Click += (_, _) =>
        {
            try
            {
                using var dialog = new OpenFileDialog { Title = "Windows SDKのsigntool.exeを選択", Filter = "Microsoft SignTool|signtool.exe", CheckFileExists = true };
                if (dialog.ShowDialog(this) == DialogResult.OK) usbTool.Text = dialog.FileName;
            }
            catch (Exception ex) { ShowError(ex, "SignToolの参照"); }
        };
        findSignTool.Click += (_, _) =>
        {
            try { usbTool.Text = UsbSigning.FindSignTool(usbTool.Text.Trim().Trim('"')); tokenNotice.Text = File.Exists(usbTool.Text) ? "導入済みのSignToolを確認しました。" : "SignToolが見つかりません。Microsoft公式サイトからWindows SDKを導入してください。"; }
            catch (Exception ex) { ShowError(ex, "SignToolの検出"); }
        };
        AddRow(fields, "Microsoft SignTool", signToolPicker);
        var getSdk = new LinkLabel { Text = "Windows SDKの公式入手ページ", AutoSize = true, LinkColor = UiTheme.Text };
        getSdk.LinkClicked += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://developer.microsoft.com/windows/downloads/windows-sdk/") { UseShellExecute = true }); }
            catch (Exception ex) { ShowError(ex, "Windows SDKの入手ページ"); }
        };
        AddRow(fields, "未導入の場合", getSdk);
        AddRow(fields, "USB利用時の注意", tokenNotice);
        azureRows = fields.Controls.Cast<Control>().Where(control => fields.GetRow(control) >= azureFirstRow && fields.GetRow(control) < usbFirstRow).ToArray();
        usbRows = fields.Controls.Cast<Control>().Where(control => fields.GetRow(control) >= usbFirstRow).ToArray();
        mode.SelectedIndexChanged += (_, _) => ApplySigningMode();
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
        usbTool.Text = UsbSigning.FindSignTool(Settings.SignToolPath); thumbprint.Text = Settings.UsbThumbprint; usbPublisher.Text = Settings.UsbPublisherName;
        ApplySigningMode();
        Shown += (_, _) => { if (mode.SelectedIndex == 0 && !File.Exists(tool.Text)) ShowInstallationGuide(); };
    }

    // 署名方式の切替
    //***************************************************************************
    //*<引数> なし / <返値> なし
    //*<説明> 使用しない方式の入力を無効化し、保存時も選択中の方式だけを要求します。
    //***************************************************************************
    private void ApplySigningMode()
    {
        var usb = mode.SelectedIndex == 1;
        // 選択した方式の項目だけを表示し、保存前の切替でも他方式の入力値を保持する。
        foreach (var control in azureRows) control.Visible = !usb;
        foreach (var control in usbRows) control.Visible = usb;
        foreach (var control in new Control[] { certificate, vault, client, tenant, secret, tool }) control.Enabled = !usb;
        foreach (var control in new Control[] { usbStore, tokenCertificates, thumbprint, usbTool, loadCertificates, browseSignTool }) control.Enabled = usb;
    }

    // USB証明書候補の表示
    //***************************************************************************
    //*<引数> なし / <返値> なし
    //*<説明> 公開情報だけを読込し、選択時に拇印と発行元を反映します。自動選択や署名はしません。
    //***************************************************************************
    private void LoadUsbCertificates()
    {
        try
        {
            var choices = UsbSigning.ReadCertificates(usbStore.SelectedIndex == 1);
            tokenCertificates.Items.Clear(); tokenCertificates.Items.AddRange(choices.Cast<object>().ToArray());
            tokenNotice.Text = choices.Length == 0 ? "有効なコード署名証明書がありません。USB接続、公式ドライバー、証明書ストアを確認してください。"
                : "USB管理画面と拇印を照合して選んでください。候補にはUSB以外の鍵も含まれます。PINは保存しません。";
            tokenCertificates.SelectedIndex = -1;
        }
        catch (Exception ex) { ShowError(ex, "USB証明書の読込"); }
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
            draft.UseUsbToken = mode.SelectedIndex == 1;
            draft.SignToolPath = usbTool.Text.Trim().Trim('"');
            draft.UsbThumbprint = UsbSigning.NormalizeThumbprint(thumbprint.Text);
            draft.UsbPublisherName = usbPublisher.Text.Trim();
            draft.UsbLocalMachine = usbStore.SelectedIndex == 1;
            draft.LimitUsagePeriod = limitPeriod.Checked;
            draft.StartDate = start.Value.Date; draft.EndDate = end.Value.Date;
            draft.CertificateName = certificate.Text.Trim(); draft.VaultUrl = vault.Text.Trim();
            draft.ClientId = client.Text.Trim(); draft.TenantId = tenant.Text.Trim();
            draft.ToolPath = tool.Text.Trim().Trim('"'); draft.TimestampUrl = timestamp.Text.Trim(); draft.PublisherName = publisher.Text.Trim();
            SettingsValidation.ValidateConnection(draft);
            if (draft.IsExpired(DateTime.Today)) throw new ArgumentException("利用終了日を今日以降に設定してください。");
            if (draft.UseUsbToken)
            {
                if (!File.Exists(draft.SignToolPath)) throw new FileNotFoundException("SignToolが見つかりません。Windows SDKを導入し「自動検出」を押してください。");
                UsbSigning.ValidateCertificate(draft);
            }
            else if (!File.Exists(draft.ToolPath))
            {
                if (!ShowInstallationGuide()) return;
                draft.ToolPath = tool.Text.Trim().Trim('"');
                SettingsValidation.ValidateConnection(draft);
                if (!File.Exists(draft.ToolPath)) throw new FileNotFoundException("AzureSignToolが見つかりません。");
            }
            if (!draft.UseUsbToken)
            {
                if (!string.IsNullOrEmpty(secret.Text)) draft.ProtectedSecret = SecretProtection.Protect(secret.Text);
                if (!draft.IsConfigured) throw new ArgumentException("クライアントシークレットを入力してください。");
                _ = SecretProtection.Unprotect(draft.ProtectedSecret);
            }
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
