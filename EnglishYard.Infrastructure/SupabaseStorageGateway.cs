using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishYard.Application.Imagens;
using Microsoft.Extensions.Configuration;

namespace EnglishYard.Infrastructure;

public sealed class SupabaseStorageGateway(IConfiguration configuration) : IImagemStorageGateway
{
    private static readonly HttpClient SharedClient = new();
    private static readonly SemaphoreSlim BucketLock = new(1, 1);
    private static readonly ConcurrentDictionary<string, byte> BucketsReady = new(StringComparer.Ordinal);
    private static readonly string[] AllowedMimeTypes = ["image/jpeg", "image/png", "image/webp"];

    public async Task<string> SalvarFotoPerfilAsync(
        string categoria,
        Guid entidadeId,
        Stream conteudo,
        string contentType,
        CancellationToken cancellationToken)
    {
        var settings = GetSettings();
        await GarantirBucketAsync(settings, cancellationToken);

        var extension = ObterExtensao(contentType);
        var path = $"{SanitizeSegment(categoria)}/{entidadeId:D}/perfil{extension}";
        var uploadUrl = $"{settings.Url}/storage/v1/object/{EscapeSegment(settings.Bucket)}/{EscapePath(path)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
        ConfigureAdminHeaders(request, settings.ServiceRoleKey);
        request.Headers.TryAddWithoutValidation("x-upsert", "true");

        var content = new StreamContent(conteudo);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Headers.TryAddWithoutValidation("Cache-Control", "public, max-age=3600");
        request.Content = content;

        using var response = await SharedClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new ImagemStorageException(
                $"Não foi possível enviar a foto para o Supabase Storage ({(int)response.StatusCode}). {ExtractMessage(body)}");
        }

        // O sucesso do endpoint de upload é a confirmação de que o objeto foi gravado.
        // Não fazemos um GET imediato da URL pública aqui: a camada de CDN pode levar
        // alguns instantes para refletir o novo objeto. A persistência de foto_url é
        // confirmada logo depois pelo próprio cadastro no PostgreSQL.
        var version = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return $"{settings.Url}/storage/v1/object/public/{EscapeSegment(settings.Bucket)}/{EscapePath(path)}?v={version}";
    }

    private async Task GarantirBucketAsync(StorageSettings settings, CancellationToken cancellationToken)
    {
        var cacheKey = $"{settings.Url}|{settings.Bucket}";
        if (BucketsReady.ContainsKey(cacheKey))
            return;

        await BucketLock.WaitAsync(cancellationToken);
        try
        {
            if (BucketsReady.ContainsKey(cacheKey))
                return;

            using var getRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"{settings.Url}/storage/v1/bucket/{EscapeSegment(settings.Bucket)}");
            ConfigureAdminHeaders(getRequest, settings.ServiceRoleKey);

            using var getResponse = await SharedClient.SendAsync(getRequest, cancellationToken);
            var getBody = await getResponse.Content.ReadAsStringAsync(cancellationToken);

            if (getResponse.IsSuccessStatusCode)
            {
                if (!BucketEhPublico(getBody))
                    await TornarBucketPublicoAsync(settings, cancellationToken);

                BucketsReady.TryAdd(cacheKey, 0);
                return;
            }

            // A API do Supabase Storage currently answers a missing bucket with
            // HTTP 400 and a JSON payload whose statusCode is "404" and whose
            // code is "NoSuchBucket". Looking only at the HTTP status prevents
            // the self-healing creation path from ever running.
            if (!RespostaIndicaBucketInexistente(getResponse.StatusCode, getBody))
            {
                throw new ImagemStorageException(
                    $"Não foi possível verificar o bucket de fotos no Supabase Storage ({(int)getResponse.StatusCode}). {ExtractMessage(getBody)}");
            }

            using var createRequest = new HttpRequestMessage(HttpMethod.Post, $"{settings.Url}/storage/v1/bucket");
            ConfigureAdminHeaders(createRequest, settings.ServiceRoleKey);
            createRequest.Content = JsonContent.Create(new
            {
                id = settings.Bucket,
                name = settings.Bucket,
                @public = true,
                file_size_limit = ImagemPerfilValidator.TamanhoMaximoBytes,
                allowed_mime_types = AllowedMimeTypes
            });

            using var createResponse = await SharedClient.SendAsync(createRequest, cancellationToken);
            var createBody = await createResponse.Content.ReadAsStringAsync(cancellationToken);

            if (createResponse.StatusCode == HttpStatusCode.Conflict)
            {
                await TornarBucketPublicoAsync(settings, cancellationToken);
            }
            else if (!createResponse.IsSuccessStatusCode)
            {
                throw new ImagemStorageException(
                    $"Não foi possível criar o bucket de fotos no Supabase Storage ({(int)createResponse.StatusCode}). {ExtractMessage(createBody)}");
            }

            BucketsReady.TryAdd(cacheKey, 0);
        }
        finally
        {
            BucketLock.Release();
        }
    }

    private async Task TornarBucketPublicoAsync(StorageSettings settings, CancellationToken cancellationToken)
    {
        using var updateRequest = new HttpRequestMessage(
            HttpMethod.Put,
            $"{settings.Url}/storage/v1/bucket/{EscapeSegment(settings.Bucket)}");
        ConfigureAdminHeaders(updateRequest, settings.ServiceRoleKey);

        // updateBucket altera as opções do bucket; o identificador já está na URL.
        updateRequest.Content = JsonContent.Create(new
        {
            @public = true,
            file_size_limit = ImagemPerfilValidator.TamanhoMaximoBytes,
            allowed_mime_types = AllowedMimeTypes
        });

        using var updateResponse = await SharedClient.SendAsync(updateRequest, cancellationToken);
        var updateBody = await updateResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!updateResponse.IsSuccessStatusCode)
        {
            throw new ImagemStorageException(
                $"O bucket de fotos existe, mas não foi possível configurá-lo como público ({(int)updateResponse.StatusCode}). {ExtractMessage(updateBody)}");
        }
    }

    private StorageSettings GetSettings()
    {
        var url = configuration["Supabase:Url"]?.TrimEnd('/');
        var serviceRoleKey = configuration["Supabase:ServiceRoleKey"];
        var bucket = configuration["Supabase:PhotoBucket"]?.Trim();

        if (string.IsNullOrWhiteSpace(serviceRoleKey))
            serviceRoleKey = configuration["Supabase:SecretKey"];

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(serviceRoleKey))
        {
            throw new ImagemStorageConfigurationException(
                "Configure 'Supabase:Url' e 'Supabase:ServiceRoleKey' (ou 'Supabase:SecretKey') no backend para habilitar o upload de fotos.");
        }

        if (string.IsNullOrWhiteSpace(bucket))
            bucket = "english-yard-fotos";

        return new StorageSettings(url, serviceRoleKey, bucket);
    }

    private static void ConfigureAdminHeaders(HttpRequestMessage request, string serviceRoleKey)
    {
        // O StorageClient oficial envia a chave administrativa nos dois headers.
        // Isso funciona tanto com a legacy service_role JWT quanto com as novas secret keys.
        request.Headers.TryAddWithoutValidation("apikey", serviceRoleKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceRoleKey);
    }

    private static bool BucketEhPublico(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return false;

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("public", out var property)
                && property.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static bool RespostaIndicaBucketInexistente(HttpStatusCode statusCode, string body)
    {
        if (statusCode == HttpStatusCode.NotFound)
            return true;

        if (string.IsNullOrWhiteSpace(body))
            return false;

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String
                && string.Equals(code.GetString(), "NoSuchBucket", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!root.TryGetProperty("statusCode", out var responseStatusCode))
                return false;

            return responseStatusCode.ValueKind switch
            {
                JsonValueKind.Number => responseStatusCode.TryGetInt32(out var numericStatusCode)
                    && numericStatusCode == (int)HttpStatusCode.NotFound,
                JsonValueKind.String => string.Equals(
                    responseStatusCode.GetString(),
                    ((int)HttpStatusCode.NotFound).ToString(),
                    StringComparison.Ordinal),
                _ => false
            };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ObterExtensao(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => string.Empty
    };

    private static string SanitizeSegment(string value)
    {
        var sanitized = new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .ToArray());

        return string.IsNullOrWhiteSpace(sanitized) ? "perfil" : sanitized;
    }

    private static string EscapePath(string path) =>
        string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(EscapeSegment));

    private static string EscapeSegment(string value) => Uri.EscapeDataString(value);

    private static string ExtractMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "Erro sem detalhes.";

        try
        {
            using var document = JsonDocument.Parse(body);
            foreach (var propertyName in new[] { "message", "error", "msg", "error_description", "code" })
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
            // Corpo não JSON: retornamos abaixo de forma limitada.
        }

        return body.Length > 300 ? body[..300] : body;
    }

    private sealed record StorageSettings(string Url, string ServiceRoleKey, string Bucket);
}
