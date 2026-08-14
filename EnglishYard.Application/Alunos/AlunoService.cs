using EnglishYard.Application.Imagens;

namespace EnglishYard.Application.Alunos;

public sealed class AlunoService(IAlunoRepository repository, IImagemStorageGateway imagemStorage)
{
    private static readonly HashSet<string> StatusPermitidos = ["Ativo", "Pendente", "Experimental", "Inadimplente"];

    public async Task<IReadOnlyList<AlunoResponse>> ListarAsync(Guid? professoraId, CancellationToken cancellationToken)
    {
        var alunos = await repository.ListarAsync(professoraId, cancellationToken);
        return alunos.Select(AlunoResponse.FromEntity).ToArray();
    }


    public async Task<AlunoListagemPaginadaResponse> ListarPaginadoAsync(
        Guid? professoraAcessoId,
        string? busca,
        Guid? professoraFiltroId,
        string? status,
        short? diaSemana,
        int pagina,
        int tamanhoPagina,
        CancellationToken cancellationToken)
    {
        pagina = Math.Max(1, pagina);
        tamanhoPagina = tamanhoPagina is 10 or 25 or 50 ? tamanhoPagina : 10;
        busca = string.IsNullOrWhiteSpace(busca) ? null : busca.Trim();
        status = string.IsNullOrWhiteSpace(status) ? null : status.Trim();

        if (status is not null && !StatusPermitidos.Contains(status))
            throw new AlunoValidationException("Status de filtro inválido.");

        if (diaSemana is < 0 or > 6)
            throw new AlunoValidationException("Dia da semana inválido.");

        var (itens, total) = await repository.ListarPaginadoAsync(
            professoraAcessoId,
            busca,
            professoraFiltroId,
            status,
            diaSemana,
            pagina,
            tamanhoPagina,
            cancellationToken);

        var totalPaginas = total == 0 ? 0 : (int)Math.Ceiling(total / (double)tamanhoPagina);
        return new AlunoListagemPaginadaResponse(
            itens.Select(AlunoResponse.FromEntity).ToArray(),
            pagina,
            tamanhoPagina,
            total,
            totalPaginas);
    }

    public Task<IReadOnlyList<AlunoExportacaoResponse>> ListarExportacaoAsync(
        Guid? professoraAcessoId,
        string? busca,
        Guid? professoraFiltroId,
        string? status,
        short? diaSemana,
        CancellationToken cancellationToken)
    {
        busca = string.IsNullOrWhiteSpace(busca) ? null : busca.Trim();
        status = string.IsNullOrWhiteSpace(status) ? null : status.Trim();

        if (status is not null && !StatusPermitidos.Contains(status))
            throw new AlunoValidationException("Status de filtro inválido.");

        if (diaSemana is < 0 or > 6)
            throw new AlunoValidationException("Dia da semana inválido.");

        return repository.ListarExportacaoAsync(
            professoraAcessoId,
            busca,
            professoraFiltroId,
            status,
            diaSemana,
            cancellationToken);
    }

    public Task<IReadOnlyList<ProfessoraResumoResponse>> ListarProfessorasAtivasAsync(CancellationToken cancellationToken) =>
        repository.ListarProfessorasAtivasAsync(cancellationToken);

    public async Task<AlunoResponse> BuscarPorIdAsync(Guid alunoId, CancellationToken cancellationToken)
    {
        var aluno = await repository.BuscarPorIdAsync(alunoId, cancellationToken)
            ?? throw new AlunoNaoEncontradoException("Aluno não encontrado ou inativo.");
        return AlunoResponse.FromEntity(aluno);
    }

    public async Task<AgendaAlunoResponse> BuscarAgendaAsync(Guid alunoId, CancellationToken cancellationToken)
    {
        _ = await repository.BuscarPorIdAsync(alunoId, cancellationToken)
            ?? throw new AlunoNaoEncontradoException("Aluno não encontrado ou inativo.");

        var horarios = await repository.ListarHorariosRecorrentesAsync(alunoId, cancellationToken);
        var hoje = DateOnly.FromDateTime(DateTime.Today);

        var vigentes = horarios
            .Where(h => h.Ativo
                && h.DataInicio <= hoje
                && (!h.DataFim.HasValue || h.DataFim.Value >= hoje))
            .OrderBy(h => h.DiaSemana)
            .ThenBy(h => h.HoraInicio)
            .ToArray();

        var programados = horarios
            .Where(h => h.Ativo && h.DataInicio > hoje)
            .OrderBy(h => h.DataInicio)
            .ThenBy(h => h.DiaSemana)
            .ThenBy(h => h.HoraInicio)
            .ToArray();

        var historico = horarios
            .Where(h => !h.Ativo || (h.DataFim.HasValue && h.DataFim.Value < hoje))
            .OrderByDescending(h => h.DataFim ?? h.DataInicio)
            .ThenBy(h => h.DiaSemana)
            .ThenBy(h => h.HoraInicio)
            .ToArray();

        return new AgendaAlunoResponse(vigentes, programados, historico);
    }

    public Task<bool> ExcluirAsync(Guid alunoId, CancellationToken cancellationToken) =>
        repository.ExcluirAsync(alunoId, cancellationToken);

    public async Task<AlunoResponse> AtualizarAsync(
        Guid alunoId,
        AtualizarAlunoRequest request,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(request);
        Validate(normalized);

        if (!string.IsNullOrWhiteSpace(normalized.Email)
            && await repository.EmailExisteAsync(normalized.Email, alunoId, cancellationToken))
        {
            throw new AlunoConflitoException("Já existe outro aluno cadastrado com este e-mail.");
        }

        if (normalized.HorariosRecorrentes is not null)
        {
            await ValidarProfessorasEConflitosAsync(
                alunoId,
                normalized.HorariosRecorrentes,
                cancellationToken);
        }

        var atualizado = await repository.AtualizarAsync(alunoId, normalized, cancellationToken)
            ?? throw new AlunoNaoEncontradoException("Aluno não encontrado ou inativo.");

        return AlunoResponse.FromEntity(atualizado);
    }

    public async Task<AlunoResponse> AtualizarFotoAsync(
        Guid alunoId,
        Stream conteudo,
        string contentType,
        long tamanho,
        CancellationToken cancellationToken)
    {
        var erro = ImagemPerfilValidator.ObterErro(contentType, tamanho);
        if (erro is not null)
            throw new AlunoValidationException(erro);

        var fotoUrl = await imagemStorage.SalvarFotoPerfilAsync(
            "alunos",
            alunoId,
            conteudo,
            contentType,
            cancellationToken);

        if (!await repository.AtualizarFotoUrlAsync(alunoId, fotoUrl, cancellationToken))
            throw new AlunoNaoEncontradoException("Aluno não encontrado ou inativo.");

        var atualizado = await repository.BuscarPorIdAsync(alunoId, cancellationToken)
            ?? throw new AlunoNaoEncontradoException("A foto foi enviada, mas o aluno não pôde ser recarregado do banco.");

        if (string.IsNullOrWhiteSpace(atualizado.FotoUrl))
            throw new ImagemStorageException("A foto foi enviada, mas a URL não foi persistida no cadastro do aluno.");

        return AlunoResponse.FromEntity(atualizado);
    }

    public async Task<AlunoResponse> CadastrarAsync(CadastrarAlunoRequest request, CancellationToken cancellationToken)
    {
        var normalized = Normalize(request);
        Validate(normalized);

        if (!string.IsNullOrWhiteSpace(normalized.Email)
            && await repository.EmailExisteAsync(normalized.Email, null, cancellationToken))
        {
            throw new AlunoConflitoException("Já existe um aluno cadastrado com este e-mail.");
        }

        await ValidarProfessorasEConflitosAsync(null, normalized.HorariosRecorrentes ?? [], cancellationToken);

        var aluno = await repository.CadastrarAsync(normalized, cancellationToken);
        return AlunoResponse.FromEntity(aluno);
    }

    private async Task ValidarProfessorasEConflitosAsync(
        Guid? alunoId,
        IReadOnlyCollection<HorarioRecorrenteAlunoRequest> horarios,
        CancellationToken cancellationToken)
    {
        foreach (var horario in horarios)
        {
            if (!horario.ProfessoraId.HasValue)
                throw new AlunoValidationException("Selecione a professora responsável por cada horário da agenda.");

            if (!await repository.ProfessoraExisteAsync(horario.ProfessoraId.Value, cancellationToken))
                throw new AlunoValidationException("Uma das professoras selecionadas na agenda não existe ou está inativa.");

            var conflito = await repository.BuscarConflitoHorarioAsync(
                alunoId,
                horario.ProfessoraId.Value,
                horario.DiaSemana,
                horario.HoraInicio,
                horario.HoraFim,
                horario.DataInicio,
                cancellationToken);

            if (conflito is null)
                continue;

            // O mesmo intervalo exato pode representar uma aula em grupo. Sobreposições parciais
            // são bloqueadas porque tornam a professora indisponível para duas aulas diferentes.
            if (conflito.HoraInicio == horario.HoraInicio && conflito.HoraFim == horario.HoraFim)
                continue;

            throw new AlunoConflitoException(
                $"A professora {conflito.ProfessoraNome} já possui aula de {conflito.AlunoNome} " +
                $"das {conflito.HoraInicio:HH:mm} às {conflito.HoraFim:HH:mm} nesse dia. " +
                "Escolha outro horário para evitar sobreposição.");
        }
    }

    private static CadastrarAlunoRequest Normalize(CadastrarAlunoRequest request) => request with
    {
        Nome = request.Nome.Trim(),
        Genero = NullIfWhiteSpace(request.Genero),
        Email = NullIfWhiteSpace(request.Email)?.ToLowerInvariant(),
        Telefone = NullIfWhiteSpace(request.Telefone),
        ResponsavelNome = NullIfWhiteSpace(request.ResponsavelNome),
        ResponsavelTelefone = NullIfWhiteSpace(request.ResponsavelTelefone),
        Status = string.IsNullOrWhiteSpace(request.Status) ? "Ativo" : request.Status.Trim(),
        FormaPagamento = NullIfWhiteSpace(request.FormaPagamento),
        Observacoes = NullIfWhiteSpace(request.Observacoes),
        HorariosRecorrentes = request.HorariosRecorrentes ?? []
    };

    private static AtualizarAlunoRequest Normalize(AtualizarAlunoRequest request)
    {
        var agendaVigenteDesde = request.AgendaVigenteDesde;
        var horarios = request.HorariosRecorrentes;
        if (horarios is not null && agendaVigenteDesde.HasValue)
        {
            horarios = horarios
                .Select(h => h with { DataInicio = agendaVigenteDesde.Value })
                .ToArray();
        }

        return request with
        {
            Nome = request.Nome.Trim(),
            Genero = NullIfWhiteSpace(request.Genero),
            Email = NullIfWhiteSpace(request.Email)?.ToLowerInvariant(),
            Telefone = NullIfWhiteSpace(request.Telefone),
            ResponsavelNome = NullIfWhiteSpace(request.ResponsavelNome),
            ResponsavelTelefone = NullIfWhiteSpace(request.ResponsavelTelefone),
            Status = string.IsNullOrWhiteSpace(request.Status) ? "Ativo" : request.Status.Trim(),
            FormaPagamento = NullIfWhiteSpace(request.FormaPagamento),
            Observacoes = NullIfWhiteSpace(request.Observacoes),
            HorariosRecorrentes = horarios
        };
    }

    private static void Validate(CadastrarAlunoRequest request)
    {
        ValidateCommon(
            request.Nome,
            request.Email,
            request.Telefone,
            request.Status,
            request.ValorMensalidade,
            request.DiaVencimento,
            request.TaxaMatricula,
            request.PercentualDesconto);

        ValidateHorarios(request.HorariosRecorrentes ?? [], exigirAoMenosUm: true);
    }

    private static void Validate(AtualizarAlunoRequest request)
    {
        ValidateCommon(
            request.Nome,
            request.Email,
            request.Telefone,
            request.Status,
            request.ValorMensalidade,
            request.DiaVencimento,
            request.TaxaMatricula,
            request.PercentualDesconto);

        if (request.HorariosRecorrentes is null)
            return;

        if (!request.AgendaVigenteDesde.HasValue)
            throw new AlunoValidationException("Informe a data a partir da qual a nova grade semanal será válida.");

        var hoje = DateOnly.FromDateTime(DateTime.Today);
        if (request.AgendaVigenteDesde.Value <= hoje)
            throw new AlunoValidationException("Para preservar as aulas de hoje e o histórico, a nova grade deve começar a partir de amanhã.");

        ValidateHorarios(request.HorariosRecorrentes, exigirAoMenosUm: true);
    }

    private static void ValidateHorarios(
        IReadOnlyCollection<HorarioRecorrenteAlunoRequest> horarios,
        bool exigirAoMenosUm)
    {
        if (exigirAoMenosUm && horarios.Count == 0)
            throw new AlunoValidationException("Cadastre pelo menos um dia e horário na agenda semanal do aluno.");

        foreach (var horario in horarios)
        {
            if (horario.DiaSemana is < 0 or > 6)
                throw new AlunoValidationException("Dia da semana inválido na agenda do aluno.");

            if (horario.HoraFim <= horario.HoraInicio)
                throw new AlunoValidationException("Na agenda semanal, o horário final deve ser maior que o horário inicial.");

            if (!horario.ProfessoraId.HasValue)
                throw new AlunoValidationException("Selecione a professora responsável por cada horário da agenda.");
        }

        var duplicado = horarios
            .GroupBy(h => new { h.DiaSemana, h.HoraInicio })
            .Any(grupo => grupo.Count() > 1);

        if (duplicado)
            throw new AlunoValidationException("Existem horários duplicados na agenda semanal do aluno.");

        foreach (var grupoDia in horarios.GroupBy(h => h.DiaSemana))
        {
            var ordenados = grupoDia.OrderBy(h => h.HoraInicio).ToArray();
            for (var indice = 1; indice < ordenados.Length; indice++)
            {
                if (ordenados[indice].HoraInicio < ordenados[indice - 1].HoraFim)
                    throw new AlunoValidationException("Existem horários sobrepostos no mesmo dia da agenda do aluno.");
            }
        }
    }

    private static void ValidateCommon(
        string nome,
        string? email,
        string? telefone,
        string status,
        decimal? valorMensalidade,
        short? diaVencimento,
        decimal taxaMatricula,
        decimal percentualDesconto)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new AlunoValidationException("Informe o nome do aluno.");

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            throw new AlunoValidationException("Informe um e-mail válido para o aluno.");

        if (string.IsNullOrWhiteSpace(telefone))
            throw new AlunoValidationException("Informe o telefone do aluno.");

        if (!StatusPermitidos.Contains(status))
            throw new AlunoValidationException("Status do aluno inválido.");

        if (diaVencimento is < 1 or > 31)
            throw new AlunoValidationException("O dia de vencimento deve estar entre 1 e 31.");

        if (valorMensalidade is < 0 || taxaMatricula < 0)
            throw new AlunoValidationException("Os valores financeiros não podem ser negativos.");

        if (percentualDesconto is < 0 or > 100)
            throw new AlunoValidationException("O desconto deve estar entre 0% e 100%.");
    }

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class AlunoValidationException(string message) : Exception(message);
public sealed class AlunoConflitoException(string message) : Exception(message);
public sealed class AlunoNaoEncontradoException(string message) : Exception(message);
