using EnglishYard.Application.Autenticacao;
using EnglishYard.Application.Imagens;
using EnglishYard.Application.Professoras;
using EnglishYard.Domain.Entities;

namespace EnglishYard.Tests;

public sealed class ProfessoraServiceFotoTests
{
    [Fact]
    public async Task AtualizarAsync_NormalizaPersisteEDevolveCadastroAtualizado()
    {
        var professoraId = Guid.NewGuid();
        var repository = new ProfessoraRepositoryFake(professoraId);
        var service = new ProfessoraService(repository, new AuthAdminFake(), new ImagemStorageFake("unused"));

        var response = await service.AtualizarAsync(professoraId, new AtualizarProfessoraRequest(
            "  Teacher Updated  ", "  Teacher Pro  ", new DateOnly(1990, 1, 2), " DOC-1 ",
            " 11999999999 ", "Ativa", "Por aula", 12, "E-mail", " teacher@example.com ",
            " Bank ", " Notes ", 120m, 80m, new DateOnly(2026, 8, 10)), CancellationToken.None);

        Assert.NotNull(repository.AtualizacaoPersistida);
        Assert.Equal("Teacher Updated", repository.AtualizacaoPersistida.Nome);
        Assert.Equal("Teacher Pro", repository.AtualizacaoPersistida.NomeProfissional);
        Assert.Equal("teacher@example.com", repository.AtualizacaoPersistida.ChavePix);
        Assert.Equal("Teacher Updated", response.Nome);
        Assert.Equal("https://example.test/original-teacher.png", response.FotoUrl);
    }

    [Fact]
    public async Task AtualizarPerfilProprioAsync_AlteraSomenteNomeProfissionalETelefone()
    {
        var professoraId = Guid.NewGuid();
        var repository = new ProfessoraRepositoryFake(professoraId);
        var service = new ProfessoraService(repository, new AuthAdminFake(), new ImagemStorageFake("unused"));

        var response = await service.AtualizarPerfilProprioAsync(
            professoraId,
            new AtualizarMeuPerfilProfessoraRequest("  Teacher Pro  ", " 11911112222 "),
            CancellationToken.None);

        Assert.Equal("Teacher Pro", repository.NomeProfissionalProprioPersistido);
        Assert.Equal("11911112222", repository.TelefoneProprioPersistido);
        Assert.Equal("Teacher Pro", response.NomeProfissional);
        Assert.Equal("11911112222", response.Telefone);
        Assert.Equal("teacher@example.com", response.Email);
    }

    [Fact]
    public async Task AtualizarFotoAsync_EnviaAoStorage_PersisteUrl_EDevolveCadastroAtualizado()
    {
        var professoraId = Guid.NewGuid();
        const string publicUrl = "https://project.supabase.co/storage/v1/object/public/english-yard-fotos/professoras/id/perfil.png?v=1";
        var repository = new ProfessoraRepositoryFake(professoraId);
        var storage = new ImagemStorageFake(publicUrl);
        var service = new ProfessoraService(repository, new AuthAdminFake(), storage);
        await using var image = new MemoryStream([1, 2, 3]);

        var response = await service.AtualizarFotoAsync(
            professoraId,
            image,
            "image/png",
            image.Length,
            CancellationToken.None);

        Assert.Equal("professoras", storage.CategoriaRecebida);
        Assert.Equal(professoraId, storage.EntidadeIdRecebida);
        Assert.Equal(publicUrl, repository.FotoUrlPersistida);
        Assert.Equal(publicUrl, response.FotoUrl);
    }

    private sealed class ImagemStorageFake(string publicUrl) : IImagemStorageGateway
    {
        public string? CategoriaRecebida { get; private set; }
        public Guid EntidadeIdRecebida { get; private set; }

        public Task<string> SalvarFotoPerfilAsync(
            string categoria,
            Guid entidadeId,
            Stream conteudo,
            string contentType,
            CancellationToken cancellationToken)
        {
            CategoriaRecebida = categoria;
            EntidadeIdRecebida = entidadeId;
            return Task.FromResult(publicUrl);
        }
    }

    private sealed class ProfessoraRepositoryFake(Guid professoraId) : IProfessoraRepository
    {
        public string? FotoUrlPersistida { get; private set; }
        public AtualizarProfessoraRequest? AtualizacaoPersistida { get; private set; }
        public string? NomeProfissionalProprioPersistido { get; private set; }
        public string? TelefoneProprioPersistido { get; private set; }

        public Task<IReadOnlyList<ValorAulaProfessoraHistoricoResponse>> ListarHistoricoValoresAsync(Guid professoraId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Professora?> BuscarPorIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<Professora?>(id == professoraId ? CreateProfessora(FotoUrlPersistida) : null);

        public Task<Professora?> AtualizarPerfilProprioAsync(Guid id, string? nomeProfissional, string telefone, CancellationToken cancellationToken)
        {
            if (id != professoraId)
                return Task.FromResult<Professora?>(null);

            NomeProfissionalProprioPersistido = nomeProfissional;
            TelefoneProprioPersistido = telefone;
            return Task.FromResult<Professora?>(CreateProfessora(FotoUrlPersistida ?? "https://example.test/original-teacher.png"));
        }

        public Task<bool> AtualizarFotoUrlAsync(Guid id, string fotoUrl, CancellationToken cancellationToken)
        {
            if (id != professoraId)
                return Task.FromResult(false);

            FotoUrlPersistida = fotoUrl;
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<Professora>> ListarAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(IReadOnlyList<Professora> Itens, int Total)> ListarPaginadoAsync(string? busca, string? status, int pagina, int tamanhoPagina, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Professora>> ListarExportacaoAsync(string? busca, string? status, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Professora> CadastrarAsync(CadastrarProfessoraRequest request, Guid usuarioAuthId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Professora?> AtualizarAsync(Guid id, AtualizarProfessoraRequest request, CancellationToken cancellationToken)
        {
            if (id != professoraId)
                return Task.FromResult<Professora?>(null);

            AtualizacaoPersistida = request;
            return Task.FromResult<Professora?>(CreateProfessora(FotoUrlPersistida ?? "https://example.test/original-teacher.png"));
        }
        public Task<bool> ExcluirAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(string Nome, string Email)?> ObterDadosParaAcessoAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> PossuiPerfilAcessoAtivoAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CriarPerfilAcessoAsync(Guid id, Guid usuarioAuthId, string nome, string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid?> ObterUsuarioAuthIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid?> ObterUsuarioAuthIdInativoPorEmailAsync(string email, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);
        public Task<bool> EmailExisteAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();

        private Professora CreateProfessora(string? fotoUrl) => new()
        {
            Id = professoraId,
            Nome = AtualizacaoPersistida?.Nome ?? "Teacher Test",
            NomeProfissional = NomeProfissionalProprioPersistido ?? AtualizacaoPersistida?.NomeProfissional,
            DataNascimento = AtualizacaoPersistida?.DataNascimento,
            DocumentoIdentidade = AtualizacaoPersistida?.DocumentoIdentidade,
            Email = "teacher@example.com",
            Telefone = TelefoneProprioPersistido ?? AtualizacaoPersistida?.Telefone,
            Status = AtualizacaoPersistida?.Status ?? "Ativa",
            ModeloPagamento = AtualizacaoPersistida?.ModeloPagamento ?? "Por aula",
            DiaPagamento = AtualizacaoPersistida?.DiaPagamento,
            TipoChavePix = AtualizacaoPersistida?.TipoChavePix,
            ChavePix = AtualizacaoPersistida?.ChavePix,
            Banco = AtualizacaoPersistida?.Banco,
            Observacoes = AtualizacaoPersistida?.Observacoes,
            FotoUrl = fotoUrl,
            Ativo = true,
            ValorAulaIndividual = AtualizacaoPersistida?.ValorAulaIndividual ?? 0,
            ValorAulaGrupo = AtualizacaoPersistida?.ValorAulaGrupo ?? 0,
            VigenteDesde = AtualizacaoPersistida?.VigenteDesde,
            CriadoEm = DateTimeOffset.UtcNow,
            AtualizadoEm = DateTimeOffset.UtcNow
        };
    }

    private sealed class AuthAdminFake : ISupabaseAuthAdminGateway
    {
        public Task<Guid> CriarUsuarioAsync(string email, string senha, string nome, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ExcluirUsuarioAsync(Guid usuarioAuthId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AlterarSenhaUsuarioAtualAsync(string accessToken, string novaSenha, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
