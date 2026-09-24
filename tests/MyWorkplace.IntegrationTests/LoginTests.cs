using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using static MyWorkplace.IntegrationTests.IdentityApi;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: Sign-in, token contents and key publication through the gateway (T-023).
/// TR: Gateway üzerinden giriş, token içeriği ve anahtar yayını (T-023).
/// </summary>
/// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
public sealed class LoginTests(AppFixture app)
{
    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Login_ValidCredentials_ReturnsSignedTokenWithClaims()
    {
        using var client = app.CreateGatewayClient();
        var email = UniqueEmail();
        var (tenantId, userId) = await RegisterNewTenantAsync(client, email, Ct);

        // EN: Different letter case on purpose — sign-in must be case-insensitive too.
        // TR: Bilerek farklı harf büyüklüğü — giriş de büyük/küçük harfe duyarsız olmalı.
        using var response = await LoginAsync(client, email.ToUpperInvariant(), ValidPassword, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(900, body.GetProperty("expiresIn").GetInt32());

        var token = new JsonWebTokenHandler().ReadJsonWebToken(body.GetProperty("accessToken").GetString());
        Assert.Equal(SecurityAlgorithms.RsaSha256, token.Alg);
        Assert.False(string.IsNullOrEmpty(token.Kid));
        Assert.Equal(userId.ToString(), token.Subject);
        Assert.Equal(tenantId.ToString(), token.GetClaim("tenant_id").Value);
        Assert.Equal("basic", token.GetClaim("plan").Value);
        Assert.Equal(TimeSpan.FromMinutes(15), token.ValidTo - token.IssuedAt);
    }

    [Fact]
    public async Task Login_FirstUserOfACompany_IsOwner_WithEveryPermissionInTheToken()
    {
        using var client = app.CreateGatewayClient();
        var email = UniqueEmail();
        await RegisterNewTenantAsync(client, email, Ct);

        using var response = await LoginAsync(client, email, ValidPassword, Ct);

        var token = new JsonWebTokenHandler().ReadJsonWebToken(
            (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("accessToken").GetString());
        var permissions = token.Claims.Where(c => c.Type == "perm").Select(c => c.Value).Order(StringComparer.Ordinal);
        Assert.Equal(
            [
                "customers.delete", "customers.read", "customers.write",
                "inventory.delete", "inventory.read", "inventory.write",
                "plan.manage", "settings.manage", "users.manage",
            ],
            permissions);
    }

    [Fact]
    public async Task Login_WrongPasswordAndUnknownEmail_ReturnIdenticalResponses()
    {
        using var client = app.CreateGatewayClient();
        var email = UniqueEmail();
        await RegisterNewTenantAsync(client, email, Ct);

        using var wrongPassword = await LoginAsync(client, email, "Wrong-Password-1", Ct);
        using var unknownEmail = await LoginAsync(client, UniqueEmail(), ValidPassword, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal(
            await ReadWithoutTraceIdAsync(wrongPassword),
            await ReadWithoutTraceIdAsync(unknownEmail));
    }

    [Fact]
    public async Task Jwks_ContainsOnlyPublicKeyMaterial()
    {
        using var client = app.CreateGatewayClient();

        var jwks = await client.GetFromJsonAsync<JsonElement>("/identity/.well-known/jwks.json", Ct);

        var key = Assert.Single(jwks.GetProperty("keys").EnumerateArray());
        Assert.Equal("RSA", key.GetProperty("kty").GetString());
        foreach (var privateField in new[] { "d", "p", "q", "dp", "dq", "qi" })
        {
            Assert.False(key.TryGetProperty(privateField, out _), $"JWKS must not contain '{privateField}'.");
        }
    }

    [Fact]
    public async Task Token_CanBeValidatedWithJwksAlone()
    {
        using var client = app.CreateGatewayClient();
        var email = UniqueEmail();
        await RegisterNewTenantAsync(client, email, Ct);
        using var login = await LoginAsync(client, email, ValidPassword, Ct);
        var accessToken = (await login.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("accessToken").GetString();

        // EN: Exactly what the gateway will do in T-007: fetch the public keys, then verify locally.
        // TR: Gateway'in T-007'de yapacağının aynısı: açık anahtarları al, sonra yerelde doğrula.
        var jwks = new JsonWebKeySet(await client.GetStringAsync("/identity/.well-known/jwks.json", Ct));
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(accessToken, new TokenValidationParameters
        {
            ValidIssuer = "myworkplace-identity",
            ValidAudience = "myworkplace-api",
            IssuerSigningKeys = jwks.GetSigningKeys(),
        });

        Assert.True(result.IsValid, result.Exception?.Message);
    }

    [Fact]
    public async Task Discovery_PointsToJwks()
    {
        using var client = app.CreateGatewayClient();

        var configuration = await client.GetFromJsonAsync<JsonElement>("/identity/.well-known/openid-configuration", Ct);

        Assert.Equal("myworkplace-identity", configuration.GetProperty("issuer").GetString());
        Assert.EndsWith("/identity/.well-known/jwks.json", configuration.GetProperty("jwks_uri").GetString());
    }

    /// <summary>
    /// EN: Reads a problem response without <c>traceId</c>, which differs per request by design.
    /// TR: Bir problem cevabını, doğası gereği her istekte farklı olan <c>traceId</c> olmadan okur.
    /// </summary>
    /// <param name="response">EN: The response. TR: Cevap.</param>
    /// <returns>EN: The body as JSON text. TR: JSON metni olarak gövde.</returns>
    private static async Task<string> ReadWithoutTraceIdAsync(HttpResponseMessage response)
    {
        var body = (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!;
        body.Remove("traceId");
        return body.ToJsonString();
    }
}
