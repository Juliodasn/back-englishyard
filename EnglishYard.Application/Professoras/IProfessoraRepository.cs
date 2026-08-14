using EnglishYard.Domain.Entities;

namespace EnglishYard.Application.Professoras;

public interface IProfessoraRepository
{
    Task<IReadOnlyList<Professora>> ListarAsync(CancellationToken cancellationToken);
    Task<(IReadOnlyList<Professora> Itens, int Total)> ListarPaginadoAsync(
        string? busca,
        string? status,
        int pagina,
        int tamanhoPagina,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<Professora>> ListarExportacaoAsync(
        string? busca,
        string? status,
        CancellationToken cancellationToken);
    Task<Professora?> BuscarPorIdAsync(Guid professoraId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ValorAulaProfessoraHistoricoResponse>> ListarHistoricoValoresAsync(Guid professoraId, CancellationToken cancellationToken);
    Task<Professora> CadastrarAsync(CadastrarProfessoraRequest request, Guid usuarioAuthId, CancellationToken cancellationToken);
    Task<Professora?> AtualizarAsync(Guid professoraId, AtualizarProfessoraRequest request, CancellationToken cancellationToken);
    Task<Professora?> AtualizarPerfilProprioAsync(
        Guid professoraId,
        string? nomeProfissional,
        string telefone,
        CancellationToken cancellationToken);
    Task<bool> ExcluirAsync(Guid professoraId, CancellationToken cancellationToken);
    Task<bool> AtualizarFotoUrlAsync(Guid professoraId, string fotoUrl, CancellationToken cancellationToken);
    Task<(string Nome, string Email)?> ObterDadosParaAcessoAsync(Guid professoraId, CancellationToken cancellationToken);
    Task<bool> PossuiPerfilAcessoAtivoAsync(Guid professoraId, CancellationToken cancellationToken);
    Task CriarPerfilAcessoAsync(Guid professoraId, Guid usuarioAuthId, string nome, string email, CancellationToken cancellationToken);
    Task<Guid?> ObterUsuarioAuthIdAsync(Guid professoraId, CancellationToken cancellationToken);
    Task<Guid?> ObterUsuarioAuthIdInativoPorEmailAsync(string email, CancellationToken cancellationToken);
    Task<bool> EmailExisteAsync(string email, CancellationToken cancellationToken);
}
