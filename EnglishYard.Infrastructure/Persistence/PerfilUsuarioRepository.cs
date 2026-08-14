using EnglishYard.Application.Autenticacao;
using Npgsql;

namespace EnglishYard.Infrastructure.Persistence;

public sealed class PerfilUsuarioRepository(NpgsqlDataSource dataSource) : IPerfilUsuarioRepository
{
    public async Task<PerfilUsuarioPortal?> BuscarPorUsuarioAuthIdAsync(Guid usuarioAuthId, CancellationToken cancellationToken)
    {
        const string sql = """
            select
                pu.usuario_auth_id,
                pu.nome,
                pu.email,
                pu.tipo_usuario,
                pu.professora_id,
                coalesce(nullif(p.foto_url, ''), nullif(pu.foto_url, '')) as foto_url,
                pu.ativo,
                pu.deve_alterar_senha,
                pu.ultimo_acesso_em,
                pu.sessoes_revogadas_antes_de
            from public.perfis_usuarios pu
            left join public.professoras p
              on p.id = pu.professora_id
             and p.ativo = true
            where pu.usuario_auth_id = @usuario_auth_id
              and pu.ativo = true
              and (pu.tipo_usuario <> 'Professora' or p.status in ('Ativa', 'Em onboarding', 'Em férias'))
            order by pu.atualizado_em desc, pu.criado_em desc
            limit 1;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("usuario_auth_id", usuarioAuthId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new PerfilUsuarioPortal(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetBoolean(6),
            reader.GetBoolean(7),
            reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
            reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9));
    }

    public async Task<bool> ExisteAdministradorAtivoAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            select exists (
                select 1
                from public.perfis_usuarios
                where tipo_usuario = 'Administrador'
                  and ativo = true
            );
            """;

        await using var command = dataSource.CreateCommand(sql);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<bool> EmailAtivoExisteAsync(string email, CancellationToken cancellationToken)
    {
        const string sql = "select exists(select 1 from public.perfis_usuarios where lower(email) = lower(@email) and ativo = true);";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("email", email);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task CriarAdministradorAsync(Guid usuarioAuthId, string nome, string email, CancellationToken cancellationToken)
    {
        const string sql = """
            insert into public.perfis_usuarios (
                usuario_auth_id,
                nome,
                email,
                tipo_usuario,
                professora_id,
                ativo,
                deve_alterar_senha
            ) values (
                @usuario_auth_id,
                @nome,
                @email,
                'Administrador',
                null,
                true,
                true
            );
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("usuario_auth_id", usuarioAuthId);
        command.Parameters.AddWithValue("nome", nome);
        command.Parameters.AddWithValue("email", email);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> ConfirmarPrimeiroAcessoAsync(Guid usuarioAuthId, CancellationToken cancellationToken)
    {
        const string sql = """
            update public.perfis_usuarios
            set deve_alterar_senha = false,
                atualizado_em = now()
            where usuario_auth_id = @usuario_auth_id
              and ativo = true;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("usuario_auth_id", usuarioAuthId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task RegistrarAcessoAsync(Guid usuarioAuthId, CancellationToken cancellationToken)
    {
        const string sql = """
            update public.perfis_usuarios
            set ultimo_acesso_em = now(),
                atualizado_em = now()
            where usuario_auth_id = @usuario_auth_id
              and ativo = true;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("usuario_auth_id", usuarioAuthId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
