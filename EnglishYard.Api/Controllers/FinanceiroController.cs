using System.Security.Claims;
using EnglishYard.Application.Autenticacao;
using EnglishYard.Application.Financeiro;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishYard.Api.Controllers;

[ApiController]
[Route("api/financeiro")]
[Authorize]
public sealed class FinanceiroController(FinanceiroService service) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = PortalRoles.Administrador)]
    public async Task<ActionResult<FinanceiroResumoResponse>> ObterResumo(
        [FromQuery] string? competencia,
        CancellationToken cancellationToken)
    {
        try
        {
            var date = ParseCompetencia(competencia);
            return Ok(await service.ObterResumoAsync(date, cancellationToken));
        }
        catch (FinanceiroValidationException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: exception.Message);
        }
    }

    [HttpGet("meu-pagamento")]
    [Authorize(Roles = PortalRoles.Professora)]
    public async Task<ActionResult<DemonstrativoProfessoraResponse>> ObterMeuPagamento(
        [FromQuery] string? competencia,
        CancellationToken cancellationToken)
    {
        var professoraId = GetProfessoraId();
        if (!professoraId.HasValue)
            return Forbid();

        var result = await service.ObterDemonstrativoProfessoraAsync(professoraId.Value, ParseCompetencia(competencia), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("professoras/{professoraId:guid}/demonstrativo")]
    public async Task<ActionResult<DemonstrativoProfessoraResponse>> ObterDemonstrativoProfessora(
        Guid professoraId,
        [FromQuery] string? competencia,
        CancellationToken cancellationToken)
    {
        if (User.IsInRole(PortalRoles.Professora) && GetProfessoraId() != professoraId)
            return Forbid();

        var result = await service.ObterDemonstrativoProfessoraAsync(professoraId, ParseCompetencia(competencia), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("mensalidades/{alunoId:guid}/recebimentos")]
    [Authorize(Roles = PortalRoles.Administrador)]
    public async Task<IActionResult> RegistrarRecebimento(
        Guid alunoId,
        [FromQuery] string? competencia,
        RegistrarRecebimentoRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await service.RegistrarRecebimentoAsync(alunoId, ParseCompetencia(competencia), request, cancellationToken);
            return NoContent();
        }
        catch (FinanceiroValidationException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: exception.Message);
        }
        catch (FinanceiroNotFoundException exception)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, detail: exception.Message);
        }
        catch (FinanceiroConflitoException exception)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, detail: exception.Message);
        }
    }

    [HttpPost("despesas")]
    [Authorize(Roles = PortalRoles.Administrador)]
    public async Task<ActionResult<DespesaFinanceiroResponse>> CadastrarDespesa(
        [FromQuery] string? competencia,
        CadastrarDespesaRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.CadastrarDespesaAsync(ParseCompetencia(competencia), request, cancellationToken);
            return Created($"/api/financeiro/despesas/{result.Id}", result);
        }
        catch (FinanceiroValidationException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: exception.Message);
        }
    }

    [HttpPost("despesas/{despesaId:guid}/pagar")]
    [Authorize(Roles = PortalRoles.Administrador)]
    public async Task<IActionResult> MarcarDespesaPaga(
        Guid despesaId,
        MarcarDespesaPagaRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await service.MarcarDespesaPagaAsync(despesaId, request, cancellationToken);
        return updated ? NoContent() : NotFound();
    }

    private Guid? GetProfessoraId()
    {
        var value = User.FindFirstValue(PortalClaimTypes.ProfessoraId);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private static DateOnly ParseCompetencia(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            return new DateOnly(today.Year, today.Month, 1);
        }

        if (DateOnly.TryParseExact($"{value}-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed))
            return parsed;

        throw new FinanceiroValidationException("Competência inválida. Use o formato yyyy-MM.");
    }
}
