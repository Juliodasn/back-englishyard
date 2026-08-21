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
        if (!User.IsInRole(PortalRoles.Administrador) && GetProfessoraId() != professoraId)
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

    [HttpPost("recebimentos/{recebimentoId:guid}/estornar")]
    [Authorize(Roles = PortalRoles.Administrador)]
    public async Task<IActionResult> EstornarRecebimento(Guid recebimentoId, MotivoOperacaoFinanceiraRequest request, CancellationToken ct) =>
        await ExecuteAdminMutation(id => service.EstornarRecebimentoAsync(recebimentoId, request.Motivo, id, ct));

    [HttpPatch("mensalidades/{mensalidadeId:guid}/desconto")]
    [Authorize(Roles = PortalRoles.Administrador)]
    public async Task<IActionResult> AjustarMensalidade(Guid mensalidadeId, AjustarMensalidadeRequest request, CancellationToken ct) =>
        await ExecuteAdminMutation(id => service.AjustarMensalidadeAsync(mensalidadeId, request, id, ct));

    [HttpPost("mensalidades/{mensalidadeId:guid}/cancelar")]
    [Authorize(Roles = PortalRoles.Administrador)]
    public async Task<IActionResult> CancelarMensalidade(Guid mensalidadeId, MotivoOperacaoFinanceiraRequest request, CancellationToken ct) =>
        await ExecuteAdminMutation(id => service.CancelarMensalidadeAsync(mensalidadeId, request.Motivo, id, ct));

    [HttpPut("despesas/{despesaId:guid}")]
    [Authorize(Roles = PortalRoles.Administrador)]
    public async Task<IActionResult> AtualizarDespesa(Guid despesaId, AtualizarDespesaRequest request, CancellationToken ct) =>
        await ExecuteAdminMutation(id => service.AtualizarDespesaAsync(despesaId, request, id, ct));

    [HttpPost("despesas/{despesaId:guid}/cancelar")]
    [Authorize(Roles = PortalRoles.Administrador)]
    public async Task<IActionResult> CancelarDespesa(Guid despesaId, MotivoOperacaoFinanceiraRequest request, CancellationToken ct) =>
        await ExecuteAdminMutation(id => service.CancelarDespesaAsync(despesaId, request.Motivo, id, ct));

    [HttpPost("despesas/{despesaId:guid}/reabrir")]
    [Authorize(Roles = PortalRoles.Administrador)]
    public async Task<IActionResult> ReabrirDespesa(Guid despesaId, MotivoOperacaoFinanceiraRequest request, CancellationToken ct) =>
        await ExecuteAdminMutation(id => service.ReabrirDespesaAsync(despesaId, request.Motivo, id, ct));

    [HttpPost("professoras/{professoraId:guid}/ajustes")]
    [Authorize(Roles = PortalRoles.Administrador)]
    public async Task<IActionResult> CriarAjuste(Guid professoraId, [FromQuery] string? competencia, CriarAjusteProfessoraRequest request, CancellationToken ct)
    {
        if (!TryGetAuthUserId(out var userId)) return Forbid();
        try { return Ok(new { id = await service.CriarAjusteProfessoraAsync(professoraId, ParseCompetencia(competencia), request, userId, ct) }); }
        catch (FinanceiroValidationException e) { return Problem(statusCode: 400, detail: e.Message); }
        catch (FinanceiroConflitoException e) { return Problem(statusCode: 409, detail: e.Message); }
    }

    [HttpDelete("ajustes/{ajusteId:guid}")]
    [Authorize(Roles = PortalRoles.Administrador)]
    public async Task<IActionResult> ExcluirAjuste(Guid ajusteId, CancellationToken ct) =>
        await ExecuteAdminMutation(id => service.ExcluirAjusteProfessoraAsync(ajusteId, id, ct));

    [HttpPost("professoras/{professoraId:guid}/fechamentos/aprovar")]
    [Authorize(Roles = PortalRoles.Administrador)]
    public async Task<IActionResult> AprovarFechamento(Guid professoraId, [FromQuery] string? competencia, CancellationToken ct) =>
        await ExecuteAdminMutation(id => service.AprovarFechamentoAsync(professoraId, ParseCompetencia(competencia), id, ct));

    [HttpPost("professoras/{professoraId:guid}/fechamentos/pagar")]
    [Authorize(Roles = PortalRoles.Administrador)]
    public async Task<IActionResult> PagarFechamento(Guid professoraId, [FromQuery] string? competencia, MarcarFechamentoPagoRequest request, CancellationToken ct) =>
        await ExecuteAdminMutation(id => service.MarcarFechamentoPagoAsync(professoraId, ParseCompetencia(competencia), request, id, ct));

    [HttpPost("professoras/{professoraId:guid}/fechamentos/reabrir")]
    [Authorize(Roles = PortalRoles.Administrador)]
    public async Task<IActionResult> ReabrirFechamento(Guid professoraId, [FromQuery] string? competencia, MotivoOperacaoFinanceiraRequest request, CancellationToken ct) =>
        await ExecuteAdminMutation(id => service.ReabrirFechamentoAsync(professoraId, ParseCompetencia(competencia), request.Motivo, id, ct));

    [HttpGet("politica-pagamento")]
    [Authorize(Roles = PortalRoles.Administrador)]
    public async Task<ActionResult<PoliticaPagamentoResponse>> ObterPolitica([FromQuery] DateOnly? data, CancellationToken ct)
    {
        var policy = await service.ObterPoliticaPagamentoAsync(data ?? DateOnly.FromDateTime(DateTime.Today), ct);
        return policy is null ? NotFound() : Ok(policy);
    }

    [HttpPost("politica-pagamento")]
    [Authorize(Roles = PortalRoles.Administrador)]
    public async Task<ActionResult<PoliticaPagamentoResponse>> SalvarPolitica(SalvarPoliticaPagamentoRequest request, CancellationToken ct)
    {
        if (!TryGetAuthUserId(out var userId)) return Forbid();
        return Ok(await service.SalvarPoliticaPagamentoAsync(request, userId, ct));
    }

    private async Task<IActionResult> ExecuteAdminMutation(Func<Guid, Task<bool>> operation)
    {
        if (!TryGetAuthUserId(out var userId)) return Forbid();
        try { return await operation(userId) ? NoContent() : NotFound(); }
        catch (FinanceiroValidationException e) { return Problem(statusCode: 400, detail: e.Message); }
        catch (FinanceiroConflitoException e) { return Problem(statusCode: 409, detail: e.Message); }
    }

    private bool TryGetAuthUserId(out Guid id)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out id);
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
