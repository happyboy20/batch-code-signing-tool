//*******************************************************************************
//*<機能名>     : デジタル署名ツール
//*<ID>         : Program
//*<種別>       : メインモジュール
//*<依存ﾓｼﾞｭｰﾙ> : SettingsStore / SettingsForm / MainForm
//*------------------------------------------------------------------------------
//*<説明>       : 初回はメイン画面を表示し、期限切れ時だけ設定画面へ移動します。
//*------------------------------------------------------------------------------
//*<必要な      : .NET 10 Desktop Runtime（Windows x64）
//*  参照設定>  : DigitalSigningTool.NET10.csproj
//*------------------------------------------------------------------------------
//*******************************************************************************



namespace DigitalSigningTool;

#region 起動処理

//==============================
//1. 定義部・共通処理
//==============================

internal static class Program
{
    [STAThread]
    // アプリケーション起動
    //***************************************************************************
    //*<引数> なし
    //*-------------------------------------------------------------------------
    //*<返値> なし
    //*-------------------------------------------------------------------------
    //*<説明> 画面環境を初期化し、未処理例外を表示して終了します。
    //***************************************************************************
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        try { RunApplication(); }
        catch (Exception ex)
        {
            MessageBox.Show(ErrorDetails.Format(ex, typeof(Program), nameof(RunApplication)), "署名ツールの実行エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // 設定読込と起動画面の選択
    //***************************************************************************
    //*<引数> なし
    //*-------------------------------------------------------------------------
    //*<返値> なし
    //*-------------------------------------------------------------------------
    //*<説明> 保存設定を読み込み、期限に応じて画面を表示します。読込障害時は既存設定を変更せず終了します。
    //***************************************************************************
    private static void RunApplication()
    {
        SigningSettings settings;
        try { settings = SettingsStore.Load(); }
        catch (Exception ex)
        {
            MessageBox.Show("設定を読み込めないため終了します。既存の設定は変更していません。\n" +
                ErrorDetails.Format(ex, typeof(SettingsStore), nameof(SettingsStore.Load)), "設定の確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        // 起動時の画面遷移は利用期限だけで判定する。
        // 認証情報・署名ツールの準備は設定操作または署名開始時に扱う。
        if (settings.IsExpired(DateTime.Today))
        {
            using var form = new SettingsForm(settings);
            if (form.ShowDialog() != DialogResult.OK) return;
            settings = form.Settings;
        }
        Application.Run(new MainForm(settings));
    }
}

#endregion

#region 画面共通デザイン

internal static class UiTheme
{
    public static readonly Color Background = Color.FromArgb(243, 246, 250);
    public static readonly Color Text = Color.FromArgb(25, 48, 77);
    public static readonly Color Primary = Color.FromArgb(35, 101, 194);
    public static readonly Color Muted = Color.FromArgb(95, 112, 134);
    public static readonly Color Border = Color.FromArgb(210, 220, 231);

    // 画面共通の表示設定
    //***************************************************************************
    //*<引数> form : 対象画面 / title : 画面タイトル
    //*-------------------------------------------------------------------------
    //*<返値> なし
    //*-------------------------------------------------------------------------
    //*<説明> 全画面で同じ書体、背景、文字色、アプリアイコンを使います。
    //***************************************************************************
    public static void Apply(Form form, string title)
    {
        form.Text = title;
        form.Font = new Font("Yu Gothic UI", 10);
        form.BackColor = Background; form.ForeColor = Text;
        form.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
    }

    // ボタンの共通表示設定
    //***************************************************************************
    //*<引数> button : 対象 / primary : 主要操作か / danger : 停止操作か
    //*-------------------------------------------------------------------------
    //*<返値> なし
    //*-------------------------------------------------------------------------
    //*<説明> 主要操作を青、補助操作を白、停止操作の文字を赤に統一します。
    //***************************************************************************
    public static void StyleButton(Button button, bool primary = false, bool danger = false)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = primary ? Primary : Color.White;
        button.ForeColor = primary ? Color.White : danger ? Color.Firebrick : Text;
        button.FlatAppearance.BorderColor = primary ? Primary : Border;
        button.Padding = new Padding(12, 6, 12, 6);
        button.Margin = new Padding(0, 0, 8, 0);
        button.Cursor = Cursors.Hand; button.UseVisualStyleBackColor = false;
    }

    // 画面見出しの作成
    //***************************************************************************
    //*<引数> title : 見出し / description : 説明用コントロール
    //*-------------------------------------------------------------------------
    //*<返値> 見出しと説明を配置したパネル
    //*-------------------------------------------------------------------------
    //*<説明> 共通サイズの見出しと補助色の説明を縦に配置します。
    //***************************************************************************
    public static Control Heading(string title, Control description)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        panel.Controls.Add(new Label { Text = title, AutoSize = true, ForeColor = Text, Font = new Font("Yu Gothic UI", 18, FontStyle.Bold), Margin = Padding.Empty });
        description.ForeColor = Muted; description.Margin = new Padding(0, 6, 0, 0);
        panel.Controls.Add(description);
        return panel;
    }
}

#endregion

#region エラー表示

//==============================
//1. 定義部・共通処理
//==============================

public static class ErrorDetails
{
    // エラー表示文字列の作成
    //***************************************************************************
    //*<引数> error : 例外 / owner : モジュール型 / procedure : 処理名 / secrets : 非表示にする値
    //*-------------------------------------------------------------------------
    //*<返値> 画面表示用のエラー文字列
    //*-------------------------------------------------------------------------
    //*<説明> 例外内容・モジュール名・処理名を組み立てます。ログは保存しません。
    //***************************************************************************
    public static string Format(Exception error, Type owner, string procedure, params string[] secrets)
    {
        var text = error.Message + Environment.NewLine + Environment.NewLine + owner.Name + Environment.NewLine + procedure;
        return Redact(text, secrets);
    }

    // 認証情報の伏字化
    //***************************************************************************
    //*<引数> text : 表示文字列 / secrets : 非表示にする値
    //*-------------------------------------------------------------------------
    //*<返値> 認証情報を置換した文字列
    //*-------------------------------------------------------------------------
    //*<説明> 認証情報を伏せた後、表示用の文字数上限を適用します。
    //***************************************************************************
    public static string Redact(string text, params string[] secrets)
    {
        foreach (var value in secrets)
        {
            if (!string.IsNullOrEmpty(value)) text = text.Replace(value, "[非表示]", StringComparison.Ordinal);
        }
        return text.Length > 6000 ? text[..6000] + "\n（以降省略）" : text;
    }
}

#endregion
