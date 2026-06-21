using System.Text.Json;
using Trayage.Core.Providers.GitLab;

namespace Trayage.Core.Tests;

/// <summary>
/// Guards the JSON property mappings on the GitLab DTOs. The mapping tests build objects
/// directly, so only these exercise the [JsonPropertyName] attributes against real payloads.
/// </summary>
public sealed class GitLabModelTests
{
    [Fact]
    public void Todos_DeserializeNestedProjectAndTarget()
    {
        const string json = """
        [
          {
            "id": 102,
            "action_name": "review_requested",
            "target_type": "MergeRequest",
            "target_url": "https://gitlab.com/acme/widgets/-/merge_requests/7",
            "body": "Add widget",
            "state": "pending",
            "updated_at": "2026-02-03T10:00:00.000Z",
            "project": { "path_with_namespace": "acme/widgets" },
            "target": { "title": "Add widget" }
          }
        ]
        """;

        var todos = JsonSerializer.Deserialize<List<GitLabTodo>>(json);

        Assert.NotNull(todos);
        var todo = Assert.Single(todos!);
        Assert.Equal(102, todo.Id);
        Assert.Equal("review_requested", todo.ActionName);
        Assert.Equal("MergeRequest", todo.TargetType);
        Assert.Equal("https://gitlab.com/acme/widgets/-/merge_requests/7", todo.TargetUrl);
        Assert.Equal("pending", todo.State);
        Assert.Equal("2026-02-03T10:00:00.000Z", todo.UpdatedAt);
        Assert.Equal("acme/widgets", todo.Project?.PathWithNamespace);
        Assert.Equal("Add widget", todo.Target?.Title);
    }

    [Fact]
    public void DeviceAuthResponse_DeserializesSnakeCaseFields()
    {
        const string json = """
        {
          "device_code": "abc123",
          "user_code": "WXYZ-1234",
          "verification_uri": "https://gitlab.com/-/device",
          "verification_uri_complete": "https://gitlab.com/-/device?user_code=WXYZ-1234",
          "expires_in": 300,
          "interval": 5
        }
        """;

        var device = JsonSerializer.Deserialize<GitLabDeviceAuthResponse>(json);

        Assert.NotNull(device);
        Assert.Equal("abc123", device!.DeviceCode);
        Assert.Equal("WXYZ-1234", device.UserCode);
        Assert.Equal("https://gitlab.com/-/device", device.VerificationUri);
        Assert.Equal("https://gitlab.com/-/device?user_code=WXYZ-1234", device.VerificationUriComplete);
        Assert.Equal(300, device.ExpiresIn);
        Assert.Equal(5, device.Interval);
    }

    [Fact]
    public void TokenResponse_DeserializesSuccessTokens()
    {
        const string json = """
        { "access_token": "at", "refresh_token": "rt", "expires_in": 7200, "token_type": "Bearer" }
        """;

        var token = JsonSerializer.Deserialize<GitLabTokenResponse>(json);

        Assert.NotNull(token);
        Assert.Equal("at", token!.AccessToken);
        Assert.Equal("rt", token.RefreshToken);
        Assert.Equal(7200, token.ExpiresIn);
        Assert.Null(token.Error);
    }

    [Fact]
    public void TokenResponse_DeserializesDeviceFlowPollError()
    {
        // The poll states arrive as an error body (HTTP 400) that the provider inspects.
        const string json = """{ "error": "authorization_pending", "error_description": "waiting" }""";

        var token = JsonSerializer.Deserialize<GitLabTokenResponse>(json);

        Assert.NotNull(token);
        Assert.Equal("authorization_pending", token!.Error);
        Assert.Null(token.AccessToken);
    }
}
