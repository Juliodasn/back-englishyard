using EnglishYard.Application.Aulas;

namespace EnglishYard.Tests;

public sealed class RegistroAulaServiceTests
{
    [Fact]
    public async Task AgendarAulaAvulsa_Admin_EncaminhaAulaParaRepositorio()
    {
        var occurrence = CreateOccurrence(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), "agendado") with
        {
            OcorrenciaId = $"a:{Guid.NewGuid()}",
            AulaId = Guid.NewGuid(),
            HorarioRecorrenteId = null,
            PossuiRegistroReal = true
        };
        var repository = new FakeRepository(occurrence);
        var service = new RegistroAulaService(repository);

        var request = new AgendarAulaAvulsaRequest(
            occurrence.AlunoId,
            occurrence.ProfessoraId,
            occurrence.Data,
            new TimeOnly(14, 0),
            new TimeOnly(15, 0),
            "individual",
            "Aula extra");

        var result = await service.AgendarAulaAvulsaAsync(
            request,
            null,
            true,
            Guid.NewGuid(),
            "Admin Teste",
            CancellationToken.None);

        Assert.NotNull(repository.LastStandaloneScheduleRequest);
        Assert.Null(repository.LastStandaloneScheduleRequest!.ProfessoraRestritaId);
        Assert.Equal("individual", repository.LastStandaloneScheduleRequest.Tipo);
        Assert.Equal(occurrence.AulaId, result.AulaId);
    }

    [Fact]
    public async Task AgendarAulaAvulsa_Professora_NaoPermiteOutraProfessora()
    {
        var occurrence = CreateOccurrence(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), "agendado");
        var repository = new FakeRepository(occurrence);
        var service = new RegistroAulaService(repository);

        await Assert.ThrowsAsync<RegistroAulaValidationException>(() => service.AgendarAulaAvulsaAsync(
            new AgendarAulaAvulsaRequest(
                occurrence.AlunoId,
                Guid.NewGuid(),
                occurrence.Data,
                new TimeOnly(14, 0),
                new TimeOnly(15, 0),
                "individual",
                null),
            occurrence.ProfessoraId,
            false,
            Guid.NewGuid(),
            "Professora Teste",
            CancellationToken.None));

        Assert.Null(repository.LastStandaloneScheduleRequest);
    }

    [Fact]
    public async Task RegistrarResultado_AulaFutura_NaoPermiteAplicada()
    {
        var occurrence = CreateOccurrence(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), "agendado");
        var repository = new FakeRepository(occurrence);
        var service = new RegistroAulaService(repository);

        var exception = await Assert.ThrowsAsync<RegistroAulaValidationException>(() => service.RegistrarResultadoAsync(
            new RegistrarResultadoAulaRequest(occurrence.OcorrenciaId, ResultadoAulaCodigos.Aplicada, null),
            occurrence.ProfessoraId,
            Guid.NewGuid(),
            "Professora Teste",
            CancellationToken.None));

        Assert.Contains("futuras", exception.Message.ToLowerInvariant());
        Assert.Null(repository.LastPersistedRequest);
    }

    [Fact]
    public async Task RegistrarResultado_AulaFutura_PermiteRemarcacaoEEncaminhaAoRepositorio()
    {
        var occurrence = CreateOccurrence(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), "agendado");
        var repository = new FakeRepository(occurrence);
        var service = new RegistroAulaService(repository);

        var result = await service.RegistrarResultadoAsync(
            new RegistrarResultadoAulaRequest(occurrence.OcorrenciaId, ResultadoAulaCodigos.RemarcadaAluno, "Pedido do aluno"),
            occurrence.ProfessoraId,
            Guid.NewGuid(),
            "Professora Teste",
            CancellationToken.None);

        Assert.NotNull(repository.LastPersistedRequest);
        Assert.Equal(ResultadoAulaCodigos.RemarcadaAluno, repository.LastPersistedRequest!.Status);
        Assert.Equal("Remarcada pelo aluno", result.Status);
        Assert.Equal(ResultadoAulaCodigos.RemarcadaAluno, result.StatusCodigo);
    }

    [Fact]
    public async Task RegistrarResultado_AulaPassada_PermiteFaltaDoAluno()
    {
        var occurrence = CreateOccurrence(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)), "agendado");
        var repository = new FakeRepository(occurrence);
        var service = new RegistroAulaService(repository);

        var result = await service.RegistrarResultadoAsync(
            new RegistrarResultadoAulaRequest(occurrence.OcorrenciaId, ResultadoAulaCodigos.FaltaAluno, null),
            occurrence.ProfessoraId,
            Guid.NewGuid(),
            "Professora Teste",
            CancellationToken.None);

        Assert.Equal(ResultadoAulaCodigos.FaltaAluno, repository.LastPersistedRequest?.Status);
        Assert.Equal("Falta do aluno", result.Status);
    }

    [Fact]
    public async Task AtualizarOcorrencia_AulaFutura_EncaminhaNovoHorarioAoRepositorio()
    {
        var occurrence = CreateOccurrence(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), "agendado");
        var repository = new FakeRepository(occurrence);
        var service = new RegistroAulaService(repository);

        var result = await service.AtualizarOcorrenciaAsync(
            new AtualizarOcorrenciaAulaRequest(occurrence.OcorrenciaId, new TimeOnly(14, 0), new TimeOnly(15, 0), "Exceção da semana"),
            occurrence.ProfessoraId,
            Guid.NewGuid(),
            "Admin Teste",
            CancellationToken.None);

        Assert.NotNull(repository.LastOccurrenceUpdateRequest);
        Assert.Equal(new TimeOnly(14, 0), repository.LastOccurrenceUpdateRequest!.HoraInicio);
        Assert.Equal(new TimeOnly(15, 0), result.HoraFim);
    }

    [Fact]
    public async Task CancelarOcorrencia_AulaFutura_MarcaCancelamentoNoRepositorio()
    {
        var occurrence = CreateOccurrence(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), "agendado");
        var repository = new FakeRepository(occurrence);
        var service = new RegistroAulaService(repository);

        var result = await service.CancelarOcorrenciaAsync(
            new CancelarOcorrenciaAulaRequest(occurrence.OcorrenciaId, "Aluno avisou com antecedência"),
            occurrence.ProfessoraId,
            Guid.NewGuid(),
            "Admin Teste",
            CancellationToken.None);

        Assert.NotNull(repository.LastCancellationRequest);
        Assert.Equal(ResultadoAulaCodigos.Cancelada, result.StatusCodigo);
        Assert.Equal("Cancelada", result.Status);
    }

    [Fact]
    public async Task CancelarOcorrencia_AulaPassada_EhBloqueada()
    {
        var occurrence = CreateOccurrence(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)), "agendado");
        var repository = new FakeRepository(occurrence);
        var service = new RegistroAulaService(repository);

        await Assert.ThrowsAsync<RegistroAulaValidationException>(() => service.CancelarOcorrenciaAsync(
            new CancelarOcorrenciaAulaRequest(occurrence.OcorrenciaId, null),
            occurrence.ProfessoraId,
            Guid.NewGuid(),
            "Admin Teste",
            CancellationToken.None));

        Assert.Null(repository.LastCancellationRequest);
    }

    [Fact]
    public async Task ListarDia_TraduzAulaPassadaSemRegistroParaAguardandoRegistro()
    {
        var occurrence = CreateOccurrence(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)), "agendado");
        var service = new RegistroAulaService(new FakeRepository(occurrence));

        var result = await service.ListarDiaAsync(occurrence.Data, occurrence.ProfessoraId, CancellationToken.None);

        var item = Assert.Single(result);
        Assert.Equal("passada", item.SituacaoTemporal);
        Assert.Equal("Aguardando registro", item.Status);
        Assert.True(item.PodeRegistrarResultado);
        Assert.True(item.PodeRemarcar);
    }

    private static OcorrenciaAulaData CreateOccurrence(DateOnly date, string participantStatus, bool replacement = false) => new(
        $"r:{Guid.NewGuid()}:{date:yyyy-MM-dd}",
        null,
        Guid.NewGuid(),
        replacement ? Guid.NewGuid() : null,
        date,
        new TimeOnly(10, 0),
        new TimeOnly(11, 0),
        Guid.NewGuid(),
        "Aluno Teste",
        Guid.NewGuid(),
        "Professora Teste",
        "Individual",
        participantStatus,
        replacement,
        false,
        participantStatus is "aplicada" or "perdida",
        50m,
        null,
        replacement ? "agendada" : null);

    private sealed class FakeRepository(OcorrenciaAulaData occurrence) : IRegistroAulaRepository
    {
        private OcorrenciaAulaData current = occurrence;
        public RegistroAulaPersistenciaRequest? LastPersistedRequest { get; private set; }
        public AtualizarOcorrenciaPersistenciaRequest? LastOccurrenceUpdateRequest { get; private set; }
        public CancelarOcorrenciaPersistenciaRequest? LastCancellationRequest { get; private set; }
        public AgendarAulaAvulsaPersistenciaRequest? LastStandaloneScheduleRequest { get; private set; }

        public Task<IReadOnlyList<OcorrenciaAulaData>> ListarDiaAsync(DateOnly data, Guid? professoraId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OcorrenciaAulaData>>(new[] { current });

        public Task<OcorrenciaAulaData?> BuscarOcorrenciaAsync(string ocorrenciaId, Guid? professoraId, CancellationToken cancellationToken) =>
            Task.FromResult<OcorrenciaAulaData?>(current.OcorrenciaId == ocorrenciaId ? current : null);

        public Task<OcorrenciaAulaData> RegistrarResultadoAsync(RegistroAulaPersistenciaRequest request, CancellationToken cancellationToken)
        {
            LastPersistedRequest = request;
            var raw = request.Status switch
            {
                ResultadoAulaCodigos.Aplicada => "aplicada",
                ResultadoAulaCodigos.FaltaAluno => "perdida",
                ResultadoAulaCodigos.RemarcadaAluno => "remarcada_aluno",
                ResultadoAulaCodigos.RemarcadaProfessora => "remarcada_professora",
                _ => "agendado"
            };
            current = current with { ParticipanteStatus = raw, PossuiRegistroReal = true };
            return Task.FromResult(current);
        }

        public Task<OcorrenciaAulaData> AtualizarOcorrenciaAsync(AtualizarOcorrenciaPersistenciaRequest request, CancellationToken cancellationToken)
        {
            LastOccurrenceUpdateRequest = request;
            current = current with
            {
                HoraInicio = request.HoraInicio,
                HoraFim = request.HoraFim,
                PossuiRegistroReal = true
            };
            return Task.FromResult(current);
        }

        public Task<OcorrenciaAulaData> CancelarOcorrenciaAsync(CancelarOcorrenciaPersistenciaRequest request, CancellationToken cancellationToken)
        {
            LastCancellationRequest = request;
            current = current with { ParticipanteStatus = "cancelada", PossuiRegistroReal = true, Observacao = request.Motivo };
            return Task.FromResult(current);
        }

        public Task<OcorrenciaAulaData> AgendarAulaAvulsaAsync(AgendarAulaAvulsaPersistenciaRequest request, CancellationToken cancellationToken)
        {
            LastStandaloneScheduleRequest = request;
            current = current with
            {
                OcorrenciaId = current.OcorrenciaId.StartsWith("a:", StringComparison.OrdinalIgnoreCase)
                    ? current.OcorrenciaId
                    : $"a:{Guid.NewGuid()}",
                AulaId = current.AulaId ?? Guid.NewGuid(),
                HorarioRecorrenteId = null,
                Data = request.Data,
                HoraInicio = request.HoraInicio,
                HoraFim = request.HoraFim,
                AlunoId = request.AlunoId,
                ProfessoraId = request.ProfessoraId,
                Tipo = request.Tipo == "grupo" ? "Grupo" : "Individual",
                ParticipanteStatus = "agendado",
                PossuiRegistroReal = true,
                Observacao = request.Observacao
            };
            return Task.FromResult(current);
        }

        public Task<IReadOnlyList<HistoricoAulaResponse>> ListarHistoricoAsync(Guid aulaId, Guid? professoraId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HistoricoAulaResponse>>(Array.Empty<HistoricoAulaResponse>());

        public Task<IReadOnlyList<ReposicaoResponse>> ListarReposicoesAsync(Guid? professoraId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReposicaoResponse>>(Array.Empty<ReposicaoResponse>());

        public Task<ReposicaoResponse?> BuscarReposicaoAsync(Guid reposicaoId, Guid? professoraId, CancellationToken cancellationToken) =>
            Task.FromResult<ReposicaoResponse?>(null);

        public Task<ReposicaoResponse> AgendarReposicaoAsync(Guid reposicaoId, AgendarReposicaoRequest request, Guid usuarioAuthId, string usuarioNome, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReposicaoResponse> CancelarAgendamentoReposicaoAsync(Guid reposicaoId, Guid usuarioAuthId, string usuarioNome, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

    }
}
