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

    public async Task<IReadOnlyList<AlunoOperacionalResponse>> ListarOperacionaisAsync(
        Guid? professoraId,
        CancellationToken cancellationToken)
    {
        var alunos = await repository.ListarAsync(professoraId, cancellationToken);
        return alunos.Select(aluno => new AlunoOperacionalResponse(aluno.Id, aluno.Nome, aluno.FotoUrl)).ToArray();
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

    public async Task<IReadOnlyList<AlunoHorarioResumoResponse>> ListarHorariosGerenciaveisAsync(
        Guid? professoraId, CancellationToken cancellationToken) =>
        await repository.ListarAlunosHorariosAsync(professoraId, cancellationToken);

    public async Task<AgendaAlunoResponse> BuscarAgendaGerenciavelAsync(
        Guid alunoId, Guid? professoraId, CancellationToken cancellationToken)
    {
        await VerificarAcessoAgendaAsync(alunoId, professoraId, cancellationToken);
        var agenda = await BuscarAgendaAsync(alunoId, cancellationToken);
        if (!professoraId.HasValue) return agenda;
        return new AgendaAlunoResponse(
            agenda.Vigentes.Where(h => h.ProfessoraId == professoraId.Value).ToArray(),
            agenda.Programados.Where(h => h.ProfessoraId == professoraId.Value).ToArray(),
            agenda.Historico.Where(h => h.ProfessoraId == professoraId.Value).ToArray());
    }

    public async Task<AgendaAlunoResponse> AlterarSomenteHorariosAsync(
        Guid alunoId, Guid? professoraId, AlterarGradeHorariosRequest request, CancellationToken cancellationToken)
    {
        if (request is null || request.Horarios is null)
            throw new AlunoValidationException("Informe os horários da nova grade.");
        if (request.VigenteDesde <= DateOnly.FromDateTime(DateTime.Today))
            throw new AlunoValidationException("A nova grade precisa começar a partir de amanhã.");

        await VerificarAcessoAgendaAsync(alunoId, professoraId, cancellationToken);
        var agenda = await BuscarAgendaAsync(alunoId, cancellationToken);
        // Uma versão futura substitui toda a grade na mesma data; respeitar sua composição.
        var referencia = agenda.Programados.Count > 0 ? agenda.Programados : agenda.Vigentes;
        if (referencia.Count == 0)
            throw new AlunoValidationException("O aluno não possui uma grade ativa para editar. Procure a administração.");

        var responsaveisPermitidos = referencia.Select(h => h.ProfessoraId).ToHashSet();
        var recebidos = request.Horarios;
        if (recebidos.Any(h => !responsaveisPermitidos.Contains(h.ProfessoraId)
            || (professoraId.HasValue && h.ProfessoraId != professoraId.Value)))
            throw new AlunoValidationException("Não é permitido alterar a professora responsável por uma aula.");

        var originaisEditaveis = referencia.Where(h => !professoraId.HasValue || h.ProfessoraId == professoraId.Value).ToArray();
        if (originaisEditaveis.Length == 0)
            throw new AlunoValidationException("Não existem horários desta professora na grade válida para alteração.");

        // Edição de horário NÃO permite adicionar/remover aulas, trocar professora ou
        // mudar carga horária (isso poderia afetar contratos, duplas, grupos e financeiro).
        static string[] Assinatura(IReadOnlyCollection<(Guid ProfessoraId, int Minutos)> aulas) =>
            aulas.Select(item => $"{item.ProfessoraId:N}:{item.Minutos}").OrderBy(x => x).ToArray();
        var original = Assinatura(originaisEditaveis
            .Select(h => (h.ProfessoraId, (int)(h.HoraFim - h.HoraInicio).TotalMinutes)).ToArray());
        var recebido = Assinatura(recebidos
            .Select(h => (h.ProfessoraId, (int)(h.HoraFim - h.HoraInicio).TotalMinutes)).ToArray());
        if (!original.SequenceEqual(recebido))
            throw new AlunoValidationException("Altere apenas os dias e horários, mantendo a professora, quantidade de aulas e duração original.");

        var preservados = referencia.Where(h => professoraId.HasValue && h.ProfessoraId != professoraId.Value);
        var horariosCompletos = recebidos.Select(h =>
            new HorarioRecorrenteAlunoRequest(h.DiaSemana, h.HoraInicio, h.HoraFim,
                h.ProfessoraId, request.VigenteDesde))
            .Concat(preservados.Select(h =>
                new HorarioRecorrenteAlunoRequest(h.DiaSemana, h.HoraInicio, h.HoraFim,
                    h.ProfessoraId, request.VigenteDesde)))
            .ToArray();
        ValidateHorarios(horariosCompletos, exigirAoMenosUm: true);
        await ValidarProfessorasEConflitosAsync(alunoId, horariosCompletos, cancellationToken);

        // Aulas de dupla/grupo são representadas pelo mesmo intervalo para múltiplos
        // alunos. Uma mudança isolada poderia converter a modalidade e afetar valores.
        // Mantemos esse tipo de alteração no fluxo administrativo completo.
        foreach (var anterior in originaisEditaveis)
        {
            if (recebidos.Any(h => h.ProfessoraId == anterior.ProfessoraId
                && h.DiaSemana == anterior.DiaSemana
                && h.HoraInicio == anterior.HoraInicio && h.HoraFim == anterior.HoraFim))
                continue;
            var referenciaData = anterior.DataInicio > DateOnly.FromDateTime(DateTime.Today)
                ? anterior.DataInicio : DateOnly.FromDateTime(DateTime.Today);
            if (await repository.ExisteAulaCompartilhadaAsync(alunoId, anterior.ProfessoraId,
                    anterior.DiaSemana, anterior.HoraInicio, anterior.HoraFim, referenciaData, cancellationToken))
                throw new AlunoValidationException("Este horário pertence a uma dupla ou grupo. Solicite à administração a alteração da turma.");
        }
        foreach (var novo in recebidos)
        {
            if (originaisEditaveis.Any(h => h.ProfessoraId == novo.ProfessoraId
                && h.DiaSemana == novo.DiaSemana
                && h.HoraInicio == novo.HoraInicio && h.HoraFim == novo.HoraFim))
                continue;
            if (await repository.ExisteAulaCompartilhadaAsync(alunoId, novo.ProfessoraId,
                    novo.DiaSemana, novo.HoraInicio, novo.HoraFim, request.VigenteDesde, cancellationToken))
                throw new AlunoValidationException("O novo horário coincide com uma dupla ou grupo. Solicite à administração.");
        }

        // O repositório bloqueia o aluno e confere a versão vigente/futura para
        // impedir sobrescrever modificações simultâneas de outra professora.
        var versao = agenda.Vigentes.Concat(agenda.Programados).Select(h => h.Id).ToArray();
        if (!await repository.AtualizarApenasAgendaAsync(
                alunoId, request.VigenteDesde, horariosCompletos, versao, cancellationToken))
            throw new AlunoNaoEncontradoException("Aluno não encontrado ou inativo.");

        return await BuscarAgendaGerenciavelAsync(alunoId, professoraId, cancellationToken);
    }

    private async Task VerificarAcessoAgendaAsync(Guid alunoId, Guid? professoraId, CancellationToken cancellationToken)
    {
        if (professoraId.HasValue
            && !await repository.PossuiHorarioAtivoAsync(alunoId, professoraId.Value, cancellationToken))
            throw new AlunoAcessoNegadoException("Você só pode gerenciar os horários dos seus próprios alunos.");
        _ = await repository.BuscarPorIdAsync(alunoId, cancellationToken)
            ?? throw new AlunoNaoEncontradoException("Aluno não encontrado ou inativo.");
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

    public Task<IReadOnlyList<AlunoArquivadoResponse>> ListarArquivadosAsync(CancellationToken cancellationToken) => repository.ListarArquivadosAsync(cancellationToken);
    public Task<bool> RestaurarAsync(Guid alunoId, CancellationToken cancellationToken) => repository.RestaurarAsync(alunoId, cancellationToken);

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

public sealed class AlunoAcessoNegadoException(string message) : Exception(message);
