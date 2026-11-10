#nullable enable

namespace YMusic.WinUI.Services.Interfaces;

public interface ICredentialStorageService
{
    string? GetToken();
    void SaveToken(string token, string? userId = null);
    void RemoveToken();
    bool HasToken();
    string? GetUserId();
    void SaveUserId(string userId);
}
