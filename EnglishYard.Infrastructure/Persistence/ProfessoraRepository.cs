using EnglishYard.Application.Professoras;
using EnglishYard.Domain.Entities;
using Npgsql;
using NpgsqlTypes;

namespace EnglishYard.Infrastructure.Persistence;

public sealed class ProfessoraRepository(NpgsqlDataSource dataSource) : IProfessoraRepository
{
    public async Task<IReadOnlyList<Professora>> ListarAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            select
                p.id, p.nome, p.nome_profissional, p.data_nascimento, p.documento_identidade,
                p.email, p.telefone, p.status, p.modelo_pagamento, p.dia_pagamento,
                p.tipo_chave_pix, p.chave_pix, p.banco, p.observacoes, p.foto_url, p.ativo,
                coalesce(v.valor_aula_individual, 0) as valor_aula_individual,
                coalesce(v.valor_aula_grupo, 0) as valor_aula_grupo,
                v.vigente_desde,
                (select count(distinct a.id)::int
                   from public.alunos a
                  where a.ativo = true
                    and (
                      exists (
                        select 1 from public.horarios_recorrentes_alunos h
                        where h.aluno_id = a.id
                          and h.professora_id = p.id
                          and h.ativo = true
                          and h.data_inicio <= current_date
                          and (h.data_fim is null or h.data_fim >= current_date)
                      )
                      or (
                        a.professora_id = p.id
                        and not exists (
                          select 1 from public.horarios_recorrentes_alunos h_current
                          where h_current.aluno_id = a.id
                            and h_current.ativo = true
                            and h_current.data_inicio <= current_date
                            and (h_current.data_fim is null or h_current.data_fim >= current_date)
                        )
                      )
                    )) as quantidade_alunos,
                (select count(*)::int
                   from public.horarios_recorrentes_alunos h
                   join public.alunos a on a.id = h.aluno_id and a.ativo = true
                  where h.professora_id = p.id
                    and h.ativo = true
                    and h.data_inicio <= current_date
                    and (h.data_fim is null or h.data_fim >= current_date)) as quantidade_aulas,
                proxima.data_aula as proxima_aula_data,
                proxima.hora_inicio as proxima_aula_hora,
                exists (
                    select 1 from public.perfis_usuarios pu
                    where pu.professora_id = p.id and pu.ativo = true
                ) as possui_acesso,
                p.criado_em, p.atualizado_em
            from public.professoras p
            left join lateral (
                select valor_aula_individual, valor_aula_grupo, vigente_desde
                from public.valores_aula_professoras
                where professora_id = p.id
                order by vigente_desde desc
                limit 1
            ) v on true
            left join lateral (
                select data_aula, hora_inicio
                from public.aulas
                where professora_id = p.id
                  and status in ('agendada', 'em_andamento')
                  and (data_aula > current_date or (data_aula = current_date and hora_inicio >= localtime))
                order by data_aula, hora_inicio
                limit 1
            ) proxima on true
            where p.ativo = true
            order by p.nome;
            """;

        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var professoras = new List<Professora>();

        while (await reader.ReadAsync(cancellationToken))
            professoras.Add(MapProfessora(reader));

        return professoras;
    }


    public async Task<(IReadOnlyList<Professora> Itens, int Total)> ListarPaginadoAsync(
        string? busca,
        string? status,
        int pagina,
        int tamanhoPagina,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select
                p.id, p.nome, p.nome_profissional, p.data_nascimento, p.documento_identidade,
                p.email, p.telefone, p.status, p.modelo_pagamento, p.dia_pagamento,
                p.tipo_chave_pix, p.chave_pix, p.banco, p.observacoes, p.foto_url, p.ativo,
                coalesce(v.valor_aula_individual, 0) as valor_aula_individual,
                coalesce(v.valor_aula_grupo, 0) as valor_aula_grupo,
                v.vigente_desde,
                (select count(distinct a.id)::int
                   from public.alunos a
                  where a.ativo = true
                    and (
                      exists (
                        select 1 from public.horarios_recorrentes_alunos h
                        where h.aluno_id = a.id
                          and h.professora_id = p.id
                          and h.ativo = true
                          and h.data_inicio <= current_date
                          and (h.data_fim is null or h.data_fim >= current_date)
                      )
                      or (
                        a.professora_id = p.id
                        and not exists (
                          select 1 from public.horarios_recorrentes_alunos h_current
                          where h_current.aluno_id = a.id
                            and h_current.ativo = true
                            and h_current.data_inicio <= current_date
                            and (h_current.data_fim is null or h_current.data_fim >= current_date)
                        )
                      )
                    )) as quantidade_alunos,
                (select count(*)::int
                   from public.horarios_recorrentes_alunos h
                   join public.alunos a on a.id = h.aluno_id and a.ativo = true
                  where h.professora_id = p.id
                    and h.ativo = true
                    and h.data_inicio <= current_date
                    and (h.data_fim is null or h.data_fim >= current_date)) as quantidade_aulas,
                proxima.data_aula as proxima_aula_data,
                proxima.hora_inicio as proxima_aula_hora,
                exists (
                    select 1 from public.perfis_usuarios pu
                    where pu.professora_id = p.id and pu.ativo = true
                ) as possui_acesso,
                p.criado_em, p.atualizado_em,
                count(*) over()::int as total_count
            from public.professoras p
            left join lateral (
                select valor_aula_individual, valor_aula_grupo, vigente_desde
                from public.valores_aula_professoras
                where professora_id = p.id
                order by vigente_desde desc
                limit 1
            ) v on true
            left join lateral (
                select data_aula, hora_inicio
                from public.aulas
                where professora_id = p.id
                  and status in ('agendada', 'em_andamento')
                  and (data_aula > current_date or (data_aula = current_date and hora_inicio >= localtime))
                order by data_aula, hora_inicio
                limit 1
            ) proxima on true
            where p.ativo = true
              and (@status is null or p.status = @status)
              and (
                @busca is null
                or lower(p.nome) like @busca_like
                or lower(coalesce(p.nome_profissional, '')) like @busca_like
                or lower(p.email) like @busca_like
                or lower(coalesce(p.telefone, '')) like @busca_like
              )
            order by p.nome
            limit @tamanho_pagina offset @offset;
            """;

        await using var command = dataSource.CreateCommand(sql);
        AddTeacherListFilterParameters(command, busca, status);
        command.Parameters.AddWithValue("tamanho_pagina", tamanhoPagina);
        command.Parameters.AddWithValue("offset", (pagina - 1) * tamanhoPagina);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var professoras = new List<Professora>();
        var total = 0;

        while (await reader.ReadAsync(cancellationToken))
        {
            professoras.Add(MapProfessora(reader));
            total = reader.GetInt32(26);
        }

        return (professoras, total);
    }

    public async Task<IReadOnlyList<Professora>> ListarExportacaoAsync(
        string? busca,
        string? status,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select
                p.id, p.nome, p.nome_profissional, p.data_nascimento, p.documento_identidade,
                p.email, p.telefone, p.status, p.modelo_pagamento, p.dia_pagamento,
                p.tipo_chave_pix, p.chave_pix, p.banco, p.observacoes, p.foto_url, p.ativo,
                coalesce(v.valor_aula_individual, 0) as valor_aula_individual,
                coalesce(v.valor_aula_grupo, 0) as valor_aula_grupo,
                v.vigente_desde,
                (select count(distinct a.id)::int
                   from public.alunos a
                  where a.ativo = true
                    and (
                      exists (
                        select 1 from public.horarios_recorrentes_alunos h
                        where h.aluno_id = a.id
                          and h.professora_id = p.id
                          and h.ativo = true
                          and h.data_inicio <= current_date
                          and (h.data_fim is null or h.data_fim >= current_date)
                      )
                      or (
                        a.professora_id = p.id
                        and not exists (
                          select 1 from public.horarios_recorrentes_alunos h_current
                          where h_current.aluno_id = a.id
                            and h_current.ativo = true
                            and h_current.data_inicio <= current_date
                            and (h_current.data_fim is null or h_current.data_fim >= current_date)
                        )
                      )
                    )) as quantidade_alunos,
                (select count(*)::int
                   from public.horarios_recorrentes_alunos h
                   join public.alunos a on a.id = h.aluno_id and a.ativo = true
                  where h.professora_id = p.id
                    and h.ativo = true
                    and h.data_inicio <= current_date
                    and (h.data_fim is null or h.data_fim >= current_date)) as quantidade_aulas,
                proxima.data_aula as proxima_aula_data,
                proxima.hora_inicio as proxima_aula_hora,
                exists (
                    select 1 from public.perfis_usuarios pu
                    where pu.professora_id = p.id and pu.ativo = true
                ) as possui_acesso,
                p.criado_em, p.atualizado_em
            from public.professoras p
            left join lateral (
                select valor_aula_individual, valor_aula_grupo, vigente_desde
                from public.valores_aula_professoras
                where professora_id = p.id
                order by vigente_desde desc
                limit 1
            ) v on true
            left join lateral (
                select data_aula, hora_inicio
                from public.aulas
                where professora_id = p.id
                  and status in ('agendada', 'em_andamento')
                  and (data_aula > current_date or (data_aula = current_date and hora_inicio >= localtime))
                order by data_aula, hora_inicio
                limit 1
            ) proxima on true
            where p.ativo = true
              and (@status is null or p.status = @status)
              and (
                @busca is null
                or lower(p.nome) like @busca_like
                or lower(coalesce(p.nome_profissional, '')) like @busca_like
                or lower(p.email) like @busca_like
                or lower(coalesce(p.telefone, '')) like @busca_like
              )
            order by p.nome;
            """;

        await using var command = dataSource.CreateCommand(sql);
        AddTeacherListFilterParameters(command, busca, status);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var professoras = new List<Professora>();

        while (await reader.ReadAsync(cancellationToken))
            professoras.Add(MapProfessora(reader));

        return professoras;
    }

    public async Task<IReadOnlyList<ValorAulaProfessoraHistoricoResponse>> ListarHistoricoValoresAsync(
        Guid professoraId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select id, valor_aula_individual, valor_aula_grupo, vigente_desde, vigente_ate
            from public.valores_aula_professoras
            where professora_id = @professora_id
            order by vigente_desde desc;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("professora_id", professoraId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ValorAulaProfessoraHistoricoResponse>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ValorAulaProfessoraHistoricoResponse(
                reader.GetGuid(0),
                reader.GetDecimal(1),
                reader.GetDecimal(2),
                reader.GetFieldValue<DateOnly>(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4)));
        }
        return result;
    }

    public async Task<Professora> CadastrarAsync(CadastrarProfessoraRequest request, Guid usuarioAuthId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        Guid professoraId;

        try
        {
            const string insertProfessoraSql = """
                insert into public.professoras (
                    nome, nome_profissional, data_nascimento, documento_identidade,
                    email, telefone, status, modelo_pagamento,
                    dia_pagamento, tipo_chave_pix, chave_pix, banco, observacoes, ativo
                ) values (
                    @nome, @nome_profissional, @data_nascimento, @documento_identidade,
                    @email, @telefone, @status, @modelo_pagamento,
                    @dia_pagamento, @tipo_chave_pix, @chave_pix, @banco, @observacoes, true
                )
                returning id;
                """;

            await using var insertProfessora = new NpgsqlCommand(insertProfessoraSql, connection, transaction);
            AddParameter(insertProfessora, "nome", request.Nome);
            AddParameter(insertProfessora, "nome_profissional", request.NomeProfissional);
            AddParameter(insertProfessora, "data_nascimento", request.DataNascimento, NpgsqlDbType.Date);
            AddParameter(insertProfessora, "documento_identidade", request.DocumentoIdentidade);
            AddParameter(insertProfessora, "email", request.Email);
            AddParameter(insertProfessora, "telefone", request.Telefone);
            AddParameter(insertProfessora, "status", request.Status);
            AddParameter(insertProfessora, "modelo_pagamento", request.ModeloPagamento);
            AddParameter(insertProfessora, "dia_pagamento", request.DiaPagamento, NpgsqlDbType.Smallint);
            AddParameter(insertProfessora, "tipo_chave_pix", request.TipoChavePix);
            AddParameter(insertProfessora, "chave_pix", request.ChavePix);
            AddParameter(insertProfessora, "banco", request.Banco);
            AddParameter(insertProfessora, "observacoes", request.Observacoes);

            professoraId = (Guid)(await insertProfessora.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("O banco não retornou o identificador da professora."));

            const string insertValorSql = """
                insert into public.valores_aula_professoras (
                    professora_id, valor_aula_individual, valor_aula_grupo, vigente_desde
                ) values (
                    @professora_id, @valor_aula_individual, @valor_aula_grupo, @vigente_desde
                );
                """;

            await using (var insertValor = new NpgsqlCommand(insertValorSql, connection, transaction))
            {
                insertValor.Parameters.AddWithValue("professora_id", professoraId);
                insertValor.Parameters.AddWithValue("valor_aula_individual", NpgsqlDbType.Numeric, request.ValorAulaIndividual);
                insertValor.Parameters.AddWithValue("valor_aula_grupo", NpgsqlDbType.Numeric, request.ValorAulaGrupo);
                insertValor.Parameters.AddWithValue("vigente_desde", NpgsqlDbType.Date, request.VigenteDesde);
                await insertValor.ExecuteNonQueryAsync(cancellationToken);
            }

            const string insertPerfilSql = """
                insert into public.perfis_usuarios (
                    usuario_auth_id, nome, email, tipo_usuario, professora_id,
                    ativo, deve_alterar_senha
                ) values (
                    @usuario_auth_id, @nome, @email, 'Professora', @professora_id,
                    true, true
                );
                """;

            await using (var insertPerfil = new NpgsqlCommand(insertPerfilSql, connection, transaction))
            {
                insertPerfil.Parameters.AddWithValue("usuario_auth_id", usuarioAuthId);
                insertPerfil.Parameters.AddWithValue("nome", request.Nome);
                insertPerfil.Parameters.AddWithValue("email", request.Email!);
                insertPerfil.Parameters.AddWithValue("professora_id", professoraId);
                await insertPerfil.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation &&
            exception.ConstraintName == "ux_professoras_email_lower")
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ProfessoraConflitoException("Já existe uma professora ativa cadastrada com este e-mail.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return await BuscarPorIdAsync(professoraId, cancellationToken)
            ?? throw new InvalidOperationException("A professora foi criada, mas não pôde ser carregada em seguida.");
    }

    public async Task<Professora?> AtualizarAsync(
        Guid professoraId,
        AtualizarProfessoraRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            const string updateProfessoraSql = """
                update public.professoras
                set nome = @nome,
                    nome_profissional = @nome_profissional,
                    data_nascimento = @data_nascimento,
                    documento_identidade = @documento_identidade,
                    telefone = @telefone,
                    status = @status,
                    modelo_pagamento = @modelo_pagamento,
                    dia_pagamento = @dia_pagamento,
                    tipo_chave_pix = @tipo_chave_pix,
                    chave_pix = @chave_pix,
                    banco = @banco,
                    observacoes = @observacoes,
                    atualizado_em = now()
                where id = @id and ativo = true;
                """;

            await using (var updateProfessora = new NpgsqlCommand(updateProfessoraSql, connection, transaction))
            {
                updateProfessora.Parameters.AddWithValue("id", professoraId);
                AddParameter(updateProfessora, "nome", request.Nome);
                AddParameter(updateProfessora, "nome_profissional", request.NomeProfissional);
                AddParameter(updateProfessora, "data_nascimento", request.DataNascimento, NpgsqlDbType.Date);
                AddParameter(updateProfessora, "documento_identidade", request.DocumentoIdentidade);
                AddParameter(updateProfessora, "telefone", request.Telefone);
                AddParameter(updateProfessora, "status", request.Status);
                AddParameter(updateProfessora, "modelo_pagamento", request.ModeloPagamento);
                AddParameter(updateProfessora, "dia_pagamento", request.DiaPagamento, NpgsqlDbType.Smallint);
                AddParameter(updateProfessora, "tipo_chave_pix", request.TipoChavePix);
                AddParameter(updateProfessora, "chave_pix", request.ChavePix);
                AddParameter(updateProfessora, "banco", request.Banco);
                AddParameter(updateProfessora, "observacoes", request.Observacoes);

                if (await updateProfessora.ExecuteNonQueryAsync(cancellationToken) == 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return null;
                }
            }

            const string updatePerfilSql = """
                update public.perfis_usuarios
                set nome = @nome, atualizado_em = now()
                where professora_id = @professora_id and ativo = true;
                """;
            await using (var updatePerfil = new NpgsqlCommand(updatePerfilSql, connection, transaction))
            {
                updatePerfil.Parameters.AddWithValue("professora_id", professoraId);
                updatePerfil.Parameters.AddWithValue("nome", request.Nome);
                await updatePerfil.ExecuteNonQueryAsync(cancellationToken);
            }

            const string upsertValorSql = """
                insert into public.valores_aula_professoras (
                    professora_id, valor_aula_individual, valor_aula_grupo, vigente_desde
                ) values (
                    @professora_id, @valor_aula_individual, @valor_aula_grupo, @vigente_desde
                )
                on conflict (professora_id, vigente_desde) do update set
                    valor_aula_individual = excluded.valor_aula_individual,
                    valor_aula_grupo = excluded.valor_aula_grupo,
                    atualizado_em = now();
                """;
            await using (var upsertValor = new NpgsqlCommand(upsertValorSql, connection, transaction))
            {
                upsertValor.Parameters.AddWithValue("professora_id", professoraId);
                upsertValor.Parameters.AddWithValue("valor_aula_individual", NpgsqlDbType.Numeric, request.ValorAulaIndividual);
                upsertValor.Parameters.AddWithValue("valor_aula_grupo", NpgsqlDbType.Numeric, request.ValorAulaGrupo);
                upsertValor.Parameters.AddWithValue("vigente_desde", NpgsqlDbType.Date, request.VigenteDesde);
                await upsertValor.ExecuteNonQueryAsync(cancellationToken);
            }

            const string normalizeRatePeriodsSql = """
                with ordered as (
                    select id, lead(vigente_desde) over (order by vigente_desde) as proxima_vigencia
                    from public.valores_aula_professoras
                    where professora_id = @professora_id
                )
                update public.valores_aula_professoras v
                set vigente_ate = case
                    when o.proxima_vigencia is null then null
                    else o.proxima_vigencia - 1
                end,
                atualizado_em = now()
                from ordered o
                where v.id = o.id;
                """;
            await using (var normalizeRates = new NpgsqlCommand(normalizeRatePeriodsSql, connection, transaction))
            {
                normalizeRates.Parameters.AddWithValue("professora_id", professoraId);
                await normalizeRates.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return await BuscarPorIdAsync(professoraId, cancellationToken);
    }

    public async Task<Professora?> AtualizarPerfilProprioAsync(
        Guid professoraId,
        string? nomeProfissional,
        string telefone,
        CancellationToken cancellationToken)
    {
        const string sql = """
            update public.professoras
            set nome_profissional = @nome_profissional,
                telefone = @telefone,
                atualizado_em = now()
            where id = @id
              and ativo = true;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", professoraId);
        AddParameter(command, "nome_profissional", nomeProfissional);
        AddParameter(command, "telefone", telefone);

        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            return null;

        return await BuscarPorIdAsync(professoraId, cancellationToken);
    }

    public async Task<bool> AtualizarFotoUrlAsync(Guid professoraId, string fotoUrl, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            const string updateProfessoraSql = """
                update public.professoras
                set foto_url = @foto_url, atualizado_em = now()
                where id = @id and ativo = true;
                """;

            await using (var professora = new NpgsqlCommand(updateProfessoraSql, connection, transaction))
            {
                professora.Parameters.AddWithValue("id", professoraId);
                professora.Parameters.AddWithValue("foto_url", fotoUrl);
                if (await professora.ExecuteNonQueryAsync(cancellationToken) == 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return false;
                }
            }

            const string updatePerfilSql = """
                update public.perfis_usuarios
                set foto_url = @foto_url, atualizado_em = now()
                where professora_id = @professora_id;
                """;

            await using (var perfil = new NpgsqlCommand(updatePerfilSql, connection, transaction))
            {
                perfil.Parameters.AddWithValue("professora_id", professoraId);
                perfil.Parameters.AddWithValue("foto_url", fotoUrl);
                await perfil.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> ExcluirAsync(Guid professoraId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            const string existeSql = """
                select exists (
                    select 1 from public.professoras
                    where id = @id and ativo = true
                );
                """;
            await using (var existe = new NpgsqlCommand(existeSql, connection, transaction))
            {
                existe.Parameters.AddWithValue("id", professoraId);
                var ativa = (bool)(await existe.ExecuteScalarAsync(cancellationToken) ?? false);
                if (!ativa)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return false;
                }
            }

            const string encerrarHorariosAlunosSql = """
                update public.horarios_recorrentes_alunos
                set ativo = false,
                    data_fim = coalesce(data_fim, greatest(data_inicio, current_date)),
                    atualizado_em = now()
                where professora_id = @professora_id
                  and ativo = true;
                """;
            await using (var horarios = new NpgsqlCommand(encerrarHorariosAlunosSql, connection, transaction))
            {
                horarios.Parameters.AddWithValue("professora_id", professoraId);
                await horarios.ExecuteNonQueryAsync(cancellationToken);
            }

            const string desvincularAlunosSql = """
                update public.alunos
                set professora_id = null, atualizado_em = now()
                where professora_id = @professora_id;
                """;
            await using (var alunos = new NpgsqlCommand(desvincularAlunosSql, connection, transaction))
            {
                alunos.Parameters.AddWithValue("professora_id", professoraId);
                await alunos.ExecuteNonQueryAsync(cancellationToken);
            }

            const string desativarPerfilSql = """
                update public.perfis_usuarios
                set ativo = false, atualizado_em = now()
                where professora_id = @professora_id
                  and ativo = true;
                """;
            await using (var perfil = new NpgsqlCommand(desativarPerfilSql, connection, transaction))
            {
                perfil.Parameters.AddWithValue("professora_id", professoraId);
                await perfil.ExecuteNonQueryAsync(cancellationToken);
            }

            const string excluirSql = """
                update public.professoras
                set ativo = false, status = 'Pausada', atualizado_em = now()
                where id = @id and ativo = true;
                """;
            await using var excluir = new NpgsqlCommand(excluirSql, connection, transaction);
            excluir.Parameters.AddWithValue("id", professoraId);
            var affected = await excluir.ExecuteNonQueryAsync(cancellationToken);

            if (affected == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<(string Nome, string Email)?> ObterDadosParaAcessoAsync(Guid professoraId, CancellationToken cancellationToken)
    {
        const string sql = """
            select nome, email
            from public.professoras
            where id = @id and ativo = true
            limit 1;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", professoraId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetString(0), reader.GetString(1))
            : null;
    }

    public async Task<IReadOnlyList<ProfessoraArquivadaResponse>> ListarArquivadasAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            select id, nome, email, status, foto_url, data_desativacao
            from public.professoras where ativo = false
            order by data_desativacao desc nulls last, nome;
            """;
        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ProfessoraArquivadaResponse>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), GetNullableString(reader, 4), reader.IsDBNull(5) ? null : reader.GetFieldValue<DateOnly>(5)));
        return result;
    }

    public async Task<bool> RestaurarAsync(Guid professoraId, CancellationToken cancellationToken)
    {
        const string sql = """
            update public.professoras set ativo = true, status = 'Pausada', data_desativacao = null, atualizado_em = now()
            where id = @id and ativo = false returning id;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", professoraId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async Task AtualizarEmailAcessoAsync(Guid professoraId, string email, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var teacher = new NpgsqlCommand("update public.professoras set email=@email, atualizado_em=now() where id=@id and ativo=true;", connection, transaction))
            { teacher.Parameters.AddWithValue("id", professoraId); teacher.Parameters.AddWithValue("email", email); if (await teacher.ExecuteNonQueryAsync(cancellationToken) == 0) throw new InvalidOperationException("Professora não encontrada."); }
            await using (var profile = new NpgsqlCommand("update public.perfis_usuarios set email=@email, atualizado_em=now() where professora_id=@id and ativo=true;", connection, transaction))
            { profile.Parameters.AddWithValue("id", professoraId); profile.Parameters.AddWithValue("email", email); await profile.ExecuteNonQueryAsync(cancellationToken); }
            await transaction.CommitAsync(cancellationToken);
        }
        catch { await transaction.RollbackAsync(cancellationToken); throw; }
    }

    public async Task MarcarTrocaSenhaObrigatoriaAsync(Guid professoraId, CancellationToken cancellationToken)
    {
        const string sql = "update public.perfis_usuarios set deve_alterar_senha=true, atualizado_em=now() where professora_id=@id and ativo=true;";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", professoraId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RevogarSessoesAsync(Guid professoraId, CancellationToken cancellationToken)
    {
        const string sql = "update public.perfis_usuarios set sessoes_revogadas_antes_de=now(), atualizado_em=now() where professora_id=@id and ativo=true;";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", professoraId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> PossuiPerfilAcessoAtivoAsync(Guid professoraId, CancellationToken cancellationToken)
    {
        const string sql = """
            select exists (
                select 1 from public.perfis_usuarios
                where professora_id = @professora_id and ativo = true
            );
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("professora_id", professoraId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task CriarPerfilAcessoAsync(Guid professoraId, Guid usuarioAuthId, string nome, string email, CancellationToken cancellationToken)
    {
        const string sql = """
            insert into public.perfis_usuarios (
                usuario_auth_id, nome, email, tipo_usuario, professora_id,
                ativo, deve_alterar_senha
            ) values (
                @usuario_auth_id, @nome, @email, 'Professora', @professora_id,
                true, true
            );
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("usuario_auth_id", usuarioAuthId);
        command.Parameters.AddWithValue("nome", nome);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("professora_id", professoraId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Guid?> ObterUsuarioAuthIdAsync(Guid professoraId, CancellationToken cancellationToken)
    {
        const string sql = """
            select usuario_auth_id
            from public.perfis_usuarios
            where professora_id = @professora_id
            order by criado_em desc
            limit 1;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("professora_id", professoraId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid id ? id : null;
    }

    public async Task<Guid?> ObterUsuarioAuthIdInativoPorEmailAsync(string email, CancellationToken cancellationToken)
    {
        const string sql = """
            select pu.usuario_auth_id
            from public.professoras p
            inner join public.perfis_usuarios pu on pu.professora_id = p.id
            where lower(p.email) = lower(@email)
              and p.ativo = false
              and pu.tipo_usuario = 'Professora'
            order by p.atualizado_em desc, pu.criado_em desc
            limit 1;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("email", email);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid id ? id : null;
    }

    public async Task<bool> EmailExisteAsync(string email, CancellationToken cancellationToken)
    {
        const string sql = "select exists(select 1 from public.professoras where lower(email) = lower(@email) and ativo = true);";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("email", email);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<Professora?> BuscarPorIdAsync(Guid id, CancellationToken cancellationToken)
    {
        const string sql = """
            select
                p.id, p.nome, p.nome_profissional, p.data_nascimento, p.documento_identidade,
                p.email, p.telefone, p.status, p.modelo_pagamento, p.dia_pagamento,
                p.tipo_chave_pix, p.chave_pix, p.banco, p.observacoes, p.foto_url, p.ativo,
                coalesce(v.valor_aula_individual, 0) as valor_aula_individual,
                coalesce(v.valor_aula_grupo, 0) as valor_aula_grupo,
                v.vigente_desde,
                (select count(distinct a.id)::int
                   from public.alunos a
                  where a.ativo = true
                    and (
                      exists (
                        select 1 from public.horarios_recorrentes_alunos h
                        where h.aluno_id = a.id
                          and h.professora_id = p.id
                          and h.ativo = true
                          and h.data_inicio <= current_date
                          and (h.data_fim is null or h.data_fim >= current_date)
                      )
                      or (
                        a.professora_id = p.id
                        and not exists (
                          select 1 from public.horarios_recorrentes_alunos h_current
                          where h_current.aluno_id = a.id
                            and h_current.ativo = true
                            and h_current.data_inicio <= current_date
                            and (h_current.data_fim is null or h_current.data_fim >= current_date)
                        )
                      )
                    )) as quantidade_alunos,
                (select count(*)::int
                   from public.horarios_recorrentes_alunos h
                   join public.alunos a on a.id = h.aluno_id and a.ativo = true
                  where h.professora_id = p.id
                    and h.ativo = true
                    and h.data_inicio <= current_date
                    and (h.data_fim is null or h.data_fim >= current_date)) as quantidade_aulas,
                proxima.data_aula as proxima_aula_data,
                proxima.hora_inicio as proxima_aula_hora,
                exists (
                    select 1 from public.perfis_usuarios pu
                    where pu.professora_id = p.id and pu.ativo = true
                ) as possui_acesso,
                p.criado_em, p.atualizado_em
            from public.professoras p
            left join lateral (
                select valor_aula_individual, valor_aula_grupo, vigente_desde
                from public.valores_aula_professoras
                where professora_id = p.id
                order by vigente_desde desc
                limit 1
            ) v on true
            left join lateral (
                select data_aula, hora_inicio
                from public.aulas
                where professora_id = p.id
                  and status in ('agendada', 'em_andamento')
                  and (data_aula > current_date or (data_aula = current_date and hora_inicio >= localtime))
                order by data_aula, hora_inicio
                limit 1
            ) proxima on true
            where p.id = @id;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapProfessora(reader) : null;
    }


    private static void AddTeacherListFilterParameters(
        NpgsqlCommand command,
        string? busca,
        string? status)
    {
        var normalizedSearch = string.IsNullOrWhiteSpace(busca) ? null : busca.Trim().ToLowerInvariant();
        AddParameter(command, "status", status);
        AddParameter(command, "busca", normalizedSearch);
        AddParameter(command, "busca_like", normalizedSearch is null ? null : $"%{normalizedSearch}%");
    }

    private static Professora MapProfessora(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        Nome = reader.GetString(1),
        NomeProfissional = GetNullableString(reader, 2),
        DataNascimento = GetNullableDateOnly(reader, 3),
        DocumentoIdentidade = GetNullableString(reader, 4),
        Email = reader.GetString(5),
        Telefone = GetNullableString(reader, 6),
        Status = reader.GetString(7),
        ModeloPagamento = reader.GetString(8),
        DiaPagamento = reader.IsDBNull(9) ? null : reader.GetInt16(9),
        TipoChavePix = GetNullableString(reader, 10),
        ChavePix = GetNullableString(reader, 11),
        Banco = GetNullableString(reader, 12),
        Observacoes = GetNullableString(reader, 13),
        FotoUrl = GetNullableString(reader, 14),
        Ativo = reader.GetBoolean(15),
        ValorAulaIndividual = reader.GetDecimal(16),
        ValorAulaGrupo = reader.GetDecimal(17),
        VigenteDesde = GetNullableDateOnly(reader, 18),
        QuantidadeAlunos = reader.GetInt32(19),
        QuantidadeAulas = reader.GetInt32(20),
        ProximaAulaData = GetNullableDateOnly(reader, 21),
        ProximaAulaHora = reader.IsDBNull(22) ? null : reader.GetFieldValue<TimeOnly>(22),
        PossuiAcesso = reader.GetBoolean(23),
        CriadoEm = reader.GetFieldValue<DateTimeOffset>(24),
        AtualizadoEm = reader.GetFieldValue<DateTimeOffset>(25)
    };

    private static string? GetNullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static DateOnly? GetNullableDateOnly(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateOnly>(ordinal);

    private static void AddParameter(NpgsqlCommand command, string name, object? value, NpgsqlDbType type = NpgsqlDbType.Text)
    {
        var parameter = new NpgsqlParameter(name, type)
        {
            Value = value ?? DBNull.Value
        };
        command.Parameters.Add(parameter);
    }
}
