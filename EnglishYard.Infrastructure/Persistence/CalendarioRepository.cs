using EnglishYard.Application.Calendario;
using Npgsql;
using NpgsqlTypes;

namespace EnglishYard.Infrastructure.Persistence;

public sealed class CalendarioRepository(NpgsqlDataSource dataSource) : ICalendarioRepository
{
    public async Task<IReadOnlyList<AulaCalendarioResponse>> ListarAulasAsync(
        DateOnly dataInicio,
        DateOnly dataFim,
        Guid? professoraId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            with dias as (
                select generate_series(@data_inicio::date, @data_fim::date, interval '1 day')::date as data
            ), recorrentes as (
                select
                    h.id as horario_recorrente_id,
                    real.aula_id,
                    real.reposicao_id,
                    d.data,
                    coalesce(real.hora_inicio, h.hora_inicio) as hora_inicio,
                    coalesce(real.hora_fim, h.hora_fim) as hora_fim,
                    a.id as aluno_id,
                    a.nome as aluno_nome,
                    p.id as professora_id,
                    p.nome as professora_nome,
                    case
                        when real.participante_status in ('aplicada', 'perdida') then 'Realizada'
                        when real.participante_status in ('remarcada_aluno', 'remarcada_professora') then 'Remarcada'
                        when real.aula_status = 'cancelada' or real.participante_status = 'cancelada' then 'Cancelada'
                        when real.aula_status = 'em_andamento' then 'Em andamento'
                        else 'Agendada'
                    end as status,
                    case coalesce(
                        real.tipo_aula,
                        case when count(*) over (partition by p.id, d.data, h.hora_inicio, h.hora_fim) > 1 then 'grupo' else 'individual' end
                    ) when 'grupo' then 'Grupo' else 'Individual' end as tipo,
                    (real.aula_id is not null) as possui_registro_real,
                    case real.participante_status
                        when 'aplicada' then 'Aula aplicada'
                        when 'perdida' then 'Falta do aluno'
                        when 'remarcada_aluno' then 'Remarcada pelo aluno'
                        when 'remarcada_professora' then 'Remarcada pela professora'
                        when 'cancelada' then 'Aula cancelada'
                        else null
                    end as resultado,
                    false as eh_reposicao
                from public.horarios_recorrentes_alunos h
                join public.alunos a on a.id = h.aluno_id and a.ativo = true
                join public.professoras p on p.id = h.professora_id and p.ativo = true
                join dias d on extract(dow from d.data)::smallint = h.dia_semana
                left join lateral (
                    select
                        au.id as aula_id,
                        r.id as reposicao_id,
                        au.hora_inicio,
                        au.hora_fim,
                        au.status as aula_status,
                        au.tipo_aula,
                        aa.status as participante_status
                    from public.aula_alunos aa
                    join public.aulas au on au.id = aa.aula_id
                    left join public.reposicoes r
                      on r.aula_origem_id = au.id
                     and r.aluno_id = aa.aluno_id
                    where aa.horario_recorrente_aluno_id = h.id
                      and aa.aluno_id = h.aluno_id
                      and au.data_aula = d.data
                      and au.eh_reposicao = false
                    order by au.atualizado_em desc
                    limit 1
                ) real on true
                where h.ativo = true
                  and (@professora_id is null or p.id = @professora_id)
                  and h.data_inicio <= d.data
                  and (h.data_fim is null or h.data_fim >= d.data)
            ), aulas_avulsas as (
                select
                    null::uuid as horario_recorrente_id,
                    au.id as aula_id,
                    r.id as reposicao_id,
                    au.data_aula as data,
                    au.hora_inicio,
                    au.hora_fim,
                    a.id as aluno_id,
                    a.nome as aluno_nome,
                    p.id as professora_id,
                    p.nome as professora_nome,
                    case
                        when aa.status in ('aplicada', 'perdida') then 'Realizada'
                        when aa.status in ('remarcada_aluno', 'remarcada_professora') then 'Remarcada'
                        when au.status = 'cancelada' or aa.status = 'cancelada' then 'Cancelada'
                        when au.status = 'em_andamento' then 'Em andamento'
                        else 'Agendada'
                    end as status,
                    case au.tipo_aula when 'grupo' then 'Grupo' else 'Individual' end as tipo,
                    true as possui_registro_real,
                    case aa.status
                        when 'aplicada' then 'Aula aplicada'
                        when 'perdida' then 'Falta do aluno'
                        when 'remarcada_aluno' then 'Remarcada pelo aluno'
                        when 'remarcada_professora' then 'Remarcada pela professora'
                        when 'cancelada' then 'Aula cancelada'
                        else 'Aula avulsa'
                    end as resultado,
                    false as eh_reposicao
                from public.aulas au
                join public.aula_alunos aa on aa.aula_id = au.id
                left join public.reposicoes r
                  on r.aula_origem_id = au.id
                 and r.aluno_id = aa.aluno_id
                join public.alunos a on a.id = aa.aluno_id and a.ativo = true
                join public.professoras p on p.id = au.professora_id and p.ativo = true
                where au.eh_reposicao = false
                  and aa.horario_recorrente_aluno_id is null
                  and au.data_aula between @data_inicio and @data_fim
                  and (@professora_id is null or p.id = @professora_id)
            ), reposicoes_agendadas as (
                select
                    null::uuid as horario_recorrente_id,
                    au.id as aula_id,
                    r.id as reposicao_id,
                    au.data_aula as data,
                    au.hora_inicio,
                    au.hora_fim,
                    a.id as aluno_id,
                    a.nome as aluno_nome,
                    p.id as professora_id,
                    p.nome as professora_nome,
                    case
                        when aa.status = 'aplicada' or au.status = 'realizada' then 'Realizada'
                        when au.status = 'cancelada' or aa.status = 'cancelada' then 'Cancelada'
                        else 'Agendada'
                    end as status,
                    case au.tipo_aula when 'grupo' then 'Grupo' else 'Individual' end as tipo,
                    true as possui_registro_real,
                    case aa.status
                        when 'aplicada' then 'Reposição aplicada'
                        when 'perdida' then 'Falta do aluno'
                        else 'Reposição agendada'
                    end as resultado,
                    true as eh_reposicao
                from public.aulas au
                join public.aula_alunos aa on aa.aula_id = au.id
                join public.alunos a on a.id = aa.aluno_id and a.ativo = true
                join public.professoras p on p.id = au.professora_id and p.ativo = true
                join public.reposicoes r on r.id = au.reposicao_origem_id
                where au.eh_reposicao = true
                  and au.status <> 'cancelada'
                  and au.data_aula between @data_inicio and @data_fim
                  and (@professora_id is null or p.id = @professora_id)
            )
            select * from recorrentes
            union all
            select * from aulas_avulsas
            union all
            select * from reposicoes_agendadas
            order by data, hora_inicio, professora_nome, aluno_nome;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.Add(new NpgsqlParameter("data_inicio", NpgsqlDbType.Date) { Value = dataInicio });
        command.Parameters.Add(new NpgsqlParameter("data_fim", NpgsqlDbType.Date) { Value = dataFim });
        command.Parameters.Add(new NpgsqlParameter("professora_id", NpgsqlDbType.Uuid)
        {
            Value = professoraId.HasValue ? professoraId.Value : DBNull.Value
        });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var aulas = new List<AulaCalendarioResponse>();

        while (await reader.ReadAsync(cancellationToken))
        {
            aulas.Add(new AulaCalendarioResponse(
                reader.IsDBNull(0) ? null : reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.GetFieldValue<DateOnly>(3),
                reader.GetFieldValue<TimeOnly>(4),
                reader.GetFieldValue<TimeOnly>(5),
                reader.GetGuid(6),
                reader.GetString(7),
                reader.GetGuid(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.GetString(11),
                reader.GetBoolean(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.GetBoolean(14)));
        }

        return aulas;
    }
    public async Task<IReadOnlyList<HorarioGradeSemanalResponse>> ListarGradeSemanalAsync(
        DateOnly dataInicioSemana,
        DateOnly dataFimSemana,
        Guid? professoraId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select
                h.id,
                h.dia_semana,
                h.hora_inicio,
                h.hora_fim,
                a.id as aluno_id,
                a.nome as aluno_nome,
                p.id as professora_id,
                p.nome as professora_nome
            from public.horarios_recorrentes_alunos h
            join public.alunos a
              on a.id = h.aluno_id
             and a.ativo = true
            join public.professoras p
              on p.id = h.professora_id
             and p.ativo = true
            where h.ativo = true
              and (@professora_id is null or p.id = @professora_id)
              -- Conta a regra apenas quando existe, de fato, um dia daquela semana
              -- que cai dentro da vigência. Isso evita duplicar indicadores quando
              -- uma troca de grade acontece no meio da semana.
              and exists (
                  select 1
                  from generate_series(
                      @data_inicio_semana::date,
                      @data_fim_semana::date,
                      interval '1 day'
                  ) as ocorrencia(data)
                  where extract(dow from ocorrencia.data)::smallint = h.dia_semana
                    and h.data_inicio <= ocorrencia.data::date
                    and (h.data_fim is null or h.data_fim >= ocorrencia.data::date)
              )
            order by h.dia_semana, h.hora_inicio, professora_nome, aluno_nome;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.Add(new NpgsqlParameter("data_inicio_semana", NpgsqlDbType.Date) { Value = dataInicioSemana });
        command.Parameters.Add(new NpgsqlParameter("data_fim_semana", NpgsqlDbType.Date) { Value = dataFimSemana });
        command.Parameters.Add(new NpgsqlParameter("professora_id", NpgsqlDbType.Uuid)
        {
            Value = professoraId.HasValue ? professoraId.Value : DBNull.Value
        });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var horarios = new List<HorarioGradeSemanalResponse>();

        while (await reader.ReadAsync(cancellationToken))
        {
            horarios.Add(new HorarioGradeSemanalResponse(
                reader.GetGuid(0),
                reader.GetInt16(1),
                reader.GetFieldValue<TimeOnly>(2),
                reader.GetFieldValue<TimeOnly>(3),
                reader.GetGuid(4),
                reader.GetString(5),
                reader.GetGuid(6),
                reader.GetString(7)));
        }

        return horarios;
    }

}
