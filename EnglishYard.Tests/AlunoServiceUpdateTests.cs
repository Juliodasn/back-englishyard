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
    public async Task AtualizarAsync_PermiteMesmoIntervaloExatoParaAulaEmGrupo()
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

    private sealed class AlunoRepositoryFake(Guid alunoId, Guid professoraId) : IAlunoRepository
    {
        public AtualizarAlunoRequest? AtualizacaoPersistida { get; private set; }
        public ConflitoAgendaAlunoResponse? Conflito { get; init; }
        public IReadOnlyList<HorarioRecorrenteAlunoResponse> Horarios { get; init; } = [];

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
            Task.FromResult(id == alunoId ? Horarios : (IReadOnlyList<HorarioRecorrenteAlunoResponse>)[]);
        public Task<ConflitoAgendaAlunoResponse?> BuscarConflitoHorarioAsync(Guid? ignorarAlunoId, Guid idProfessora, short diaSemana, TimeOnly horaInicio, TimeOnly horaFim, DateOnly dataInicio, CancellationToken cancellationToken) =>
            Task.FromResult(Conflito);
        public Task<bool> ProfessoraExisteAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(id == professoraId);
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
