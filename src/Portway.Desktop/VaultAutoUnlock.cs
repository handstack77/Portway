using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Portway.Desktop;

// Vault 키의 자동 복원 정보는 현재 OS 사용자 보호 저장소에 묶습니다.
internal static class VaultAutoUnlock
{
    sealed record Registration(string Platform, string Id, string? ProtectedKey);
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Portway/vault-auto/v1");
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    const string FileName = "vault-auto.json";
    const string Service = "Portway vault key";

    static string Platform => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
    static string FilePath(string root) => Path.Combine(root, FileName);

    static Registration? Read(string root)
    {
        var path = FilePath(root);
        return File.Exists(path) ? JsonSerializer.Deserialize<Registration>(File.ReadAllText(path), Json) : null;
    }

    static void Write(string root, Registration registration)
    {
        var path = FilePath(root);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(registration, Json));
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public static byte[]? Restore(string root)
    {
        try
        {
            var registration = Read(root);
            if (registration == null || registration.Platform != Platform || !Guid.TryParseExact(registration.Id, "N", out _))
                return null;
            byte[]? secret = OperatingSystem.IsWindows()
                ? registration.ProtectedKey == null ? null : ProtectedData.Unprotect(Convert.FromBase64String(registration.ProtectedKey), Entropy, DataProtectionScope.CurrentUser)
                : OperatingSystem.IsMacOS() ? MacKeychain.Read(registration.Id) : LinuxSecretService.Read(registration.Id);
            if (secret?.Length == 32)
                return secret;
            if (secret != null)
                CryptographicOperations.ZeroMemory(secret);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or CryptographicException or PlatformNotSupportedException or DllNotFoundException or EntryPointNotFoundException or Win32Exception or InvalidOperationException)
        {
            // OS 보호 저장소를 사용할 수 없으면 마스터 비밀번호로 수동 잠금 해제합니다.
        }
        return null;
    }

    public static bool Store(string root, byte[] secret)
    {
        try
        {
            Registration? current;
            try
            {
                current = Read(root);
            }
            catch (JsonException)
            {
                current = null;
            }
            var id = current?.Platform == Platform && Guid.TryParseExact(current.Id, "N", out _) ? current.Id : Guid.NewGuid().ToString("N");
            string? protectedKey = null;
            if (OperatingSystem.IsWindows())
                protectedKey = Convert.ToBase64String(ProtectedData.Protect(secret, Entropy, DataProtectionScope.CurrentUser));
            else if (OperatingSystem.IsMacOS())
                MacKeychain.Write(id, secret);
            else
                LinuxSecretService.Write(id, secret);
            Write(root, new Registration(Platform, id, protectedKey));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or CryptographicException or PlatformNotSupportedException or DllNotFoundException or EntryPointNotFoundException or Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    public static void Remove(string root)
    {
        Registration? registration;
        try
        {
            registration = Read(root);
        }
        catch (JsonException)
        {
            registration = null;
        }
        File.Delete(FilePath(root));
        try
        {
            if (registration != null && registration.Platform == Platform && Guid.TryParseExact(registration.Id, "N", out _))
            {
                if (OperatingSystem.IsMacOS())
                    MacKeychain.Remove(registration.Id);
                else if (OperatingSystem.IsLinux())
                    LinuxSecretService.Remove(registration.Id);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or DllNotFoundException or EntryPointNotFoundException or Win32Exception or InvalidOperationException)
        {
            // 등록 파일이 제거되었으므로 다음 실행에서는 키체인의 잔여 항목을 사용하지 않습니다.
        }
    }

    static class MacKeychain
    {
        const string Security = "/System/Library/Frameworks/Security.framework/Security";
        const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        const int ItemNotFound = -25300;
        static readonly byte[] ServiceBytes = Encoding.UTF8.GetBytes(Service);

        [DllImport(Security, EntryPoint = "SecKeychainFindGenericPassword")]
        static extern int FindPassword(IntPtr keychain, uint serviceLength, byte[] service, uint accountLength, byte[] account, out uint passwordLength, out IntPtr password, IntPtr item);
        [DllImport(Security, EntryPoint = "SecKeychainFindGenericPassword")]
        static extern int FindItem(IntPtr keychain, uint serviceLength, byte[] service, uint accountLength, byte[] account, IntPtr passwordLength, IntPtr password, out IntPtr item);
        [DllImport(Security, EntryPoint = "SecKeychainAddGenericPassword")]
        static extern int Add(IntPtr keychain, uint serviceLength, byte[] service, uint accountLength, byte[] account, uint passwordLength, byte[] password, IntPtr item);
        [DllImport(Security, EntryPoint = "SecKeychainItemModifyAttributesAndData")]
        static extern int Modify(IntPtr item, IntPtr attributes, uint passwordLength, byte[] password);
        [DllImport(Security, EntryPoint = "SecKeychainItemDelete")]
        static extern int Delete(IntPtr item);
        [DllImport(Security, EntryPoint = "SecKeychainItemFreeContent")]
        static extern int FreeContent(IntPtr attributes, IntPtr password);
        [DllImport(CoreFoundation, EntryPoint = "CFRelease")]
        static extern void Release(IntPtr item);

        public static byte[]? Read(string id)
        {
            var account = Encoding.UTF8.GetBytes(id);
            var status = FindPassword(IntPtr.Zero, (uint)ServiceBytes.Length, ServiceBytes, (uint)account.Length, account, out var length, out var pointer, IntPtr.Zero);
            if (status == ItemNotFound)
                return null;
            if (status != 0)
                throw new InvalidOperationException("macOS 키체인에서 Vault 키를 읽지 못했습니다.");
            try
            {
                var value = new byte[length];
                Marshal.Copy(pointer, value, 0, value.Length);
                return value;
            }
            finally
            {
                FreeContent(IntPtr.Zero, pointer);
            }
        }

        public static void Write(string id, byte[] secret)
        {
            var account = Encoding.UTF8.GetBytes(id);
            var status = FindItem(IntPtr.Zero, (uint)ServiceBytes.Length, ServiceBytes, (uint)account.Length, account, IntPtr.Zero, IntPtr.Zero, out var item);
            if (status == ItemNotFound)
                status = Add(IntPtr.Zero, (uint)ServiceBytes.Length, ServiceBytes, (uint)account.Length, account, (uint)secret.Length, secret, IntPtr.Zero);
            else if (status == 0)
            {
                try
                {
                    status = Modify(item, IntPtr.Zero, (uint)secret.Length, secret);
                }
                finally
                {
                    Release(item);
                }
            }
            if (status != 0)
                throw new InvalidOperationException("macOS 키체인에 Vault 키를 저장하지 못했습니다.");
        }

        public static void Remove(string id)
        {
            var account = Encoding.UTF8.GetBytes(id);
            var status = FindItem(IntPtr.Zero, (uint)ServiceBytes.Length, ServiceBytes, (uint)account.Length, account, IntPtr.Zero, IntPtr.Zero, out var item);
            if (status == ItemNotFound)
                return;
            if (status != 0)
                throw new InvalidOperationException("macOS 키체인에서 Vault 키를 찾지 못했습니다.");
            try
            {
                status = Delete(item);
            }
            finally
            {
                Release(item);
            }
            if (status != 0)
                throw new InvalidOperationException("macOS 키체인에서 Vault 키를 지우지 못했습니다.");
        }
    }

    static class LinuxSecretService
    {
        static string? Run(string command, string id, string? input = null)
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo("secret-tool")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.StartInfo.ArgumentList.Add(command);
            if (command == "store")
                process.StartInfo.ArgumentList.Add("--label=Portway vault key");
            process.StartInfo.ArgumentList.Add("application");
            process.StartInfo.ArgumentList.Add("Portway");
            process.StartInfo.ArgumentList.Add("profile");
            process.StartInfo.ArgumentList.Add(id);
            process.Start();
            if (input != null)
                process.StandardInput.WriteLine(input);
            process.StandardInput.Close();
            if (!process.WaitForExit(10000))
            {
                process.Kill(entireProcessTree: true);
                throw new InvalidOperationException("Linux 비밀 저장소가 응답하지 않습니다.");
            }
            var output = process.StandardOutput.ReadToEnd().TrimEnd('\r', '\n');
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Linux 비밀 저장소를 사용할 수 없습니다.");
            return output;
        }

        public static byte[]? Read(string id)
        {
            var value = Run("lookup", id);
            return string.IsNullOrEmpty(value) ? null : Convert.FromBase64String(value);
        }
        public static void Write(string id, byte[] secret) => Run("store", id, Convert.ToBase64String(secret));
        public static void Remove(string id) => Run("clear", id);
    }
}
