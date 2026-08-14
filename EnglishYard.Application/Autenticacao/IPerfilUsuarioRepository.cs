namespace EnglishYard.Application.Autenticacao;

public interface IPerfilUsuarioRepository
{
    Task<PerfilUsuarioPortal?> BuscarPorUsuarioAuthIdAsync(Guid usuarioAuthId, CancellationToken cancellationToken);
    Task<bool> ExisteAdministradorAtivoAsync(CancellationToken cancellationToken);
    Task<bool> EmailAtivoExisteAsync(string email, CancellationToken cancellationToken);
    Task CriarAdministradorAsync(Guid usuarioAuthId, string nome, string email, CancellationToken cancellationToken);
    Task<bool> ConfirmarPrimeiroAcessoAsync(Guid usuarioAuthId, CancellationToken cancellationToken);
    Task RegistrarAcessoAsync(Guid usuarioAuthId, CancellationToken cancellationToken);
}
