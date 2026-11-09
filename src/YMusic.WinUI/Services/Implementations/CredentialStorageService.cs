#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Security.Credentials;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Services.Implementations;

public sealed class CredentialStorageService : ICredentialStorageService
{
    private const string ResourceName = "YMusic.Desktop";
    private const string TokenCredentialName = "OAuthToken";
    private const string UserIdCredentialName = "CurrentUserId";

    private readonly PasswordVault _vault = new();
    private readonly object _lock = new();

    private string? _cachedToken;
    private string? _cachedUserId;

    private readonly string _localSessionPath;
    private readonly string _roamingSessionPath;
    private readonly string _userProfileSessionPath;

    private const string Win32CredTarget = "YMusic_Desktop_Session_Token";
    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

    private sealed class SessionPayload
    {
        [JsonPropertyName("token")]
        public string? Token { get; set; }

        [JsonPropertyName("userId")]
        public string? UserId { get; set; }

        [JsonPropertyName("updatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public CredentialStorageService()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string localDir = Path.Combine(localAppData, "YMusic");
        Directory.CreateDirectory(localDir);
        _localSessionPath = Path.Combine(localDir, "session.json");

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string roamingDir = Path.Combine(appData, "YMusic");
        Directory.CreateDirectory(roamingDir);
        _roamingSessionPath = Path.Combine(roamingDir, "session.json");

        // UserProfile directory is OUTSIDE the MSIX container sandbox and survives rebuilds/redeploys
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string profileDir = Path.Combine(userProfile, ".ymusic");
        Directory.CreateDirectory(profileDir);
        _userProfileSessionPath = Path.Combine(profileDir, "session.json");

        LoadInitialCredentials();
    }

    private void LoadInitialCredentials()
    {
        lock (_lock)
        {
            // 2. Try Windows Credential Manager (Win32 API - persists across builds and package resets)
            var (win32Token, win32UserId) = LoadFromWin32Cred();
            if (!string.IsNullOrWhiteSpace(win32Token))
            {
                _cachedToken = win32Token;
                _cachedUserId = win32UserId;
                SyncLoadedCredentials();
                return;
            }

            // 5. Try Windows PasswordVault
            try
            {
                var cred = _vault.Retrieve(ResourceName, TokenCredentialName);
                cred.RetrievePassword();
                if (!string.IsNullOrWhiteSpace(cred.Password))
                {
                    _cachedToken = cred.Password;
                    try
                    {
                        var userCred = _vault.Retrieve(ResourceName, UserIdCredentialName);
                        userCred.RetrievePassword();
                        _cachedUserId = userCred.Password;
                    }
                    catch { }

                    SyncLoadedCredentials();
                    return;
                }
            }
            catch { }


        }
    }

    private void SyncLoadedCredentials()
    {
        if (string.IsNullOrWhiteSpace(_cachedToken)) return;

        SaveToWin32Cred(_cachedToken, _cachedUserId);
    }

    public string? GetToken()
    {
        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(_cachedToken))
            {
                return _cachedToken;
            }

            LoadInitialCredentials();
            return _cachedToken;
        }
    }

    public void SaveToken(string token, string? userId = null)
    {
        if (string.IsNullOrWhiteSpace(token)) return;

        lock (_lock)
        {
            _cachedToken = token.Trim();
            if (!string.IsNullOrWhiteSpace(userId))
            {
                _cachedUserId = userId.Trim();
            }

            // Persist only in Windows-protected credential stores.
            SaveToWin32Cred(_cachedToken, _cachedUserId);

            // Also persist to PasswordVault as secondary storage
            try
            {
                try
                {
                    var existing = _vault.Retrieve(ResourceName, TokenCredentialName);
                    _vault.Remove(existing);
                }
                catch { }

                var tokenCredential = new PasswordCredential(ResourceName, TokenCredentialName, _cachedToken);
                _vault.Add(tokenCredential);

                if (!string.IsNullOrWhiteSpace(_cachedUserId))
                {
                    SaveUserId(_cachedUserId);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CredentialStorage] PasswordVault save warning: {ex.Message}");
            }
        }
    }

    public void RemoveToken()
    {
        lock (_lock)
        {
            _cachedToken = null;
            _cachedUserId = null;

            try
            {
                if (File.Exists(_userProfileSessionPath)) File.Delete(_userProfileSessionPath);
            }
            catch { }

            try
            {
                if (File.Exists(_localSessionPath)) File.Delete(_localSessionPath);
            }
            catch { }

            try
            {
                if (File.Exists(_roamingSessionPath)) File.Delete(_roamingSessionPath);
            }
            catch { }

            DeleteFromWin32Cred();

            try
            {
                var credential = _vault.Retrieve(ResourceName, TokenCredentialName);
                _vault.Remove(credential);
            }
            catch { }

            try
            {
                var userCred = _vault.Retrieve(ResourceName, UserIdCredentialName);
                _vault.Remove(userCred);
            }
            catch { }
        }
    }

    public bool HasToken()
    {
        return !string.IsNullOrWhiteSpace(GetToken());
    }

    public string? GetUserId()
    {
        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(_cachedUserId))
            {
                return _cachedUserId;
            }

            GetToken(); // forces reload from files/vault
            return _cachedUserId;
        }
    }

    public void SaveUserId(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;

        lock (_lock)
        {
            _cachedUserId = userId.Trim();
            if (!string.IsNullOrWhiteSpace(_cachedToken))
            {
                SaveToWin32Cred(_cachedToken, _cachedUserId);
            }

            try
            {
                try
                {
                    var existing = _vault.Retrieve(ResourceName, UserIdCredentialName);
                    _vault.Remove(existing);
                }
                catch { }

                var cred = new PasswordCredential(ResourceName, UserIdCredentialName, _cachedUserId);
                _vault.Add(cred);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CredentialStorage] PasswordVault SaveUserId warning: {ex.Message}");
            }
        }
    }

    #region Win32 Credential Manager & .env Helpers

    private static void SaveToWin32Cred(string token, string? userId)
    {
        try
        {
            byte[] bytes = System.Text.Encoding.Unicode.GetBytes(token);
            IntPtr pBlob = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, pBlob, bytes.Length);
                var cred = new CREDENTIAL
                {
                    Type = CRED_TYPE_GENERIC,
                    TargetName = Win32CredTarget,
                    CredentialBlob = pBlob,
                    CredentialBlobSize = (uint)bytes.Length,
                    Persist = CRED_PERSIST_LOCAL_MACHINE,
                    UserName = userId ?? "YMusicUser"
                };
                CredWrite(ref cred, 0);
            }
            finally
            {
                Marshal.FreeHGlobal(pBlob);
            }
        }
        catch { }
    }

    private static (string? Token, string? UserId) LoadFromWin32Cred()
    {
        try
        {
            if (CredRead(Win32CredTarget, CRED_TYPE_GENERIC, 0, out IntPtr pCred))
            {
                try
                {
                    var cred = Marshal.PtrToStructure<CREDENTIAL>(pCred);
                    if (cred.CredentialBlob != IntPtr.Zero && cred.CredentialBlobSize > 0)
                    {
                        byte[] bytes = new byte[cred.CredentialBlobSize];
                        Marshal.Copy(cred.CredentialBlob, bytes, 0, (int)cred.CredentialBlobSize);
                        string token = System.Text.Encoding.Unicode.GetString(bytes);
                        return (token, cred.UserName);
                    }
                }
                finally
                {
                    CredFree(pCred);
                }
            }
        }
        catch { }
        return (null, null);
    }

    private static void DeleteFromWin32Cred()
    {
        try
        {
            CredDelete(Win32CredTarget, CRED_TYPE_GENERIC, 0);
        }
        catch { }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite([In] ref CREDENTIAL userCredential, [In] uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead([In] string target, [In] uint type, [In] uint reservedFlag, [Out] out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete([In] string target, [In] uint type, [In] uint flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree([In] IntPtr credentialPtr);

    #endregion
}
