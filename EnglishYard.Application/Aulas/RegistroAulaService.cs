namespace EnglishYard.Application.Aulas;

public sealed class RegistroAulaService(IRegistroAulaRepository repository)
{
    public async Task<IReadOnlyList<RegistroAulaDiaResponse>> ListarDiaAsync(
        DateOnly data,
        Guid? professoraId,
        CancellationToken cancellationToken)
    {
        var agora = ObterAgoraSaoPaulo();
        var ocorrencias = await repository.ListarDiaAsync(data, professoraId, cancellationToken);
        return ocorrencias.Select(item => Map(item, agora)).ToArray();
    }

    public async Task<RegistroAulaDiaResponse> AgendarAulaAvulsaAsync(
        AgendarAulaAvulsaRequest request,
        Guid? professoraLogadaId,
        bool administrador,
        Guid usuarioAuthId,
        string usuarioNome,
        CancellationToken cancellationToken)
    {
        var alunoIds = (request.AlunoIds ?? [])
            .Append(request.AlunoId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (alunoIds.Length == 0)
            throw new RegistroAulaValidationException("Selecione pelo menos um aluno para a nova aula.");

        if (request.ProfessoraId == Guid.Empty)
            throw new RegistroAulaValidationException("Selecione a professora responsável pela nova aula.");

        if (request.HoraFim <= request.HoraInicio)
            throw new RegistroAulaValidationException("O horário final deve ser posterior ao horário inicial.");

        var tipo = alunoIds.Length > 1 ? "grupo" : "individual";

        if (request.Observacao?.Trim().Length > 1000)
            throw new RegistroAulaValidationException("A observação pode ter no máximo 1000 caracteres.");

        var agora = ObterAgoraSaoPaulo();
        var inicio = Combinar(request.Data, request.HoraInicio, agora.Offset);
        if (inicio < agora.AddMinutes(-1))
            throw new RegistroAulaValidationException("A nova aula deve ser agendada para um horário atual ou futuro.");

        if (!administrador)
        {
            if (!professoraLogadaId.HasValue)
                throw new RegistroAulaValidationException("Não foi possível identificar a professora logada.");

            if (request.ProfessoraId != professoraLogadaId.Value)
                throw new RegistroAulaValidationException("Professoras só podem agendar novas aulas para a própria agenda.");
        }

        var ocorrencia = await repository.AgendarAulaAvulsaAsync(
            new AgendarAulaAvulsaPersistenciaRequest(
                request.AlunoId,
                request.ProfessoraId,
                request.Data,
                request.HoraInicio,
                request.HoraFim,
                tipo,
                request.Observacao?.Trim(),
                usuarioAuthId,
                usuarioNome,
                administrador ? null : professoraLogadaId,
                alunoIds),
            cancellationToken);

        return Map(ocorrencia, ObterAgoraSaoPaulo());
    }

    public async Task<RegistroAulaDiaResponse> RegistrarResultadoAsync(
        RegistrarResultadoAulaRequest request,
        Guid? professoraId,
        Guid usuarioAuthId,
        string usuarioNome,
        CancellationToken cancellationToken)
    {
        var status = request.Status?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!ResultadoAulaCodigos.Todos.Contains(status))
            throw new RegistroAulaValidationException("Status de aula inválido.");

        if (string.IsNullOrWhiteSpace(request.OcorrenciaId))
            throw new RegistroAulaValidationException("A ocorrência da aula não foi informada.");

        if (request.Observacao?.Trim().Length > 1000)
            throw new RegistroAulaValidationException("A observação pode ter no máximo 1000 caracteres.");

        var ocorrencia = await repository.BuscarOcorrenciaAsync(request.OcorrenciaId, professoraId, cancellationToken)
            ?? throw new RegistroAulaNotFoundException("A aula selecionada não foi encontrada ou não pertence à professora logada.");

        if (string.Equals(ocorrencia.ParticipanteStatus, "cancelada", StringComparison.OrdinalIgnoreCase))
            throw new RegistroAulaConflictException("Uma aula cancelada não pode receber resultado. Consulte o histórico ou crie um novo agendamento.");

        var agora = ObterAgoraSaoPaulo();
        var inicio = Combinar(ocorrencia.Data, ocorrencia.HoraInicio, agora.Offset);
        var ehFutura = inicio > agora;
        var ehRemarcacao = status is ResultadoAulaCodigos.RemarcadaAluno or ResultadoAulaCodigos.RemarcadaProfessora;

        if (ehFutura && !ehRemarcacao)
            throw new RegistroAulaValidationException("Aulas futuras só podem ser remarcadas pelo aluno ou pela professora.");

        if (ocorrencia.EhReposicao && ehRemarcacao)
            throw new RegistroAulaValidationException("Uma reposição já agendada deve ser cancelada e reagendada pela tela de Reposições.");

        var atualizado = await repository.RegistrarResultadoAsync(
            new RegistroAulaPersistenciaRequest(
                ocorrencia,
                status,
                request.Observacao?.Trim(),
                usuarioAuthId,
                usuarioNome),
            cancellationToken);

        return Map(atualizado, ObterAgoraSaoPaulo());
    }

    public async Task<RegistroAulaDiaResponse> AtualizarOcorrenciaAsync(
        AtualizarOcorrenciaAulaRequest request,
        Guid? professoraId,
        Guid usuarioAuthId,
        string usuarioNome,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.OcorrenciaId))
            throw new RegistroAulaValidationException("A ocorrência da aula não foi informada.");

        if (request.HoraFim <= request.HoraInicio)
            throw new RegistroAulaValidationException("O horário final deve ser posterior ao horário inicial.");

        if (request.Observacao?.Trim().Length > 1000)
            throw new RegistroAulaValidationException("A observação pode ter no máximo 1000 caracteres.");

        var ocorrencia = await repository.BuscarOcorrenciaAsync(request.OcorrenciaId, professoraId, cancellationToken)
            ?? throw new RegistroAulaNotFoundException("A aula selecionada não foi encontrada ou não pertence à professora logada.");

        if (ocorrencia.EhReposicao)
            throw new RegistroAulaValidationException("Reposições devem ser reagendadas pela tela de Reposições.");

        if (!string.Equals(ocorrencia.ParticipanteStatus, "agendado", StringComparison.OrdinalIgnoreCase))
            throw new RegistroAulaConflictException("Uma aula que já possui resultado, remarcação ou cancelamento não pode ter o horário editado.");

        var agora = ObterAgoraSaoPaulo();
        var inicioAtual = Combinar(ocorrencia.Data, ocorrencia.HoraInicio, agora.Offset);
        if (inicioAtual <= agora)
            throw new RegistroAulaValidationException("Somente aulas futuras podem ter o horário alterado pelo Calendário. Para aulas iniciadas ou passadas, use o Registro de aulas.");

        var atualizado = await repository.AtualizarOcorrenciaAsync(
            new AtualizarOcorrenciaPersistenciaRequest(
                ocorrencia,
                request.HoraInicio,
                request.HoraFim,
                request.Observacao?.Trim(),
                usuarioAuthId,
                usuarioNome),
            cancellationToken);

        return Map(atualizado, ObterAgoraSaoPaulo());
    }

    public async Task<RegistroAulaDiaResponse> CancelarOcorrenciaAsync(
        CancelarOcorrenciaAulaRequest request,
        Guid? professoraId,
        Guid usuarioAuthId,
        string usuarioNome,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.OcorrenciaId))
            throw new RegistroAulaValidationException("A ocorrência da aula não foi informada.");

        if (request.Motivo?.Trim().Length > 1000)
            throw new RegistroAulaValidationException("O motivo pode ter no máximo 1000 caracteres.");

        var ocorrencia = await repository.BuscarOcorrenciaAsync(request.OcorrenciaId, professoraId, cancellationToken)
            ?? throw new RegistroAulaNotFoundException("A aula selecionada não foi encontrada ou não pertence à professora logada.");

        if (ocorrencia.EhReposicao)
            throw new RegistroAulaValidationException("O cancelamento de uma reposição deve ser feito pela tela de Reposições.");

        if (!string.Equals(ocorrencia.ParticipanteStatus, "agendado", StringComparison.OrdinalIgnoreCase))
            throw new RegistroAulaConflictException("Esta aula já possui um resultado, remarcação ou cancelamento registrado.");

        var agora = ObterAgoraSaoPaulo();
        var inicio = Combinar(ocorrencia.Data, ocorrencia.HoraInicio, agora.Offset);
        if (inicio <= agora)
            throw new RegistroAulaValidationException("Somente aulas futuras podem ser canceladas diretamente pelo Calendário.");

        var atualizado = await repository.CancelarOcorrenciaAsync(
            new CancelarOcorrenciaPersistenciaRequest(
                ocorrencia,
                request.Motivo?.Trim(),
                usuarioAuthId,
                usuarioNome),
            cancellationToken);

        return Map(atualizado, ObterAgoraSaoPaulo());
    }

    public Task<IReadOnlyList<HistoricoAulaResponse>> ListarHistoricoAsync(
        Guid aulaId,
        Guid? professoraId,
        CancellationToken cancellationToken) =>
        repository.ListarHistoricoAsync(aulaId, professoraId, cancellationToken);

    public Task<IReadOnlyList<ReposicaoResponse>> ListarReposicoesAsync(
        Guid? professoraId,
        CancellationToken cancellationToken) =>
        repository.ListarReposicoesAsync(professoraId, cancellationToken);

    public async Task<ReposicaoResponse> AgendarReposicaoAsync(
        Guid reposicaoId,
        AgendarReposicaoRequest request,
        Guid? professoraLogadaId,
        bool administrador,
        Guid usuarioAuthId,
        string usuarioNome,
        CancellationToken cancellationToken)
    {
        if (request.HoraFim <= request.HoraInicio)
            throw new RegistroAulaValidationException("O horário final da reposição deve ser posterior ao horário inicial.");

        var inicio = Combinar(request.Data, request.HoraInicio, ObterAgoraSaoPaulo().Offset);
        if (inicio < ObterAgoraSaoPaulo().AddMinutes(-1))
            throw new RegistroAulaValidationException("A reposição deve ser agendada para um horário atual ou futuro.");

        var reposicao = await repository.BuscarReposicaoAsync(reposicaoId, professoraLogadaId, cancellationToken)
            ?? throw new RegistroAulaNotFoundException("A reposição não foi encontrada.");

        if (!administrador && professoraLogadaId.HasValue && request.ProfessoraId != professoraLogadaId.Value)
            throw new RegistroAulaValidationException("Professoras só podem agendar a reposição para a própria agenda.");

        if (reposicao.Status == "concluida")
            throw new RegistroAulaConflictException("Esta reposição já foi concluída e não pode ser reagendada.");

        return await repository.AgendarReposicaoAsync(
            reposicaoId,
            request with { Observacao = request.Observacao?.Trim() },
            usuarioAuthId,
            usuarioNome,
            cancellationToken);
    }

    public async Task<ReposicaoResponse> CancelarAgendamentoReposicaoAsync(
        Guid reposicaoId,
        Guid? professoraLogadaId,
        Guid usuarioAuthId,
        string usuarioNome,
        CancellationToken cancellationToken)
    {
        var reposicao = await repository.BuscarReposicaoAsync(reposicaoId, professoraLogadaId, cancellationToken)
            ?? throw new RegistroAulaNotFoundException("A reposição não foi encontrada.");

        if (reposicao.Status == "concluida")
            throw new RegistroAulaConflictException("Uma reposição concluída não pode voltar para pendente.");

        return await repository.CancelarAgendamentoReposicaoAsync(
            reposicaoId,
            usuarioAuthId,
            usuarioNome,
            cancellationToken);
    }

    private static RegistroAulaDiaResponse Map(OcorrenciaAulaData item, DateTimeOffset agora)
    {
        var inicio = Combinar(item.Data, item.HoraInicio, agora.Offset);
        var fim = Combinar(item.Data, item.HoraFim, agora.Offset);
        if (fim <= inicio) fim = fim.AddDays(1);

        var temporal = agora < inicio ? "futura" : agora < fim ? "em_andamento" : "passada";
        var codigo = MapStatusCode(item.ParticipanteStatus);
        var status = MapStatusLabel(item.ParticipanteStatus, temporal, item.EhReposicao);
        return new RegistroAulaDiaResponse(
            item.OcorrenciaId,
            item.AulaId,
            item.HorarioRecorrenteId,
            item.ReposicaoId,
            item.Data,
            item.HoraInicio,
            item.HoraFim,
            item.AlunoId,
            item.AlunoNome,
            item.ProfessoraId,
            item.ProfessoraNome,
            item.Tipo,
            status,
            codigo,
            temporal,
            item.EhReposicao,
            item.PossuiRegistroReal,
            item.ElegivelPagamento,
            item.ValorPagamento,
            item.Observacao,
            item.ReposicaoStatus,
            codigo != ResultadoAulaCodigos.Cancelada && temporal != "futura",
            codigo != ResultadoAulaCodigos.Cancelada && !item.EhReposicao,
            item.AlunoFotoUrl,
            item.ProfessoraFotoUrl);
    }

    private static string MapStatusCode(string raw) => raw switch
    {
        "aplicada" => ResultadoAulaCodigos.Aplicada,
        "perdida" => ResultadoAulaCodigos.FaltaAluno,
        "remarcada_aluno" => ResultadoAulaCodigos.RemarcadaAluno,
        "remarcada_professora" => ResultadoAulaCodigos.RemarcadaProfessora,
        "cancelada" => ResultadoAulaCodigos.Cancelada,
        _ => "agendada"
    };

    private static string MapStatusLabel(string raw, string temporal, bool reposicao) => raw switch
    {
        "aplicada" => reposicao ? "Reposição aplicada" : "Aula aplicada",
        "perdida" => "Falta do aluno",
        "remarcada_aluno" => "Remarcada pelo aluno",
        "remarcada_professora" => "Remarcada pela professora",
        "cancelada" => "Cancelada",
        _ when temporal == "em_andamento" => "Em andamento",
        _ when temporal == "passada" => "Aguardando registro",
        _ => reposicao ? "Reposição agendada" : "Agendada"
    };

    private static DateTimeOffset Combinar(DateOnly data, TimeOnly hora, TimeSpan offset) =>
        new(data.ToDateTime(hora), offset);

    private static DateTimeOffset ObterAgoraSaoPaulo()
    {
        var utc = DateTimeOffset.UtcNow;
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
            return TimeZoneInfo.ConvertTime(utc, zone);
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
                return TimeZoneInfo.ConvertTime(utc, zone);
            }
            catch
            {
                return utc.ToOffset(TimeSpan.FromHours(-3));
            }
        }
    }
}
