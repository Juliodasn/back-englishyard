using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using EnglishYard.Application.Autenticacao;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EnglishYard.Api.Authentication;

public sealed class SupabaseAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration,
    IPerfilUsuarioRepository profileRepository)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Supabase";
    private static readonly HttpClient SharedClient = new();

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization)
            || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = authorization["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
            return AuthenticateResult.NoResult();

        var supabaseUrl = configuration["Supabase:Url"]?.TrimEnd('/');
        var publishableKey = configuration["Supabase:PublishableKey"];
        if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(publishableKey))
        {
            Logger.LogError("Supabase:Url ou Supabase:PublishableKey não foi configurado no backend.");
            return AuthenticateResult.Fail("Autenticação do Supabase não configurada.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{supabaseUrl}/auth/v1/user");
            request.Headers.TryAddWithoutValidation("apikey", publishableKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await SharedClient.SendAsync(request, Context.RequestAborted);
            if (!response.IsSuccessStatusCode)
                return AuthenticateResult.Fail("Sessão inválida ou expirada.");

            var body = await response.Content.ReadAsStringAsync(Context.RequestAborted);
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("id", out var idElement)
                || !Guid.TryParse(idElement.GetString(), out var authUserId))
            {
                return AuthenticateResult.Fail("Usuário autenticado sem identificador válido.");
            }

            var profile = await profileRepository.BuscarPorUsuarioAuthIdAsync(authUserId, Context.RequestAborted);
            if (profile is null || !profile.Ativo)
                return AuthenticateResult.Fail("Usuário sem perfil ativo no portal.");

            if (profile.SessoesRevogadasAntesDe.HasValue && GetIssuedAt(token) is { } issuedAt
                && issuedAt <= profile.SessoesRevogadasAntesDe.Value)
                return AuthenticateResult.Fail("Sessão revogada pelo administrador.");

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, profile.UsuarioAuthId.ToString()),
                new(ClaimTypes.Name, profile.Nome),
                new(ClaimTypes.Email, profile.Email),
                new(ClaimTypes.Role, profile.TipoUsuario),
                new(PortalClaimTypes.DeveAlterarSenha, profile.DeveAlterarSenha ? "true" : "false")
            };

            if (profile.ProfessoraId.HasValue)
                claims.Add(new Claim(PortalClaimTypes.ProfessoraId, profile.ProfessoraId.Value.ToString()));

            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return AuthenticateResult.Success(ticket);
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Falha ao validar a sessão do Supabase.");
            return AuthenticateResult.Fail("Não foi possível validar a sessão atual.");
        }
    }

    private static DateTimeOffset? GetIssuedAt(string token)
    {
        try
        {
            var segment = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
            segment = segment.PadRight(segment.Length + ((4 - segment.Length % 4) % 4), '=');
            using var payload = JsonDocument.Parse(Convert.FromBase64String(segment));
            return payload.RootElement.TryGetProperty("iat", out var iat) && iat.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
        }
        catch { return null; }
    }
}
