using EnglishYard.Application.Alunos;
using EnglishYard.Domain.Entities;
using Npgsql;
using NpgsqlTypes;

namespace EnglishYard.Infrastructure.Persistence;

public sealed class AlunoRepository(NpgsqlDataSource dataSource) : IAlunoRepository
{
    public async Task<IReadOnlyList<Aluno>> ListarAsync(Guid? professoraId, CancellationToken cancellationToken)
    {
        const string sql = """
            select
                a.id, a.nome, a.data_nascimento, a.genero, a.email, a.telefone,
                a.responsavel_nome, a.responsavel_telefone, a.status,
                case when @professora_id is not null then agenda.professora_id else coalesce(agenda.professora_id, a.professora_id) end as professora_id,
                case when @professora_id is not null then agenda.professora_nome else coalesce(agenda.professora_nome, p.nome) end as professora_nome,
                case when @professora_id is not null then agenda.professora_foto_url else coalesce(agenda.professora_foto_url, p.foto_url) end as professora_foto_url,
                a.valor_mensalidade, a.dia_vencimento, a.forma_pagamento,
                a.taxa_matricula, a.percentual_desconto,
                a.observacoes, a.foto_url, a.ativo, a.criado_em, a.atualizado_em
            from public.alunos a
            left join public.professoras p on p.id = a.professora_id
            left join lateral (
                select hr.professora_id, pr.nome as professora_nome, pr.foto_url as professora_foto_url
                from public.horarios_recorrentes_alunos hr
                join public.professoras pr on pr.id = hr.professora_id and pr.ativo = true
                where hr.aluno_id = a.id
                  and hr.ativo = true
                  and hr.data_inicio <= current_date
                  and (hr.data_fim is null or hr.data_fim >= current_date)
                  and (@professora_id is null or hr.professora_id = @professora_id)
                order by hr.data_inicio desc, hr.hora_inicio
                limit 1
            ) agenda on true
            where a.ativo = true
              and (
                @professora_id is null
                or exists (
                    select 1
                    from public.horarios_recorrentes_alunos hr_filter
                    where hr_filter.aluno_id = a.id
                      and hr_filter.professora_id = @professora_id
                      and hr_filter.ativo = true
                      and hr_filter.data_inicio <= current_date
                      and (hr_filter.data_fim is null or hr_filter.data_fim >= current_date)
                )
                or (
                    a.professora_id = @professora_id
                    and not exists (
                        select 1
                        from public.horarios_recorrentes_alunos hr_current
                        where hr_current.aluno_id = a.id
                          and hr_current.ativo = true
                          and hr_current.data_inicio <= current_date
                          and (hr_current.data_fim is null or hr_current.data_fim >= current_date)
                    )
                )
              )
            order by a.nome;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.Add(new NpgsqlParameter("professora_id", NpgsqlDbType.Uuid)
        {
            Value = professoraId.HasValue ? professoraId.Value : DBNull.Value
        });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var alunos = new List<Aluno>();

        while (await reader.ReadAsync(cancellationToken))
            alunos.Add(MapAluno(reader));

        return alunos;
    }


    public async Task<(IReadOnlyList<Aluno> Itens, int Total)> ListarPaginadoAsync(
        Guid? professoraAcessoId,
        string? busca,
        Guid? professoraFiltroId,
        string? status,
        short? diaSemana,
        int pagina,
        int tamanhoPagina,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select
                a.id, a.nome, a.data_nascimento, a.genero, a.email, a.telefone,
                a.responsavel_nome, a.responsavel_telefone, a.status,
                case when @professora_acesso_id is not null then agenda_acesso.professora_id else coalesce(agenda.professora_id, a.professora_id) end as professora_id,
                case when @professora_acesso_id is not null then agenda_acesso.professora_nome else coalesce(agenda.professora_nome, p.nome) end as professora_nome,
                case when @professora_acesso_id is not null then agenda_acesso.professora_foto_url else coalesce(agenda.professora_foto_url, p.foto_url) end as professora_foto_url,
                a.valor_mensalidade, a.dia_vencimento, a.forma_pagamento,
                a.taxa_matricula, a.percentual_desconto,
                a.observacoes, a.foto_url, a.ativo, a.criado_em, a.atualizado_em,
                count(*) over()::int as total_count
            from public.alunos a
            left join public.professoras p on p.id = a.professora_id
            left join lateral (
                select hr.professora_id, pr.nome as professora_nome, pr.foto_url as professora_foto_url
                from public.horarios_recorrentes_alunos hr
                join public.professoras pr on pr.id = hr.professora_id and pr.ativo = true
                where hr.aluno_id = a.id
                  and hr.ativo = true
                  and hr.data_inicio <= current_date
                  and (hr.data_fim is null or hr.data_fim >= current_date)
                  and (@professora_filtro_id is null or hr.professora_id = @professora_filtro_id)
                order by hr.data_inicio desc, hr.hora_inicio
                limit 1
            ) agenda on true
            left join lateral (
                select hr.professora_id, pr.nome as professora_nome, pr.foto_url as professora_foto_url
                from public.horarios_recorrentes_alunos hr
                join public.professoras pr on pr.id = hr.professora_id and pr.ativo = true
                where hr.aluno_id = a.id
                  and hr.professora_id = @professora_acesso_id
                  and hr.ativo = true
                  and hr.data_inicio <= current_date
                  and (hr.data_fim is null or hr.data_fim >= current_date)
                order by hr.data_inicio desc, hr.hora_inicio
                limit 1
            ) agenda_acesso on @professora_acesso_id is not null
            where a.ativo = true
              and (
                @professora_acesso_id is null
                or exists (
                    select 1
                    from public.horarios_recorrentes_alunos hr_access
                    where hr_access.aluno_id = a.id
                      and hr_access.professora_id = @professora_acesso_id
                      and hr_access.ativo = true
                      and hr_access.data_inicio <= current_date
                      and (hr_access.data_fim is null or hr_access.data_fim >= current_date)
                )
                or (
                    a.professora_id = @professora_acesso_id
                    and not exists (
                        select 1
                        from public.horarios_recorrentes_alunos hr_access_current
                        where hr_access_current.aluno_id = a.id
                          and hr_access_current.ativo = true
                          and hr_access_current.data_inicio <= current_date
                          and (hr_access_current.data_fim is null or hr_access_current.data_fim >= current_date)
                    )
                )
              )
              and (
                @professora_filtro_id is null
                or exists (
                    select 1
                    from public.horarios_recorrentes_alunos hr_teacher
                    where hr_teacher.aluno_id = a.id
                      and hr_teacher.professora_id = @professora_filtro_id
                      and hr_teacher.ativo = true
                      and hr_teacher.data_inicio <= current_date
                      and (hr_teacher.data_fim is null or hr_teacher.data_fim >= current_date)
                )
                or (
                    a.professora_id = @professora_filtro_id
                    and not exists (
                        select 1
                        from public.horarios_recorrentes_alunos hr_teacher_current
                        where hr_teacher_current.aluno_id = a.id
                          and hr_teacher_current.ativo = true
                          and hr_teacher_current.data_inicio <= current_date
                          and (hr_teacher_current.data_fim is null or hr_teacher_current.data_fim >= current_date)
                    )
                )
              )
              and (
                @dia_semana is null
                or exists (
                    select 1
                    from public.horarios_recorrentes_alunos hr_day
                    where hr_day.aluno_id = a.id
                      and hr_day.dia_semana = @dia_semana
                      and hr_day.ativo = true
                      and hr_day.data_inicio <= current_date
                      and (hr_day.data_fim is null or hr_day.data_fim >= current_date)
                      and (@professora_acesso_id is null or hr_day.professora_id = @professora_acesso_id)
                )
              )
              and (@status is null or a.status = @status)
              and (
                @busca is null
                or lower(a.nome) like @busca_like
                or lower(coalesce(a.email, '')) like @busca_like
                or lower(coalesce(a.telefone, '')) like @busca_like
                or (
                    @busca_digits is not null
                    and regexp_replace(coalesce(a.telefone, ''), '\D', '', 'g') like @busca_digits_like
                )
              )
            order by a.nome
            limit @tamanho_pagina offset @offset;
            """;

        await using var command = dataSource.CreateCommand(sql);
        AddListFilterParameters(
            command,
            professoraAcessoId,
            busca,
            professoraFiltroId,
            status,
            diaSemana);
        command.Parameters.AddWithValue("tamanho_pagina", tamanhoPagina);
        command.Parameters.AddWithValue("offset", (pagina - 1) * tamanhoPagina);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var alunos = new List<Aluno>();
        var total = 0;

        while (await reader.ReadAsync(cancellationToken))
        {
            alunos.Add(MapAluno(reader));
            total = reader.GetInt32(22);
        }

        return (alunos, total);
    }

    public async Task<IReadOnlyList<AlunoExportacaoResponse>> ListarExportacaoAsync(
        Guid? professoraAcessoId,
        string? busca,
        Guid? professoraFiltroId,
        string? status,
        short? diaSemana,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select
                a.nome,
                a.email,
                a.telefone,
                coalesce(
                    nullif(string_agg(distinct pr.nome, ', '), ''),
                    p_base.nome,
                    'Não definida'
                ) as professoras,
                coalesce(
                    nullif(string_agg(distinct case h.dia_semana
                        when 0 then 'Domingo'
                        when 1 then 'Segunda-feira'
                        when 2 then 'Terça-feira'
                        when 3 then 'Quarta-feira'
                        when 4 then 'Quinta-feira'
                        when 5 then 'Sexta-feira'
                        when 6 then 'Sábado'
                    end, ', '), ''),
                    'Sem aula semanal'
                ) as dias_aula,
                a.valor_mensalidade,
                a.dia_vencimento,
                a.status
            from public.alunos a
            left join public.professoras p_base on p_base.id = a.professora_id
            left join public.horarios_recorrentes_alunos h
              on h.aluno_id = a.id
             and h.ativo = true
             and h.data_inicio <= current_date
             and (h.data_fim is null or h.data_fim >= current_date)
             and (@professora_acesso_id is null or h.professora_id = @professora_acesso_id)
             and (@professora_filtro_id is null or h.professora_id = @professora_filtro_id)
            left join public.professoras pr on pr.id = h.professora_id and pr.ativo = true
            where a.ativo = true
              and (
                @professora_acesso_id is null
                or exists (
                    select 1
                    from public.horarios_recorrentes_alunos hr_access
                    where hr_access.aluno_id = a.id
                      and hr_access.professora_id = @professora_acesso_id
                      and hr_access.ativo = true
                      and hr_access.data_inicio <= current_date
                      and (hr_access.data_fim is null or hr_access.data_fim >= current_date)
                )
                or (
                    a.professora_id = @professora_acesso_id
                    and not exists (
                        select 1
                        from public.horarios_recorrentes_alunos hr_access_current
                        where hr_access_current.aluno_id = a.id
                          and hr_access_current.ativo = true
                          and hr_access_current.data_inicio <= current_date
                          and (hr_access_current.data_fim is null or hr_access_current.data_fim >= current_date)
                    )
                )
              )
              and (
                @professora_filtro_id is null
                or exists (
                    select 1
                    from public.horarios_recorrentes_alunos hr_teacher
                    where hr_teacher.aluno_id = a.id
                      and hr_teacher.professora_id = @professora_filtro_id
                      and hr_teacher.ativo = true
                      and hr_teacher.data_inicio <= current_date
                      and (hr_teacher.data_fim is null or hr_teacher.data_fim >= current_date)
                )
                or (
                    a.professora_id = @professora_filtro_id
                    and not exists (
                        select 1
                        from public.horarios_recorrentes_alunos hr_teacher_current
                        where hr_teacher_current.aluno_id = a.id
                          and hr_teacher_current.ativo = true
                          and hr_teacher_current.data_inicio <= current_date
                          and (hr_teacher_current.data_fim is null or hr_teacher_current.data_fim >= current_date)
                    )
                )
              )
              and (
                @dia_semana is null
                or exists (
                    select 1
                    from public.horarios_recorrentes_alunos hr_day
                    where hr_day.aluno_id = a.id
                      and hr_day.dia_semana = @dia_semana
                      and hr_day.ativo = true
                      and hr_day.data_inicio <= current_date
                      and (hr_day.data_fim is null or hr_day.data_fim >= current_date)
                      and (@professora_acesso_id is null or hr_day.professora_id = @professora_acesso_id)
                )
              )
              and (@status is null or a.status = @status)
              and (
                @busca is null
                or lower(a.nome) like @busca_like
                or lower(coalesce(a.email, '')) like @busca_like
                or lower(coalesce(a.telefone, '')) like @busca_like
                or (
                    @busca_digits is not null
                    and regexp_replace(coalesce(a.telefone, ''), '\D', '', 'g') like @busca_digits_like
                )
              )
            group by
                a.id, a.nome, a.email, a.telefone, p_base.nome,
                a.valor_mensalidade, a.dia_vencimento, a.status
            order by a.nome;
            """;

        await using var command = dataSource.CreateCommand(sql);
        AddListFilterParameters(
            command,
            professoraAcessoId,
            busca,
            professoraFiltroId,
            status,
            diaSemana);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<AlunoExportacaoResponse>();

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new AlunoExportacaoResponse(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                reader.IsDBNull(6) ? null : reader.GetInt16(6),
                reader.GetString(7)));
        }

        return result;
    }

    public async Task<IReadOnlyList<HorarioRecorrenteAlunoResponse>> ListarHorariosRecorrentesAsync(
        Guid alunoId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select
                h.id, h.dia_semana, h.hora_inicio, h.hora_fim,
                h.professora_id, p.nome as professora_nome,
                h.data_inicio, h.data_fim, h.ativo
            from public.horarios_recorrentes_alunos h
            join public.professoras p on p.id = h.professora_id
            where h.aluno_id = @aluno_id
            order by h.data_inicio desc, h.dia_semana, h.hora_inicio;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("aluno_id", alunoId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<HorarioRecorrenteAlunoResponse>();

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new HorarioRecorrenteAlunoResponse(
                reader.GetGuid(0),
                reader.GetInt16(1),
                reader.GetFieldValue<TimeOnly>(2),
                reader.GetFieldValue<TimeOnly>(3),
                reader.GetGuid(4),
                reader.GetString(5),
                reader.GetFieldValue<DateOnly>(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateOnly>(7),
                reader.GetBoolean(8)));
        }

        return result;
    }

    public async Task<ConflitoAgendaAlunoResponse?> BuscarConflitoHorarioAsync(
        Guid? ignorarAlunoId,
        Guid professoraId,
        short diaSemana,
        TimeOnly horaInicio,
        TimeOnly horaFim,
        DateOnly dataInicio,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select
                a.id, a.nome, p.id, p.nome,
                h.dia_semana, h.hora_inicio, h.hora_fim
            from public.horarios_recorrentes_alunos h
            join public.alunos a on a.id = h.aluno_id and a.ativo = true
            join public.professoras p on p.id = h.professora_id and p.ativo = true
            where h.ativo = true
              and h.professora_id = @professora_id
              and h.dia_semana = @dia_semana
              and (@ignorar_aluno_id is null or h.aluno_id <> @ignorar_aluno_id)
              and (h.data_fim is null or h.data_fim >= @data_inicio)
              and h.hora_inicio < @hora_fim
              and h.hora_fim > @hora_inicio
            order by
                case when h.hora_inicio = @hora_inicio and h.hora_fim = @hora_fim then 1 else 0 end,
                h.hora_inicio
            limit 1;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("professora_id", professoraId);
        command.Parameters.AddWithValue("dia_semana", NpgsqlDbType.Smallint, diaSemana);
        command.Parameters.AddWithValue("hora_inicio", NpgsqlDbType.Time, horaInicio);
        command.Parameters.AddWithValue("hora_fim", NpgsqlDbType.Time, horaFim);
        command.Parameters.AddWithValue("data_inicio", NpgsqlDbType.Date, dataInicio);
        command.Parameters.Add(new NpgsqlParameter("ignorar_aluno_id", NpgsqlDbType.Uuid)
        {
            Value = ignorarAlunoId.HasValue ? ignorarAlunoId.Value : DBNull.Value
        });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new ConflitoAgendaAlunoResponse(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetInt16(4),
            reader.GetFieldValue<TimeOnly>(5),
            reader.GetFieldValue<TimeOnly>(6));
    }

    public async Task<Aluno> CadastrarAsync(CadastrarAlunoRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        Guid alunoId;

        try
        {
            var professorasDaAgenda = (request.HorariosRecorrentes ?? [])
                .Where(h => h.ProfessoraId.HasValue)
                .Select(h => h.ProfessoraId!.Value)
                .Distinct()
                .ToArray();
            var professoraAssociada = professorasDaAgenda.Length == 1 ? professorasDaAgenda[0] : (Guid?)null;

            const string insertSql = """
                insert into public.alunos (
                    nome, data_nascimento, genero, email, telefone,
                    responsavel_nome, responsavel_telefone, status, professora_id,
                    valor_mensalidade, dia_vencimento, forma_pagamento,
                    taxa_matricula, percentual_desconto, observacoes, ativo
                ) values (
                    @nome, @data_nascimento, @genero, @email, @telefone,
                    @responsavel_nome, @responsavel_telefone, @status, @professora_id,
                    @valor_mensalidade, @dia_vencimento, @forma_pagamento,
                    @taxa_matricula, @percentual_desconto, @observacoes, true
                )
                returning id;
                """;

            await using (var insert = new NpgsqlCommand(insertSql, connection, transaction))
            {
                AddParameter(insert, "nome", request.Nome);
                AddParameter(insert, "data_nascimento", request.DataNascimento, NpgsqlDbType.Date);
                AddParameter(insert, "genero", request.Genero);
                AddParameter(insert, "email", request.Email);
                AddParameter(insert, "telefone", request.Telefone);
                AddParameter(insert, "responsavel_nome", request.ResponsavelNome);
                AddParameter(insert, "responsavel_telefone", request.ResponsavelTelefone);
                AddParameter(insert, "status", request.Status);
                AddParameter(insert, "professora_id", professoraAssociada, NpgsqlDbType.Uuid);
                AddParameter(insert, "valor_mensalidade", request.ValorMensalidade, NpgsqlDbType.Numeric);
                AddParameter(insert, "dia_vencimento", request.DiaVencimento, NpgsqlDbType.Smallint);
                AddParameter(insert, "forma_pagamento", request.FormaPagamento);
                AddParameter(insert, "taxa_matricula", request.TaxaMatricula, NpgsqlDbType.Numeric);
                AddParameter(insert, "percentual_desconto", request.PercentualDesconto, NpgsqlDbType.Numeric);
                AddParameter(insert, "observacoes", request.Observacoes);

                alunoId = (Guid)(await insert.ExecuteScalarAsync(cancellationToken)
                    ?? throw new InvalidOperationException("O banco não retornou o identificador do aluno."));
            }

            await InserirHorariosRecorrentesAsync(connection, transaction, alunoId, request, cancellationToken);
            await CriarCobrancaInicialAsync(connection, transaction, alunoId, request, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation &&
            exception.ConstraintName == "ux_alunos_email_lower")
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new AlunoConflitoException("Já existe um aluno ativo cadastrado com este e-mail.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return await BuscarPorIdAsync(alunoId, cancellationToken)
            ?? throw new InvalidOperationException("O aluno foi criado, mas não pôde ser carregado em seguida.");
    }

    private static async Task CriarCobrancaInicialAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid alunoId,
        CadastrarAlunoRequest request,
        CancellationToken cancellationToken)
    {
        var mensalidade = Math.Max(0m, request.ValorMensalidade ?? 0m);
        var taxaMatricula = Math.Max(0m, request.TaxaMatricula);
        if (mensalidade <= 0m && taxaMatricula <= 0m) return;

        await using var todayCommand = new NpgsqlCommand(
            "select (now() at time zone 'America/Sao_Paulo')::date;",
            connection,
            transaction);
        var hoje = (DateOnly)(await todayCommand.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Não foi possível determinar a data atual da escola."));
        var competencia = new DateOnly(hoje.Year, hoje.Month, 1);
        var descontoMensalidade = Math.Round(
            mensalidade * Math.Max(0m, request.PercentualDesconto) / 100m,
            2,
            MidpointRounding.AwayFromZero);
        var valorOriginal = mensalidade + taxaMatricula;
        var valorFinal = Math.Max(0m, mensalidade - descontoMensalidade) + taxaMatricula;
        var diaVencimento = Math.Clamp((int)(request.DiaVencimento ?? 10), 1, DateTime.DaysInMonth(hoje.Year, hoje.Month));
        var vencimento = new DateOnly(hoje.Year, hoje.Month, diaVencimento);
        var descricao = mensalidade > 0m && taxaMatricula > 0m
            ? "Mensalidade + taxa de matrícula"
            : taxaMatricula > 0m ? "Taxa de matrícula" : "Mensalidade";

        const string sql = """
            insert into public.mensalidades (
                aluno_id, competencia, descricao, valor_original, desconto, valor_final,
                data_vencimento, status, forma_pagamento_prevista, observacoes
            ) values (
                @aluno_id, @competencia, @descricao, @valor_original, @desconto, @valor_final,
                @data_vencimento, 'em_aberto', @forma_pagamento,
                case when @taxa_matricula > 0 then 'Cobrança inicial gerada automaticamente no cadastro do aluno.' else null end
            )
            on conflict (aluno_id, competencia) do nothing;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("aluno_id", alunoId);
        command.Parameters.AddWithValue("competencia", NpgsqlDbType.Date, competencia);
        command.Parameters.AddWithValue("descricao", descricao);
        command.Parameters.AddWithValue("valor_original", NpgsqlDbType.Numeric, valorOriginal);
        command.Parameters.AddWithValue("desconto", NpgsqlDbType.Numeric, descontoMensalidade);
        command.Parameters.AddWithValue("valor_final", NpgsqlDbType.Numeric, valorFinal);
        command.Parameters.AddWithValue("data_vencimento", NpgsqlDbType.Date, vencimento);
        command.Parameters.Add(new NpgsqlParameter("forma_pagamento", NpgsqlDbType.Text)
        {
            Value = request.FormaPagamento ?? (object)DBNull.Value
        });
        command.Parameters.AddWithValue("taxa_matricula", NpgsqlDbType.Numeric, taxaMatricula);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Aluno?> AtualizarAsync(
        Guid alunoId,
        AtualizarAlunoRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            const string sql = """
                update public.alunos
                set nome = @nome,
                    data_nascimento = @data_nascimento,
                    genero = @genero,
                    email = @email,
                    telefone = @telefone,
                    responsavel_nome = @responsavel_nome,
                    responsavel_telefone = @responsavel_telefone,
                    status = @status,
                    valor_mensalidade = @valor_mensalidade,
                    dia_vencimento = @dia_vencimento,
                    forma_pagamento = @forma_pagamento,
                    taxa_matricula = @taxa_matricula,
                    percentual_desconto = @percentual_desconto,
                    observacoes = @observacoes,
                    atualizado_em = now()
                where id = @id and ativo = true;
                """;

            await using (var command = new NpgsqlCommand(sql, connection, transaction))
            {
                command.Parameters.AddWithValue("id", alunoId);
                AddParameter(command, "nome", request.Nome);
                AddParameter(command, "data_nascimento", request.DataNascimento, NpgsqlDbType.Date);
                AddParameter(command, "genero", request.Genero);
                AddParameter(command, "email", request.Email);
                AddParameter(command, "telefone", request.Telefone);
                AddParameter(command, "responsavel_nome", request.ResponsavelNome);
                AddParameter(command, "responsavel_telefone", request.ResponsavelTelefone);
                AddParameter(command, "status", request.Status);
                AddParameter(command, "valor_mensalidade", request.ValorMensalidade, NpgsqlDbType.Numeric);
                AddParameter(command, "dia_vencimento", request.DiaVencimento, NpgsqlDbType.Smallint);
                AddParameter(command, "forma_pagamento", request.FormaPagamento);
                AddParameter(command, "taxa_matricula", request.TaxaMatricula, NpgsqlDbType.Numeric);
                AddParameter(command, "percentual_desconto", request.PercentualDesconto, NpgsqlDbType.Numeric);
                AddParameter(command, "observacoes", request.Observacoes);

                if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return null;
                }
            }

            if (request.HorariosRecorrentes is not null)
            {
                var vigenteDesde = request.AgendaVigenteDesde
                    ?? throw new InvalidOperationException("A atualização da agenda está sem data de vigência.");

                await SubstituirAgendaRecorrenteAsync(
                    connection,
                    transaction,
                    alunoId,
                    vigenteDesde,
                    request.HorariosRecorrentes,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation
            && exception.ConstraintName == "ux_alunos_email_lower")
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new AlunoConflitoException("Já existe outro aluno ativo cadastrado com este e-mail.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return await BuscarPorIdAsync(alunoId, cancellationToken);
    }

    public async Task<bool> AtualizarFotoUrlAsync(Guid alunoId, string fotoUrl, CancellationToken cancellationToken)
    {
        const string sql = """
            update public.alunos
            set foto_url = @foto_url, atualizado_em = now()
            where id = @id and ativo = true;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", alunoId);
        command.Parameters.AddWithValue("foto_url", fotoUrl);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> ExcluirAsync(Guid alunoId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            const string existeSql = """
                select exists (
                    select 1 from public.alunos
                    where id = @id and ativo = true
                );
                """;
            await using (var existe = new NpgsqlCommand(existeSql, connection, transaction))
            {
                existe.Parameters.AddWithValue("id", alunoId);
                var ativo = (bool)(await existe.ExecuteScalarAsync(cancellationToken) ?? false);
                if (!ativo)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return false;
                }
            }

            const string desativarHorariosSql = """
                update public.horarios_recorrentes_alunos
                set ativo = false,
                    data_fim = coalesce(data_fim, greatest(data_inicio, current_date)),
                    atualizado_em = now()
                where aluno_id = @aluno_id
                  and ativo = true;
                """;
            await using (var desativarHorarios = new NpgsqlCommand(desativarHorariosSql, connection, transaction))
            {
                desativarHorarios.Parameters.AddWithValue("aluno_id", alunoId);
                await desativarHorarios.ExecuteNonQueryAsync(cancellationToken);
            }

            const string excluirSql = """
                update public.alunos
                set ativo = false, atualizado_em = now()
                where id = @id and ativo = true;
                """;
            await using var excluir = new NpgsqlCommand(excluirSql, connection, transaction);
            excluir.Parameters.AddWithValue("id", alunoId);
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

    public async Task<bool> EmailExisteAsync(string email, Guid? ignorarAlunoId, CancellationToken cancellationToken)
    {
        const string sql = """
            select exists(
                select 1
                from public.alunos
                where lower(email) = lower(@email)
                  and ativo = true
                  and (@ignorar_aluno_id is null or id <> @ignorar_aluno_id)
            );
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.Add(new NpgsqlParameter("ignorar_aluno_id", NpgsqlDbType.Uuid)
        {
            Value = ignorarAlunoId.HasValue ? ignorarAlunoId.Value : DBNull.Value
        });
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<bool> ProfessoraExisteAsync(Guid professoraId, CancellationToken cancellationToken)
    {
        const string sql = "select exists(select 1 from public.professoras where id = @id and ativo = true);";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", professoraId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<IReadOnlyList<ProfessoraResumoResponse>> ListarProfessorasAtivasAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            select id, nome, status
            from public.professoras
            where ativo = true and status in ('Ativa', 'Em onboarding')
            order by nome;
            """;

        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var professoras = new List<ProfessoraResumoResponse>();

        while (await reader.ReadAsync(cancellationToken))
            professoras.Add(new ProfessoraResumoResponse(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));

        return professoras;
    }

    public async Task<Aluno?> BuscarPorIdAsync(Guid id, CancellationToken cancellationToken)
    {
        const string sql = """
            select
                a.id, a.nome, a.data_nascimento, a.genero, a.email, a.telefone,
                a.responsavel_nome, a.responsavel_telefone, a.status,
                coalesce(agenda.professora_id, a.professora_id) as professora_id,
                coalesce(agenda.professora_nome, p.nome) as professora_nome,
                coalesce(agenda.professora_foto_url, p.foto_url) as professora_foto_url,
                a.valor_mensalidade, a.dia_vencimento, a.forma_pagamento,
                a.taxa_matricula, a.percentual_desconto,
                a.observacoes, a.foto_url, a.ativo, a.criado_em, a.atualizado_em
            from public.alunos a
            left join public.professoras p on p.id = a.professora_id
            left join lateral (
                select hr.professora_id, pr.nome as professora_nome, pr.foto_url as professora_foto_url
                from public.horarios_recorrentes_alunos hr
                join public.professoras pr on pr.id = hr.professora_id and pr.ativo = true
                where hr.aluno_id = a.id
                  and hr.ativo = true
                  and hr.data_inicio <= current_date
                  and (hr.data_fim is null or hr.data_fim >= current_date)
                order by hr.data_inicio desc, hr.hora_inicio
                limit 1
            ) agenda on true
            where a.id = @id;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapAluno(reader) : null;
    }

    private static async Task SubstituirAgendaRecorrenteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid alunoId,
        DateOnly vigenteDesde,
        IReadOnlyCollection<HorarioRecorrenteAlunoRequest> horarios,
        CancellationToken cancellationToken)
    {
        if (await AgendaAtualEhIgualAsync(connection, transaction, alunoId, vigenteDesde, horarios, cancellationToken))
            return;

        // Existe no máximo uma alteração futura pendente por aluno. Como uma versão com
        // data_inicio > hoje ainda não gerou nenhuma aula, ela pode ser substituída sem
        // contaminar o histórico quando o administrador muda a data ou o conteúdo da grade.
        const string removerFuturasSql = """
            delete from public.horarios_recorrentes_alunos
            where aluno_id = @aluno_id
              and ativo = true
              and data_inicio > current_date;
            """;
        await using (var removerFuturas = new NpgsqlCommand(removerFuturasSql, connection, transaction))
        {
            removerFuturas.Parameters.AddWithValue("aluno_id", alunoId);
            await removerFuturas.ExecuteNonQueryAsync(cancellationToken);
        }

        // Ao substituir uma alteração futura já programada, a versão que continua valendo
        // hoje pode ter sido encerrada na data da programação anterior. Reabrimos apenas
        // essa versão atual para que ela seja novamente encerrada na nova data escolhida.
        const string reabrirAtualSql = """
            update public.horarios_recorrentes_alunos
            set data_fim = null,
                atualizado_em = now()
            where aluno_id = @aluno_id
              and ativo = true
              and data_inicio <= current_date
              and data_fim is not null
              and data_fim >= current_date;
            """;
        await using (var reabrirAtual = new NpgsqlCommand(reabrirAtualSql, connection, transaction))
        {
            reabrirAtual.Parameters.AddWithValue("aluno_id", alunoId);
            await reabrirAtual.ExecuteNonQueryAsync(cancellationToken);
        }

        var ultimoDiaAnterior = vigenteDesde.AddDays(-1);
        const string encerrarAtuaisSql = """
            update public.horarios_recorrentes_alunos
            set data_fim = @ultimo_dia_anterior,
                atualizado_em = now()
            where aluno_id = @aluno_id
              and ativo = true
              and data_inicio < @vigente_desde
              and (data_fim is null or data_fim >= @vigente_desde);
            """;
        await using (var encerrarAtuais = new NpgsqlCommand(encerrarAtuaisSql, connection, transaction))
        {
            encerrarAtuais.Parameters.AddWithValue("aluno_id", alunoId);
            encerrarAtuais.Parameters.AddWithValue("vigente_desde", NpgsqlDbType.Date, vigenteDesde);
            encerrarAtuais.Parameters.AddWithValue("ultimo_dia_anterior", NpgsqlDbType.Date, ultimoDiaAnterior);
            await encerrarAtuais.ExecuteNonQueryAsync(cancellationToken);
        }

        const string inserirSql = """
            insert into public.horarios_recorrentes_alunos (
                aluno_id, professora_id, dia_semana, hora_inicio, hora_fim, data_inicio, data_fim, ativo
            ) values (
                @aluno_id, @professora_id, @dia_semana, @hora_inicio, @hora_fim, @data_inicio, null, true
            );
            """;

        foreach (var horario in horarios)
        {
            var professoraId = horario.ProfessoraId
                ?? throw new InvalidOperationException("A agenda do aluno está sem professora.");

            await using var command = new NpgsqlCommand(inserirSql, connection, transaction);
            command.Parameters.AddWithValue("aluno_id", alunoId);
            command.Parameters.AddWithValue("professora_id", professoraId);
            command.Parameters.AddWithValue("dia_semana", NpgsqlDbType.Smallint, horario.DiaSemana);
            command.Parameters.AddWithValue("hora_inicio", NpgsqlDbType.Time, horario.HoraInicio);
            command.Parameters.AddWithValue("hora_fim", NpgsqlDbType.Time, horario.HoraFim);
            command.Parameters.AddWithValue("data_inicio", NpgsqlDbType.Date, vigenteDesde);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<bool> AgendaAtualEhIgualAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid alunoId,
        DateOnly vigenteDesde,
        IReadOnlyCollection<HorarioRecorrenteAlunoRequest> horarios,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select dia_semana, hora_inicio, hora_fim, professora_id, data_inicio
            from public.horarios_recorrentes_alunos
            where aluno_id = @aluno_id
              and ativo = true
              and data_fim is null
            order by dia_semana, hora_inicio, hora_fim, professora_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("aluno_id", alunoId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var atuais = new List<(short DiaSemana, TimeOnly Inicio, TimeOnly Fim, Guid ProfessoraId, DateOnly DataInicio)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            atuais.Add((
                reader.GetInt16(0),
                reader.GetFieldValue<TimeOnly>(1),
                reader.GetFieldValue<TimeOnly>(2),
                reader.GetGuid(3),
                reader.GetFieldValue<DateOnly>(4)));
        }

        var novos = horarios
            .Select(h => (h.DiaSemana, h.HoraInicio, h.HoraFim, h.ProfessoraId ?? Guid.Empty))
            .OrderBy(h => h.DiaSemana)
            .ThenBy(h => h.HoraInicio)
            .ThenBy(h => h.HoraFim)
            .ThenBy(h => h.Item4)
            .ToArray();

        var mesmaForma = atuais.Count == novos.Length
            && atuais.Select(h => (h.DiaSemana, h.Inicio, h.Fim, h.ProfessoraId)).SequenceEqual(novos);
        if (!mesmaForma)
            return false;

        // Se a grade aberta já está valendo, salvar os mesmos horários não cria uma
        // versão histórica redundante. Se ela ainda é futura, a data de vigência faz
        // parte da alteração e precisa ser respeitada.
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var gradeAbertaEhFutura = atuais.Count > 0 && atuais.All(h => h.DataInicio > hoje);
        return !gradeAbertaEhFutura || atuais.All(h => h.DataInicio == vigenteDesde);
    }

    private static async Task InserirHorariosRecorrentesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid alunoId,
        CadastrarAlunoRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            insert into public.horarios_recorrentes_alunos (
                aluno_id, professora_id, dia_semana, hora_inicio, hora_fim, data_inicio, ativo
            ) values (
                @aluno_id, @professora_id, @dia_semana, @hora_inicio, @hora_fim, @data_inicio, true
            );
            """;

        foreach (var horario in request.HorariosRecorrentes ?? [])
        {
            var professoraId = horario.ProfessoraId
                ?? throw new InvalidOperationException("A agenda do aluno está sem professora.");

            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("aluno_id", alunoId);
            command.Parameters.AddWithValue("professora_id", professoraId);
            command.Parameters.AddWithValue("dia_semana", NpgsqlDbType.Smallint, horario.DiaSemana);
            command.Parameters.AddWithValue("hora_inicio", NpgsqlDbType.Time, horario.HoraInicio);
            command.Parameters.AddWithValue("hora_fim", NpgsqlDbType.Time, horario.HoraFim);
            command.Parameters.AddWithValue("data_inicio", NpgsqlDbType.Date, horario.DataInicio);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }


    private static void AddListFilterParameters(
        NpgsqlCommand command,
        Guid? professoraAcessoId,
        string? busca,
        Guid? professoraFiltroId,
        string? status,
        short? diaSemana)
    {
        var normalizedSearch = string.IsNullOrWhiteSpace(busca) ? null : busca.Trim().ToLowerInvariant();
        var digits = normalizedSearch is null
            ? null
            : new string(normalizedSearch.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits))
            digits = null;

        command.Parameters.Add(new NpgsqlParameter("professora_acesso_id", NpgsqlDbType.Uuid)
        {
            Value = professoraAcessoId.HasValue ? professoraAcessoId.Value : DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("professora_filtro_id", NpgsqlDbType.Uuid)
        {
            Value = professoraFiltroId.HasValue ? professoraFiltroId.Value : DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("dia_semana", NpgsqlDbType.Smallint)
        {
            Value = diaSemana.HasValue ? diaSemana.Value : DBNull.Value
        });
        AddParameter(command, "status", status);
        AddParameter(command, "busca", normalizedSearch);
        AddParameter(command, "busca_like", normalizedSearch is null ? null : $"%{normalizedSearch}%");
        AddParameter(command, "busca_digits", digits);
        AddParameter(command, "busca_digits_like", digits is null ? null : $"%{digits}%");
    }

    private static Aluno MapAluno(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        Nome = reader.GetString(1),
        DataNascimento = GetNullableDateOnly(reader, 2),
        Genero = GetNullableString(reader, 3),
        Email = GetNullableString(reader, 4),
        Telefone = GetNullableString(reader, 5),
        ResponsavelNome = GetNullableString(reader, 6),
        ResponsavelTelefone = GetNullableString(reader, 7),
        Status = reader.GetString(8),
        ProfessoraId = reader.IsDBNull(9) ? null : reader.GetGuid(9),
        ProfessoraNome = GetNullableString(reader, 10),
        ProfessoraFotoUrl = GetNullableString(reader, 11),
        ValorMensalidade = reader.IsDBNull(12) ? null : reader.GetDecimal(12),
        DiaVencimento = reader.IsDBNull(13) ? null : reader.GetInt16(13),
        FormaPagamento = GetNullableString(reader, 14),
        TaxaMatricula = reader.GetDecimal(15),
        PercentualDesconto = reader.GetDecimal(16),
        Observacoes = GetNullableString(reader, 17),
        FotoUrl = GetNullableString(reader, 18),
        Ativo = reader.GetBoolean(19),
        CriadoEm = reader.GetFieldValue<DateTimeOffset>(20),
        AtualizadoEm = reader.GetFieldValue<DateTimeOffset>(21)
    };

    private static string? GetNullableString(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static DateOnly? GetNullableDateOnly(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateOnly>(ordinal);

    private static void AddParameter(NpgsqlCommand command, string name, object? value, NpgsqlDbType type = NpgsqlDbType.Text)
    {
        var parameter = new NpgsqlParameter(name, type)
        {
            Value = value ?? DBNull.Value
        };
        command.Parameters.Add(parameter);
    }
}
