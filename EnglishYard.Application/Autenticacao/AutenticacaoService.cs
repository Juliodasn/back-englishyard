namespace EnglishYard.Application.Autenticacao;

public sealed class AutenticacaoService(
    IPerfilUsuarioRepository repository,
    ISupabaseAuthAdminGateway authAdminGateway)
{
    public async Task<PerfilUsuarioResponse?> ObterPerfilAsync(Guid usuarioAuthId, CancellationToken cancellationToken)
    {
        var profile = await repository.BuscarPorUsuarioAuthIdAsync(usuarioAuthId, cancellationToken);
        if (profile is null || !profile.Ativo)
            return null;

        await repository.RegistrarAcessoAsync(usuarioAuthId, cancellationToken);
        return PerfilUsuarioResponse.FromProfile(profile);
    }

    public async Task AlterarSenhaAsync(
        Guid usuarioAuthId,
        string accessToken,
        AlterarSenhaPortalRequest request,
        CancellationToken cancellationToken)
    {
        var novaSenha = request.NovaSenha ?? string.Empty;
        var senhaError = SenhaPortalValidator.ObterErro(novaSenha);
        if (senhaError is not null)
            throw new AutenticacaoValidationException(senhaError);

        if (string.IsNullOrWhiteSpace(accessToken))
            throw new AutenticacaoValidationException("A sessão atual não possui um token válido para alterar a senha.");

        await authAdminGateway.AlterarSenhaUsuarioAtualAsync(accessToken, novaSenha, cancellationToken);

        var atualizado = await repository.ConfirmarPrimeiroAcessoAsync(usuarioAuthId, cancellationToken);
        if (!atualizado)
            throw new AutenticacaoValidationException("O perfil do usuário não está ativo no portal.");
    }

    public async Task<PerfilUsuarioResponse> CriarAdministradorInicialAsync(
        BootstrapAdministradorRequest request,
        CancellationToken cancellationToken)
    {
        var nome = request.Nome?.Trim() ?? string.Empty;
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var senha = request.SenhaInicial ?? string.Empty;

        if (await repository.ExisteAdministradorAtivoAsync(cancellationToken))
            throw new BootstrapAdministradorConflitoException("Já existe um administrador ativo. O bootstrap inicial foi desabilitado automaticamente.");

        if (await repository.EmailAtivoExisteAsync(email, cancellationToken))
            throw new BootstrapAdministradorConflitoException("Já existe um perfil ativo usando este e-mail.");

        if (string.IsNullOrWhiteSpace(nome))
            throw new BootstrapAdministradorValidationException("Informe o nome do administrador.");

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            throw new BootstrapAdministradorValidationException("Informe um e-mail válido para o administrador.");

        var senhaError = SenhaPortalValidator.ObterErro(senha);
        if (senhaError is not null)
            throw new BootstrapAdministradorValidationException(senhaError);

        Guid? usuarioAuthId = null;
        try
        {
            usuarioAuthId = await authAdminGateway.CriarUsuarioAsync(email, senha, nome, cancellationToken);
            await repository.CriarAdministradorAsync(usuarioAuthId.Value, nome, email, cancellationToken);

            var profile = await repository.BuscarPorUsuarioAuthIdAsync(usuarioAuthId.Value, cancellationToken)
                ?? throw new InvalidOperationException("O administrador foi criado, mas o perfil não pôde ser carregado.");

            return PerfilUsuarioResponse.FromProfile(profile);
        }
        catch
        {
            if (usuarioAuthId.HasValue)
            {
                try
                {
                    await authAdminGateway.ExcluirUsuarioAsync(usuarioAuthId.Value, cancellationToken);
                }
                catch
                {
                    // Compensação best-effort: preserva a exceção original.
                }
            }

            throw;
        }
    }

}

public sealed class AutenticacaoValidationException(string message) : Exception(message);
public sealed class BootstrapAdministradorValidationException(string message) : Exception(message);
public sealed class BootstrapAdministradorConflitoException(string message) : Exception(message);
