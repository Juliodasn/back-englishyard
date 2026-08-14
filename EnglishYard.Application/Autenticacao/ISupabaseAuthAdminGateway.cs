namespace EnglishYard.Application.Autenticacao;

public interface ISupabaseAuthAdminGateway
{
    Task<Guid> CriarUsuarioAsync(string email, string senha, string nome, CancellationToken cancellationToken);
    Task ExcluirUsuarioAsync(Guid usuarioAuthId, CancellationToken cancellationToken);
    Task AlterarSenhaUsuarioAtualAsync(string accessToken, string novaSenha, CancellationToken cancellationToken);
}

public sealed class SupabaseAuthConflitoException(string message) : Exception(message);
public sealed class SupabaseAuthConfigurationException(string message) : Exception(message);
public sealed class SupabaseAuthException(string message) : Exception(message);
