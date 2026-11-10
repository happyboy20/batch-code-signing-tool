// 実署名・ネットワーク接続・利用者設定への書込みをせずに検証する回帰テスト。
using DigitalSigningTool;
using System.Text.Json;
using System.Text;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

// 検証用の子プロセスもC#で実装し、スクリプトには依存しない。
if (args.FirstOrDefault() == "--process-fixture")
{
    Console.OutputEncoding = Encoding.UTF8;
    Console.WriteLine("standard output"); Console.Error.WriteLine("diagnostic output");
    return;
}
if (args.FirstOrDefault() == "--wait")
{
    File.WriteAllText(args[1], Environment.ProcessId.ToString());
    Thread.Sleep(TimeSpan.FromSeconds(30));
    return;
}

var root = Path.Combine(Path.GetTempPath(), "SigningToolChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int count = 0;
void Check(bool ok, string title)
{
    if (!ok) throw new Exception("FAIL: " + title);
    Console.WriteLine("PASS: " + title); count++;
}
void Reject(Action action, string title)
{
    bool rejected = false;
    try { action(); }
    catch (Exception ex) when (ex is ArgumentException or InvalidDataException or JsonException) { rejected = true; }
    Check(rejected, title);
}
SigningSettings Configured() => new()
{
    LimitUsagePeriod = true, StartDate = new(2026, 11, 1), EndDate = new(2027, 10, 31),
    VaultUrl = "https://example-vault.vault.azure.net/", CertificateName = "2026SigningKey",
    ClientId = "11111111-1111-1111-1111-111111111111", TenantId = "22222222-2222-2222-2222-222222222222",
    PublisherName = "Example Publisher"
};

try
{
    var settings = new SigningSettings();
    Check(!settings.LimitUsagePeriod && !settings.IsExpired(new DateTime(2099, 1, 1)), "generic version has no usage limit by default");
    Check(new[] { settings.VaultUrl, settings.CertificateName, settings.ClientId, settings.TenantId, settings.PublisherName, settings.ProtectedSecret }.All(string.IsNullOrEmpty), "all organization and credential defaults are blank");
    Check(SettingsStore.FilePath.Contains("DigitalSigningTool.NET10") && AzureSignToolInstaller.InstallPath.Contains("DigitalSigningTool.NET10"), "configuration and tool storage are isolated from other versions");
    Check(settings.StartDate == DateTime.Today && settings.EndDate == DateTime.Today.AddYears(1), "optional period uses current date instead of fixed organization dates");
    settings = Configured();
    Check(!settings.IsExpired(new(2025, 1, 1)), "past PC date is allowed");
    Check(!settings.IsExpired(new(2027, 10, 31, 23, 59, 59)) && settings.IsExpired(new(2027, 11, 1)), "expiry boundary");
    settings.AdvanceYear();
    Check(settings.CertificateName == "2027SigningKey" && settings.StartDate == new DateTime(2027, 11, 1) && settings.EndDate == new DateTime(2028, 10, 31), "renew year and period together");
    var genericName = Configured(); genericName.CertificateName = "release-signing-key";
    genericName.AdvanceYear();
    Check(genericName.CertificateName == "release-signing-key" && genericName.EndDate == new DateTime(2028, 10, 31), "renewal preserves certificate names without a leading year");
    settings.EndDate = new(9998, 12, 31);
    var original = JsonSerializer.Serialize(settings);
    Reject(settings.AdvanceYear, "date overflow rejected");
    Check(JsonSerializer.Serialize(settings) == original, "failed renewal leaves settings unchanged");
    settings = Configured(); settings.CertificateName = "0000SigningKey";
    Reject(settings.AdvanceYear, "year zero rejected");
    settings.CertificateName = "２０２６SigningKey";
    Reject(settings.AdvanceYear, "non-ASCII year rejected");
    settings = Configured(); settings.VaultUrl = "http://example.com";
    Reject(() => SettingsValidation.ValidateConnection(settings), "insecure vault URL rejected");
    settings = Configured(); settings.TimestampUrl = "https://user:password@example.com";
    Reject(() => SettingsValidation.ValidateConnection(settings), "credentials in URL rejected");
    settings = Configured(); settings.ClientId = Guid.Empty.ToString();
    Reject(() => SettingsValidation.ValidateConnection(settings), "empty GUID rejected");
    settings = Configured(); settings.ToolPath = "tool.exe";
    Reject(() => SettingsValidation.ValidateConnection(settings), "relative tool path rejected");
    settings = Configured(); settings.CertificateName = null!;
    Reject(() => SettingsValidation.ValidateStructure(settings), "null field rejected");
    settings = Configured(); settings.StartDate = settings.EndDate.AddDays(1);
    Reject(() => SettingsValidation.ValidateStructure(settings), "reversed period rejected");
    Reject(() => SecretProtection.Protect("   "), "blank secret rejected before encryption");
    Reject(() => SecretProtection.Unprotect(""), "missing secret rejected before decryption");

    var config = Path.Combine(root, "settings.json");
    Check(!SettingsStore.Load(config).LimitUsagePeriod && string.IsNullOrEmpty(SettingsStore.Load(config).VaultUrl), "missing settings use blank generic defaults");
    File.WriteAllText(config, "{ broken");
    Reject(() => SettingsStore.Load(config), "corrupt settings rejected");
    Check(File.ReadAllText(config) == "{ broken", "corrupt settings not overwritten");
    File.WriteAllText(config, "{\"PublisherName\":null}");
    Reject(() => SettingsStore.Load(config), "JSON null rejected");
    SettingsStore.Save(new(), config);
    var saved = File.ReadAllText(config);
    settings = Configured(); settings.EndDate = DateTime.MinValue;
    Reject(() => SettingsStore.Save(settings, config), "invalid settings cannot save");
    Check(File.ReadAllText(config) == saved && Directory.GetFiles(root, "*.tmp").Length == 0, "failed save preserves settings and leaves no temp file");

    var data = Path.Combine(root, "data"); Directory.CreateDirectory(Path.Combine(data, "child"));
    var file = Path.Combine(data, "sample.exe"); File.WriteAllText(file, "untouched");
    File.WriteAllText(Path.Combine(data, "child", "library.DLL"), "untouched");
    File.WriteAllText(Path.Combine(data, "readme.txt"), "ignored");
    Check(TargetFiles.Collect(data, false).Length == 2, "recursive EXE and DLL collection");
    Check(TargetFiles.Collect(file, true).Single() == file, "single-file selection");
    var other = Path.Combine(data, "child", "sample.exe"); File.WriteAllText(other, "same name different directory");
    var multi = TargetFiles.CollectMany(new[] { file, file.ToUpperInvariant(), other });
    Check(multi.Length == 2 && multi[0] == file && multi[1] == other, "multiple selection deduplicates paths and preserves same names in different directories");
    var rowOne = new SigningItem(file); var rowTwo = new SigningItem(other);
    Check(rowOne.IsSelected && rowOne.FileName == "sample.exe" && rowOne.DirectoryPath == data && rowOne.Symbol == "", "new rows are checked with separate name and location");
    rowTwo.IsSelected = false;
    Check(new[] { rowOne, rowTwo }.Where(row => row.IsSelected).Select(row => row.FullPath).SequenceEqual(new[] { file }), "unchecked files are excluded from selection snapshot");
    rowOne.ApplyResult(new(file, "成功", "verified"));
    Check(rowOne.Symbol == "○" && rowOne.Detail == "verified", "successful row uses circle and reason");
    rowOne.ApplyResult(new(file, "スキップ", "other publisher"));
    Check(rowOne.Symbol == "△" && rowOne.Detail == "other publisher", "skipped row uses triangle and reason");
    rowOne.ApplyResult(new(file, "失敗", "failure reason"));
    Check(rowOne.Symbol == "×" && rowOne.Detail == "failure reason", "failed row uses cross and reason");
    rowOne.SetState("未処理（中止）", "中止操作のため未処理");
    Check(rowOne.Symbol == "△", "unprocessed rows after stop use triangle");
    Reject(() => rowOne.ApplyResult(new(other, "成功", "wrong file")), "result cannot be applied to a different file");
    Check(!rowTwo.IsSelected && rowTwo.Symbol == "", "unchecked row preserves state when another row changes");
    Reject(() => TargetFiles.Collect("relative", false), "relative target rejected");
    var redacted = ErrorDetails.Format(new Exception("secret-value"), typeof(SigningService), "Check", "secret-value");
    Check(!redacted.Contains("secret-value") && redacted.Contains("SigningService") && redacted.Contains("Check"), "error context with secret redaction");

    settings = Configured(); settings.ToolPath = file;
    int signCalls = 0, inspectCalls = 0;
    SignatureInfo before = new() { Status = "NotSigned" };
    SignatureInfo after = new() { Status = "Valid", HasCertificate = true, HasTimestamp = true, Publisher = settings.PublisherName };
    Task<SignatureInfo> Inspect(string _) => Task.FromResult(++inspectCalls == 1 ? before : after);
    Task<(int, string)> Sign(SigningSettings _, string __, string ___) { signCalls++; return Task.FromResult((0, "")); }
    async Task<FileResult> Run()
    {
        signCalls = inspectCalls = 0;
        return await SigningService.SignCoreAsync(file, settings, "test-secret", Inspect, Sign, new(2026, 11, 10));
    }
    before = new() { HasCertificate = true, Publisher = "Other Company", Status = "HashMismatch" };
    Check((await Run()).Result == "スキップ" && signCalls == 0, "other-company signature never overwritten even if invalid");
    before = new() { HasCertificate = true, Publisher = "", Status = "UnknownError" };
    Check((await Run()).Result == "失敗" && signCalls == 0, "unknown publisher blocks signing");
    before = new() { Status = "UnknownError" };
    Check((await Run()).Result == "失敗" && signCalls == 0, "unknown signature status blocks signing");
    before = new() { Status = "NotSigned" };
    Check((await Run()).Result == "成功" && signCalls == 1 && inspectCalls == 2, "unsigned file signs and verifies");
    after.HasTimestamp = false;
    Check((await Run()).Result == "失敗", "missing timestamp fails post-sign verification");
    after.HasTimestamp = true; after.Publisher = "Unexpected";
    Check((await Run()).Result == "失敗", "wrong publisher fails post-sign verification");
    settings.EndDate = new(2026, 11, 9);
    Check((await Run()).Result == "失敗" && signCalls == 0 && inspectCalls == 0, "expired tool never inspects or signs");
    settings.EndDate = new(2027, 10, 31);
    File.SetAttributes(file, FileAttributes.ReadOnly);
    try { Check((await Run()).Result == "失敗" && signCalls == 0, "read-only file rejected before signing"); }
    finally { File.SetAttributes(file, FileAttributes.Normal); }
    using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        Check((await Run()).Result == "失敗" && signCalls == 0, "locked file rejected before signing");
    inspectCalls = 0;
    var exceptionResult = await SigningService.SignCoreAsync(file, settings, "test-secret", Inspect,
        (_, _, _) => throw new Exception("test-secret"), new(2026, 11, 10));
    Check(exceptionResult.Result == "失敗" && exceptionResult.Detail.Contains("SigningService") && exceptionResult.Detail.Contains("変更されている可能性") && !exceptionResult.Detail.Contains("test-secret"), "per-file exception redacts secret and reports possible modification");
    inspectCalls = 0;
    var commandFailure = await SigningService.SignCoreAsync(file, settings, "test-secret", Inspect,
        (_, _, _) => Task.FromResult((2, "error test-secret")), new(2026, 11, 10));
    Check(commandFailure.Result == "失敗" && commandFailure.Detail.Contains("2") && !commandFailure.Detail.Contains("test-secret"), "nonzero signer exit fails without leaking secret");
    Check(File.ReadAllText(file) == "untouched", "all signing tests leave file contents untouched");

    // USB分岐も実機・ストアへの書込・実署名なしで確認する。
    var usb = new SigningSettings
    {
        UseUsbToken = true, SignToolPath = Path.Combine(root, "signtool.exe"),
        UsbThumbprint = new string('A', 40), UsbPublisherName = "USB Example Publisher"
    };
    File.WriteAllText(usb.SignToolPath, "not executed");
    SettingsValidation.ValidateConnection(usb);
    Check(usb.IsConfigured && usb.ActiveToolPath == usb.SignToolPath && usb.ActivePublisherName == usb.UsbPublisherName, "USB does not require Azure settings or a secret");
    var usbCopy = usb.Copy();
    Check(usbCopy.UseUsbToken && usbCopy.UsbThumbprint == usb.UsbThumbprint && usbCopy.UsbPublisherName == usb.UsbPublisherName, "USB settings survive serialization");
    Check(!JsonSerializer.Deserialize<SigningSettings>("{}")!.UseUsbToken, "legacy settings retain Azure mode");
    Check(UsbSigning.NormalizeThumbprint("\u200e" + new string('a', 20) + " \n" + new string('a', 20)) == usb.UsbThumbprint, "copied thumbprint normalizes whitespace and direction markers");
    var badUsb = usb.Copy(); badUsb.UsbThumbprint = new string('G', 40);
    Reject(() => SettingsValidation.ValidateConnection(badUsb), "nonhex USB thumbprint rejected");
    badUsb.UsbThumbprint = new string('A', 39);
    Reject(() => SettingsValidation.ValidateConnection(badUsb), "short USB thumbprint rejected");
    badUsb = usb.Copy(); badUsb.SignToolPath = Path.Combine(root, "other.exe");
    Reject(() => SettingsValidation.ValidateConnection(badUsb), "USB requires explicit signtool.exe path");
    badUsb = usb.Copy(); badUsb.UsbPublisherName = "";
    Reject(() => SettingsValidation.ValidateConnection(badUsb), "USB publisher cannot be blank");
    badUsb = usb.Copy(); badUsb.TimestampUrl = "https://user:password@example.com";
    Reject(() => SettingsValidation.ValidateConnection(badUsb), "USB timestamp credentials rejected");
    var spacedPath = Path.Combine(root, "対象 ' & [1].exe");
    var usbArguments = SigningService.BuildSignerArguments(usb, "must-not-pass", spacedPath);
    Check(usbArguments.Contains("/sha1") && usbArguments.Contains(usb.UsbThumbprint) && !usbArguments.Contains("/sm") && !usbArguments.Contains("must-not-pass") && usbArguments.Last() == spacedPath,
        "USB current-user store uses exact thumbprint and isolated target argument without secrets");
    usb.UsbLocalMachine = true;
    Check(SigningService.BuildSignerArguments(usb, "", file).Contains("/sm"), "USB machine-store selection reaches SignTool");
    var azureArguments = SigningService.BuildSignerArguments(Configured(), "azure-secret", file);
    Check(azureArguments.Contains("-kvu") && azureArguments.Contains("azure-secret") && !azureArguments.Contains("/sha1"), "Azure signing arguments remain separate");
    usb.CertificateName = "2026AzureKey"; usb.AdvanceYear();
    Check(usb.CertificateName == "2026AzureKey", "USB period renewal leaves Azure certificate name unchanged");
    using (var key = RSA.Create(2048))
    {
        var request = new CertificateRequest("CN=USB Example Publisher", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.3") }, true));
        using var certificateFixture = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        Check(UsbSigning.IsSigningCertificate(certificateFixture, DateTime.Now), "in-memory valid code-signing certificate accepted");
        Check(!UsbSigning.IsSigningCertificate(certificateFixture, DateTime.Now.AddDays(2)), "expired USB certificate rejected");
        using var publicOnly = X509CertificateLoader.LoadCertificate(certificateFixture.Export(X509ContentType.Cert));
        Check(!UsbSigning.IsSigningCertificate(publicOnly, DateTime.Now), "USB certificate without private-key association rejected");
        var wrongUsage = new CertificateRequest("CN=Wrong Usage", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var wrongCertificate = wrongUsage.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        Check(!UsbSigning.IsSigningCertificate(wrongCertificate, DateTime.Now), "certificate without code-signing EKU rejected");
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyEncipherment, true));
        using var wrongKeyUsage = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        Check(!UsbSigning.IsSigningCertificate(wrongKeyUsage, DateTime.Now), "certificate without digital-signature key usage rejected");
    }
    var usbBefore = new SignatureInfo { Status = "NotSigned" };
    var usbAfter = new SignatureInfo { Status = "Valid", HasCertificate = true, HasTimestamp = true, Publisher = usb.UsbPublisherName, Thumbprint = usb.UsbThumbprint };
    int usbInspections = 0, usbSignCalls = 0;
    Task<SignatureInfo> InspectUsb(string _) => Task.FromResult(++usbInspections == 1 ? usbBefore : usbAfter);
    Task<(int, string)> SignUsb(SigningSettings _, string __, string ___) { usbSignCalls++; return Task.FromResult((0, "")); }
    async Task<FileResult> RunUsb()
    {
        usbInspections = usbSignCalls = 0;
        return await SigningService.SignCoreAsync(file, usb, "", InspectUsb, SignUsb, DateTime.Today);
    }
    Check((await RunUsb()).Result == "成功" && usbSignCalls == 1, "USB core succeeds without Azure secret");
    usbAfter.Thumbprint = new string('B', 40);
    Check((await RunUsb()).Result == "失敗", "USB post-check rejects wrong certificate even with same publisher");
    usbAfter.Thumbprint = usb.UsbThumbprint; usbAfter.HasTimestamp = false;
    Check((await RunUsb()).Result == "失敗", "USB requires timestamp after signing");
    usbBefore = new() { Status = "Valid", HasCertificate = true, Publisher = "Other Publisher" };
    Check((await RunUsb()).Result == "スキップ" && usbSignCalls == 0, "USB preserves another publisher's signature");
    usbBefore = new() { Status = "UnknownError", HasCertificate = true };
    Check((await RunUsb()).Result == "失敗" && usbSignCalls == 0, "USB preserves unknown existing signature");
    Check(File.ReadAllText(file) == "untouched", "USB regression tests never modify the target");

    // プロセスの出力分離をC#の検証用子プロセスで確認する。
    var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
    var self = System.Reflection.Assembly.GetExecutingAssembly().Location;
    var separated = await SigningService.ExecuteAsync(dotnet, [self, "--process-fixture"]);
    Check(separated.Code == 0 && separated.StandardOutput.Contains("standard output") && !separated.StandardOutput.Contains("diagnostic output") && separated.StandardError.Contains("diagnostic output"), "C# child stdout and stderr remain separate");
    var actualInfo = await SigningService.InspectAsync(typeof(SigningService).Assembly.Location);
    Check(actualInfo.Status == "NotSigned" && !actualInfo.HasCertificate, "native inspection identifies unsigned assembly");
    var signedInfo = await SigningService.InspectAsync(dotnet);
    Console.WriteLine($"Native signed file: {signedInfo.Status}; publisher={signedInfo.Publisher}; timestamp={signedInfo.HasTimestamp}");
    Check(signedInfo.HasCertificate && !string.IsNullOrWhiteSpace(signedInfo.Publisher), "native inspection reads signer from signed host");
    Check(signedInfo.Status == "Valid" && signedInfo.HasTimestamp, "native verification reads timestamp on valid signed file");
    Check(signedInfo.Thumbprint.Length == 40, "native inspection supplies exact signer thumbprint");
    var damaged = Path.Combine(root, "署名検査'[] #.exe");
    File.Copy(dotnet, damaged);
    using (var changed = new FileStream(damaged, FileMode.Open, FileAccess.ReadWrite))
    {
        changed.Position = 0x400; var value = changed.ReadByte(); changed.Position = 0x400; changed.WriteByte((byte)(value ^ 1));
    }
    var damagedInfo = await SigningService.InspectAsync(damaged);
    Check(damagedInfo.Status == "HashMismatch" && damagedInfo.HasCertificate, "tampered signed file is protected and fails digest check");
    var catalogFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "acpi.sys");
    if (File.Exists(catalogFile))
    {
        var catalogInfo = await SigningService.InspectAsync(catalogFile);
        Console.WriteLine($"Native system driver: {catalogInfo.Status}; publisher={catalogInfo.Publisher}");
        Check(catalogInfo.HasCertificate, "native inspection protects system-driver signatures including catalog signatures");
    }

    using var stopBefore = new CancellationTokenSource(); stopBefore.Cancel();
    inspectCalls = signCalls = 0;
    var beforeStop = await SigningService.SignCoreAsync(file, settings, "test-secret", Inspect, Sign, new(2026, 11, 10), stopBefore.Token);
    Check(beforeStop.Result == "中止" && inspectCalls == 0 && signCalls == 0, "stop before inspection never signs");
    using var stopDuring = new CancellationTokenSource();
    inspectCalls = 0;
    var duringStop = await SigningService.SignCoreAsync(file, settings, "test-secret", Inspect,
        (_, _, _) => { stopDuring.Cancel(); return Task.FromResult((0, "")); }, new(2026, 11, 10), stopDuring.Token);
    Check(duringStop.Result == "中止" && duringStop.Detail.Contains("変更されている可能性"), "stop during signer reports interrupted file");
    try { TargetFiles.Collect(data, false, stopBefore.Token); throw new Exception("Collection did not stop"); }
    catch (OperationCanceledException) { Check(true, "target collection can stop"); }

    // 長時間待機する検証専用プロセスを中止し、プロセスが残らないことを確認。
    var pidPath = Path.Combine(root, "child.pid");
    using var processStop = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    var watch = Stopwatch.StartNew();
    try
    {
        await SigningService.ExecuteAsync(dotnet, [self, "--wait", pidPath], processStop.Token);
        throw new Exception("Process did not stop");
    }
    catch (OperationCanceledException) { Check(watch.Elapsed < TimeSpan.FromSeconds(10), "active process force-stops promptly"); }
    Check(File.Exists(pidPath), "cancellation test started the external process");
    var childPid = int.Parse(File.ReadAllText(pidPath).Trim());
    var childAlive = false;
    try { using var child = Process.GetProcessById(childPid); childAlive = !child.HasExited; }
    catch (ArgumentException) { }
    Check(!childAlive, "force-stopped child process does not remain");

    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    try { await AzureSignToolInstaller.InstallAsync(cancellationToken: cancelled.Token); throw new Exception("Cancellation failed"); }
    catch (OperationCanceledException) { Check(true, "installer cancellation before IO"); }
    Console.WriteLine($"ALL PASS: {count} checks; no actual signing, network, or user settings writes.");
}
finally
{
    // テスト専用に作成した絶対パスだけを削除する。
    if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Unexpected test path");
    Directory.Delete(root, true);
}
