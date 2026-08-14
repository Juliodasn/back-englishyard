using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishYard.Application.Autenticacao;
using Microsoft.Extensions.Configuration;

namespace EnglishYard.Infrastructure;

public sealed class SupabaseAuthAdminGateway(IConfiguration configuration) : ISupabaseAuthAdminGateway
{
    private static readonly HttpClient SharedClient = new();
    public async Task<Guid> CriarUsuarioAsync(string email, string senha, string nome, CancellationToken cancellationToken)
    {
        var settings = GetSettings();
        var client = SharedClient;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.Url}/auth/v1/admin/users");
        ConfigureHeaders(request, settings.ServiceRoleKey);
        request.Content = JsonContent.Create(new
        {
            email,
            password = senha,
            email_confirm = true,
            user_metadata = new { nome }
        });

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict)
        {
            var message = ExtractMessage(body);
            if (message.Contains("already", StringComparison.OrdinalIgnoreCase)
                || message.Contains("registered", StringComparison.OrdinalIgnoreCase)
                || message.Contains("exists", StringComparison.OrdinalIgnoreCase))
            {
                throw new SupabaseAuthConflitoException("Já existe uma conta de acesso no Supabase Auth com este e-mail.");
            }

            throw new SupabaseAuthException($"O Supabase Auth recusou a criação da conta: {message}");
        }

        if (!response.IsSuccessStatusCode)
            throw new SupabaseAuthException($"Não foi possível criar a conta no Supabase Auth ({(int)response.StatusCode}). {ExtractMessage(body)}");

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("id", out var idElement)
            || !Guid.TryParse(idElement.GetString(), out var userId))
        {
            throw new SupabaseAuthException("O Supabase Auth criou a conta, mas não retornou um identificador de usuário válido.");
        }

        return userId;
    }

    public async Task ExcluirUsuarioAsync(Guid usuarioAuthId, CancellationToken cancellationToken)
    {
        var settings = GetSettings();
        var client = SharedClient;

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{settings.Url}/auth/v1/admin/users/{usuarioAuthId}");
        ConfigureHeaders(request, settings.ServiceRoleKey);
        using var response = await client.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new SupabaseAuthException($"Não foi possível remover a conta de acesso no Supabase Auth ({(int)response.StatusCode}). {ExtractMessage(body)}");
    }

    public async Task AlterarSenhaUsuarioAtualAsync(string accessToken, string novaSenha, CancellationToken cancellationToken)
    {
        var settings = GetPublicSettings();

        using var request = new HttpRequestMessage(HttpMethod.Put, $"{settings.Url}/auth/v1/user");
        request.Headers.TryAddWithoutValidation("apikey", settings.PublishableKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new { password = novaSenha });

        using var response = await SharedClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new SupabaseAuthException($"O Supabase Auth não conseguiu atualizar a senha ({(int)response.StatusCode}). {ExtractMessage(body)}");
    }

    private (string Url, string PublishableKey) GetPublicSettings()
    {
        var url = configuration["Supabase:Url"]?.TrimEnd('/');
        var publishableKey = configuration["Supabase:PublishableKey"];

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(publishableKey))
        {
            throw new SupabaseAuthConfigurationException(
                "Configure 'Supabase:Url' e 'Supabase:PublishableKey' no backend. Em desenvolvimento, preencha backend/EnglishYard.Api/appsettings.Local.json.");
        }

        return (url, publishableKey);
    }

    private (string Url, string ServiceRoleKey) GetSettings()
    {
        var url = configuration["Supabase:Url"]?.TrimEnd('/');
        var serviceRoleKey = configuration["Supabase:ServiceRoleKey"];

        // Compatibilidade: algumas instalações antigas podem ter salvo a própria
        // service_role JWT na chave Supabase:SecretKey. Uma sb_secret_... não é JWT
        // e não pode ser usada diretamente como Bearer nas chamadas REST do GoTrue Admin.
        if (string.IsNullOrWhiteSpace(serviceRoleKey))
        {
            var legacyValue = configuration["Supabase:SecretKey"];
            if (!string.IsNullOrWhiteSpace(legacyValue)
                && legacyValue.Split('.').Length == 3)
            {
                serviceRoleKey = legacyValue;
            }
        }

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(serviceRoleKey))
        {
            throw new SupabaseAuthConfigurationException(
                "Para criar contas pelo Auth Admin, preencha 'Supabase:ServiceRoleKey' em backend/EnglishYard.Api/appsettings.Local.json " +
                "com a chave JWT legacy service_role do projeto. A chave sb_secret_... não substitui a service_role nesta chamada REST direta.");
        }

        return (url, serviceRoleKey);
    }

    private static void ConfigureHeaders(HttpRequestMessage request, string serviceRoleKey)
    {
        request.Headers.TryAddWithoutValidation("apikey", serviceRoleKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceRoleKey);
    }

    private static string ExtractMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "Erro sem detalhes.";

        try
        {
            using var document = JsonDocument.Parse(body);
            foreach (var propertyName in new[] { "msg", "message", "error_description", "error" })
            {
                if (document.RootElement.TryGetProperty(propertyName, out var property)
                    && property.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(property.GetString()))
                {
                    return property.GetString()!;
                }
            }
        }
        catch (JsonException)
        {
            // Se não for JSON, devolvemos o corpo sanitizado abaixo.
        }

        return body.Length > 300 ? body[..300] : body;
    }
}
