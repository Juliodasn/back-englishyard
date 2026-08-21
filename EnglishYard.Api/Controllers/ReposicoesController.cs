using System.Security.Claims;
using EnglishYard.Application.Aulas;
using EnglishYard.Application.Autenticacao;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishYard.Api.Controllers;

[ApiController]
[Route("api/reposicoes")]
[Authorize]
public sealed class ReposicoesController(RegistroAulaService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReposicaoResponse>>> Listar(CancellationToken cancellationToken)
    {
        if (!TryGetProfessoraFilter(out var professoraId)) return Forbid();
        return Ok(await service.ListarReposicoesAsync(professoraId, cancellationToken));
    }

    [HttpPost("{reposicaoId:guid}/agendar")]
    public async Task<ActionResult<ReposicaoResponse>> Agendar(
        Guid reposicaoId,
        AgendarReposicaoRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfessoraFilter(out var professoraId) || !TryGetAuthUserId(out var authUserId)) return Forbid();
        try
        {
            return Ok(await service.AgendarReposicaoAsync(
                reposicaoId,
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

    [HttpPost("{reposicaoId:guid}/cancelar-agendamento")]
    public async Task<ActionResult<ReposicaoResponse>> CancelarAgendamento(
        Guid reposicaoId,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfessoraFilter(out var professoraId) || !TryGetAuthUserId(out var authUserId)) return Forbid();
        try
        {
            return Ok(await service.CancelarAgendamentoReposicaoAsync(
                reposicaoId,
                professoraId,
                authUserId,
                User.Identity?.Name ?? "Usuário do portal",
                cancellationToken));
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
