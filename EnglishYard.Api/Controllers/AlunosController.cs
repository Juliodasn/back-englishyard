using System.Security.Claims;
using EnglishYard.Application.Alunos;
using EnglishYard.Application.Autenticacao;
using EnglishYard.Application.Imagens;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishYard.Api.Controllers;

[ApiController]
[Route("api/alunos")]
[Authorize]
public sealed class AlunosController(AlunoService service) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = PortalRoles.Administrador)]
    [ProducesResponseType<IReadOnlyList<AlunoResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AlunoResponse>>> Listar(CancellationToken cancellationToken)
    {
        Guid? professoraId = null;
        if (User.IsInRole(PortalRoles.Professora))
        {
            professoraId = GetProfessoraId();
            if (!professoraId.HasValue)
                return Forbid();
        }

        var alunos = await service.ListarAsync(professoraId, cancellationToken);
        return Ok(alunos);
    }


    [HttpGet("paginado")]
    [Authorize(Roles = PortalRoles.Administrador)]
    [ProducesResponseType<AlunoListagemPaginadaResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AlunoListagemPaginadaResponse>> ListarPaginado(
        [FromQuery] string? search,
        [FromQuery] Guid? professoraId,
        [FromQuery] string? status,
        [FromQuery] short? diaSemana,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        Guid? professoraAcessoId = null;
        if (User.IsInRole(PortalRoles.Professora))
        {
            professoraAcessoId = GetProfessoraId();
            if (!professoraAcessoId.HasValue)
                return Forbid();

            professoraId = null;
        }

        try
        {
            return Ok(await service.ListarPaginadoAsync(
                professoraAcessoId,
                search,
                professoraId,
                status,
                diaSemana,
                page,
                pageSize,
                cancellationToken));
        }
        catch (AlunoValidationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest, title: "Filtros inválidos");
        }
    }

    [HttpGet("exportacao")]
    [Authorize(Roles = PortalRoles.Administrador)]
    [ProducesResponseType<IReadOnlyList<AlunoExportacaoResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<AlunoExportacaoResponse>>> ListarExportacao(
        [FromQuery] string? search,
        [FromQuery] Guid? professoraId,
        [FromQuery] string? status,
        [FromQuery] short? diaSemana,
        CancellationToken cancellationToken = default)
    {
        Guid? professoraAcessoId = null;
        if (User.IsInRole(PortalRoles.Professora))
        {
            professoraAcessoId = GetProfessoraId();
            if (!professoraAcessoId.HasValue)
                return Forbid();

            professoraId = null;
        }

        try
        {
            return Ok(await service.ListarExportacaoAsync(
                professoraAcessoId,
                search,
                professoraId,
                status,
                diaSemana,
                cancellationToken));
        }
        catch (AlunoValidationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest, title: "Filtros inválidos");
        }
    }

    [HttpGet("operacionais")]
    [ProducesResponseType<IReadOnlyList<AlunoOperacionalResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AlunoOperacionalResponse>>> ListarOperacionais(
        CancellationToken cancellationToken)
    {
        Guid? professoraId = null;
        if (User.IsInRole(PortalRoles.Professora))
        {
            professoraId = GetProfessoraId();
            if (!professoraId.HasValue) return Forbid();
        }

        return Ok(await service.ListarOperacionaisAsync(professoraId, cancellationToken));
    }

    [HttpGet("arquivados")]
    [Authorize(Roles = PortalRoles.Administrador)]
    public async Task<ActionResult<IReadOnlyList<AlunoArquivadoResponse>>> ListarArquivados(CancellationToken cancellationToken) =>
        Ok(await service.ListarArquivadosAsync(cancellationToken));

    [HttpPost("{id:guid}/restaurar")]
    [Authorize(Roles = PortalRoles.Administrador)]
    public async Task<IActionResult> Restaurar(Guid id, CancellationToken cancellationToken) =>
        await service.RestaurarAsync(id, cancellationToken) ? NoContent() : NotFound();

    [HttpGet("{id:guid}")]
    [Authorize(Roles = PortalRoles.Administrador)]
    [ProducesResponseType<AlunoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AlunoResponse>> ObterPorId(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.BuscarPorIdAsync(id, cancellationToken));
        }
        catch (AlunoNaoEncontradoException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status404NotFound, title: "Aluno não encontrado");
        }
    }

    [HttpGet("{id:guid}/agenda")]
    [Authorize(Roles = PortalRoles.Administrador)]
    [ProducesResponseType<AgendaAlunoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AgendaAlunoResponse>> ObterAgenda(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.BuscarAgendaAsync(id, cancellationToken));
        }
        catch (AlunoNaoEncontradoException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status404NotFound, title: "Aluno não encontrado");
        }
    }

    [HttpGet("professoras")]
    [Authorize(Roles = PortalRoles.Administrador)]
    [ProducesResponseType<IReadOnlyList<ProfessoraResumoResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProfessoraResumoResponse>>> ListarProfessoras(CancellationToken cancellationToken)
    {
        var professoras = await service.ListarProfessorasAtivasAsync(cancellationToken);
        return Ok(professoras);
    }

    [HttpPost("{id:guid}/foto")]
    [Authorize(Roles = PortalRoles.Administrador)]
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
            var aluno = await service.AtualizarFotoAsync(id, stream, foto.ContentType, foto.Length, cancellationToken);
            return Ok(aluno);
        }
        catch (AlunoValidationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest, title: "Foto inválida");
        }
        catch (AlunoNaoEncontradoException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status404NotFound, title: "Aluno não encontrado");
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
    [Authorize(Roles = PortalRoles.Administrador)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken cancellationToken)
    {
        var excluido = await service.ExcluirAsync(id, cancellationToken);
        if (!excluido)
            return Problem("Aluno não encontrado ou já excluído.", statusCode: StatusCodes.Status404NotFound, title: "Aluno não encontrado");

        return NoContent();
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = PortalRoles.Administrador)]
    [ProducesResponseType<AlunoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AlunoResponse>> Atualizar(
        Guid id,
        [FromBody] AtualizarAlunoRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.AtualizarAsync(id, request, cancellationToken));
        }
        catch (AlunoValidationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest, title: "Dados do aluno inválidos");
        }
        catch (AlunoNaoEncontradoException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status404NotFound, title: "Aluno não encontrado");
        }
        catch (AlunoConflitoException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status409Conflict, title: "Conflito no cadastro do aluno");
        }
    }

    [HttpPost]
    [Authorize(Roles = PortalRoles.Administrador)]
    [ProducesResponseType<AlunoResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AlunoResponse>> Cadastrar(
        [FromBody] CadastrarAlunoRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var aluno = await service.CadastrarAsync(request, cancellationToken);
            return Created($"/api/alunos/{aluno.Id}", aluno);
        }
        catch (AlunoValidationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest, title: "Dados do aluno inválidos");
        }
        catch (AlunoConflitoException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status409Conflict, title: "Conflito no cadastro do aluno");
        }
    }

    private Guid? GetProfessoraId()
    {
        var value = User.FindFirstValue(PortalClaimTypes.ProfessoraId);
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
