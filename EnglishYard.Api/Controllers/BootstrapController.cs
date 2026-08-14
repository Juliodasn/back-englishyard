using System.Security.Cryptography;
using EnglishYard.Application.Autenticacao;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishYard.Api.Controllers;

[ApiController]
[Route("api/bootstrap")]
public sealed class BootstrapController(
    AutenticacaoService service,
    IConfiguration configuration) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("administrador")]
    [ProducesResponseType<PerfilUsuarioResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PerfilUsuarioResponse>> CriarAdministradorInicial(
        [FromBody] BootstrapAdministradorRequest request,
        [FromHeader(Name = "X-Bootstrap-Key")] string? bootstrapKey,
        CancellationToken cancellationToken)
    {
        var expectedKey = configuration["Bootstrap:AdminKey"];
        if (string.IsNullOrWhiteSpace(expectedKey))
        {
            return Problem(
                "Configure 'Bootstrap:AdminKey' no backend antes de criar o primeiro administrador.",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Bootstrap não configurado");
        }

        if (string.IsNullOrWhiteSpace(bootstrapKey)
            || !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(bootstrapKey),
                System.Text.Encoding.UTF8.GetBytes(expectedKey)))
        {
            return Problem("Chave de bootstrap inválida.", statusCode: StatusCodes.Status403Forbidden, title: "Acesso negado");
        }

        try
        {
            var created = await service.CriarAdministradorInicialAsync(request, cancellationToken);
            return Created("/api/autenticacao/me", created);
        }
        catch (BootstrapAdministradorValidationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest, title: "Dados do administrador inválidos");
        }
        catch (BootstrapAdministradorConflitoException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status409Conflict, title: "Administrador já configurado");
        }
        catch (SupabaseAuthConflitoException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status409Conflict, title: "Conta de acesso já existente");
        }
        catch (SupabaseAuthConfigurationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable, title: "Supabase Auth não configurado");
        }
        catch (SupabaseAuthException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway, title: "Falha no Supabase Auth");
        }
    }
}
