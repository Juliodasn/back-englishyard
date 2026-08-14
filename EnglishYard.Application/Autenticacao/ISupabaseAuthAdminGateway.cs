namespace EnglishYard.Application.Autenticacao;

public interface ISupabaseAuthAdminGateway
{
    Task<Guid> CriarUsuarioAsync(string email, string senha, string nome, CancellationToken cancellationToken);
    Task ExcluirUsuarioAsync(Guid usuarioAuthId, CancellationToken cancellationToken);
    Task AlterarSenhaUsuarioAtualAsync(string accessToken, string novaSenha, CancellationToken cancellationToken);
    Task RedefinirSenhaUsuarioAsync(Guid usuarioAuthId, string novaSenha, CancellationToken cancellationToken) => Task.CompletedTask;
    Task AlterarEmailUsuarioAsync(Guid usuarioAuthId, string novoEmail, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class SupabaseAuthConflitoException(string message) : Exception(message);
public sealed class SupabaseAuthConfigurationException(string message) : Exception(message);
public sealed class SupabaseAuthException(string message) : Exception(message);
