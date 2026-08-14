using EnglishYard.Application.Autenticacao;
using EnglishYard.Application.Imagens;
using EnglishYard.Application.Professoras;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishYard.Api.Controllers;

[ApiController]
[Route("api/professoras")]
[Authorize(Roles = PortalRoles.Administrador)]
public sealed class ProfessorasController(ProfessoraService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ProfessoraResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProfessoraResponse>>> Listar(CancellationToken cancellationToken)
    {
        var professoras = await service.ListarAsync(cancellationToken);
        return Ok(professoras);
    }


    [HttpGet("paginado")]
    [ProducesResponseType<ProfessoraListagemPaginadaResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProfessoraListagemPaginadaResponse>> ListarPaginado(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await service.ListarPaginadoAsync(search, status, page, pageSize, cancellationToken));
        }
        catch (ProfessoraValidationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest, title: "Filtros inválidos");
        }
    }

    [HttpGet("exportacao")]
    [ProducesResponseType<IReadOnlyList<ProfessoraExportacaoResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<ProfessoraExportacaoResponse>>> ListarExportacao(
        [FromQuery] string? search,
        [FromQuery] string? status,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await service.ListarExportacaoAsync(search, status, cancellationToken));
        }
        catch (ProfessoraValidationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest, title: "Filtros inválidos");
        }
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProfessoraResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProfessoraResponse>> ObterPorId(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.BuscarPorIdAsync(id, cancellationToken));
        }
        catch (ProfessoraNaoEncontradaException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status404NotFound, title: "Professora não encontrada");
        }
    }

    [HttpGet("{id:guid}/valores-aula")]
    [ProducesResponseType<IReadOnlyList<ValorAulaProfessoraHistoricoResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ValorAulaProfessoraHistoricoResponse>>> ListarHistoricoValores(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await service.BuscarPorIdAsync(id, cancellationToken);
            return Ok(await service.ListarHistoricoValoresAsync(id, cancellationToken));
        }
        catch (ProfessoraNaoEncontradaException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status404NotFound, title: "Professora não encontrada");
        }
    }

    [HttpPost("{id:guid}/foto")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> AtualizarFoto(
        Guid id,
        [FromForm] IFormFile foto,
        CancellationToken cancellationToken)
    {
        if (foto is null || foto.Length == 0)
            return Problem("Selecione uma imagem válida.", statusCode: StatusCodes.Status400BadRequest, title: "Foto inválida");

        try
        {
            await using var stream = foto.OpenReadStream();
            var professora = await service.AtualizarFotoAsync(id, stream, foto.ContentType, foto.Length, cancellationToken);
            return Ok(professora);
        }
        catch (ProfessoraValidationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest, title: "Foto inválida");
        }
        catch (ProfessoraNaoEncontradaException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status404NotFound, title: "Professora não encontrada");
        }
        catch (ImagemStorageConfigurationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable, title: "Storage não configurado");
        }
        catch (ImagemStorageException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway, title: "Falha no upload da foto");
        }
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken cancellationToken)
    {
        var excluida = await service.ExcluirAsync(id, cancellationToken);
        if (!excluida)
            return Problem("Professora não encontrada ou já excluída.", statusCode: StatusCodes.Status404NotFound, title: "Professora não encontrada");

        return NoContent();
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ProfessoraResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProfessoraResponse>> Atualizar(
        Guid id,
        [FromBody] AtualizarProfessoraRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.AtualizarAsync(id, request, cancellationToken));
        }
        catch (ProfessoraValidationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest, title: "Dados da professora inválidos");
        }
        catch (ProfessoraNaoEncontradaException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status404NotFound, title: "Professora não encontrada");
        }
    }

    [HttpPost("{id:guid}/acesso")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CriarAcesso(
        Guid id,
        [FromBody] CriarAcessoProfessoraRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await service.CriarAcessoAsync(id, request, cancellationToken);
            return NoContent();
        }
        catch (ProfessoraValidationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest, title: "Acesso inválido");
        }
        catch (ProfessoraConflitoException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status409Conflict, title: "Acesso já configurado");
        }
        catch (SupabaseAuthConfigurationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable, title: "Supabase Auth não configurado");
        }
        catch (SupabaseAuthException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway, title: "Falha ao criar acesso da professora");
        }
    }

    [HttpPost]
    [ProducesResponseType<ProfessoraResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ProfessoraResponse>> Cadastrar(
        [FromBody] CadastrarProfessoraRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var professora = await service.CadastrarAsync(request, cancellationToken);
            return Created($"/api/professoras/{professora.Id}", professora);
        }
        catch (ProfessoraValidationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest, title: "Dados da professora inválidos");
        }
        catch (ProfessoraConflitoException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status409Conflict, title: "Professora já cadastrada");
        }
        catch (SupabaseAuthConfigurationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable, title: "Supabase Auth não configurado");
        }
        catch (SupabaseAuthException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway, title: "Falha ao criar acesso da professora");
        }
    }
}
