using System.Security.Claims;
using EnglishYard.Application.Autenticacao;
using EnglishYard.Application.Professoras;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishYard.Api.Controllers;

[ApiController]
[Route("api/autenticacao")]
[Authorize]
public sealed class AutenticacaoController(AutenticacaoService service, ProfessoraService professoraService) : ControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType<PerfilUsuarioResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PerfilUsuarioResponse>> Me(CancellationToken cancellationToken)
    {
        var authUserId = GetAuthUserId();
        var profile = await service.ObterPerfilAsync(authUserId, cancellationToken);
        return profile is null ? Unauthorized() : Ok(profile);
    }

    [HttpGet("me/perfil")]
    [ProducesResponseType<MeuPerfilResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MeuPerfilResponse>> MeuPerfil(CancellationToken cancellationToken)
    {
        var profile = await service.ObterPerfilAsync(GetAuthUserId(), cancellationToken);
        if (profile is null)
            return Unauthorized();

        if (!profile.ProfessoraId.HasValue)
            return Ok(BuildMeuPerfilResponse(profile, null));

        try
        {
            var professora = await professoraService.BuscarPorIdAsync(profile.ProfessoraId.Value, cancellationToken);
            return Ok(BuildMeuPerfilResponse(profile, professora));
        }
        catch (ProfessoraNaoEncontradaException)
        {
            return NotFound();
        }
    }

    [HttpPut("me/perfil")]
    [Authorize(Roles = PortalRoles.Professora)]
    [ProducesResponseType<MeuPerfilResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MeuPerfilResponse>> AtualizarMeuPerfil(
        [FromBody] AtualizarMeuPerfilRequest request,
        CancellationToken cancellationToken)
    {
        var profile = await service.ObterPerfilAsync(GetAuthUserId(), cancellationToken);
        if (profile is null || !profile.ProfessoraId.HasValue)
            return Unauthorized();

        try
        {
            var professora = await professoraService.AtualizarPerfilProprioAsync(
                profile.ProfessoraId.Value,
                new AtualizarMeuPerfilProfessoraRequest(request.NomeProfissional, request.Telefone),
                cancellationToken);
            return Ok(BuildMeuPerfilResponse(profile, professora));
        }
        catch (ProfessoraValidationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest, title: "Dados do perfil inválidos");
        }
        catch (ProfessoraNaoEncontradaException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status404NotFound, title: "Professora não encontrada");
        }
    }

    [HttpPost("me/foto")]
    [Authorize(Roles = PortalRoles.Professora)]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [ProducesResponseType<MeuPerfilResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<MeuPerfilResponse>> AtualizarMinhaFoto(
        [FromForm] IFormFile foto,
        CancellationToken cancellationToken)
    {
        if (foto is null || foto.Length == 0)
            return Problem("Selecione uma imagem válida.", statusCode: StatusCodes.Status400BadRequest, title: "Foto inválida");

        var profile = await service.ObterPerfilAsync(GetAuthUserId(), cancellationToken);
        if (profile is null || !profile.ProfessoraId.HasValue)
            return Unauthorized();

        try
        {
            await using var stream = foto.OpenReadStream();
            var professora = await professoraService.AtualizarFotoAsync(
                profile.ProfessoraId.Value,
                stream,
                foto.ContentType,
                foto.Length,
                cancellationToken);
            return Ok(BuildMeuPerfilResponse(profile, professora));
        }
        catch (ProfessoraValidationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest, title: "Foto inválida");
        }
        catch (ProfessoraNaoEncontradaException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status404NotFound, title: "Professora não encontrada");
        }
        catch (EnglishYard.Application.Imagens.ImagemStorageConfigurationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable, title: "Storage não configurado");
        }
        catch (EnglishYard.Application.Imagens.ImagemStorageException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway, title: "Falha no upload da foto");
        }
    }

    [HttpGet("me/professora")]
    [Authorize(Roles = PortalRoles.Professora)]
    [ProducesResponseType<FotoPerfilProfessoraResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FotoPerfilProfessoraResponse>> MinhaProfessora(CancellationToken cancellationToken)
    {
        var value = User.FindFirstValue(PortalClaimTypes.ProfessoraId);
        if (!Guid.TryParse(value, out var professoraId))
            return Unauthorized();

        try
        {
            var professora = await professoraService.BuscarPorIdAsync(professoraId, cancellationToken);
            return Ok(new FotoPerfilProfessoraResponse(professora.Id, professora.Nome, professora.FotoUrl));
        }
        catch (ProfessoraNaoEncontradaException)
        {
            return NotFound();
        }
    }

    [HttpPost("alterar-senha")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> AlterarSenha(
        [FromBody] AlterarSenhaPortalRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await service.AlterarSenhaAsync(
                GetAuthUserId(),
                GetBearerToken(),
                request,
                cancellationToken);
            return NoContent();
        }
        catch (AutenticacaoValidationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest, title: "Senha inválida");
        }
        catch (SupabaseAuthConfigurationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable, title: "Supabase Auth não configurado");
        }
        catch (SupabaseAuthException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway, title: "Falha ao alterar senha");
        }
    }


    private static MeuPerfilResponse BuildMeuPerfilResponse(PerfilUsuarioResponse profile, ProfessoraResponse? professora) => new(
        profile.UsuarioAuthId,
        profile.Nome,
        profile.Email,
        profile.TipoUsuario,
        profile.ProfessoraId,
        professora?.FotoUrl ?? profile.FotoUrl,
        professora?.NomeProfissional,
        professora?.Telefone,
        professora?.Status,
        professora?.ModeloPagamento,
        professora?.DiaPagamento,
        professora?.TipoChavePix,
        professora?.ChavePix,
        professora?.Banco,
        professora?.ValorAulaIndividual,
        professora?.ValorAulaGrupo,
        professora?.VigenteDesde,
        profile.ProfessoraId.HasValue);

    private Guid GetAuthUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("A sessão autenticada não possui um identificador válido.");
    }

    private string GetBearerToken()
    {
        var authorization = Request.Headers.Authorization.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization["Bearer ".Length..].Trim()
            : string.Empty;
    }
}
