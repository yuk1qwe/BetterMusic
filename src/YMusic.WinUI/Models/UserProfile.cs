#nullable enable

using System.Text.Json.Serialization;

namespace YMusic.WinUI.Models;

public sealed class AccountDetails
{
    [JsonPropertyName("uid")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long Uid { get; set; }

    [JsonPropertyName("login")]
    public string Login { get; set; } = string.Empty;

    [JsonPropertyName("fullName")]
    public string? FullName { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("secondName")]
    public string? SecondName { get; set; }

    [JsonPropertyName("firstName")]
    public string? FirstName { get; set; }

    [JsonPropertyName("hasPlus")]
    public bool HasPlus { get; set; }

    [JsonPropertyName("defaultEmail")]
    public string? DefaultEmail { get; set; }
}

public sealed class PlusStatus
{
    [JsonPropertyName("hasPlus")]
    public bool HasPlus { get; set; }
}

public sealed class AccountStatusResult
{
    [JsonPropertyName("account")]
    public AccountDetails? Account { get; set; }

    [JsonPropertyName("plus")]
    public PlusStatus? Plus { get; set; }
}

public sealed class UserProfile
{
    public string UserId { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? DefaultEmail { get; set; }
    public bool HasPlus { get; set; }
    public string? AvatarUrl { get; set; }

    public static UserProfile FromAccountStatus(AccountStatusResult status)
    {
        var account = status.Account;
        if (account == null)
        {
            return new UserProfile();
        }

        string displayName = !string.IsNullOrWhiteSpace(account.DisplayName)
            ? account.DisplayName
            : (!string.IsNullOrWhiteSpace(account.FullName) ? account.FullName : account.Login);

        bool hasPlus = account.HasPlus || (status.Plus?.HasPlus ?? false);

        return new UserProfile
        {
            UserId = account.Uid.ToString(),
            Login = account.Login,
            DisplayName = displayName,
            FullName = account.FullName,
            DefaultEmail = account.DefaultEmail,
            HasPlus = hasPlus,
            AvatarUrl = $"https://avatars.yandex.net/get-yapic/{account.Uid}/islands-200"
        };
    }
}
