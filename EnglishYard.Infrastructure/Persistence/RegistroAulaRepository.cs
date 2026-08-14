using EnglishYard.Application.Aulas;
using Npgsql;
using NpgsqlTypes;

namespace EnglishYard.Infrastructure.Persistence;

public sealed class RegistroAulaRepository(NpgsqlDataSource dataSource) : IRegistroAulaRepository
{
    public async Task<IReadOnlyList<OcorrenciaAulaData>> ListarDiaAsync(
        DateOnly data,
        Guid? professoraId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            with recorrentes as (
                select
                    ('r:' || h.id::text || ':' || to_char(@data::date, 'YYYY-MM-DD')) as ocorrencia_id,
                    real.aula_id,
                    h.id as horario_recorrente_id,
                    real.reposicao_id,
                    @data::date as data_aula,
                    coalesce(real.hora_inicio, h.hora_inicio) as hora_inicio,
                    coalesce(real.hora_fim, h.hora_fim) as hora_fim,
                    a.id as aluno_id,
                    a.nome as aluno_nome,
                    p.id as professora_id,
                    p.nome as professora_nome,
                    case
                        when count(*) over (partition by h.professora_id, h.hora_inicio, h.hora_fim) > 1 then 'Grupo'
                        else 'Individual'
                    end as tipo,
                    coalesce(real.participante_status, 'agendado') as participante_status,
                    false as eh_reposicao,
                    (real.aula_id is not null) as possui_registro_real,
                    coalesce(real.elegivel_pagamento, false) as elegivel_pagamento,
                    coalesce(real.valor_pagamento, 0)::numeric as valor_pagamento,
                    coalesce(real.participante_observacao, real.aula_observacao) as observacao,
                    real.reposicao_status
                from public.horarios_recorrentes_alunos h
                join public.alunos a on a.id = h.aluno_id and a.ativo = true
                join public.professoras p on p.id = h.professora_id and p.ativo = true
                left join lateral (
                    select
                        au.id as aula_id,
                        au.hora_inicio,
                        au.hora_fim,
                        aa.status as participante_status,
                        aa.observacoes as participante_observacao,
                        au.observacoes as aula_observacao,
                        au.elegivel_pagamento,
                        au.valor_pagamento,
                        r.id as reposicao_id,
                        r.status as reposicao_status
                    from public.aula_alunos aa
                    join public.aulas au on au.id = aa.aula_id
                    left join public.reposicoes r
                      on r.aula_origem_id = au.id
                     and r.aluno_id = aa.aluno_id
                    where aa.horario_recorrente_aluno_id = h.id
                      and aa.aluno_id = h.aluno_id
                      and au.data_aula = @data
                      and au.eh_reposicao = false
                    order by au.atualizado_em desc
                    limit 1
                ) real on true
                where h.ativo = true
                  and extract(dow from @data::date)::smallint = h.dia_semana
                  and h.data_inicio <= @data
                  and (h.data_fim is null or h.data_fim >= @data)
                  and (@professora_id is null or h.professora_id = @professora_id)
            ), aulas_avulsas as (
                select
                    ('a:' || au.id::text || ':' || a.id::text) as ocorrencia_id,
                    au.id as aula_id,
                    null::uuid as horario_recorrente_id,
                    r.id as reposicao_id,
                    au.data_aula,
                    au.hora_inicio,
                    au.hora_fim,
                    a.id as aluno_id,
                    a.nome as aluno_nome,
                    p.id as professora_id,
                    p.nome as professora_nome,
                    case au.tipo_aula when 'grupo' then 'Grupo' else 'Individual' end as tipo,
                    aa.status as participante_status,
                    false as eh_reposicao,
                    true as possui_registro_real,
                    au.elegivel_pagamento,
                    au.valor_pagamento,
                    coalesce(aa.observacoes, au.observacoes) as observacao,
                    r.status as reposicao_status
                from public.aulas au
                join public.aula_alunos aa on aa.aula_id = au.id
                left join public.reposicoes r
                  on r.aula_origem_id = au.id
                 and r.aluno_id = aa.aluno_id
                join public.alunos a on a.id = aa.aluno_id and a.ativo = true
                join public.professoras p on p.id = au.professora_id and p.ativo = true
                where au.eh_reposicao = false
                  and aa.horario_recorrente_aluno_id is null
                  and au.data_aula = @data
                  and (@professora_id is null or au.professora_id = @professora_id)
            ), reposicoes_agendadas as (
                select
                    ('a:' || au.id::text) as ocorrencia_id,
                    au.id as aula_id,
                    null::uuid as horario_recorrente_id,
                    r.id as reposicao_id,
                    au.data_aula,
                    au.hora_inicio,
                    au.hora_fim,
                    a.id as aluno_id,
                    a.nome as aluno_nome,
                    p.id as professora_id,
                    p.nome as professora_nome,
                    case au.tipo_aula when 'grupo' then 'Grupo' else 'Individual' end as tipo,
                    aa.status as participante_status,
                    true as eh_reposicao,
                    true as possui_registro_real,
                    au.elegivel_pagamento,
                    au.valor_pagamento,
                    coalesce(aa.observacoes, au.observacoes) as observacao,
                    r.status as reposicao_status
                from public.aulas au
                join public.aula_alunos aa on aa.aula_id = au.id
                join public.alunos a on a.id = aa.aluno_id and a.ativo = true
                join public.professoras p on p.id = au.professora_id and p.ativo = true
                join public.reposicoes r on r.id = au.reposicao_origem_id
                where au.eh_reposicao = true
                  and au.status <> 'cancelada'
                  and au.data_aula = @data
                  and (@professora_id is null or au.professora_id = @professora_id)
            )
            select * from recorrentes
            union all
            select * from aulas_avulsas
            union all
            select * from reposicoes_agendadas
            order by hora_inicio, professora_nome, aluno_nome;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("data", NpgsqlDbType.Date, data);
        command.Parameters.Add(new NpgsqlParameter("professora_id", NpgsqlDbType.Uuid)
        {
            Value = professoraId.HasValue ? professoraId.Value : DBNull.Value
        });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<OcorrenciaAulaData>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(MapOcorrencia(reader));

        return result;
    }

    public async Task<OcorrenciaAulaData?> BuscarOcorrenciaAsync(
        string ocorrenciaId,
        Guid? professoraId,
        CancellationToken cancellationToken)
    {
        if (TryParseRecurringOccurrence(ocorrenciaId, out _, out var data))
        {
            var day = await ListarDiaAsync(data, professoraId, cancellationToken);
            return day.FirstOrDefault(item => string.Equals(item.OcorrenciaId, ocorrenciaId, StringComparison.OrdinalIgnoreCase));
        }

        if (!TryParseLessonOccurrence(ocorrenciaId, out var aulaId, out var alunoId))
            return null;

        const string dateSql = """
            select data_aula
            from public.aulas
            where id = @id
              and (@professora_id is null or professora_id = @professora_id);
            """;
        await using var command = dataSource.CreateCommand(dateSql);
        command.Parameters.AddWithValue("id", aulaId);
        command.Parameters.Add(new NpgsqlParameter("professora_id", NpgsqlDbType.Uuid)
        {
            Value = professoraId.HasValue ? professoraId.Value : DBNull.Value
        });
        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        if (scalar is not DateOnly lessonDate) return null;

        var occurrences = await ListarDiaAsync(lessonDate, professoraId, cancellationToken);
        return occurrences.FirstOrDefault(item => item.AulaId == aulaId && item.OcorrenciaId == ocorrenciaId)
            ?? (alunoId.HasValue
                ? occurrences.FirstOrDefault(item => item.AulaId == aulaId && item.AlunoId == alunoId.Value)
                : null)
            ?? occurrences.FirstOrDefault(item => item.AulaId == aulaId);
    }

    public async Task<OcorrenciaAulaData> RegistrarResultadoAsync(
        RegistroAulaPersistenciaRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var committed = false;

        try
        {
            var occurrence = request.Ocorrencia;
            var aulaId = occurrence.AulaId ?? await EnsureRecurringLessonAsync(connection, transaction, occurrence, request.UsuarioAuthId, cancellationToken);
            var previousStatus = await GetParticipantStatusAsync(connection, transaction, aulaId, occurrence.AlunoId, cancellationToken) ?? "agendado";
            var databaseStatus = ToDatabaseParticipantStatus(request.Status);

            await EnsureParticipantAsync(
                connection,
                transaction,
                aulaId,
                occurrence.AlunoId,
                occurrence.HorarioRecorrenteId,
                cancellationToken);

            await SyncRecurringLessonTypeAsync(
                connection,
                transaction,
                aulaId,
                occurrence,
                cancellationToken);

            const string updateParticipantSql = """
                update public.aula_alunos
                set status = @status,
                    observacoes = @observacoes,
                    atualizado_em = now()
                where aula_id = @aula_id and aluno_id = @aluno_id;
                """;
            await using (var command = new NpgsqlCommand(updateParticipantSql, connection, transaction))
            {
                command.Parameters.AddWithValue("status", databaseStatus);
                command.Parameters.Add(new NpgsqlParameter("observacoes", NpgsqlDbType.Text)
                {
                    Value = request.Observacao ?? (object)DBNull.Value
                });
                command.Parameters.AddWithValue("aula_id", aulaId);
                command.Parameters.AddWithValue("aluno_id", occurrence.AlunoId);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            Guid? reposicaoId = null;
            ReplacementCreditUpsertResult? replacementCredit = null;
            if (request.Status is ResultadoAulaCodigos.RemarcadaAluno or ResultadoAulaCodigos.RemarcadaProfessora)
            {
                replacementCredit = await UpsertReplacementCreditAsync(
                    connection,
                    transaction,
                    aulaId,
                    occurrence.AlunoId,
                    request.Status,
                    request.Observacao,
                    cancellationToken);
                reposicaoId = replacementCredit.Id;
            }
            else if (!occurrence.EhReposicao)
            {
                await RemoveOpenReplacementCreditAsync(connection, transaction, aulaId, occurrence.AlunoId, cancellationToken);
            }

            await RecalculateLessonAsync(connection, transaction, aulaId, cancellationToken);

            ReplacementOriginData? completedReplacementOrigin = null;
            if (occurrence.EhReposicao
                && (request.Status is ResultadoAulaCodigos.Aplicada or ResultadoAulaCodigos.FaltaAluno)
                && occurrence.ReposicaoId.HasValue)
            {
                completedReplacementOrigin = await GetReplacementOriginAsync(
                    connection, transaction, occurrence.ReposicaoId.Value, cancellationToken);
                await MarkReplacementCompletedAsync(connection, transaction, occurrence.ReposicaoId.Value, cancellationToken);
            }

            await InsertHistoryAsync(
                connection,
                transaction,
                aulaId,
                occurrence.AlunoId,
                previousStatus == "agendado" ? "resultado_registrado" : "resultado_alterado",
                ToPublicStatus(previousStatus),
                request.Status,
                request.Observacao,
                request.UsuarioAuthId,
                request.UsuarioNome,
                cancellationToken);

            if (replacementCredit is not null)
            {
                await InsertHistoryAsync(
                    connection,
                    transaction,
                    aulaId,
                    occurrence.AlunoId,
                    replacementCredit.Created ? "reposicao_criada" : "reposicao_atualizada",
                    null,
                    replacementCredit.Status,
                    request.Observacao,
                    request.UsuarioAuthId,
                    request.UsuarioNome,
                    cancellationToken);
            }

            if (completedReplacementOrigin is not null)
            {
                await InsertHistoryAsync(
                    connection,
                    transaction,
                    completedReplacementOrigin.AulaOrigemId,
                    completedReplacementOrigin.AlunoId,
                    "reposicao_concluida",
                    completedReplacementOrigin.Status,
                    "concluida",
                    request.Observacao,
                    request.UsuarioAuthId,
                    request.UsuarioNome,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            committed = true;

            var refreshedId = occurrence.EhReposicao
                ? $"a:{aulaId}"
                : occurrence.OcorrenciaId;
            var refreshed = await BuscarOcorrenciaAsync(refreshedId, occurrence.ProfessoraId, cancellationToken);
            if (refreshed is null)
                throw new RegistroAulaNotFoundException("A aula foi salva, mas não pôde ser recarregada.");

            return refreshed with { ReposicaoId = refreshed.ReposicaoId ?? reposicaoId };
        }
        catch
        {
            if (!committed) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<OcorrenciaAulaData> AtualizarOcorrenciaAsync(
        AtualizarOcorrenciaPersistenciaRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var committed = false;

        try
        {
            var occurrence = request.Ocorrencia;
            if (await ExisteConflitoHorarioAsync(
                    connection,
                    transaction,
                    occurrence,
                    request.HoraInicio,
                    request.HoraFim,
                    cancellationToken))
            {
                throw new RegistroAulaConflictException(
                    $"A professora {occurrence.ProfessoraNome} já possui outra aula que conflita com " +
                    $"{request.HoraInicio:HH:mm}–{request.HoraFim:HH:mm} neste dia.");
            }

            var aulaId = occurrence.AulaId;
            if (aulaId.HasValue)
            {
                var participantCount = await ContarParticipantesAsync(connection, transaction, aulaId.Value, cancellationToken);
                if (participantCount > 1)
                {
                    const string detachSql = "delete from public.aula_alunos where aula_id = @aula_id and aluno_id = @aluno_id;";
                    await using (var detach = new NpgsqlCommand(detachSql, connection, transaction))
                    {
                        detach.Parameters.AddWithValue("aula_id", aulaId.Value);
                        detach.Parameters.AddWithValue("aluno_id", occurrence.AlunoId);
                        await detach.ExecuteNonQueryAsync(cancellationToken);
                    }

                    await RecalculateLessonAsync(connection, transaction, aulaId.Value, cancellationToken);
                    aulaId = await CriarAulaIndividualAsync(
                        connection,
                        transaction,
                        occurrence,
                        request.HoraInicio,
                        request.HoraFim,
                        request.Observacao,
                        request.UsuarioAuthId,
                        cancellationToken);
                }
                else
                {
                    const string updateSql = """
                        update public.aulas
                        set hora_inicio = @inicio,
                            hora_fim = @fim,
                            tipo_aula = 'individual',
                            observacoes = coalesce(@observacao, observacoes),
                            atualizado_em = now()
                        where id = @id;
                        """;
                    await using var update = new NpgsqlCommand(updateSql, connection, transaction);
                    update.Parameters.AddWithValue("inicio", NpgsqlDbType.Time, request.HoraInicio);
                    update.Parameters.AddWithValue("fim", NpgsqlDbType.Time, request.HoraFim);
                    update.Parameters.Add(new NpgsqlParameter("observacao", NpgsqlDbType.Text)
                    {
                        Value = request.Observacao ?? (object)DBNull.Value
                    });
                    update.Parameters.AddWithValue("id", aulaId.Value);
                    await update.ExecuteNonQueryAsync(cancellationToken);
                }
            }
            else
            {
                aulaId = await CriarAulaIndividualAsync(
                    connection,
                    transaction,
                    occurrence,
                    request.HoraInicio,
                    request.HoraFim,
                    request.Observacao,
                    request.UsuarioAuthId,
                    cancellationToken);
            }

            await EnsureParticipantAsync(
                connection,
                transaction,
                aulaId.Value,
                occurrence.AlunoId,
                occurrence.HorarioRecorrenteId,
                cancellationToken);

            var historyObservation =
                $"Horário alterado de {occurrence.HoraInicio:HH:mm}–{occurrence.HoraFim:HH:mm} " +
                $"para {request.HoraInicio:HH:mm}–{request.HoraFim:HH:mm}.";
            if (!string.IsNullOrWhiteSpace(request.Observacao))
                historyObservation += $" {request.Observacao}";

            await InsertHistoryAsync(
                connection,
                transaction,
                aulaId.Value,
                occurrence.AlunoId,
                "ocorrencia_horario_alterado",
                $"{occurrence.HoraInicio:HH:mm}-{occurrence.HoraFim:HH:mm}",
                $"{request.HoraInicio:HH:mm}-{request.HoraFim:HH:mm}",
                historyObservation,
                request.UsuarioAuthId,
                request.UsuarioNome,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            committed = true;

            var refreshed = await BuscarOcorrenciaAsync(occurrence.OcorrenciaId, occurrence.ProfessoraId, cancellationToken);
            return refreshed ?? throw new RegistroAulaNotFoundException(
                "O horário foi alterado, mas a ocorrência não pôde ser recarregada.");
        }
        catch
        {
            if (!committed) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<OcorrenciaAulaData> CancelarOcorrenciaAsync(
        CancelarOcorrenciaPersistenciaRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var committed = false;

        try
        {
            var occurrence = request.Ocorrencia;
            var aulaId = occurrence.AulaId
                ?? await EnsureRecurringLessonAsync(connection, transaction, occurrence, request.UsuarioAuthId, cancellationToken);

            await EnsureParticipantAsync(
                connection,
                transaction,
                aulaId,
                occurrence.AlunoId,
                occurrence.HorarioRecorrenteId,
                cancellationToken);

            await SyncRecurringLessonTypeAsync(
                connection,
                transaction,
                aulaId,
                occurrence,
                cancellationToken);

            var previousStatus = await GetParticipantStatusAsync(
                connection, transaction, aulaId, occurrence.AlunoId, cancellationToken) ?? "agendado";

            const string updateSql = """
                update public.aula_alunos
                set status = 'cancelada',
                    observacoes = @motivo,
                    atualizado_em = now()
                where aula_id = @aula_id and aluno_id = @aluno_id;
                """;
            await using (var command = new NpgsqlCommand(updateSql, connection, transaction))
            {
                command.Parameters.Add(new NpgsqlParameter("motivo", NpgsqlDbType.Text)
                {
                    Value = request.Motivo ?? (object)DBNull.Value
                });
                command.Parameters.AddWithValue("aula_id", aulaId);
                command.Parameters.AddWithValue("aluno_id", occurrence.AlunoId);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await RecalculateLessonAsync(connection, transaction, aulaId, cancellationToken);
            await InsertHistoryAsync(
                connection,
                transaction,
                aulaId,
                occurrence.AlunoId,
                "aula_cancelada",
                ToPublicStatus(previousStatus),
                ResultadoAulaCodigos.Cancelada,
                request.Motivo,
                request.UsuarioAuthId,
                request.UsuarioNome,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            committed = true;

            var refreshed = await BuscarOcorrenciaAsync(occurrence.OcorrenciaId, occurrence.ProfessoraId, cancellationToken);
            return refreshed ?? throw new RegistroAulaNotFoundException(
                "A aula foi cancelada, mas não pôde ser recarregada.");
        }
        catch
        {
            if (!committed) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<OcorrenciaAulaData> AgendarAulaAvulsaAsync(
        AgendarAulaAvulsaPersistenciaRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var committed = false;

        try
        {
            var alunoIds = (request.AlunoIds ?? [])
                .Append(request.AlunoId)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToArray();
            if (alunoIds.Length == 0)
                throw new RegistroAulaValidationException("Selecione pelo menos um aluno para a nova aula.");

            if (!await TeacherExistsAsync(connection, transaction, request.ProfessoraId, cancellationToken))
                throw new RegistroAulaNotFoundException("A professora selecionada não foi encontrada ou está inativa.");

            foreach (var alunoId in alunoIds)
            {
                if (!await StudentExistsAsync(connection, transaction, alunoId, cancellationToken))
                    throw new RegistroAulaNotFoundException("Um dos alunos selecionados não foi encontrado ou está inativo.");

                if (request.ProfessoraRestritaId.HasValue
                    && !await StudentBelongsToTeacherAsync(
                        connection,
                        transaction,
                        alunoId,
                        request.ProfessoraRestritaId.Value,
                        cancellationToken))
                {
                    throw new RegistroAulaNotFoundException("Um dos alunos selecionados não está vinculado à professora logada.");
                }
            }

            foreach (var alunoId in alunoIds)
            {
                if (await ExisteConflitoNovaAulaAsync(
                        connection,
                        transaction,
                        request.ProfessoraId,
                        alunoId,
                        request.Data,
                        request.HoraInicio,
                        request.HoraFim,
                        cancellationToken))
                {
                    throw new RegistroAulaConflictException(
                        $"Já existe uma aula do aluno ou da professora que conflita com " +
                        $"{request.HoraInicio:HH:mm}–{request.HoraFim:HH:mm} nessa data.");
                }
            }

            var tipo = alunoIds.Length > 1 ? "grupo" : "individual";
            const string insertLessonSql = """
                insert into public.aulas (
                    professora_id, tipo_aula, titulo, descricao, data_aula, hora_inicio, hora_fim,
                    status, observacoes, eh_reposicao, elegivel_pagamento,
                    valor_aula_aplicado, valor_pagamento, status_pagamento, criado_por)
                values (
                    @professora_id, @tipo, 'Aula avulsa', @observacao, @data, @inicio, @fim,
                    'agendada', @observacao, false, false,
                    0, 0, 'pending', @criado_por)
                returning id;
                """;

            Guid aulaId;
            await using (var command = new NpgsqlCommand(insertLessonSql, connection, transaction))
            {
                command.Parameters.AddWithValue("professora_id", request.ProfessoraId);
                command.Parameters.AddWithValue("tipo", tipo);
                command.Parameters.Add(new NpgsqlParameter("observacao", NpgsqlDbType.Text)
                {
                    Value = request.Observacao ?? (object)DBNull.Value
                });
                command.Parameters.AddWithValue("data", NpgsqlDbType.Date, request.Data);
                command.Parameters.AddWithValue("inicio", NpgsqlDbType.Time, request.HoraInicio);
                command.Parameters.AddWithValue("fim", NpgsqlDbType.Time, request.HoraFim);
                command.Parameters.AddWithValue("criado_por", request.UsuarioAuthId);
                aulaId = (Guid)(await command.ExecuteScalarAsync(cancellationToken)
                    ?? throw new InvalidOperationException("Não foi possível criar a nova aula."));
            }

            const string insertParticipantSql = """
                insert into public.aula_alunos (aula_id, aluno_id, status, observacoes, horario_recorrente_aluno_id)
                values (@aula_id, @aluno_id, 'agendado', @observacao, null);
                """;
            foreach (var alunoId in alunoIds)
            {
                await using var command = new NpgsqlCommand(insertParticipantSql, connection, transaction);
                command.Parameters.AddWithValue("aula_id", aulaId);
                command.Parameters.AddWithValue("aluno_id", alunoId);
                command.Parameters.Add(new NpgsqlParameter("observacao", NpgsqlDbType.Text)
                {
                    Value = request.Observacao ?? (object)DBNull.Value
                });
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            var historyObservation =
                $"Aula avulsa {tipo} agendada para {request.Data:dd/MM/yyyy}, " +
                $"{request.HoraInicio:HH:mm}–{request.HoraFim:HH:mm} com {alunoIds.Length} participante(s).";
            if (!string.IsNullOrWhiteSpace(request.Observacao))
                historyObservation += $" {request.Observacao}";

            foreach (var alunoId in alunoIds)
            {
                await InsertHistoryAsync(
                    connection,
                    transaction,
                    aulaId,
                    alunoId,
                    "aula_avulsa_agendada",
                    null,
                    "agendada",
                    historyObservation,
                    request.UsuarioAuthId,
                    request.UsuarioNome,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            committed = true;

            return await BuscarOcorrenciaAsync($"a:{aulaId}:{alunoIds[0]}", request.ProfessoraId, cancellationToken)
                ?? throw new RegistroAulaNotFoundException("A aula foi criada, mas não pôde ser recarregada.");
        }
        catch
        {
            if (!committed) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<HistoricoAulaResponse>> ListarHistoricoAsync(
        Guid aulaId,
        Guid? professoraId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select h.id, h.aula_id, h.aluno_id, h.acao, h.status_anterior, h.status_novo,
                   h.observacao, h.alterado_por_nome, h.alterado_em
            from public.historico_aulas h
            join public.aulas a on a.id = h.aula_id
            where h.aula_id = @aula_id
              and (@professora_id is null or a.professora_id = @professora_id)
            order by h.alterado_em desc;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("aula_id", aulaId);
        command.Parameters.Add(new NpgsqlParameter("professora_id", NpgsqlDbType.Uuid)
        {
            Value = professoraId.HasValue ? professoraId.Value : DBNull.Value
        });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<HistoricoAulaResponse>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new HistoricoAulaResponse(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.GetString(3),
                GetNullableString(reader, 4),
                GetNullableString(reader, 5),
                GetNullableString(reader, 6),
                reader.GetString(7),
                reader.GetFieldValue<DateTimeOffset>(8)));
        }
        return result;
    }

    public async Task<IReadOnlyList<ReposicaoResponse>> ListarReposicoesAsync(
        Guid? professoraId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select
                r.id,
                r.aula_origem_id,
                r.aluno_id,
                al.nome,
                origem.professora_id,
                po.nome,
                r.motivo,
                r.status,
                origem.data_aula,
                origem.hora_inicio,
                origem.hora_fim,
                r.observacao_origem,
                r.data_agendada,
                r.hora_inicio,
                r.hora_fim,
                r.professora_agendada_id,
                pa.nome,
                r.observacao_agendamento,
                reposicao_aula.id,
                r.criado_em,
                r.concluida_em
            from public.reposicoes r
            join public.aulas origem on origem.id = r.aula_origem_id
            join public.alunos al on al.id = r.aluno_id
            join public.professoras po on po.id = origem.professora_id
            left join public.professoras pa on pa.id = r.professora_agendada_id
            left join lateral (
                select au.id
                from public.aulas au
                where au.reposicao_origem_id = r.id
                  and au.eh_reposicao = true
                order by au.atualizado_em desc
                limit 1
            ) reposicao_aula on true
            where @professora_id is null
               or origem.professora_id = @professora_id
               or r.professora_agendada_id = @professora_id
            order by
                case r.status when 'pendente' then 0 when 'agendada' then 1 else 2 end,
                coalesce(r.data_agendada, origem.data_aula),
                coalesce(r.hora_inicio, origem.hora_inicio);
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.Add(new NpgsqlParameter("professora_id", NpgsqlDbType.Uuid)
        {
            Value = professoraId.HasValue ? professoraId.Value : DBNull.Value
        });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ReposicaoResponse>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(MapReposicao(reader));
        return result;
    }

    public async Task<ReposicaoResponse?> BuscarReposicaoAsync(
        Guid reposicaoId,
        Guid? professoraId,
        CancellationToken cancellationToken)
    {
        var list = await ListarReposicoesAsync(professoraId, cancellationToken);
        return list.FirstOrDefault(item => item.Id == reposicaoId);
    }

    public async Task<ReposicaoResponse> AgendarReposicaoAsync(
        Guid reposicaoId,
        AgendarReposicaoRequest request,
        Guid usuarioAuthId,
        string usuarioNome,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var committed = false;
        try
        {
            var origin = await GetReplacementOriginAsync(connection, transaction, reposicaoId, cancellationToken)
                ?? throw new RegistroAulaNotFoundException("A reposição não foi encontrada.");

            if (origin.Status == "concluida")
                throw new RegistroAulaConflictException("A reposição já foi concluída.");

            if (!await TeacherExistsAsync(connection, transaction, request.ProfessoraId, cancellationToken))
                throw new RegistroAulaValidationException("A professora escolhida para a reposição não está ativa.");

            const string updateSql = """
                update public.reposicoes
                set status = 'agendada',
                    data_agendada = @data,
                    hora_inicio = @inicio,
                    hora_fim = @fim,
                    professora_agendada_id = @professora_id,
                    observacao_agendamento = @observacao,
                    concluida_em = null,
                    atualizado_em = now()
                where id = @id;
                """;
            await using (var command = new NpgsqlCommand(updateSql, connection, transaction))
            {
                command.Parameters.AddWithValue("data", NpgsqlDbType.Date, request.Data);
                command.Parameters.AddWithValue("inicio", NpgsqlDbType.Time, request.HoraInicio);
                command.Parameters.AddWithValue("fim", NpgsqlDbType.Time, request.HoraFim);
                command.Parameters.AddWithValue("professora_id", request.ProfessoraId);
                command.Parameters.Add(new NpgsqlParameter("observacao", NpgsqlDbType.Text) { Value = request.Observacao ?? (object)DBNull.Value });
                command.Parameters.AddWithValue("id", reposicaoId);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            var aulaReposicaoId = await EnsureReplacementLessonAsync(
                connection,
                transaction,
                reposicaoId,
                origin.AlunoId,
                request.ProfessoraId,
                request.Data,
                request.HoraInicio,
                request.HoraFim,
                request.Observacao,
                usuarioAuthId,
                cancellationToken);

            var scheduleHistoryAction = origin.Status == "agendada" ? "reposicao_reagendada" : "reposicao_agendada";
            await InsertHistoryAsync(
                connection,
                transaction,
                aulaReposicaoId,
                origin.AlunoId,
                scheduleHistoryAction,
                origin.Status,
                "agendada",
                request.Observacao,
                usuarioAuthId,
                usuarioNome,
                cancellationToken);
            await InsertHistoryAsync(
                connection,
                transaction,
                origin.AulaOrigemId,
                origin.AlunoId,
                scheduleHistoryAction,
                origin.Status,
                "agendada",
                request.Observacao,
                usuarioAuthId,
                usuarioNome,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            committed = true;
            return await BuscarReposicaoAsync(reposicaoId, null, cancellationToken)
                ?? throw new RegistroAulaNotFoundException("A reposição foi agendada, mas não pôde ser recarregada.");
        }
        catch
        {
            if (!committed) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ReposicaoResponse> CancelarAgendamentoReposicaoAsync(
        Guid reposicaoId,
        Guid usuarioAuthId,
        string usuarioNome,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var committed = false;
        try
        {
            var origin = await GetReplacementOriginAsync(connection, transaction, reposicaoId, cancellationToken)
                ?? throw new RegistroAulaNotFoundException("A reposição não foi encontrada.");
            if (origin.Status == "concluida")
                throw new RegistroAulaConflictException("Uma reposição concluída não pode ser cancelada.");

            var aulaReposicaoId = await GetReplacementLessonIdAsync(connection, transaction, reposicaoId, cancellationToken);
            if (aulaReposicaoId.HasValue)
            {
                const string cancelLessonSql = """
                    update public.aulas
                    set status = 'cancelada', elegivel_pagamento = false, valor_pagamento = 0, atualizado_em = now()
                    where id = @id and status <> 'realizada';
                    update public.aula_alunos
                    set status = 'cancelada', atualizado_em = now()
                    where aula_id = @id and status <> 'aplicada';
                    """;
                await using var cancelCommand = new NpgsqlCommand(cancelLessonSql, connection, transaction);
                cancelCommand.Parameters.AddWithValue("id", aulaReposicaoId.Value);
                await cancelCommand.ExecuteNonQueryAsync(cancellationToken);

                await InsertHistoryAsync(
                    connection,
                    transaction,
                    aulaReposicaoId.Value,
                    origin.AlunoId,
                    "reposicao_agendamento_cancelado",
                    "agendada",
                    "pendente",
                    null,
                    usuarioAuthId,
                    usuarioNome,
                    cancellationToken);
                await InsertHistoryAsync(
                    connection,
                    transaction,
                    origin.AulaOrigemId,
                    origin.AlunoId,
                    "reposicao_agendamento_cancelado",
                    "agendada",
                    "pendente",
                    null,
                    usuarioAuthId,
                    usuarioNome,
                    cancellationToken);
            }

            const string updateRepoSql = """
                update public.reposicoes
                set status = 'pendente',
                    data_agendada = null,
                    hora_inicio = null,
                    hora_fim = null,
                    professora_agendada_id = null,
                    observacao_agendamento = null,
                    concluida_em = null,
                    atualizado_em = now()
                where id = @id;
                """;
            await using (var command = new NpgsqlCommand(updateRepoSql, connection, transaction))
            {
                command.Parameters.AddWithValue("id", reposicaoId);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            committed = true;
            return await BuscarReposicaoAsync(reposicaoId, null, cancellationToken)
                ?? throw new RegistroAulaNotFoundException("A reposição não pôde ser recarregada.");
        }
        catch
        {
            if (!committed) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static OcorrenciaAulaData MapOcorrencia(NpgsqlDataReader reader) => new(
        reader.GetString(0),
        reader.IsDBNull(1) ? null : reader.GetGuid(1),
        reader.IsDBNull(2) ? null : reader.GetGuid(2),
        reader.IsDBNull(3) ? null : reader.GetGuid(3),
        reader.GetFieldValue<DateOnly>(4),
        reader.GetFieldValue<TimeOnly>(5),
        reader.GetFieldValue<TimeOnly>(6),
        reader.GetGuid(7),
        reader.GetString(8),
        reader.GetGuid(9),
        reader.GetString(10),
        reader.GetString(11),
        reader.GetString(12),
        reader.GetBoolean(13),
        reader.GetBoolean(14),
        reader.GetBoolean(15),
        reader.GetDecimal(16),
        GetNullableString(reader, 17),
        GetNullableString(reader, 18));

    private static ReposicaoResponse MapReposicao(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetGuid(2),
        reader.GetString(3),
        reader.GetGuid(4),
        reader.GetString(5),
        reader.GetString(6),
        reader.GetString(7),
        reader.GetFieldValue<DateOnly>(8),
        reader.GetFieldValue<TimeOnly>(9),
        reader.GetFieldValue<TimeOnly>(10),
        GetNullableString(reader, 11),
        reader.IsDBNull(12) ? null : reader.GetFieldValue<DateOnly>(12),
        reader.IsDBNull(13) ? null : reader.GetFieldValue<TimeOnly>(13),
        reader.IsDBNull(14) ? null : reader.GetFieldValue<TimeOnly>(14),
        reader.IsDBNull(15) ? null : reader.GetGuid(15),
        GetNullableString(reader, 16),
        GetNullableString(reader, 17),
        reader.IsDBNull(18) ? null : reader.GetGuid(18),
        reader.GetFieldValue<DateTimeOffset>(19),
        reader.IsDBNull(20) ? null : reader.GetFieldValue<DateTimeOffset>(20));

    private static async Task<bool> ExisteConflitoNovaAulaAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid professoraId,
        Guid alunoId,
        DateOnly data,
        TimeOnly horaInicio,
        TimeOnly horaFim,
        CancellationToken cancellationToken)
    {
        const string sql = """
            with agenda_recorrente as (
                select
                    h.professora_id,
                    h.aluno_id,
                    coalesce(real.hora_inicio, h.hora_inicio) as hora_inicio,
                    coalesce(real.hora_fim, h.hora_fim) as hora_fim,
                    coalesce(real.participante_status, 'agendado') as participante_status,
                    coalesce(real.aula_status, 'agendada') as aula_status
                from public.horarios_recorrentes_alunos h
                join public.alunos a on a.id = h.aluno_id and a.ativo = true
                join public.professoras p on p.id = h.professora_id and p.ativo = true
                left join lateral (
                    select
                        au.hora_inicio,
                        au.hora_fim,
                        au.status as aula_status,
                        aa.status as participante_status
                    from public.aula_alunos aa
                    join public.aulas au on au.id = aa.aula_id
                    where aa.horario_recorrente_aluno_id = h.id
                      and aa.aluno_id = h.aluno_id
                      and au.data_aula = @data
                      and au.eh_reposicao = false
                    order by au.atualizado_em desc
                    limit 1
                ) real on true
                where h.ativo = true
                  and h.dia_semana = extract(dow from @data::date)::smallint
                  and h.data_inicio <= @data
                  and (h.data_fim is null or h.data_fim >= @data)
            )
            select
                exists (
                    select 1
                    from agenda_recorrente ar
                    where ar.professora_id = @professora_id
                      and ar.aula_status not in ('cancelada', 'remarcada')
                      and ar.participante_status not in ('cancelada', 'remarcada_aluno', 'remarcada_professora')
                      and ar.hora_inicio < @fim
                      and ar.hora_fim > @inicio
                )
                or exists (
                    select 1
                    from agenda_recorrente ar
                    where ar.aluno_id = @aluno_id
                      and ar.aula_status not in ('cancelada', 'remarcada')
                      and ar.participante_status not in ('cancelada', 'remarcada_aluno', 'remarcada_professora')
                      and ar.hora_inicio < @fim
                      and ar.hora_fim > @inicio
                )
                or exists (
                    select 1
                    from public.aulas au
                    join public.aula_alunos aa on aa.aula_id = au.id
                    where au.data_aula = @data
                      and au.status not in ('cancelada', 'remarcada')
                      and aa.status not in ('cancelada', 'remarcada_aluno', 'remarcada_professora')
                      and (au.professora_id = @professora_id or aa.aluno_id = @aluno_id)
                      and au.hora_inicio < @fim
                      and au.hora_fim > @inicio
                );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("professora_id", professoraId);
        command.Parameters.AddWithValue("aluno_id", alunoId);
        command.Parameters.AddWithValue("data", NpgsqlDbType.Date, data);
        command.Parameters.AddWithValue("inicio", NpgsqlDbType.Time, horaInicio);
        command.Parameters.AddWithValue("fim", NpgsqlDbType.Time, horaFim);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<int> ContarParticipantesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid aulaId,
        CancellationToken cancellationToken)
    {
        const string sql = "select count(*) from public.aula_alunos where aula_id = @aula_id;";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("aula_id", aulaId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    private static async Task<bool> ExisteConflitoHorarioAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OcorrenciaAulaData occurrence,
        TimeOnly horaInicio,
        TimeOnly horaFim,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select exists (
                select 1
                from public.horarios_recorrentes_alunos h
                join public.alunos a on a.id = h.aluno_id and a.ativo = true
                left join lateral (
                    select au.id, au.hora_inicio, au.hora_fim, aa.status
                    from public.aula_alunos aa
                    join public.aulas au on au.id = aa.aula_id
                    where aa.horario_recorrente_aluno_id = h.id
                      and aa.aluno_id = h.aluno_id
                      and au.data_aula = @data
                      and au.eh_reposicao = false
                    order by au.atualizado_em desc
                    limit 1
                ) real on true
                where h.professora_id = @professora_id
                  and h.ativo = true
                  and h.id <> coalesce(@horario_id, '00000000-0000-0000-0000-000000000000'::uuid)
                  and h.dia_semana = extract(dow from @data::date)::smallint
                  and h.data_inicio <= @data
                  and (h.data_fim is null or h.data_fim >= @data)
                  and coalesce(real.status, 'agendado') <> 'cancelada'
                  and coalesce(real.hora_inicio, h.hora_inicio) < @fim
                  and coalesce(real.hora_fim, h.hora_fim) > @inicio
            ) or exists (
                select 1
                from public.aulas au
                join public.aula_alunos aa on aa.aula_id = au.id
                where au.professora_id = @professora_id
                  and au.data_aula = @data
                  and au.status <> 'cancelada'
                  and aa.status <> 'cancelada'
                  and au.id <> coalesce(@aula_id, '00000000-0000-0000-0000-000000000000'::uuid)
                  and au.hora_inicio < @fim
                  and au.hora_fim > @inicio
                  and (aa.horario_recorrente_aluno_id is null
                       or aa.horario_recorrente_aluno_id <> coalesce(@horario_id, '00000000-0000-0000-0000-000000000000'::uuid))
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("professora_id", occurrence.ProfessoraId);
        command.Parameters.AddWithValue("data", NpgsqlDbType.Date, occurrence.Data);
        command.Parameters.AddWithValue("inicio", NpgsqlDbType.Time, horaInicio);
        command.Parameters.AddWithValue("fim", NpgsqlDbType.Time, horaFim);
        command.Parameters.Add(new NpgsqlParameter("horario_id", NpgsqlDbType.Uuid)
        {
            Value = occurrence.HorarioRecorrenteId.HasValue ? occurrence.HorarioRecorrenteId.Value : DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("aula_id", NpgsqlDbType.Uuid)
        {
            Value = occurrence.AulaId.HasValue ? occurrence.AulaId.Value : DBNull.Value
        });
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<Guid> CriarAulaIndividualAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OcorrenciaAulaData occurrence,
        TimeOnly horaInicio,
        TimeOnly horaFim,
        string? observacao,
        Guid usuarioAuthId,
        CancellationToken cancellationToken)
    {
        const string insertSql = """
            insert into public.aulas (
                professora_id, tipo_aula, data_aula, hora_inicio, hora_fim,
                status, observacoes, eh_reposicao, elegivel_pagamento,
                valor_aula_aplicado, valor_pagamento, status_pagamento, criado_por)
            values (
                @professora_id, 'individual', @data, @inicio, @fim,
                'agendada', @observacao, false, false,
                0, 0, 'pending', @criado_por)
            returning id;
            """;
        Guid aulaId;
        await using (var command = new NpgsqlCommand(insertSql, connection, transaction))
        {
            command.Parameters.AddWithValue("professora_id", occurrence.ProfessoraId);
            command.Parameters.AddWithValue("data", NpgsqlDbType.Date, occurrence.Data);
            command.Parameters.AddWithValue("inicio", NpgsqlDbType.Time, horaInicio);
            command.Parameters.AddWithValue("fim", NpgsqlDbType.Time, horaFim);
            command.Parameters.Add(new NpgsqlParameter("observacao", NpgsqlDbType.Text)
            {
                Value = observacao ?? (object)DBNull.Value
            });
            command.Parameters.AddWithValue("criado_por", usuarioAuthId);
            aulaId = (Guid)(await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Não foi possível criar a exceção da aula."));
        }

        await EnsureParticipantAsync(
            connection,
            transaction,
            aulaId,
            occurrence.AlunoId,
            occurrence.HorarioRecorrenteId,
            cancellationToken);
        return aulaId;
    }

    private async Task<Guid> EnsureRecurringLessonAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OcorrenciaAulaData occurrence,
        Guid usuarioAuthId,
        CancellationToken cancellationToken)
    {
        const string findSql = """
            select id
            from public.aulas
            where professora_id = @professora_id
              and data_aula = @data
              and hora_inicio = @inicio
              and hora_fim = @fim
              and eh_reposicao = false
            order by atualizado_em desc
            limit 1;
            """;
        await using (var command = new NpgsqlCommand(findSql, connection, transaction))
        {
            command.Parameters.AddWithValue("professora_id", occurrence.ProfessoraId);
            command.Parameters.AddWithValue("data", NpgsqlDbType.Date, occurrence.Data);
            command.Parameters.AddWithValue("inicio", NpgsqlDbType.Time, occurrence.HoraInicio);
            command.Parameters.AddWithValue("fim", NpgsqlDbType.Time, occurrence.HoraFim);
            var existing = await command.ExecuteScalarAsync(cancellationToken);
            if (existing is Guid existingId)
            {
                await PopulateScheduledParticipantsAsync(connection, transaction, existingId, occurrence, cancellationToken);
                await SyncRecurringLessonTypeAsync(connection, transaction, existingId, occurrence, cancellationToken);
                return existingId;
            }
        }

        var type = await CountScheduledParticipantsAsync(connection, transaction, occurrence, cancellationToken) > 1 ? "grupo" : "individual";
        const string insertSql = """
            insert into public.aulas (
                professora_id, tipo_aula, data_aula, hora_inicio, hora_fim,
                status, observacoes, eh_reposicao, elegivel_pagamento,
                valor_aula_aplicado, valor_pagamento, status_pagamento, criado_por)
            values (
                @professora_id, @tipo, @data, @inicio, @fim,
                'agendada', null, false, false,
                0, 0, 'pending', @criado_por)
            returning id;
            """;
        Guid aulaId;
        await using (var command = new NpgsqlCommand(insertSql, connection, transaction))
        {
            command.Parameters.AddWithValue("professora_id", occurrence.ProfessoraId);
            command.Parameters.AddWithValue("tipo", type);
            command.Parameters.AddWithValue("data", NpgsqlDbType.Date, occurrence.Data);
            command.Parameters.AddWithValue("inicio", NpgsqlDbType.Time, occurrence.HoraInicio);
            command.Parameters.AddWithValue("fim", NpgsqlDbType.Time, occurrence.HoraFim);
            command.Parameters.AddWithValue("criado_por", usuarioAuthId);
            aulaId = (Guid)(await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Não foi possível criar a ocorrência da aula."));
        }

        await PopulateScheduledParticipantsAsync(connection, transaction, aulaId, occurrence, cancellationToken);
        return aulaId;
    }

    private static async Task SyncRecurringLessonTypeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid aulaId,
        OcorrenciaAulaData occurrence,
        CancellationToken cancellationToken)
    {
        if (occurrence.EhReposicao || !occurrence.HorarioRecorrenteId.HasValue)
            return;

        var participantCount = await CountScheduledParticipantsAsync(connection, transaction, occurrence, cancellationToken);
        var type = participantCount > 1 ? "grupo" : "individual";

        const string sql = """
            update public.aulas
            set tipo_aula = @tipo,
                atualizado_em = now()
            where id = @aula_id
              and eh_reposicao = false
              and tipo_aula <> @tipo;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tipo", type);
        command.Parameters.AddWithValue("aula_id", aulaId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> CountScheduledParticipantsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OcorrenciaAulaData occurrence,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select count(*)
            from public.horarios_recorrentes_alunos h
            join public.alunos a on a.id = h.aluno_id and a.ativo = true
            where h.professora_id = @professora_id
              and h.ativo = true
              and h.dia_semana = extract(dow from @data::date)::smallint
              and h.hora_inicio = @inicio
              and h.hora_fim = @fim
              and h.data_inicio <= @data
              and (h.data_fim is null or h.data_fim >= @data);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("professora_id", occurrence.ProfessoraId);
        command.Parameters.AddWithValue("data", NpgsqlDbType.Date, occurrence.Data);
        command.Parameters.AddWithValue("inicio", NpgsqlDbType.Time, occurrence.HoraInicio);
        command.Parameters.AddWithValue("fim", NpgsqlDbType.Time, occurrence.HoraFim);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 1);
    }

    private static async Task PopulateScheduledParticipantsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid aulaId,
        OcorrenciaAulaData occurrence,
        CancellationToken cancellationToken)
    {
        const string sql = """
            insert into public.aula_alunos (aula_id, aluno_id, status, horario_recorrente_aluno_id)
            select @aula_id, h.aluno_id, 'agendado', h.id
            from public.horarios_recorrentes_alunos h
            join public.alunos a on a.id = h.aluno_id and a.ativo = true
            where h.professora_id = @professora_id
              and h.ativo = true
              and h.dia_semana = extract(dow from @data::date)::smallint
              and h.hora_inicio = @inicio
              and h.hora_fim = @fim
              and h.data_inicio <= @data
              and (h.data_fim is null or h.data_fim >= @data)
            on conflict (aula_id, aluno_id) do update
            set horario_recorrente_aluno_id = coalesce(public.aula_alunos.horario_recorrente_aluno_id, excluded.horario_recorrente_aluno_id);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("aula_id", aulaId);
        command.Parameters.AddWithValue("professora_id", occurrence.ProfessoraId);
        command.Parameters.AddWithValue("data", NpgsqlDbType.Date, occurrence.Data);
        command.Parameters.AddWithValue("inicio", NpgsqlDbType.Time, occurrence.HoraInicio);
        command.Parameters.AddWithValue("fim", NpgsqlDbType.Time, occurrence.HoraFim);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureParticipantAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid aulaId,
        Guid alunoId,
        Guid? horarioRecorrenteId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            insert into public.aula_alunos (aula_id, aluno_id, status, horario_recorrente_aluno_id)
            values (@aula_id, @aluno_id, 'agendado', @horario_id)
            on conflict (aula_id, aluno_id) do update
            set horario_recorrente_aluno_id = coalesce(public.aula_alunos.horario_recorrente_aluno_id, excluded.horario_recorrente_aluno_id);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("aula_id", aulaId);
        command.Parameters.AddWithValue("aluno_id", alunoId);
        command.Parameters.Add(new NpgsqlParameter("horario_id", NpgsqlDbType.Uuid)
        {
            Value = horarioRecorrenteId.HasValue ? horarioRecorrenteId.Value : DBNull.Value
        });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string?> GetParticipantStatusAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid aulaId,
        Guid alunoId,
        CancellationToken cancellationToken)
    {
        const string sql = "select status from public.aula_alunos where aula_id = @aula_id and aluno_id = @aluno_id;";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("aula_id", aulaId);
        command.Parameters.AddWithValue("aluno_id", alunoId);
        return (await command.ExecuteScalarAsync(cancellationToken)) as string;
    }

    private sealed record ReplacementCreditUpsertResult(Guid Id, bool Created, string Status);

    private static async Task<ReplacementCreditUpsertResult> UpsertReplacementCreditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid aulaId,
        Guid alunoId,
        string status,
        string? observation,
        CancellationToken cancellationToken)
    {
        const string existingSql = "select id, status from public.reposicoes where aula_origem_id = @aula_id and aluno_id = @aluno_id;";
        Guid? existingId = null;
        string? existingStatus = null;
        await using (var command = new NpgsqlCommand(existingSql, connection, transaction))
        {
            command.Parameters.AddWithValue("aula_id", aulaId);
            command.Parameters.AddWithValue("aluno_id", alunoId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                existingId = reader.GetGuid(0);
                existingStatus = reader.GetString(1);
            }
        }

        if (existingStatus == "concluida")
            throw new RegistroAulaConflictException("Esta aula já possui uma reposição concluída e o resultado original não pode mais ser alterado para remarcação.");

        var reason = status == ResultadoAulaCodigos.RemarcadaAluno ? "remarcada_aluno" : "remarcada_professora";
        if (existingId.HasValue)
        {
            const string updateSql = """
                update public.reposicoes
                set motivo = @motivo,
                    observacao_origem = @observacao,
                    atualizado_em = now()
                where id = @id;
                """;
            await using var command = new NpgsqlCommand(updateSql, connection, transaction);
            command.Parameters.AddWithValue("motivo", reason);
            command.Parameters.Add(new NpgsqlParameter("observacao", NpgsqlDbType.Text) { Value = observation ?? (object)DBNull.Value });
            command.Parameters.AddWithValue("id", existingId.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return new ReplacementCreditUpsertResult(existingId.Value, false, existingStatus ?? "pendente");
        }

        const string insertSql = """
            insert into public.reposicoes (aula_origem_id, aluno_id, motivo, status, observacao_origem)
            values (@aula_id, @aluno_id, @motivo, 'pendente', @observacao)
            returning id;
            """;
        await using var insert = new NpgsqlCommand(insertSql, connection, transaction);
        insert.Parameters.AddWithValue("aula_id", aulaId);
        insert.Parameters.AddWithValue("aluno_id", alunoId);
        insert.Parameters.AddWithValue("motivo", reason);
        insert.Parameters.Add(new NpgsqlParameter("observacao", NpgsqlDbType.Text) { Value = observation ?? (object)DBNull.Value });
        var id = (Guid)(await insert.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Não foi possível criar a reposição."));
        return new ReplacementCreditUpsertResult(id, true, "pendente");
    }

    private static async Task RemoveOpenReplacementCreditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid aulaId,
        Guid alunoId,
        CancellationToken cancellationToken)
    {
        const string findSql = "select id, status from public.reposicoes where aula_origem_id = @aula_id and aluno_id = @aluno_id;";
        Guid? id = null;
        string? status = null;
        await using (var command = new NpgsqlCommand(findSql, connection, transaction))
        {
            command.Parameters.AddWithValue("aula_id", aulaId);
            command.Parameters.AddWithValue("aluno_id", alunoId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                id = reader.GetGuid(0);
                status = reader.GetString(1);
            }
        }

        if (!id.HasValue) return;
        if (status == "concluida")
            throw new RegistroAulaConflictException("A aula possui uma reposição já concluída. Para preservar o histórico financeiro, o resultado original não pode ser alterado.");

        const string deleteSql = """
            delete from public.aulas
            where reposicao_origem_id = @reposicao_id
              and eh_reposicao = true
              and status <> 'realizada';
            delete from public.reposicoes where id = @reposicao_id;
            """;
        await using var delete = new NpgsqlCommand(deleteSql, connection, transaction);
        delete.Parameters.AddWithValue("reposicao_id", id.Value);
        await delete.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RecalculateLessonAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid aulaId,
        CancellationToken cancellationToken)
    {
        const string lessonSql = "select professora_id, data_aula, tipo_aula, eh_reposicao from public.aulas where id = @id;";
        Guid professoraId;
        DateOnly date;
        string type;
        bool replacement;
        await using (var command = new NpgsqlCommand(lessonSql, connection, transaction))
        {
            command.Parameters.AddWithValue("id", aulaId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new RegistroAulaNotFoundException("A ocorrência real da aula não foi encontrada.");
            professoraId = reader.GetGuid(0);
            date = reader.GetFieldValue<DateOnly>(1);
            type = reader.GetString(2);
            replacement = reader.GetBoolean(3);
        }

        var statuses = new List<string>();
        const string statusSql = "select status from public.aula_alunos where aula_id = @id;";
        await using (var command = new NpgsqlCommand(statusSql, connection, transaction))
        {
            command.Parameters.AddWithValue("id", aulaId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) statuses.Add(reader.GetString(0));
        }

        var policy = await GetPaymentPolicyAsync(connection, transaction, date, cancellationToken);
        var eligible = replacement
            ? statuses.Any(status => status switch
            {
                "aplicada" => policy.ReposicaoRealizadaPaga,
                "perdida" => policy.AulaPerdidaPaga,
                _ => false
            })
            : statuses.Any(status => status switch
            {
                "aplicada" => policy.AulaAplicadaPaga,
                "perdida" => policy.AulaPerdidaPaga,
                "remarcada_aluno" => policy.RemarcadaAlunoPaga,
                "remarcada_professora" => policy.RemarcadaProfessoraPaga,
                _ => false
            });

        var lessonStatus = statuses.Any(status => status is "aplicada" or "perdida")
            ? "realizada"
            : statuses.Count > 0 && statuses.All(status => status == "cancelada")
                ? "cancelada"
                : statuses.Count > 0 && statuses.All(status => status is "remarcada_aluno" or "remarcada_professora" or "cancelada")
                    ? "remarcada"
                    : "agendada";

        var rate = await GetTeacherRateAsync(connection, transaction, professoraId, date, type, cancellationToken);
        const string updateSql = """
            update public.aulas
            set status = @status,
                elegivel_pagamento = @elegivel,
                valor_aula_aplicado = @valor,
                valor_pagamento = @pagamento,
                status_pagamento = case when @elegivel then 'included' else 'pending' end,
                atualizado_em = now()
            where id = @id;
            """;
        await using var update = new NpgsqlCommand(updateSql, connection, transaction);
        update.Parameters.AddWithValue("status", lessonStatus);
        update.Parameters.AddWithValue("elegivel", eligible);
        update.Parameters.AddWithValue("valor", rate);
        update.Parameters.AddWithValue("pagamento", eligible ? rate : 0m);
        update.Parameters.AddWithValue("id", aulaId);
        await update.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record PaymentPolicy(
        bool AulaAplicadaPaga,
        bool AulaPerdidaPaga,
        bool RemarcadaAlunoPaga,
        bool RemarcadaProfessoraPaga,
        bool ReposicaoRealizadaPaga);

    private static async Task<PaymentPolicy> GetPaymentPolicyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select aula_aplicada_paga, aula_perdida_paga, remarcada_aluno_paga,
                   remarcada_professora_paga, reposicao_realizada_paga
            from public.politica_pagamento_professoras
            where vigente_desde <= @data
            order by ativo desc, vigente_desde desc, criado_em desc
            limit 1;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("data", NpgsqlDbType.Date, date);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return new PaymentPolicy(true, true, false, false, true);
        return new PaymentPolicy(reader.GetBoolean(0), reader.GetBoolean(1), reader.GetBoolean(2), reader.GetBoolean(3), reader.GetBoolean(4));
    }

    private static async Task<decimal> GetTeacherRateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid professoraId,
        DateOnly date,
        string type,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select valor_aula_individual, valor_aula_grupo
            from public.valores_aula_professoras
            where professora_id = @id
              and vigente_desde <= @data
              and (vigente_ate is null or vigente_ate >= @data)
            order by vigente_desde desc
            limit 1;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", professoraId);
        command.Parameters.AddWithValue("data", NpgsqlDbType.Date, date);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return 0m;
        return type == "grupo" ? reader.GetDecimal(1) : reader.GetDecimal(0);
    }

    private static async Task InsertHistoryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid aulaId,
        Guid? alunoId,
        string action,
        string? previousStatus,
        string? newStatus,
        string? observation,
        Guid usuarioAuthId,
        string usuarioNome,
        CancellationToken cancellationToken)
    {
        const string sql = """
            insert into public.historico_aulas (
                aula_id, aluno_id, acao, status_anterior, status_novo,
                observacao, alterado_por, alterado_por_nome, alterado_em)
            values (
                @aula_id, @aluno_id, @acao, @anterior, @novo,
                @observacao, @usuario_id, @usuario_nome, clock_timestamp());
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("aula_id", aulaId);
        command.Parameters.Add(new NpgsqlParameter("aluno_id", NpgsqlDbType.Uuid) { Value = alunoId.HasValue ? alunoId.Value : DBNull.Value });
        command.Parameters.AddWithValue("acao", action);
        command.Parameters.Add(new NpgsqlParameter("anterior", NpgsqlDbType.Text) { Value = previousStatus ?? (object)DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("novo", NpgsqlDbType.Text) { Value = newStatus ?? (object)DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("observacao", NpgsqlDbType.Text) { Value = observation ?? (object)DBNull.Value });
        command.Parameters.AddWithValue("usuario_id", usuarioAuthId);
        command.Parameters.AddWithValue("usuario_nome", string.IsNullOrWhiteSpace(usuarioNome) ? "Usuário do portal" : usuarioNome);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record ReplacementOriginData(Guid AulaOrigemId, Guid AlunoId, string Status);

    private static async Task<ReplacementOriginData?> GetReplacementOriginAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid reposicaoId,
        CancellationToken cancellationToken)
    {
        const string sql = "select aula_origem_id, aluno_id, status from public.reposicoes where id = @id for update;";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", reposicaoId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ReplacementOriginData(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2))
            : null;
    }

    private static async Task<bool> StudentBelongsToTeacherAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid studentId,
        Guid teacherId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select exists (
                select 1
                from public.horarios_recorrentes_alunos h
                where h.aluno_id = @aluno_id
                  and h.professora_id = @professora_id
                  and h.ativo = true
                  and h.data_inicio <= current_date
                  and (h.data_fim is null or h.data_fim >= current_date)
            ) or exists (
                select 1
                from public.alunos a
                where a.id = @aluno_id
                  and a.professora_id = @professora_id
                  and a.ativo = true
                  and not exists (
                      select 1
                      from public.horarios_recorrentes_alunos h
                      where h.aluno_id = a.id
                        and h.ativo = true
                        and h.data_inicio <= current_date
                        and (h.data_fim is null or h.data_fim >= current_date)
                  )
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("aluno_id", studentId);
        command.Parameters.AddWithValue("professora_id", teacherId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<bool> StudentExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid studentId,
        CancellationToken cancellationToken)
    {
        const string sql = "select exists(select 1 from public.alunos where id = @id and ativo = true);";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", studentId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<bool> TeacherExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid teacherId,
        CancellationToken cancellationToken)
    {
        const string sql = "select exists(select 1 from public.professoras where id = @id and ativo = true);";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", teacherId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<Guid> EnsureReplacementLessonAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid reposicaoId,
        Guid alunoId,
        Guid professoraId,
        DateOnly date,
        TimeOnly start,
        TimeOnly end,
        string? observation,
        Guid usuarioAuthId,
        CancellationToken cancellationToken)
    {
        var existingId = await GetReplacementLessonIdAsync(connection, transaction, reposicaoId, cancellationToken);
        Guid aulaId;
        if (existingId.HasValue)
        {
            aulaId = existingId.Value;
            const string updateSql = """
                update public.aulas
                set professora_id = @professora_id,
                    data_aula = @data,
                    hora_inicio = @inicio,
                    hora_fim = @fim,
                    tipo_aula = 'individual',
                    status = 'agendada',
                    observacoes = @observacao,
                    elegivel_pagamento = false,
                    valor_aula_aplicado = 0,
                    valor_pagamento = 0,
                    status_pagamento = 'pending',
                    atualizado_em = now()
                where id = @id;
                """;
            await using var update = new NpgsqlCommand(updateSql, connection, transaction);
            update.Parameters.AddWithValue("professora_id", professoraId);
            update.Parameters.AddWithValue("data", NpgsqlDbType.Date, date);
            update.Parameters.AddWithValue("inicio", NpgsqlDbType.Time, start);
            update.Parameters.AddWithValue("fim", NpgsqlDbType.Time, end);
            update.Parameters.Add(new NpgsqlParameter("observacao", NpgsqlDbType.Text) { Value = observation ?? (object)DBNull.Value });
            update.Parameters.AddWithValue("id", aulaId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            const string insertSql = """
                insert into public.aulas (
                    professora_id, tipo_aula, data_aula, hora_inicio, hora_fim, status,
                    observacoes, eh_reposicao, reposicao_origem_id, elegivel_pagamento,
                    valor_aula_aplicado, valor_pagamento, status_pagamento, criado_por)
                values (
                    @professora_id, 'individual', @data, @inicio, @fim, 'agendada',
                    @observacao, true, @reposicao_id, false,
                    0, 0, 'pending', @criado_por)
                returning id;
                """;
            await using var insert = new NpgsqlCommand(insertSql, connection, transaction);
            insert.Parameters.AddWithValue("professora_id", professoraId);
            insert.Parameters.AddWithValue("data", NpgsqlDbType.Date, date);
            insert.Parameters.AddWithValue("inicio", NpgsqlDbType.Time, start);
            insert.Parameters.AddWithValue("fim", NpgsqlDbType.Time, end);
            insert.Parameters.Add(new NpgsqlParameter("observacao", NpgsqlDbType.Text) { Value = observation ?? (object)DBNull.Value });
            insert.Parameters.AddWithValue("reposicao_id", reposicaoId);
            insert.Parameters.AddWithValue("criado_por", usuarioAuthId);
            aulaId = (Guid)(await insert.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Não foi possível criar a aula de reposição."));
        }

        const string participantSql = """
            insert into public.aula_alunos (aula_id, aluno_id, status, observacoes)
            values (@aula_id, @aluno_id, 'agendado', @observacao)
            on conflict (aula_id, aluno_id) do update
            set status = 'agendado', observacoes = excluded.observacoes, atualizado_em = now();
            """;
        await using var participant = new NpgsqlCommand(participantSql, connection, transaction);
        participant.Parameters.AddWithValue("aula_id", aulaId);
        participant.Parameters.AddWithValue("aluno_id", alunoId);
        participant.Parameters.Add(new NpgsqlParameter("observacao", NpgsqlDbType.Text) { Value = observation ?? (object)DBNull.Value });
        await participant.ExecuteNonQueryAsync(cancellationToken);
        return aulaId;
    }

    private static async Task<Guid?> GetReplacementLessonIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid reposicaoId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select id
            from public.aulas
            where reposicao_origem_id = @id and eh_reposicao = true
            order by atualizado_em desc
            limit 1;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", reposicaoId);
        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        return scalar is Guid id ? id : null;
    }

    private static async Task MarkReplacementCompletedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid reposicaoId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            update public.reposicoes
            set status = 'concluida', concluida_em = now(), atualizado_em = now()
            where id = @id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", reposicaoId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string ToDatabaseParticipantStatus(string status) => status switch
    {
        ResultadoAulaCodigos.Aplicada => "aplicada",
        ResultadoAulaCodigos.FaltaAluno => "perdida",
        ResultadoAulaCodigos.RemarcadaAluno => "remarcada_aluno",
        ResultadoAulaCodigos.RemarcadaProfessora => "remarcada_professora",
        _ => throw new RegistroAulaValidationException("Status de aula inválido.")
    };

    private static string ToPublicStatus(string databaseStatus) => databaseStatus switch
    {
        "aplicada" => ResultadoAulaCodigos.Aplicada,
        "perdida" => ResultadoAulaCodigos.FaltaAluno,
        "remarcada_aluno" => ResultadoAulaCodigos.RemarcadaAluno,
        "remarcada_professora" => ResultadoAulaCodigos.RemarcadaProfessora,
        "cancelada" => ResultadoAulaCodigos.Cancelada,
        _ => "agendada"
    };

    private static bool TryParseRecurringOccurrence(string value, out Guid scheduleId, out DateOnly date)
    {
        scheduleId = Guid.Empty;
        date = default;
        var parts = value.Split(':', 3, StringSplitOptions.TrimEntries);
        return parts.Length == 3
            && parts[0] == "r"
            && Guid.TryParse(parts[1], out scheduleId)
            && DateOnly.TryParse(parts[2], out date);
    }

    private static bool TryParseLessonOccurrence(string value, out Guid lessonId, out Guid? alunoId)
    {
        lessonId = Guid.Empty;
        alunoId = null;
        var parts = value.Split(':', 3, StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts[0] != "a" || !Guid.TryParse(parts[1], out lessonId)) return false;
        if (parts.Length == 3)
        {
            if (!Guid.TryParse(parts[2], out var parsedAlunoId)) return false;
            alunoId = parsedAlunoId;
        }
        return true;
    }

    private static string? GetNullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
