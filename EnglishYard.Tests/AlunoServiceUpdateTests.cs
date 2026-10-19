using EnglishYard.Application.Alunos;
using EnglishYard.Application.Imagens;
using EnglishYard.Domain.Entities;

namespace EnglishYard.Tests;

public sealed class AlunoServiceUpdateTests
{
    [Fact]
    public async Task AtualizarAsync_NormalizaPersisteEDevolveCadastroSemApagarFotoOuProfessora()
    {
        var alunoId = Guid.NewGuid();
        var professoraId = Guid.NewGuid();
        var repository = new AlunoRepositoryFake(alunoId, professoraId);
        var service = new AlunoService(repository, new StorageNotUsedFake());

        var response = await service.AtualizarAsync(alunoId, CriarRequestBase() with
        {
            Nome = "  Student Updated  ",
            DataNascimento = new DateOnly(2010, 2, 3),
            Genero = " Feminino ",
            Email = " STUDENT@EXAMPLE.COM ",
            Telefone = " 11999999999 ",
            ResponsavelNome = " Guardian ",
            ResponsavelTelefone = " 11888888888 ",
            FormaPagamento = " PIX ",
            Observacoes = " Notes "
        }, CancellationToken.None);

        Assert.NotNull(repository.AtualizacaoPersistida);
        Assert.Equal("Student Updated", repository.AtualizacaoPersistida.Nome);
        Assert.Equal("student@example.com", repository.AtualizacaoPersistida.Email);
        Assert.Equal("PIX", repository.AtualizacaoPersistida.FormaPagamento);
        Assert.Equal("Student Updated", response.Nome);
        Assert.Equal(professoraId, response.ProfessoraId);
        Assert.Equal("https://example.test/teacher.png", response.ProfessoraFotoUrl);
        Assert.Equal("https://example.test/original-student.png", response.FotoUrl);
    }

    [Fact]
    public async Task AtualizarAsync_AplicaMesmaDataDeVigenciaATodosOsHorariosDaNovaGrade()
    {
        var alunoId = Guid.NewGuid();
        var professoraId = Guid.NewGuid();
        var repository = new AlunoRepositoryFake(alunoId, professoraId);
        var service = new AlunoService(repository, new StorageNotUsedFake());
        var vigenteDesde = DateOnly.FromDateTime(DateTime.Today).AddDays(1);

        await service.AtualizarAsync(alunoId, CriarRequestBase() with
        {
            AgendaVigenteDesde = vigenteDesde,
            HorariosRecorrentes =
            [
                new HorarioRecorrenteAlunoRequest(1, new TimeOnly(8, 0), new TimeOnly(9, 0), professoraId, new DateOnly(2000, 1, 1)),
                new HorarioRecorrenteAlunoRequest(4, new TimeOnly(14, 0), new TimeOnly(15, 0), professoraId, new DateOnly(2000, 1, 1))
            ]
        }, CancellationToken.None);

        Assert.NotNull(repository.AtualizacaoPersistida?.HorariosRecorrentes);
        Assert.All(repository.AtualizacaoPersistida.HorariosRecorrentes!, horario =>
            Assert.Equal(vigenteDesde, horario.DataInicio));
    }

    [Fact]
    public async Task AtualizarAsync_BloqueiaSobreposicaoParcialDaProfessora()
    {
        var alunoId = Guid.NewGuid();
        var professoraId = Guid.NewGuid();
        var repository = new AlunoRepositoryFake(alunoId, professoraId)
        {
            Conflito = new ConflitoAgendaAlunoResponse(
                Guid.NewGuid(), "Outro aluno", professoraId, "Professora Teste",
                2, new TimeOnly(8, 30), new TimeOnly(9, 30))
        };
        var service = new AlunoService(repository, new StorageNotUsedFake());
        var vigenteDesde = DateOnly.FromDateTime(DateTime.Today).AddDays(1);

        var exception = await Assert.ThrowsAsync<AlunoConflitoException>(() =>
            service.AtualizarAsync(alunoId, CriarRequestBase() with
            {
                AgendaVigenteDesde = vigenteDesde,
                HorariosRecorrentes =
                [
                    new HorarioRecorrenteAlunoRequest(2, new TimeOnly(8, 0), new TimeOnly(9, 0), professoraId, vigenteDesde)
                ]
            }, CancellationToken.None));

        Assert.Contains("sobreposição", exception.Message.ToLowerInvariant());
        Assert.Null(repository.AtualizacaoPersistida);
    }

    [Fact]
    public async Task AtualizarAsync_PermiteMesmoIntervaloExatoParaAulaCompartilhada()
    {
        var alunoId = Guid.NewGuid();
        var professoraId = Guid.NewGuid();
        var repository = new AlunoRepositoryFake(alunoId, professoraId)
        {
            Conflito = new ConflitoAgendaAlunoResponse(
                Guid.NewGuid(), "Outro aluno", professoraId, "Professora Teste",
                2, new TimeOnly(8, 0), new TimeOnly(9, 0))
        };
        var service = new AlunoService(repository, new StorageNotUsedFake());
        var vigenteDesde = DateOnly.FromDateTime(DateTime.Today).AddDays(1);

        await service.AtualizarAsync(alunoId, CriarRequestBase() with
        {
            AgendaVigenteDesde = vigenteDesde,
            HorariosRecorrentes =
            [
                new HorarioRecorrenteAlunoRequest(2, new TimeOnly(8, 0), new TimeOnly(9, 0), professoraId, vigenteDesde)
            ]
        }, CancellationToken.None);

        Assert.NotNull(repository.AtualizacaoPersistida);
    }

    [Fact]
    public async Task BuscarAgendaAsync_SeparaGradeAtualProgramadaEHistoricaPelaVigencia()
    {
        var alunoId = Guid.NewGuid();
        var professoraId = Guid.NewGuid();
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var repository = new AlunoRepositoryFake(alunoId, professoraId)
        {
            Horarios =
            [
                new HorarioRecorrenteAlunoResponse(Guid.NewGuid(), 1, new TimeOnly(8, 0), new TimeOnly(9, 0), professoraId, "Professora", hoje.AddDays(-30), hoje.AddDays(4), true),
                new HorarioRecorrenteAlunoResponse(Guid.NewGuid(), 3, new TimeOnly(10, 0), new TimeOnly(11, 0), professoraId, "Professora", hoje.AddDays(5), null, true),
                new HorarioRecorrenteAlunoResponse(Guid.NewGuid(), 5, new TimeOnly(12, 0), new TimeOnly(13, 0), professoraId, "Professora", hoje.AddDays(-60), hoje.AddDays(-1), true)
            ]
        };
        var service = new AlunoService(repository, new StorageNotUsedFake());

        var agenda = await service.BuscarAgendaAsync(alunoId, CancellationToken.None);

        Assert.Single(agenda.Vigentes);
        Assert.Single(agenda.Programados);
        Assert.Single(agenda.Historico);
        Assert.Equal(1, agenda.Vigentes[0].DiaSemana);
        Assert.Equal(3, agenda.Programados[0].DiaSemana);
        Assert.Equal(5, agenda.Historico[0].DiaSemana);
    }

    [Fact]
    public async Task Horarios_ProfessoraAlteraApenasSuaGradeEPreservaHorariosDeOutraProfessora()
    {
        var alunoId = Guid.NewGuid();
        var professora1 = Guid.NewGuid();
        var professora2 = Guid.NewGuid();
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var repository = new AlunoRepositoryFake(alunoId, professora1)
        {
            ProfessoraExtra = professora2,
            Horarios =
            [
                new HorarioRecorrenteAlunoResponse(Guid.NewGuid(), 1, new(8, 0), new(9, 0), professora1, "Tati", hoje.AddDays(-10), null, true),
                new HorarioRecorrenteAlunoResponse(Guid.NewGuid(), 2, new(16, 0), new(17, 0), professora2, "Bia", hoje.AddDays(-10), null, true)
            ]
        };
        var service = new AlunoService(repository, new StorageNotUsedFake());

        var result = await service.AlterarSomenteHorariosAsync(alunoId, professora1,
            new AlterarGradeHorariosRequest(hoje.AddDays(2),
            [new AlterarHorarioItemRequest(3, new(10, 0), new(11, 0), professora1)]), CancellationToken.None);

        Assert.Equal(2, repository.NovaGrade!.Count);
        Assert.Contains(repository.NovaGrade, h => h.ProfessoraId == professora2 && h.DiaSemana == 2 && h.HoraInicio == new TimeOnly(16, 0));
        Assert.Contains(repository.NovaGrade, h => h.ProfessoraId == professora1 && h.DiaSemana == 3 && h.HoraInicio == new TimeOnly(10, 0));
        Assert.Single(result.Programados);
        Assert.Equal(professora1, result.Programados[0].ProfessoraId);
    }

    [Fact]
    public async Task Horarios_ProfessoraNaoPodeAlterarHorarioDeOutraProfessora()
    {
        var alunoId = Guid.NewGuid();
        var professora1 = Guid.NewGuid();
        var professora2 = Guid.NewGuid();
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var repo = new AlunoRepositoryFake(alunoId, professora1) { ProfessoraExtra = professora2,
            Horarios = [
                new HorarioRecorrenteAlunoResponse(Guid.NewGuid(), 1, new(8, 0), new(9, 0), professora1, "Tati", hoje.AddDays(-5), null, true),
                new HorarioRecorrenteAlunoResponse(Guid.NewGuid(), 2, new(10, 0), new(11, 0), professora2, "Bia", hoje.AddDays(-5), null, true)
            ] };
        var service = new AlunoService(repo, new StorageNotUsedFake());

        await Assert.ThrowsAsync<AlunoValidationException>(() => service.AlterarSomenteHorariosAsync(
            alunoId, professora1,
            new AlterarGradeHorariosRequest(hoje.AddDays(1), [
                new AlterarHorarioItemRequest(3, new(8, 0), new(9, 0), professora2)
            ]), CancellationToken.None));
        Assert.Null(repo.NovaGrade);
    }

    [Fact]
    public async Task Horarios_BloqueiaMudancaDaCargaHorariaOuQuantidadeDeAulas()
    {
        var alunoId = Guid.NewGuid();
        var professoraId = Guid.NewGuid();
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var repo = new AlunoRepositoryFake(alunoId, professoraId)
        {
            Horarios = [new HorarioRecorrenteAlunoResponse(Guid.NewGuid(), 1, new(8, 0), new(9, 0), professoraId, "Tati", hoje.AddDays(-7), null, true)]
        };
        var service = new AlunoService(repo, new StorageNotUsedFake());
        await Assert.ThrowsAsync<AlunoValidationException>(() => service.AlterarSomenteHorariosAsync(
            alunoId, professoraId, new AlterarGradeHorariosRequest(hoje.AddDays(1),
            [new AlterarHorarioItemRequest(1, new(8, 0), new(10, 0), professoraId)]), CancellationToken.None));
        await Assert.ThrowsAsync<AlunoValidationException>(() => service.AlterarSomenteHorariosAsync(
            alunoId, professoraId, new AlterarGradeHorariosRequest(hoje.AddDays(1), []), CancellationToken.None));
        Assert.Null(repo.NovaGrade);
    }

    [Fact]
    public async Task Horarios_BloqueiaMudancaQueDesfazAulaEmDupla()
    {
        var alunoId = Guid.NewGuid();
        var professoraId = Guid.NewGuid();
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var repo = new AlunoRepositoryFake(alunoId, professoraId)
        {
            HorarioCompartilhado = true,
            Horarios = [new HorarioRecorrenteAlunoResponse(Guid.NewGuid(), 1, new(8, 0), new(9, 0), professoraId, "Tati", hoje.AddDays(-7), null, true)]
        };
        var service = new AlunoService(repo, new StorageNotUsedFake());
        var error = await Assert.ThrowsAsync<AlunoValidationException>(() => service.AlterarSomenteHorariosAsync(
            alunoId, professoraId, new AlterarGradeHorariosRequest(hoje.AddDays(1),
            [new AlterarHorarioItemRequest(3, new(8, 0), new(9, 0), professoraId)]), CancellationToken.None));
        Assert.Contains("dupla ou grupo", error.Message);
        Assert.Null(repo.NovaGrade);
    }

    [Fact]
    public async Task Horarios_ProfessoraSemVinculoNaoPodeConsultarNemEditarAluno()
    {
        var alunoId = Guid.NewGuid();
        var professora1 = Guid.NewGuid();
        var professoraSemVinculo = Guid.NewGuid();
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var repo = new AlunoRepositoryFake(alunoId, professora1)
        {
            Horarios = [new HorarioRecorrenteAlunoResponse(Guid.NewGuid(), 1, new(8, 0), new(9, 0), professora1, "Tati", hoje.AddDays(-7), null, true)]
        };
        var service = new AlunoService(repo, new StorageNotUsedFake());
        await Assert.ThrowsAsync<AlunoAcessoNegadoException>(() =>
            service.BuscarAgendaGerenciavelAsync(alunoId, professoraSemVinculo, CancellationToken.None));
        await Assert.ThrowsAsync<AlunoAcessoNegadoException>(() => service.AlterarSomenteHorariosAsync(
            alunoId, professoraSemVinculo, new AlterarGradeHorariosRequest(hoje.AddDays(1),
            [new AlterarHorarioItemRequest(1, new(9, 0), new(10, 0), professoraSemVinculo)]), CancellationToken.None));
    }

    [Fact]
    public async Task Horarios_ExigeInicioFuturo()
    {
        var alunoId = Guid.NewGuid();
        var professoraId = Guid.NewGuid();
        var repo = new AlunoRepositoryFake(alunoId, professoraId);
        var service = new AlunoService(repo, new StorageNotUsedFake());
        await Assert.ThrowsAsync<AlunoValidationException>(() => service.AlterarSomenteHorariosAsync(
            alunoId, professoraId, new AlterarGradeHorariosRequest(DateOnly.FromDateTime(DateTime.Today),
            [new AlterarHorarioItemRequest(1, new(8, 0), new(9, 0), professoraId)]), CancellationToken.None));
    }

    private static AtualizarAlunoRequest CriarRequestBase() => new(
        Nome: "Student Updated",
        DataNascimento: new DateOnly(2010, 2, 3),
        Genero: "Feminino",
        Email: "student@example.com",
        Telefone: "11999999999",
        ResponsavelNome: "Guardian",
        ResponsavelTelefone: "11888888888",
        Status: "Ativo",
        ValorMensalidade: 450m,
        DiaVencimento: 12,
        FormaPagamento: "PIX",
        TaxaMatricula: 100m,
        PercentualDesconto: 5m,
        Observacoes: "Notes");

    [Fact]
    public async Task HorariosPaginados_RespeitaFiltroEContagemDaPagina()
    {
        var professora = Guid.NewGuid();
        var repo = new AlunoRepositoryFake(Guid.NewGuid(), professora);
        var service = new AlunoService(repo, new StorageNotUsedFake());
        var resposta = await service.ListarHorariosPaginadosAsync(null, professora, "  Maria  ", 3, 25, CancellationToken.None);
        Assert.Equal(3, resposta.Pagina);
        Assert.Equal(25, resposta.TamanhoPagina);
        Assert.Equal(73, resposta.Total);
        Assert.Equal(3, resposta.TotalPaginas);
        Assert.Equal(professora, repo.UltimoFiltroProfessora);
        Assert.Null(repo.UltimoAcessoProfessora);
        Assert.Equal("Maria", repo.UltimaBusca);
    }

    [Fact]
    public async Task HorariosPaginados_ProfessoraNaoRecebeFiltroDeOutraProfessora()
    {
        var professora = Guid.NewGuid();
        var repo = new AlunoRepositoryFake(Guid.NewGuid(), professora);
        var service = new AlunoService(repo, new StorageNotUsedFake());
        await service.ListarHorariosPaginadosAsync(professora, Guid.NewGuid(), "", 0, 1000, CancellationToken.None);
        Assert.Equal(professora, repo.UltimoAcessoProfessora);
        Assert.Null(repo.UltimoFiltroProfessora);
        Assert.Equal(1, repo.UltimaPagina);
        Assert.Equal(10, repo.UltimoTamanhoPagina);
    }

    private sealed class AlunoRepositoryFake(Guid alunoId, Guid professoraId) : IAlunoRepository
    {
        public AtualizarAlunoRequest? AtualizacaoPersistida { get; private set; }
        public ConflitoAgendaAlunoResponse? Conflito { get; init; }
        public IReadOnlyList<HorarioRecorrenteAlunoResponse> Horarios { get; init; } = [];
        public Guid? UltimoAcessoProfessora { get; private set; }
        public Guid? UltimoFiltroProfessora { get; private set; }
        public string? UltimaBusca { get; private set; }
        public int UltimaPagina { get; private set; }
        public int UltimoTamanhoPagina { get; private set; }
        public Task<(IReadOnlyList<AlunoHorarioResumoResponse> Itens, int Total)> ListarAlunosHorariosPaginadoAsync(
            Guid? professoraAcessoId, Guid? professoraFiltroId, string? busca, int pagina,
            int tamanhoPagina, CancellationToken cancellationToken)
        {
            UltimoAcessoProfessora = professoraAcessoId;
            UltimoFiltroProfessora = professoraFiltroId;
            UltimaBusca = busca;
            UltimaPagina = pagina;
            UltimoTamanhoPagina = tamanhoPagina;
            return Task.FromResult<(IReadOnlyList<AlunoHorarioResumoResponse> Itens, int Total)>((Array.Empty<AlunoHorarioResumoResponse>(), 73));
        }

        public Guid? ProfessoraExtra { get; init; }
        public bool HorarioCompartilhado { get; init; }
        public Task<bool> ExisteAulaCompartilhadaAsync(Guid id, Guid idProfessora,
            short dia, TimeOnly horaInicio, TimeOnly horaFim, DateOnly data,
            CancellationToken cancellationToken) => Task.FromResult(HorarioCompartilhado);

        public IReadOnlyCollection<HorarioRecorrenteAlunoRequest>? NovaGrade { get; private set; }
        private IReadOnlyList<HorarioRecorrenteAlunoResponse>? novosHorarios;

        public Task<bool> PossuiHorarioAtivoAsync(Guid id, Guid professora, CancellationToken cancellationToken) =>
            Task.FromResult(id == alunoId && (novosHorarios ?? Horarios).Any(h => h.ProfessoraId == professora && h.Ativo &&
                (!h.DataFim.HasValue || h.DataFim >= DateOnly.FromDateTime(DateTime.Today))));

        public Task<bool> AtualizarApenasAgendaAsync(Guid id, DateOnly vigenteDesde,
            IReadOnlyCollection<HorarioRecorrenteAlunoRequest> horarios,
            IReadOnlyCollection<Guid> versaoEsperada, CancellationToken cancellationToken)
        {
            if (id != alunoId) return Task.FromResult(false);
            NovaGrade = horarios.ToArray();
            novosHorarios = horarios.Select(h => new HorarioRecorrenteAlunoResponse(
                Guid.NewGuid(), h.DiaSemana, h.HoraInicio, h.HoraFim,
                h.ProfessoraId!.Value, "Professora", vigenteDesde, null, true)).ToArray();
            return Task.FromResult(true);
        }

        public Task<Aluno?> AtualizarAsync(Guid id, AtualizarAlunoRequest request, CancellationToken cancellationToken)
        {
            if (id != alunoId)
                return Task.FromResult<Aluno?>(null);

            AtualizacaoPersistida = request;
            return Task.FromResult<Aluno?>(CreateAluno());
        }

        public Task<bool> EmailExisteAsync(string email, Guid? ignorarAlunoId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<Aluno?> BuscarPorIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Aluno?>(id == alunoId ? CreateAluno() : null);
        public Task<IReadOnlyList<HorarioRecorrenteAlunoResponse>> ListarHorariosRecorrentesAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(id == alunoId ? (novosHorarios ?? Horarios) : (IReadOnlyList<HorarioRecorrenteAlunoResponse>)[]);
        public Task<ConflitoAgendaAlunoResponse?> BuscarConflitoHorarioAsync(Guid? ignorarAlunoId, Guid idProfessora, short diaSemana, TimeOnly horaInicio, TimeOnly horaFim, DateOnly dataInicio, CancellationToken cancellationToken) =>
            Task.FromResult(Conflito);
        public Task<bool> ProfessoraExisteAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(id == professoraId || id == ProfessoraExtra);
        public Task<IReadOnlyList<Aluno>> ListarAsync(Guid? id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(IReadOnlyList<Aluno> Itens, int Total)> ListarPaginadoAsync(Guid? professoraAcessoId, string? busca, Guid? professoraFiltroId, string? status, short? diaSemana, int pagina, int tamanhoPagina, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AlunoExportacaoResponse>> ListarExportacaoAsync(Guid? professoraAcessoId, string? busca, Guid? professoraFiltroId, string? status, short? diaSemana, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Aluno> CadastrarAsync(CadastrarAlunoRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ExcluirAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> AtualizarFotoUrlAsync(Guid id, string fotoUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProfessoraResumoResponse>> ListarProfessorasAtivasAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        private Aluno CreateAluno() => new()
        {
            Id = alunoId,
            Nome = AtualizacaoPersistida?.Nome ?? "Student Original",
            DataNascimento = AtualizacaoPersistida?.DataNascimento,
            Genero = AtualizacaoPersistida?.Genero,
            Email = AtualizacaoPersistida?.Email,
            Telefone = AtualizacaoPersistida?.Telefone,
            ResponsavelNome = AtualizacaoPersistida?.ResponsavelNome,
            ResponsavelTelefone = AtualizacaoPersistida?.ResponsavelTelefone,
            Status = AtualizacaoPersistida?.Status ?? "Ativo",
            ProfessoraId = professoraId,
            ProfessoraNome = "Teacher",
            ProfessoraFotoUrl = "https://example.test/teacher.png",
            ValorMensalidade = AtualizacaoPersistida?.ValorMensalidade,
            DiaVencimento = AtualizacaoPersistida?.DiaVencimento,
            FormaPagamento = AtualizacaoPersistida?.FormaPagamento,
            TaxaMatricula = AtualizacaoPersistida?.TaxaMatricula ?? 0,
            PercentualDesconto = AtualizacaoPersistida?.PercentualDesconto ?? 0,
            Observacoes = AtualizacaoPersistida?.Observacoes,
            FotoUrl = "https://example.test/original-student.png",
            Ativo = true,
            CriadoEm = DateTimeOffset.UtcNow,
            AtualizadoEm = DateTimeOffset.UtcNow
        };
    }

    private sealed class StorageNotUsedFake : IImagemStorageGateway
    {
        public Task<string> SalvarFotoPerfilAsync(string categoria, Guid entidadeId, Stream conteudo, string contentType, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
