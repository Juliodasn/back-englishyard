using EnglishYard.Application.Autenticacao;
using EnglishYard.Application.Imagens;
using EnglishYard.Application.Professoras;
using EnglishYard.Domain.Entities;

namespace EnglishYard.Tests;

public sealed class ProfessoraServiceRecriacaoTests
{
    [Fact]
    public async Task CadastrarAsync_RemoveAuthVinculadoAoPerfilInativoAntesDeRecriar()
    {
        var authAntigoId = Guid.NewGuid();
        var authNovoId = Guid.NewGuid();
        var repository = new RepositoryFake(authAntigoId, emailAtivoExiste: false);
        var auth = new AuthFake(authNovoId);
        var service = new ProfessoraService(repository, auth, new StorageFake());

        var response = await service.CadastrarAsync(CriarRequest(" PROFESSORA@EXAMPLE.COM "), CancellationToken.None);

        Assert.Equal(["delete", "create"], auth.Operacoes);
        Assert.Equal(authAntigoId, auth.UsuarioExcluidoId);
        Assert.Equal("professora@example.com", auth.EmailCriado);
        Assert.Equal(authNovoId, repository.UsuarioAuthCadastradoId);
        Assert.Equal("professora@example.com", response.Email);
    }

    [Fact]
    public async Task CadastrarAsync_MantemBloqueioQuandoJaExisteProfessoraAtiva()
    {
        var repository = new RepositoryFake(Guid.NewGuid(), emailAtivoExiste: true);
        var auth = new AuthFake(Guid.NewGuid());
        var service = new ProfessoraService(repository, auth, new StorageFake());

        await Assert.ThrowsAsync<ProfessoraConflitoException>(() =>
            service.CadastrarAsync(CriarRequest("professora@example.com"), CancellationToken.None));

        Assert.Empty(auth.Operacoes);
    }

    private static CadastrarProfessoraRequest CriarRequest(string email) => new(
        "Professora Teste", null, null, null, email, "Senha123", "11999999999",
        "Ativa", "Por aula", 10, "E-mail", email.Trim(), null, null,
        100m, 80m, new DateOnly(2026, 8, 11));

    private sealed class RepositoryFake(Guid? authInativoId, bool emailAtivoExiste) : IProfessoraRepository
    {
        public Guid? UsuarioAuthCadastradoId { get; private set; }

        public Task<bool> EmailExisteAsync(string email, CancellationToken cancellationToken) =>
            Task.FromResult(emailAtivoExiste);

        public Task<Guid?> ObterUsuarioAuthIdInativoPorEmailAsync(string email, CancellationToken cancellationToken) =>
            Task.FromResult(authInativoId);

        public Task<Professora> CadastrarAsync(CadastrarProfessoraRequest request, Guid usuarioAuthId, CancellationToken cancellationToken)
        {
            UsuarioAuthCadastradoId = usuarioAuthId;
            return Task.FromResult(new Professora
            {
                Id = Guid.NewGuid(),
                Nome = request.Nome,
                Email = request.Email!,
                Telefone = request.Telefone,
                Status = request.Status,
                ModeloPagamento = request.ModeloPagamento,
                DiaPagamento = request.DiaPagamento,
                TipoChavePix = request.TipoChavePix,
                ChavePix = request.ChavePix,
                Ativo = true,
                ValorAulaIndividual = request.ValorAulaIndividual,
                ValorAulaGrupo = request.ValorAulaGrupo,
                VigenteDesde = request.VigenteDesde,
                CriadoEm = DateTimeOffset.UtcNow,
                AtualizadoEm = DateTimeOffset.UtcNow
            });
        }

        public Task<IReadOnlyList<Professora>> ListarAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(IReadOnlyList<Professora> Itens, int Total)> ListarPaginadoAsync(string? busca, string? status, int pagina, int tamanhoPagina, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Professora>> ListarExportacaoAsync(string? busca, string? status, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ValorAulaProfessoraHistoricoResponse>> ListarHistoricoValoresAsync(Guid professoraId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Professora?> BuscarPorIdAsync(Guid professoraId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Professora?> AtualizarAsync(Guid professoraId, AtualizarProfessoraRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Professora?> AtualizarPerfilProprioAsync(Guid professoraId, string? nomeProfissional, string telefone, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ExcluirAsync(Guid professoraId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> AtualizarFotoUrlAsync(Guid professoraId, string fotoUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(string Nome, string Email)?> ObterDadosParaAcessoAsync(Guid professoraId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> PossuiPerfilAcessoAtivoAsync(Guid professoraId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CriarPerfilAcessoAsync(Guid professoraId, Guid usuarioAuthId, string nome, string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid?> ObterUsuarioAuthIdAsync(Guid professoraId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class AuthFake(Guid novoId) : ISupabaseAuthAdminGateway
    {
        public List<string> Operacoes { get; } = [];
        public Guid? UsuarioExcluidoId { get; private set; }
        public string? EmailCriado { get; private set; }

        public Task ExcluirUsuarioAsync(Guid usuarioAuthId, CancellationToken cancellationToken)
        {
            Operacoes.Add("delete");
            UsuarioExcluidoId = usuarioAuthId;
            return Task.CompletedTask;
        }

        public Task<Guid> CriarUsuarioAsync(string email, string senha, string nome, CancellationToken cancellationToken)
        {
            Operacoes.Add("create");
            EmailCriado = email;
            return Task.FromResult(novoId);
        }

        public Task AlterarSenhaUsuarioAtualAsync(string accessToken, string novaSenha, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StorageFake : IImagemStorageGateway
    {
        public Task<string> SalvarFotoPerfilAsync(string categoria, Guid entidadeId, Stream conteudo, string contentType, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
