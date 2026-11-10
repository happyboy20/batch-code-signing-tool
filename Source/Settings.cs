//*******************************************************************************
//*<機能名>     : デジタル署名ツール
//*<ID>         : Settings
//*<種別>       : 設定・共通モジュール
//*<依存ﾓｼﾞｭｰﾙ> : SettingsValidation / System.Text.Json / Windows DPAPI
//*------------------------------------------------------------------------------
//*<説明>       : 設定の保持・保存・更新と、認証情報の暗号化を行います。
//*------------------------------------------------------------------------------
//*<必要な      : .NET 10 Desktop Runtime（Windows x64）
//*  参照設定>  : DigitalSigningTool.NET10.csproj
//*------------------------------------------------------------------------------
//*******************************************************************************

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;

namespace DigitalSigningTool;

#region 設定と暗号化

//==============================
//1. 定義部・共通処理
//==============================

public sealed class SigningSettings
{
    public bool LimitUsagePeriod { get; set; } = false;
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime EndDate { get; set; } = DateTime.Today.AddYears(1);
    public string VaultUrl { get; set; } = "";
    public string CertificateName { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string ProtectedSecret { get; set; } = "";
    public string ToolPath { get; set; } = @"C:\Program Files\AzureSignTool\AzureSignTool.exe";
    public string TimestampUrl { get; set; } = "http://timestamp.globalsign.com/tsa/r45standard";
    public string PublisherName { get; set; } = "";

    // 利用期限の判定
    //***************************************************************************
    //*<引数> today : PCの日付
    //*-------------------------------------------------------------------------
    //*<返値> true : 終了日超過 / false : 利用可能
    //*-------------------------------------------------------------------------
    //*<説明> 制限が有効な場合だけ終了日を判定します。終了日当日と開始日前の日付は利用可能です。
    //***************************************************************************
    public bool IsExpired(DateTime today) => LimitUsagePeriod && today.Date > EndDate.Date;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ProtectedSecret);

    // 証明書名と利用期間の次期更新
    //***************************************************************************
    //*<引数> なし
    //*-------------------------------------------------------------------------
    //*<返値> なし（更新できない場合は例外）
    //*-------------------------------------------------------------------------
    //*<説明> 利用期間を1年進めます。証明書名が半角4桁の年で始まる場合だけ、その年も更新します。
    //***************************************************************************
    public void AdvanceYear()
    {
        SettingsValidation.ValidateStructure(this);
        var match = Regex.Match(CertificateName, @"^[0-9]{4}");
        var nextName = CertificateName;
        if (match.Success)
        {
            var year = int.Parse(match.Value);
            if (year < 1 || year >= 9999) throw new ArgumentException("証明書名の先頭年は0001～9998にしてください。");
            nextName = (year + 1).ToString("D4") + CertificateName[4..];
        }
        else if (Regex.IsMatch(CertificateName, @"^[０-９]{4}"))
            throw new ArgumentException("証明書名の先頭年は半角数字で入力してください。");
        if (StartDate.Year >= 9998 || EndDate.Year >= 9998)
            throw new ArgumentException("次期の利用期間が設定可能な日付範囲を超えます。");
        var nextStart = StartDate.AddYears(1);
        var nextEnd = EndDate.AddYears(1);
        SettingsValidation.ValidatePeriod(nextStart, nextEnd);
        CertificateName = nextName;
        StartDate = nextStart;
        EndDate = nextEnd;
    }

    // 編集用設定の複製
    //***************************************************************************
    //*<引数> なし
    //*-------------------------------------------------------------------------
    //*<返値> 独立した設定の複製
    //*-------------------------------------------------------------------------
    //*<説明> 画面での編集中に利用中の設定を直接変更しないため、作業用コピーを作成します。
    //***************************************************************************
    public SigningSettings Copy() => JsonSerializer.Deserialize<SigningSettings>(JsonSerializer.Serialize(this))!;
}

public static class SettingsStore
{
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DigitalSigningTool.NET10", "settings.json");
    // 保存済み設定の読込
    //***************************************************************************
    //*<引数> filePath : 読込先（省略時は利用者の設定ファイル）
    //*-------------------------------------------------------------------------
    //*<返値> 読込設定。未作成の場合だけ初期設定
    //*-------------------------------------------------------------------------
    //*<説明> ファイル未作成と破損・権限エラーを区別し、不正設定による初期値への戻りを防ぎます。
    //***************************************************************************
    public static SigningSettings Load(string? filePath = null)
    {
        var path = filePath ?? FilePath;
        // File.Existsでは権限エラーもfalseになるため、初回と読込障害を区別する。
        string json;
        try { json = File.ReadAllText(path); }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        var settings = JsonSerializer.Deserialize<SigningSettings>(json) ?? throw new InvalidDataException("設定ファイルが空です。");
        SettingsValidation.ValidateStructure(settings);
        return settings;
    }

    // 設定ファイルの保存
    //***************************************************************************
    //*<引数> settings : 保存設定 / filePath : 保存先（省略可）
    //*-------------------------------------------------------------------------
    //*<返値> なし（保存できない場合は例外）
    //*-------------------------------------------------------------------------
    //*<説明> 一意の一時ファイルへ書込み、成功後に置換します。置換成功まで既存設定を保持します。
    //***************************************************************************
    public static void Save(SigningSettings settings, string? filePath = null)
    {
        SettingsValidation.ValidateStructure(settings);
        var path = Path.GetFullPath(filePath ?? FilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            File.Move(temporary, path, true);
        }
        finally
        {
            // 保存前の設定は置換成功まで保持。一時ファイルも後始末する。
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

// Windows DPAPI: 同じWindowsユーザーでのみ復号可能。平文は設定ファイルに保存しない。
public static class SecretProtection
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Length; public IntPtr Data; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    // Windows DPAPI暗号化API
    //***************************************************************************
    //*<引数> input : 入力 / description : 説明 / entropy・reserved・prompt : 未使用 / flags : 動作指定 / output : 出力
    //*-------------------------------------------------------------------------
    //*<返値> true : 成功 / false : Windows API失敗
    //*-------------------------------------------------------------------------
    //*<説明> DPAPIによるユーザー単位の暗号化を呼び出します。
    //***************************************************************************
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    // Windows DPAPI復号API
    //***************************************************************************
    //*<引数> input : 入力 / description・entropy・reserved・prompt : 未使用 / flags : 動作指定 / output : 出力
    //*-------------------------------------------------------------------------
    //*<返値> true : 成功 / false : Windows API失敗
    //*-------------------------------------------------------------------------
    //*<説明> DPAPIによる復号を呼び出します。
    //***************************************************************************
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")]
    // ネイティブメモリの解放
    //***************************************************************************
    //*<引数> memory : Windows APIが確保した領域
    //*-------------------------------------------------------------------------
    //*<返値> 解放成功時はゼロ
    //*-------------------------------------------------------------------------
    //*<説明> DPAPIが返したネイティブ領域を解放します。
    //***************************************************************************
    private static extern IntPtr LocalFree(IntPtr memory);

    // 暗号化・復号とメモリの後始末
    //***************************************************************************
    //*<引数> bytes : 処理対象バイト列 / protect : 暗号化か
    //*-------------------------------------------------------------------------
    //*<返値> 変換結果のバイト列
    //*-------------------------------------------------------------------------
    //*<説明> Windows DPAPIを呼び出し、finallyで平文バッファとネイティブ領域を解放します。
    //***************************************************************************
    private static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var ok = protect
                ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new CryptographicException(Marshal.GetLastWin32Error());
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            Marshal.Copy(new byte[bytes.Length], 0, input.Data, bytes.Length);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero)
            {
                Marshal.Copy(new byte[output.Length], 0, output.Data, output.Length);
                LocalFree(output.Data);
            }
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    // シークレットの暗号化
    //***************************************************************************
    //*<引数> value : 入力したシークレット
    //*-------------------------------------------------------------------------
    //*<返値> Base64形式の暗号化データ
    //*-------------------------------------------------------------------------
    //*<説明> 空白だけの入力は拒否し、Windowsユーザー単位で暗号化します。
    //***************************************************************************
    public static string Protect(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("クライアントシークレットを入力してください。");
        return Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(value), true));
    }
    // 保存済みシークレットの復号
    //***************************************************************************
    //*<引数> value : 保存済み暗号化データ
    //*-------------------------------------------------------------------------
    //*<返値> 復号したシークレット
    //*-------------------------------------------------------------------------
    //*<説明> 未設定・別ユーザーの暗号化データ・空の復号結果を拒否し、平文バッファを消去します。
    //***************************************************************************
    public static string Unprotect(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("認証情報が未設定です。設定画面でクライアントシークレットを入力してください。");
        var bytes = Transform(Convert.FromBase64String(value), false);
        try
        {
            var secret = Encoding.UTF8.GetString(bytes);
            if (string.IsNullOrWhiteSpace(secret)) throw new CryptographicException("保存された認証情報が空です。再入力してください。");
            return secret;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}

#endregion

#region 設定値の検査

//==============================
//1. 定義部・共通処理
//==============================

public static class SettingsValidation
{
    // 画面の日付選択範囲に合わせ、読込時点で表示不能な設定を拒否する。
    public static readonly DateTime MinimumDate = new(1753, 1, 1);
    public static readonly DateTime MaximumDate = new(9998, 12, 31);

    // 設定構造の検査
    //***************************************************************************
    //*<引数> settings : 検査する設定
    //*-------------------------------------------------------------------------
    //*<返値> なし（不正な場合は例外）
    //*-------------------------------------------------------------------------
    //*<説明> 日付範囲とnullを検査します。初回の認証情報未設定は許可します。
    //***************************************************************************
    public static void ValidateStructure(SigningSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidatePeriod(settings.StartDate, settings.EndDate);
        if (new[] { settings.CertificateName, settings.VaultUrl, settings.ClientId, settings.TenantId,
            settings.ProtectedSecret, settings.ToolPath, settings.TimestampUrl, settings.PublisherName }.Any(value => value is null))
            throw new InvalidDataException("設定項目にnullが含まれています。設定ファイルを確認してください。");
    }

    // 利用期間の検査
    //***************************************************************************
    //*<引数> start : 利用開始日 / end : 利用終了日
    //*-------------------------------------------------------------------------
    //*<返値> なし（不正な場合は例外）
    //*-------------------------------------------------------------------------
    //*<説明> 日付選択範囲と開始・終了の前後関係を確認します。現在日より前かどうかは判定しません。
    //***************************************************************************
    public static void ValidatePeriod(DateTime start, DateTime end)
    {
        if (start.Date < MinimumDate || end.Date > MaximumDate || start.Date > end.Date)
            throw new ArgumentException("利用期間は1753/01/01～9998/12/31の範囲で、終了日を開始日以降に設定してください。");
    }

    // 接続情報の検査
    //***************************************************************************
    //*<引数> settings : 検査する設定
    //*-------------------------------------------------------------------------
    //*<返値> なし（不正な場合は例外）
    //*-------------------------------------------------------------------------
    //*<説明> 保存・署名前に証明書名、発行元、ID、URL、EXEパスを確認します。
    //***************************************************************************
    public static void ValidateConnection(SigningSettings settings)
    {
        ValidateStructure(settings);
        if (string.IsNullOrWhiteSpace(settings.CertificateName) || string.IsNullOrWhiteSpace(settings.PublisherName))
            throw new ArgumentException("証明書名と自社証明書の発行元名を入力してください。");
        if (!Guid.TryParse(settings.ClientId, out var client) || client == Guid.Empty ||
            !Guid.TryParse(settings.TenantId, out var tenant) || tenant == Guid.Empty)
            throw new ArgumentException("クライアントIDとテナントIDを空でないGUID形式で入力してください。");
        ValidateUrl(settings.VaultUrl, true, "Key Vault URL");
        ValidateUrl(settings.TimestampUrl, false, "タイムスタンプURL");
        if (!Path.IsPathFullyQualified(settings.ToolPath) || !Path.GetExtension(settings.ToolPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("AzureSignToolの場所にEXEのフルパスを指定してください。");
    }

    // URLの検査
    //***************************************************************************
    //*<引数> value : URL / httpsOnly : HTTPS限定か / title : 項目名
    //*-------------------------------------------------------------------------
    //*<返値> なし（不正な場合は例外）
    //*-------------------------------------------------------------------------
    //*<説明> 許可するスキームとホストを確認し、URL内の認証情報を拒否します。
    //***************************************************************************
    private static void ValidateUrl(string value, bool httpsOnly, string title)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) ||
            (uri.Scheme != Uri.UriSchemeHttps && (httpsOnly || uri.Scheme != Uri.UriSchemeHttp)))
            throw new ArgumentException($"{title}に{(httpsOnly ? "https" : "httpまたはhttps")}のURLを指定してください。認証情報やフラグメントは含められません。");
    }
}

#endregion
