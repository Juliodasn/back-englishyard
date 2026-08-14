using System.Security.Claims;
using EnglishYard.Application.Autenticacao;
using EnglishYard.Application.Calendario;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishYard.Api.Controllers;

[ApiController]
[Route("api/calendario")]
[Authorize]
public sealed class CalendarioController(CalendarioService service) : ControllerBase
{
    [HttpGet("aulas")]
    [ProducesResponseType<IReadOnlyList<AulaCalendarioResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<AulaCalendarioResponse>>> ListarAulas(
        [FromQuery] DateOnly dataInicio,
        [FromQuery] DateOnly dataFim,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfessoraFilter(out var professoraId))
            return Forbid();

        try
        {
            return Ok(await service.ListarAulasAsync(dataInicio, dataFim, professoraId, cancellationToken));
        }
        catch (CalendarioValidationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest, title: "Período do calendário inválido");
        }
    }

    [HttpGet("grade-semanal")]
    [ProducesResponseType<IReadOnlyList<HorarioGradeSemanalResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<HorarioGradeSemanalResponse>>> ListarGradeSemanal(
        [FromQuery] DateOnly dataReferencia,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfessoraFilter(out var professoraId))
            return Forbid();

        return Ok(await service.ListarGradeSemanalAsync(dataReferencia, professoraId, cancellationToken));
    }

    private bool TryGetProfessoraFilter(out Guid? professoraId)
    {
        professoraId = null;
        if (!User.IsInRole(PortalRoles.Professora))
            return true;

        var value = User.FindFirstValue(PortalClaimTypes.ProfessoraId);
        if (!Guid.TryParse(value, out var parsed))
            return false;

        professoraId = parsed;
        return true;
    }
}
