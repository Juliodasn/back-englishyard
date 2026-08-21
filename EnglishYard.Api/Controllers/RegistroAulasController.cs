using System.Security.Claims;
using EnglishYard.Application.Aulas;
using EnglishYard.Application.Autenticacao;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishYard.Api.Controllers;

[ApiController]
[Route("api/registro-aulas")]
[Authorize]
public sealed class RegistroAulasController(RegistroAulaService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RegistroAulaDiaResponse>>> ListarDia(
        [FromQuery] DateOnly data,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfessoraFilter(out var professoraId)) return Forbid();
        return Ok(await service.ListarDiaAsync(data, professoraId, cancellationToken));
    }

    [HttpPost("agendar")]
    public async Task<ActionResult<RegistroAulaDiaResponse>> AgendarAulaAvulsa(
        AgendarAulaAvulsaRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfessoraFilter(out var professoraId)) return Forbid();
        if (!TryGetAuthUserId(out var authUserId)) return Forbid();

        try
        {
            return Ok(await service.AgendarAulaAvulsaAsync(
                request,
                professoraId,
                User.IsInRole(PortalRoles.Administrador),
                authUserId,
                User.Identity?.Name ?? "Usuário do portal",
                cancellationToken));
        }
        catch (RegistroAulaValidationException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: exception.Message);
        }
        catch (RegistroAulaNotFoundException exception)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, detail: exception.Message);
        }
        catch (RegistroAulaConflictException exception)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, detail: exception.Message);
        }
    }

    [HttpPost("resultado")]
    public async Task<ActionResult<RegistroAulaDiaResponse>> RegistrarResultado(
        RegistrarResultadoAulaRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfessoraFilter(out var professoraId)) return Forbid();
        if (!TryGetAuthUserId(out var authUserId)) return Forbid();

        try
        {
            var result = await service.RegistrarResultadoAsync(
                request,
                professoraId,
                authUserId,
                User.Identity?.Name ?? "Usuário do portal",
                cancellationToken);
            return Ok(result);
        }
        catch (RegistroAulaValidationException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: exception.Message);
        }
        catch (RegistroAulaNotFoundException exception)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, detail: exception.Message);
        }
        catch (RegistroAulaConflictException exception)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, detail: exception.Message);
        }
    }

    [HttpPost("editar-ocorrencia")]
    public async Task<ActionResult<RegistroAulaDiaResponse>> AtualizarOcorrencia(
        AtualizarOcorrenciaAulaRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfessoraFilter(out var professoraId)) return Forbid();
        if (!TryGetAuthUserId(out var authUserId)) return Forbid();

        try
        {
            return Ok(await service.AtualizarOcorrenciaAsync(
                request,
                professoraId,
                authUserId,
                User.Identity?.Name ?? "Usuário do portal",
                cancellationToken));
        }
        catch (RegistroAulaValidationException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: exception.Message);
        }
        catch (RegistroAulaNotFoundException exception)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, detail: exception.Message);
        }
        catch (RegistroAulaConflictException exception)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, detail: exception.Message);
        }
    }

    [HttpPost("cancelar")]
    public async Task<ActionResult<RegistroAulaDiaResponse>> CancelarOcorrencia(
        CancelarOcorrenciaAulaRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfessoraFilter(out var professoraId)) return Forbid();
        if (!TryGetAuthUserId(out var authUserId)) return Forbid();

        try
        {
            return Ok(await service.CancelarOcorrenciaAsync(
                request,
                professoraId,
                authUserId,
                User.Identity?.Name ?? "Usuário do portal",
                cancellationToken));
        }
        catch (RegistroAulaValidationException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: exception.Message);
        }
        catch (RegistroAulaNotFoundException exception)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, detail: exception.Message);
        }
        catch (RegistroAulaConflictException exception)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, detail: exception.Message);
        }
    }

    [HttpGet("{aulaId:guid}/historico")]
    public async Task<ActionResult<IReadOnlyList<HistoricoAulaResponse>>> ListarHistorico(
        Guid aulaId,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfessoraFilter(out var professoraId)) return Forbid();
        return Ok(await service.ListarHistoricoAsync(aulaId, professoraId, cancellationToken));
    }

    private bool TryGetProfessoraFilter(out Guid? professoraId)
    {
        professoraId = null;
        if (User.IsInRole(PortalRoles.Administrador) || !User.IsInRole(PortalRoles.Professora)) return true;

        var value = User.FindFirstValue(PortalClaimTypes.ProfessoraId);
        if (!Guid.TryParse(value, out var parsed)) return false;
        professoraId = parsed;
        return true;
    }

    private bool TryGetAuthUserId(out Guid usuarioAuthId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out usuarioAuthId);
    }
}
