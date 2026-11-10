//*******************************************************************************
//*<機能名>     : デジタル署名ツール
//*<ID>         : SigningService
//*<種別>       : 署名処理モジュール
//*<依存ﾓｼﾞｭｰﾙ> : SigningSettings / SettingsValidation / ErrorDetails / AzureSignTool / Windows署名API
//*------------------------------------------------------------------------------
//*<説明>       : 既存署名を保護し、署名コマンドの実行と結果確認を行います。
//*------------------------------------------------------------------------------
//*<必要な      : .NET 10 Desktop Runtime（Windows x64）
//*  参照設定>  : DigitalSigningTool.NET10.csproj
//*------------------------------------------------------------------------------
//*******************************************************************************

using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Text;
using System.Reflection.PortableExecutable;

namespace DigitalSigningTool;

#region 署名処理

//==============================
//1. 定義部・共通処理
//==============================

public sealed record FileResult(string Path, string Result, string Detail);
internal sealed record ProcessOutput(int Code, string StandardOutput, string StandardError)
{
    public string Combined => StandardOutput + "\n" + StandardError;
}
public sealed class SignatureInfo
{
    public string Status { get; set; } = "";
    public string Publisher { get; set; } = "";
    public bool HasCertificate { get; set; }
    public bool HasTimestamp { get; set; }
}

public static class SigningService
{
    // 外部プロセスの実行と停止
    //***************************************************************************
    //*<引数> executable : EXEパス / arguments : 引数一覧 / cancellationToken : 中止要求
    //*-------------------------------------------------------------------------
    //*<返値> 終了コード・標準出力・標準エラーを分離した結果を返すTask
    //*-------------------------------------------------------------------------
    //*<説明> 標準出力と標準エラーを分離して読み取ります。3分超過または中止要求でプロセスツリーを停止します。
    //***************************************************************************
    internal static async Task<ProcessOutput> ExecuteAsync(string executable, IEnumerable<string> arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("外部ツールを起動できませんでした。");
        var stdout = ReadBoundedAsync(process.StandardOutput);
        var stderr = ReadBoundedAsync(process.StandardError);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); }
            catch (InvalidOperationException) when (process.HasExited) { /* 終了との競合時は終了待ちへ進む。 */ }
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("処理が3分以内に終了しませんでした。ファイルの署名状態を確認してください。");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(process.ExitCode, await stdout, await stderr);
    }

    // 上限付きの標準出力読込
    //***************************************************************************
    //*<引数> reader : 外部プロセスの出力
    //*-------------------------------------------------------------------------
    //*<返値> 上限文字数までの出力を返すTask
    //*-------------------------------------------------------------------------
    //*<説明> 大量出力によるメモリ増加とパイプ詰まりを防ぐため、上限以降は読み捨てます。
    //***************************************************************************
    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        const int maximum = 32768;
        var output = new StringBuilder();
        var buffer = new char[4096];
        int count;
        // 上限以降も読み捨て、標準出力の詰まりによる停止を防ぐ。
        while ((count = await reader.ReadAsync(buffer)) > 0)
        {
            var keep = Math.Min(count, maximum - output.Length);
            if (keep > 0) output.Append(buffer, 0, keep);
        }
        return output.ToString();
    }

    // ファイルの署名状態取得
    //***************************************************************************
    //*<引数> path : 対象ファイル / cancellationToken : 中止要求
    //*-------------------------------------------------------------------------
    //*<返値> 署名状態・発行元・タイムスタンプ有無を返すTask
    //*-------------------------------------------------------------------------
    //*<説明> C#からWindows署名APIを呼び出します。検証は読取専用で行い、スクリプトは使用しません。
    //***************************************************************************
    internal static Task<SignatureInfo> InspectAsync(string path, CancellationToken cancellationToken = default)
        => Task.Run(() => NativeSignature.Inspect(path, cancellationToken), cancellationToken);

    // チェック済みファイルの署名
    //***************************************************************************
    //*<引数> path : 対象ファイル / settings : 接続設定 / secret : 認証情報 / cancellationToken : 中止要求
    //*-------------------------------------------------------------------------
    //*<返値> 成功・失敗・スキップの結果を返すTask
    //*-------------------------------------------------------------------------
    //*<説明> 本番用の署名状態取得とコマンド実行を組み合わせます。
    //***************************************************************************
    public static async Task<FileResult> SignAsync(string path, SigningSettings settings, string secret, CancellationToken cancellationToken = default)
        => await SignCoreAsync(path, settings, secret, p => InspectAsync(p, cancellationToken),
            (s, key, p) => RunSignerAsync(s, key, p, cancellationToken), DateTime.Today, cancellationToken);

    // SHA-256・タイムスタンプ付き署名の実行
    //***************************************************************************
    //*<引数> settings : 接続設定 / secret : 認証情報 / path : 対象ファイル / cancellationToken : 中止要求
    //*-------------------------------------------------------------------------
    //*<返値> 署名コマンドの終了コードと出力を返すTask
    //*-------------------------------------------------------------------------
    //*<説明> SHA-256とタイムスタンプを指定し、AzureSignToolへ署名対象と接続情報を渡します。
    //***************************************************************************
    private static async Task<(int Code, string Output)> RunSignerAsync(SigningSettings settings, string secret, string path, CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(settings.ToolPath,
        [
            "sign", "-kvu", settings.VaultUrl, "-kvc", settings.CertificateName,
            "-kvi", settings.ClientId, "-kvt", settings.TenantId, "-kvs", secret,
            "-tr", settings.TimestampUrl, "-td", "sha256", "-fd", "sha256", path
        ], cancellationToken: cancellationToken);
        return (result.Code, result.Combined);
    }

    // 署名前後の検査と結果判定
    //***************************************************************************
    //*<引数> path : 対象 / settings : 設定 / secret : 認証情報 / inspect : 検査処理 / sign : 署名処理 / today : 判定日 / cancellationToken : 中止要求
    //*-------------------------------------------------------------------------
    //*<返値> 各ファイルの処理結果を返すTask
    //*-------------------------------------------------------------------------
    //*<説明> 他社署名を保護し、書込可否・署名後の状態を検査します。外部処理を差し替えて実署名なしでテストできます。
    //***************************************************************************
    internal static async Task<FileResult> SignCoreAsync(string path, SigningSettings settings, string secret,
        Func<string, Task<SignatureInfo>> inspect,
        Func<SigningSettings, string, string, Task<(int Code, string Output)>> sign,
        DateTime today, CancellationToken cancellationToken = default)
    {
        var attempted = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (settings.IsExpired(today)) return new(path, "失敗", "利用期限を過ぎたため処理しませんでした。設定を更新してください。");
            SettingsValidation.ValidateConnection(settings);
            if (string.IsNullOrWhiteSpace(secret)) throw new ArgumentException("クライアントシークレットが未設定です。");
            if (!File.Exists(path)) throw new FileNotFoundException("対象ファイルが見つかりません。", path);
            var before = await inspect(path);
            cancellationToken.ThrowIfCancellationRequested();
            if (before.HasCertificate && string.IsNullOrWhiteSpace(before.Publisher))
                return new(path, "失敗", "既存署名の発行元を特定できないため変更しませんでした。");
            if (before.HasCertificate && !string.Equals(before.Publisher, settings.PublisherName, StringComparison.OrdinalIgnoreCase))
                return new(path, "スキップ", $"他社署名を保持：{before.Publisher}（{before.Status}）");
            if (!before.HasCertificate && before.Status != "NotSigned")
                return new(path, "失敗", $"署名状態が不明なため変更しませんでした：{before.Status}");

            if (!File.Exists(settings.ToolPath)) throw new FileNotFoundException("AzureSignToolが見つかりません。自動セットアップを実行してください。");
            if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0) throw new IOException("対象ファイルは読み取り専用です。");
            // 内容を書き換えず、書込権限と使用中ロックを事前確認する。
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) { }
            cancellationToken.ThrowIfCancellationRequested();
            attempted = true;
            var result = await sign(settings, secret, path);
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Code != 0)
                return new(path, "失敗", $"署名コマンド終了コード：{result.Code}\n{Clean(result.Output, secret)}\nファイルは変更されている可能性があります。");
            var after = await inspect(path);
            cancellationToken.ThrowIfCancellationRequested();
            if (after.Status != "Valid" || !after.HasTimestamp || !string.Equals(after.Publisher, settings.PublisherName, StringComparison.OrdinalIgnoreCase))
                return new(path, "失敗", $"署名後の検証に失敗：状態={after.Status}、発行元={after.Publisher}、タイムスタンプ={(after.HasTimestamp ? "あり" : "なし")}。ファイルは変更されている可能性があります。");
            return new(path, "成功", "自社署名とタイムスタンプを確認しました。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(path, "中止", attempted
                ? "署名処理を強制停止しました。ファイルが変更されている可能性があるため、再実行前に署名状態を確認してください。"
                : "中止しました。このファイルの署名処理は開始していません。");
        }
        catch (Exception ex)
        {
            var message = ErrorDetails.Format(ex, typeof(SigningService), nameof(SignAsync), secret);
            if (attempted) message += "\nファイルは変更されている可能性があります。";
            return new(path, "失敗", message);
        }
    }

    // 外部ツール出力の整形
    //***************************************************************************
    //*<引数> text : 出力文字列 / secret : 非表示にする値
    //*-------------------------------------------------------------------------
    //*<返値> 伏字化・文字数制限後の文字列
    //*-------------------------------------------------------------------------
    //*<説明> 外部ツールの出力を画面表示する前に認証情報を除去します。
    //***************************************************************************
    private static string Clean(string text, string secret)
    {
        return ErrorDetails.Redact(text.Trim(), secret);
    }
}

#endregion

#region Windows標準APIによる署名検査

// Windows標準APIをC#から呼び出す署名検査。スクリプトや外部プロセスは使用しない。
internal static class NativeSignature
{
    private static readonly Guid VerifyAction = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
    private const uint NoSignature = 0x800B0100;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustFile
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string Path;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size;
        public IntPtr PolicyCallback, SipClient;
        public uint UiChoice, RevocationChecks, UnionChoice;
        public IntPtr File;
        public uint StateAction;
        public IntPtr State, Url;
        public uint ProviderFlags, UiContext;
        public IntPtr SignatureSettings;
    }

    // 証明書構造体の先頭2項目だけを読み取る。
    [StructLayout(LayoutKind.Sequential)]
    private struct ProviderCertificate { public uint Size; public IntPtr Certificate; }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);
    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr data, uint index, [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterIndex);
    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint index);
    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATAdminAcquireContext2(out IntPtr admin, IntPtr subsystem, string algorithm, IntPtr policy, uint flags);
    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATAdminCalcHashFromFileHandle2(IntPtr admin, SafeFileHandle file, ref uint size, [Out] byte[]? hash, uint flags);
    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr CryptCATAdminEnumCatalogFromHash(IntPtr admin, byte[] hash, uint size, uint flags, IntPtr previous);
    [DllImport("wintrust.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATAdminReleaseCatalogContext(IntPtr admin, IntPtr catalog, uint flags);
    [DllImport("wintrust.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATAdminReleaseContext(IntPtr admin, uint flags);

    // Windowsの署名検証と証明書の取得
    //***************************************************************************
    //*<引数> path : 対象パス / token : 中止要求
    //*-------------------------------------------------------------------------
    //*<返値> 署名状態、発行元、タイムスタンプ有無
    //*-------------------------------------------------------------------------
    //*<説明> 読取専用で検証し、finallyでWindows側の検証状態を解放します。
    //***************************************************************************
    internal static SignatureInfo Inspect(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(path)) throw new FileNotFoundException("対象ファイルが見つかりません。", path);
        var file = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = Path.GetFullPath(path) };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        var initialized = false;
        var data = new TrustData
        {
            Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, UnionChoice = 1,
            File = pointer, StateAction = 1,
            // キャッシュのみを使用し、検査中に証明書取得の通信を発生させない。
            // https://learn.microsoft.com/windows/win32/api/wintrust/ns-wintrust-wintrust_data
            ProviderFlags = 0x1000 | 0x10
        };
        var action = VerifyAction;
        try
        {
            Marshal.StructureToPtr(file, pointer, false); initialized = true;
            var code = unchecked((uint)WinVerifyTrust(new IntPtr(-1), ref action, ref data));
            token.ThrowIfCancellationRequested();
            var info = new SignatureInfo { Status = Status(code) };
            var provider = WTHelperProvDataFromStateData(data.State);
            if (provider != IntPtr.Zero)
            {
                var signer = WTHelperGetProvSignerFromChain(provider, 0, false, 0);
                if (signer != IntPtr.Zero)
                {
                    var certificate = WTHelperGetProvCertFromChain(signer, 0);
                    if (certificate != IntPtr.Zero)
                    {
                        var context = Marshal.PtrToStructure<ProviderCertificate>(certificate).Certificate;
                        if (context != IntPtr.Zero)
                        {
                            using var cert = new X509Certificate2(context);
                            info.HasCertificate = true;
                            info.Publisher = cert.GetNameInfo(X509NameType.SimpleName, false);
                        }
                    }
                }
                info.HasTimestamp = WTHelperGetProvSignerFromChain(provider, 0, true, 0) != IntPtr.Zero;
            }
            // 検証失敗で署名者が取得できなくても、証明書テーブルがあれば上書きしない。
            if (!info.HasCertificate && HasEmbeddedCertificate(path)) info.HasCertificate = true;
            if (!info.HasCertificate && code == NoSignature && HasCatalogSignature(path, token))
            {
                // カタログ署名は上書き対象にしない。自社の埋込み署名とは区別する。
                info.HasCertificate = true; info.Publisher = "カタログ署名"; info.Status = "CatalogSigned";
            }
            token.ThrowIfCancellationRequested();
            return info;
        }
        finally
        {
            if (data.State != IntPtr.Zero) { data.StateAction = 2; WinVerifyTrust(new IntPtr(-1), ref action, ref data); }
            if (initialized) Marshal.DestroyStructure<TrustFile>(pointer);
            Marshal.FreeHGlobal(pointer);
        }
    }

    // 埋込み証明書テーブルの存在確認
    //***************************************************************************
    //*<引数> path : 対象ファイル
    //*-------------------------------------------------------------------------
    //*<返値> true : PE内に証明書テーブルがある
    //*-------------------------------------------------------------------------
    //*<説明> 署名者が取得できない署名も保護します。証明書の信頼性はWindows APIで検証します。
    //***************************************************************************
    private static bool HasEmbeddedCertificate(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            var directory = pe.PEHeaders.PEHeader?.CertificateTableDirectory;
            if (directory is null || directory.Value.Size == 0) return false;
            var entry = directory.Value;
            if (entry.RelativeVirtualAddress < 0 || entry.Size < 8 || (long)entry.RelativeVirtualAddress + entry.Size > stream.Length)
                throw new InvalidDataException("埋込み証明書テーブルが不正なため、ファイルを変更しません。");
            return true;
        }
        catch (BadImageFormatException) { return false; }
    }

    // カタログ署名の存在確認
    //***************************************************************************
    //*<引数> path : 対象パス / token : 中止要求
    //*-------------------------------------------------------------------------
    //*<返値> true : Windowsのカタログへ登録されたファイル
    //*-------------------------------------------------------------------------
    //*<説明> SHA-256とSHA-1のカタログを読み取り、埋込み署名がない他社ファイルも保護します。
    //***************************************************************************
    private static bool HasCatalogSignature(string path, CancellationToken token)
    {
        foreach (var algorithm in new[] { "SHA256", "SHA1" })
        {
            token.ThrowIfCancellationRequested();
            if (!CryptCATAdminAcquireContext2(out var admin, IntPtr.Zero, algorithm, IntPtr.Zero, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
            IntPtr catalog = IntPtr.Zero;
            try
            {
                using var stream = File.OpenRead(path);
                uint size = 0;
                if (!CryptCATAdminCalcHashFromFileHandle2(admin, stream.SafeFileHandle, ref size, null, 0) && Marshal.GetLastWin32Error() != 122)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                if (size == 0 || size > 128) throw new InvalidDataException("カタログ署名のハッシュ情報を取得できません。");
                var hash = new byte[size];
                if (!CryptCATAdminCalcHashFromFileHandle2(admin, stream.SafeFileHandle, ref size, hash, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
                token.ThrowIfCancellationRequested();
                catalog = CryptCATAdminEnumCatalogFromHash(admin, hash, size, 0, IntPtr.Zero);
                if (catalog != IntPtr.Zero) return true;
                var error = Marshal.GetLastWin32Error();
                if (error != 0 && error != 1168 && error != unchecked((int)0x80092004)) throw new Win32Exception(error);
            }
            finally
            {
                if (catalog != IntPtr.Zero) CryptCATAdminReleaseCatalogContext(admin, catalog, 0);
                CryptCATAdminReleaseContext(admin, 0);
            }
        }
        return false;
    }

    // Windows検証結果の表示名
    //***************************************************************************
    //*<引数> code : WinVerifyTrustの戻り値
    //*-------------------------------------------------------------------------
    //*<返値> 署名状態の表示文字列
    //*-------------------------------------------------------------------------
    //*<説明> 未署名・改ざん・信頼エラーを区別し、不明な状態で署名を開始しません。
    //***************************************************************************
    private static string Status(uint code) => code switch
    {
        0 => "Valid", NoSignature => "NotSigned", 0x80096010 => "HashMismatch",
        0x800B0109 or 0x800B0004 => "NotTrusted", 0x800B0101 => "Expired", 0x800B010C => "Revoked",
        _ => $"UnknownError (0x{code:X8})"
    };
}

#endregion

#region 署名エンジンの自動セットアップ

//==============================
//1. 定義部・共通処理
//==============================

public static class AzureSignToolInstaller
{
    public const string Version = "7.0.1";
    private const string DownloadUrl = "https://github.com/vcsjones/AzureSignTool/releases/download/v7.0.1/AzureSignTool-x64.exe";
    private const string ExpectedSha256 = "DC85A3F24BCD5978C63FCFD167A9B41313AF9116722B99B831400F05F387FCBA";
    private const long ExpectedSize = 11338720;
    public static string InstallPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DigitalSigningTool.NET10", "tools", "AzureSignTool.exe");

    // 制限時間付きインストール
    //***************************************************************************
    //*<引数> progress : 進捗通知先（省略可） / cancellationToken : 中止要求
    //*-------------------------------------------------------------------------
    //*<返値> 配置したEXEのパスを返すTask
    //*-------------------------------------------------------------------------
    //*<説明> 全工程を5分で制限し、利用者の中止と時間切れを区別します。
    //***************************************************************************
    public static async Task<string> InstallAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        // 応答ヘッダー後の本体転送・検証も含め、全工程に制限時間を設ける。
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try { return await InstallCoreAsync(progress, timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("インストールが5分以内に完了しませんでした。通信環境を確認して再度お試しください。");
        }
    }

    // EXEの取得・検証・配置
    //***************************************************************************
    //*<引数> progress : 進捗通知先 / cancellationToken : 中止・時間切れ要求
    //*-------------------------------------------------------------------------
    //*<返値> 配置したEXEのパスを返すTask
    //*-------------------------------------------------------------------------
    //*<説明> 同一ファイルは再利用し、転送サイズとSHA-256検証後に配置します。一時ファイルは後始末します。
    //***************************************************************************
    private static async Task<string> InstallCoreAsync(IProgress<int>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(InstallPath)!);
        if (File.Exists(InstallPath) && await MatchesHashAsync(InstallPath, cancellationToken))
        {
            progress?.Report(100);
            return InstallPath;
        }
        var temporary = InstallPath + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DigitalSigningTool.NET10/1.0");
            using var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != ExpectedSize)
                throw new InvalidDataException("ダウンロードファイルのサイズが公式公開情報と一致しません。");
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long received = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    received += count;
                    if (received > ExpectedSize) throw new InvalidDataException("ダウンロードサイズが想定を超えました。");
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    progress?.Report((int)(received * 95 / ExpectedSize));
                }
                if (received != ExpectedSize) throw new InvalidDataException("ダウンロードが完了していません。もう一度お試しください。");
            }
            if (!await MatchesHashAsync(temporary, cancellationToken))
                throw new InvalidDataException("SHA-256が公式公開情報と一致しないため、配置を中止しました。");
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, InstallPath, true);
            progress?.Report(100);
            return InstallPath;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    // SHA-256の照合
    //***************************************************************************
    //*<引数> path : 検証対象 / token : 中止要求
    //*-------------------------------------------------------------------------
    //*<返値> true : 公開SHA-256と一致 / false : 不一致を返すTask
    //*-------------------------------------------------------------------------
    //*<説明> ファイルのSHA-256を算出します。読込失敗は呼出元へ通知します。
    //***************************************************************************
    private static async Task<bool> MatchesHashAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)) == ExpectedSha256;
    }
}

#endregion
