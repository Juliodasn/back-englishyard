using EnglishYard.Application.Financeiro;
using Npgsql;
using NpgsqlTypes;

namespace EnglishYard.Infrastructure.Persistence;

public sealed class FinanceiroRepository(NpgsqlDataSource dataSource) : IFinanceiroRepository
{
    private sealed record RateRow(DateOnly Desde, DateOnly? Ate, decimal Individual, decimal Grupo);
    private sealed record ScheduleRow(Guid Id, short DiaSemana, TimeOnly Inicio, TimeOnly Fim, DateOnly DataInicio, DateOnly? DataFim, Guid AlunoId, string AlunoNome);
    private sealed record RealLessonRow(
        Guid Id,
        DateOnly Data,
        TimeOnly Inicio,
        TimeOnly Fim,
        string Tipo,
        string Status,
        bool Reposicao,
        bool Elegivel,
        decimal ValorPagamento,
        Guid[] AlunoIds,
        string[] Alunos,
        string[] SituacoesAlunos);

    public async Task<FinanceiroResumoResponse> ObterResumoAsync(DateOnly competencia, CancellationToken cancellationToken)
    {
        // A primeira leitura da competência materializa a cobrança com as condições
        // vigentes naquele mês. Depois disso, alterações no cadastro não reescrevem o passado.
        await MaterializarMensalidadesAsync(competencia, cancellationToken);
        var mensalidades = await ListarMensalidadesAsync(competencia, cancellationToken);
        var despesas = await ListarDespesasAsync(competencia, cancellationToken);
        var professoras = await ListarProfessorasResumoAsync(competencia, cancellationToken);
        var movimentos = await ListarMovimentosAsync(competencia, cancellationToken);

        var receitaPrevista = mensalidades.Where(item => item.Status != "Cancelado").Sum(item => item.ValorFinal);
        var receitaRecebida = mensalidades.Sum(item => item.ValorRecebido);
        var receitaPendente = mensalidades.Where(item => item.Status != "Cancelado").Sum(item => item.Saldo);
        var receitaVencida = mensalidades.Where(item => item.Status == "Vencido").Sum(item => item.Saldo);
        var despesasPrevistas = despesas.Where(item => item.Status != "Cancelada").Sum(item => item.Valor);
        var despesasPagas = despesas.Where(item => item.Status == "Paga").Sum(item => item.Valor);
        var professorasPrevisto = professoras.Sum(item => item.ValorPrevisto + item.Ajustes);
        var professorasPago = professoras.Where(item => item.StatusFechamento == "Pago").Sum(item => item.ValorTotal);

        return new FinanceiroResumoResponse(
            $"{competencia:yyyy-MM}",
            receitaPrevista,
            receitaRecebida,
            receitaPendente,
            receitaVencida,
            despesasPrevistas,
            despesasPagas,
            professorasPrevisto,
            professorasPago,
            receitaPrevista - despesasPrevistas - professorasPrevisto,
            receitaRecebida - despesasPagas - professorasPago,
            mensalidades.Count,
            mensalidades.Count(item => item.Status == "Pago"),
            mensalidades.Count(item => item.Status is "Em aberto" or "Parcial" or "Vencido"),
            mensalidades,
            despesas,
            movimentos,
            professoras);
    }

    public async Task<DemonstrativoProfessoraResponse?> ObterDemonstrativoProfessoraAsync(
        Guid professoraId,
        DateOnly competencia,
        CancellationToken cancellationToken)
    {
        var fimCompetencia = competencia.AddMonths(1).AddDays(-1);

        const string teacherSql = """
            select p.id, p.nome, p.email, coalesce(p.dia_pagamento, 10), p.modelo_pagamento,
                   p.tipo_chave_pix, p.chave_pix, p.banco, coalesce(p.eh_master, false), p.eh_master_desde
            from public.professoras p
            where p.id = @id;
            """;

        Guid teacherId;
        string teacherName;
        string teacherEmail;
        short paymentDay;
        string paymentModel;
        string? pixType;
        string? pixKey;
        string? bank;
        bool teacherIsMaster;
        DateOnly? teacherMasterSince;

        await using (var command = dataSource.CreateCommand(teacherSql))
        {
            command.Parameters.AddWithValue("id", professoraId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;

            teacherId = reader.GetGuid(0);
            teacherName = reader.GetString(1);
            teacherEmail = reader.GetString(2);
            paymentDay = reader.GetInt16(3);
            paymentModel = reader.GetString(4);
            pixType = GetNullableString(reader, 5);
            pixKey = GetNullableString(reader, 6);
            bank = GetNullableString(reader, 7);
            teacherIsMaster = reader.GetBoolean(8);
            teacherMasterSince = reader.IsDBNull(9) ? null : reader.GetFieldValue<DateOnly>(9);
        }

        if (MasterRuleApplies(teacherIsMaster, teacherMasterSince, fimCompetencia))
            paymentModel = "Professora master · 100% da aula";

        var rates = await ListarRatesAsync(professoraId, cancellationToken);
        var latestRate = RateForDate(rates, fimCompetencia);
        var schedules = await ListarSchedulesAsync(professoraId, competencia, fimCompetencia, cancellationToken);
        var realLessons = await ListarRealLessonsAsync(professoraId, competencia, fimCompetencia, cancellationToken);
        var masterLessonValues = teacherIsMaster
            ? await ListarValoresIntegraisPorAlunoAsync(
                schedules.Select(item => item.AlunoId)
                    .Concat(realLessons.SelectMany(item => item.AlunoIds))
                    .Distinct()
                    .ToArray(),
                competencia,
                fimCompetencia,
                cancellationToken)
            : new Dictionary<Guid, decimal>();
        var realBySlot = realLessons
            .Where(item => !item.Reposicao)
            .GroupBy(item => SlotKey(item.Data, item.Inicio, item.Fim))
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.Id).First());

        var entries = new List<AulaPagamentoProfessoraResponse>();
        var matchedRealIds = new HashSet<Guid>();

        for (var date = competencia; date <= fimCompetencia; date = date.AddDays(1))
        {
            var schedulesForDate = schedules
                .Where(item => item.DiaSemana == (short)date.DayOfWeek
                    && item.DataInicio <= date
                    && (!item.DataFim.HasValue || item.DataFim.Value >= date))
                .GroupBy(item => new { item.Inicio, item.Fim });

            foreach (var group in schedulesForDate)
            {
                var participantNames = group.Select(item => item.AlunoNome).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
                var type = participantNames.Length > 1 ? "Grupo" : "Individual";
                var rate = RateForDate(rates, date);
                var configuredProjected = type == "Grupo" ? rate.Grupo : rate.Individual;
                var masterRuleApplies = MasterRuleApplies(teacherIsMaster, teacherMasterSince, date);
                var projected = masterRuleApplies
                    ? GetMasterProjectedValue(group.Select(item => item.AlunoId), masterLessonValues, configuredProjected)
                    : configuredProjected;
                var key = SlotKey(date, group.Key.Inicio, group.Key.Fim);
                realBySlot.TryGetValue(key, out var real);
                if (real is not null) matchedRealIds.Add(real.Id);

                var realized = real is not null && real.Elegivel
                    ? (real.ValorPagamento > 0 ? real.ValorPagamento : projected)
                    : 0m;

                entries.Add(new AulaPagamentoProfessoraResponse(
                    real?.Id.ToString() ?? $"agenda-{teacherId:N}-{date:yyyyMMdd}-{group.Key.Inicio:HHmm}",
                    date,
                    real?.Inicio ?? group.Key.Inicio,
                    real?.Fim ?? group.Key.Fim,
                    real is null ? type : ToTypeLabel(real.Tipo, participantNames.Length),
                    real is not null && real.Alunos.Length > 0 ? real.Alunos : participantNames,
                    real is null ? "Prevista" : ToLessonStatus(real.Status, real.Reposicao, real.SituacoesAlunos),
                    real is not null,
                    projected,
                    realized));
            }
        }

        foreach (var real in realLessons.Where(item => !matchedRealIds.Contains(item.Id)))
        {
            var rate = RateForDate(rates, real.Data);
            var type = ToTypeLabel(real.Tipo, real.Alunos.Length);
            var configuredRate = type == "Grupo" ? rate.Grupo : rate.Individual;
            var baseRate = MasterRuleApplies(teacherIsMaster, teacherMasterSince, real.Data)
                ? GetMasterProjectedValue(real.AlunoIds, masterLessonValues, configuredRate)
                : configuredRate;
            var projected = real.Reposicao ? 0m : baseRate;
            var realized = real.Elegivel ? (real.ValorPagamento > 0 ? real.ValorPagamento : baseRate) : 0m;
            entries.Add(new AulaPagamentoProfessoraResponse(
                real.Id.ToString(), real.Data, real.Inicio, real.Fim, type, real.Alunos,
                ToLessonStatus(real.Status, real.Reposicao, real.SituacoesAlunos), true, projected, realized));
        }

        entries = entries.OrderBy(item => item.Data).ThenBy(item => item.HoraInicio).ToList();
        var adjustmentItems = await ListarAjustesAsync(professoraId, competencia, cancellationToken);
        var adjustments = adjustmentItems.Sum(item => item.Valor);
        var closing = await ObterClosingAsync(professoraId, competencia, cancellationToken);
        var realizedTotal = entries.Sum(item => item.ValorRealizado);
        var frozen = closing.Status is "Aprovado" or "Pago";
        var displayedRealized = frozen ? closing.ValorAulas : realizedTotal;
        var displayedAdjustments = frozen ? closing.ValorAjustes : adjustments;
        var displayedTotal = frozen ? closing.ValorTotal : realizedTotal + adjustments;

        return new DemonstrativoProfessoraResponse(
            teacherId,
            teacherName,
            teacherEmail,
            paymentDay,
            paymentModel,
            pixType,
            pixKey,
            bank,
            latestRate.Individual,
            latestRate.Grupo,
            $"{competencia:yyyy-MM}",
            entries.Count,
            entries.Count(item => item.ValorRealizado > 0),
            entries.Count(item => item.Status == "Realizada" || item.Status.StartsWith("Realizada ·", StringComparison.Ordinal)),
            entries.Count(item => item.Status.Contains("Falta do aluno", StringComparison.OrdinalIgnoreCase)),
            frozen ? closing.Reposicoes : entries.Count(item => item.Status == "Reposição realizada"),
            entries.Count(item => item.Status.StartsWith("Remarcada", StringComparison.OrdinalIgnoreCase)),
            frozen ? closing.Individuais : entries.Count(item => item.Tipo == "Individual"),
            frozen ? closing.Grupos : entries.Count(item => item.Tipo == "Grupo"),
            entries.Sum(item => item.ValorPrevisto),
            displayedRealized,
            displayedAdjustments,
            displayedTotal,
            closing.Status,
            closing.DataPagamento,
            closing.ComprovanteUrl,
            closing.AprovadoEm,
            closing.PagoEm,
            adjustmentItems,
            entries);
    }

    public async Task RegistrarRecebimentoAsync(
        Guid alunoId,
        DateOnly competencia,
        RegistrarRecebimentoRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            decimal monthlyValue;
            decimal registrationFee;
            DateOnly enrollmentCompetence;
            decimal discountPercent;
            short dueDay;
            string? expectedPaymentMethod;

            const string studentSql = """
                select c.valor_mensalidade, c.taxa_matricula,
                       date_trunc('month', a.data_matricula)::date as competencia_matricula,
                       c.percentual_desconto, coalesce(c.dia_vencimento, 10), c.forma_pagamento
                from public.alunos a
                join lateral (
                  select * from public.condicoes_mensalidade_alunos c
                  where c.aluno_id = a.id
                    and c.vigente_desde <= (@competencia + interval '1 month - 1 day')::date
                    and (c.vigente_ate is null or c.vigente_ate >= @competencia)
                  order by c.vigente_desde desc limit 1
                ) c on true
                where a.id = @id
                  and a.data_matricula <= (@competencia + interval '1 month - 1 day')::date
                  and (
                    a.ativo = true
                    or (a.data_desativacao is not null and date_trunc('month', a.data_desativacao)::date >= @competencia)
                  );
                """;
            await using (var studentCommand = new NpgsqlCommand(studentSql, connection, transaction))
            {
                studentCommand.Parameters.AddWithValue("id", alunoId);
                studentCommand.Parameters.AddWithValue("competencia", NpgsqlDbType.Date, competencia);
                await using var reader = await studentCommand.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    throw new FinanceiroNotFoundException("Aluno não encontrado ou inativo.");

                monthlyValue = reader.IsDBNull(0) ? 0m : reader.GetDecimal(0);
                registrationFee = reader.GetDecimal(1);
                enrollmentCompetence = reader.GetFieldValue<DateOnly>(2);
                discountPercent = reader.GetDecimal(3);
                dueDay = reader.GetInt16(4);
                expectedPaymentMethod = GetNullableString(reader, 5);
            }

            var feeForCompetence = competencia == enrollmentCompetence ? Math.Max(0m, registrationFee) : 0m;
            if (monthlyValue <= 0m && feeForCompetence <= 0m)
                throw new FinanceiroValidationException("O aluno não possui cobrança configurada para esta competência.");

            var discount = Math.Round(monthlyValue * discountPercent / 100m, 2, MidpointRounding.AwayFromZero);
            var originalValue = Math.Max(0m, monthlyValue) + feeForCompetence;
            var finalValue = Math.Max(0m, monthlyValue - discount) + feeForCompetence;
            var dueDate = BuildDueDate(competencia, dueDay);
            var description = monthlyValue > 0m && feeForCompetence > 0m
                ? "Mensalidade + taxa de matrícula"
                : feeForCompetence > 0m ? "Taxa de matrícula" : "Mensalidade";

            const string createChargeSql = """
                insert into public.mensalidades (
                    aluno_id, competencia, descricao, valor_original, desconto, valor_final,
                    data_vencimento, status, forma_pagamento_prevista, observacoes
                ) values (
                    @aluno_id, @competencia, @descricao, @valor_original, @desconto, @valor_final,
                    @data_vencimento, 'em_aberto', @forma_pagamento,
                    case when @taxa_matricula > 0 then 'Cobrança inicial gerada automaticamente a partir da taxa de matrícula.' else null end
                )
                on conflict (aluno_id, competencia) do nothing;
                """;
            await using (var createCharge = new NpgsqlCommand(createChargeSql, connection, transaction))
            {
                createCharge.Parameters.AddWithValue("aluno_id", alunoId);
                createCharge.Parameters.AddWithValue("competencia", NpgsqlDbType.Date, competencia);
                createCharge.Parameters.AddWithValue("descricao", description);
                createCharge.Parameters.AddWithValue("valor_original", NpgsqlDbType.Numeric, originalValue);
                createCharge.Parameters.AddWithValue("desconto", NpgsqlDbType.Numeric, discount);
                createCharge.Parameters.AddWithValue("valor_final", NpgsqlDbType.Numeric, finalValue);
                createCharge.Parameters.AddWithValue("data_vencimento", NpgsqlDbType.Date, dueDate);
                createCharge.Parameters.Add(new NpgsqlParameter("forma_pagamento", NpgsqlDbType.Text) { Value = expectedPaymentMethod ?? (object)DBNull.Value });
                createCharge.Parameters.AddWithValue("taxa_matricula", NpgsqlDbType.Numeric, feeForCompetence);
                await createCharge.ExecuteNonQueryAsync(cancellationToken);
            }

            Guid chargeId;
            decimal storedFinalValue;
            DateOnly storedDueDate;
            string storedStatus;
            const string chargeSql = """
                select id, valor_final, data_vencimento, status
                from public.mensalidades
                where aluno_id = @aluno_id and competencia = @competencia
                for update;
                """;
            await using (var chargeCommand = new NpgsqlCommand(chargeSql, connection, transaction))
            {
                chargeCommand.Parameters.AddWithValue("aluno_id", alunoId);
                chargeCommand.Parameters.AddWithValue("competencia", NpgsqlDbType.Date, competencia);
                await using var reader = await chargeCommand.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("Não foi possível criar ou localizar a cobrança do aluno.");
                chargeId = reader.GetGuid(0);
                storedFinalValue = reader.GetDecimal(1);
                storedDueDate = reader.GetFieldValue<DateOnly>(2);
                storedStatus = reader.GetString(3);
            }

            if (storedStatus == "cancelado")
                throw new FinanceiroConflitoException("Não é possível registrar pagamento em uma cobrança cancelada.");

            decimal alreadyReceived;
            const string receivedSql = "select coalesce(sum(valor_recebido), 0) from public.recebimentos_mensalidades where mensalidade_id = @id and estornado_em is null;";
            await using (var receivedCommand = new NpgsqlCommand(receivedSql, connection, transaction))
            {
                receivedCommand.Parameters.AddWithValue("id", chargeId);
                alreadyReceived = (decimal)(await receivedCommand.ExecuteScalarAsync(cancellationToken) ?? 0m);
            }

            var remaining = Math.Max(0, storedFinalValue - alreadyReceived);
            if (request.Valor > remaining + 0.01m)
                throw new FinanceiroConflitoException($"O valor informado é maior que o saldo da cobrança ({remaining:C2}).");

            const string insertReceiptSql = """
                insert into public.recebimentos_mensalidades (
                    mensalidade_id, valor_recebido, data_recebimento, forma_pagamento, observacao
                ) values (
                    @mensalidade_id, @valor, @data_recebimento, @forma_pagamento, @observacao
                );
                """;
            await using (var receiptCommand = new NpgsqlCommand(insertReceiptSql, connection, transaction))
            {
                receiptCommand.Parameters.AddWithValue("mensalidade_id", chargeId);
                receiptCommand.Parameters.AddWithValue("valor", NpgsqlDbType.Numeric, request.Valor);
                receiptCommand.Parameters.AddWithValue("data_recebimento", NpgsqlDbType.Date, request.DataRecebimento ?? DateOnly.FromDateTime(DateTime.Today));
                receiptCommand.Parameters.AddWithValue("forma_pagamento", request.FormaPagamento);
                receiptCommand.Parameters.Add(new NpgsqlParameter("observacao", NpgsqlDbType.Text) { Value = request.Observacao ?? (object)DBNull.Value });
                await receiptCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            var totalReceived = alreadyReceived + request.Valor;
            var status = totalReceived >= storedFinalValue - 0.01m
                ? "pago"
                : totalReceived > 0
                    ? "parcial"
                    : storedDueDate < DateOnly.FromDateTime(DateTime.Today) ? "vencido" : "em_aberto";

            const string updateChargeSql = "update public.mensalidades set status = @status, atualizado_em = now() where id = @id;";
            await using (var updateCommand = new NpgsqlCommand(updateChargeSql, connection, transaction))
            {
                updateCommand.Parameters.AddWithValue("status", status);
                updateCommand.Parameters.AddWithValue("id", chargeId);
                await updateCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<DespesaFinanceiroResponse> CadastrarDespesaAsync(
        DateOnly competencia,
        CadastrarDespesaRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var quantity = request.Recorrencia == "Mensal" ? Math.Clamp(request.QuantidadeMeses ?? 12, 2, 60) : 1;
            DespesaFinanceiroResponse? first = null;
            var originalDueDay = request.DataVencimento.Day;

            for (var index = 0; index < quantity; index++)
            {
                var itemCompetence = competencia.AddMonths(index);
                var dueDay = Math.Min(originalDueDay, DateTime.DaysInMonth(itemCompetence.Year, itemCompetence.Month));
                var dueDate = new DateOnly(itemCompetence.Year, itemCompetence.Month, dueDay);

                const string sql = """
                    insert into public.despesas (
                        descricao, categoria, fornecedor, valor, competencia, data_vencimento,
                        status, recorrencia, forma_pagamento, observacoes
                    ) values (
                        @descricao, @categoria, @fornecedor, @valor, @competencia, @data_vencimento,
                        'em_aberto', @recorrencia, @forma_pagamento, @observacoes
                    )
                    returning id, descricao, categoria, fornecedor, valor, competencia, data_vencimento,
                              data_pagamento, status, recorrencia, forma_pagamento, observacoes;
                    """;

                await using var command = new NpgsqlCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("descricao", request.Descricao);
                command.Parameters.AddWithValue("categoria", request.Categoria);
                command.Parameters.Add(new NpgsqlParameter("fornecedor", NpgsqlDbType.Text) { Value = request.Fornecedor ?? (object)DBNull.Value });
                command.Parameters.AddWithValue("valor", NpgsqlDbType.Numeric, request.Valor);
                command.Parameters.AddWithValue("competencia", NpgsqlDbType.Date, itemCompetence);
                command.Parameters.AddWithValue("data_vencimento", NpgsqlDbType.Date, dueDate);
                command.Parameters.AddWithValue("recorrencia", request.Recorrencia);
                command.Parameters.Add(new NpgsqlParameter("forma_pagamento", NpgsqlDbType.Text) { Value = request.FormaPagamento ?? (object)DBNull.Value });
                var observationValue = request.Recorrencia == "Mensal"
                    ? $"{request.Observacoes}{(string.IsNullOrWhiteSpace(request.Observacoes) ? "" : " ")}Recorrência mensal gerada automaticamente ({index + 1}/{quantity}).".Trim()
                    : request.Observacoes;
                command.Parameters.Add(new NpgsqlParameter("observacoes", NpgsqlDbType.Text)
                {
                    Value = observationValue ?? (object)DBNull.Value
                });

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("A despesa foi criada, mas não pôde ser retornada.");
                first ??= MapDespesa(reader);
            }

            await transaction.CommitAsync(cancellationToken);
            return first ?? throw new InvalidOperationException("Nenhuma despesa foi criada.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> MarcarDespesaPagaAsync(Guid despesaId, MarcarDespesaPagaRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            update public.despesas
            set status = 'paga',
                data_pagamento = @data_pagamento,
                forma_pagamento = coalesce(@forma_pagamento, forma_pagamento),
                atualizado_em = now()
            where id = @id and status <> 'cancelada'
            returning id;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", despesaId);
        command.Parameters.AddWithValue("data_pagamento", NpgsqlDbType.Date, request.DataPagamento ?? DateOnly.FromDateTime(DateTime.Today));
        command.Parameters.Add(new NpgsqlParameter("forma_pagamento", NpgsqlDbType.Text) { Value = request.FormaPagamento ?? (object)DBNull.Value });
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async Task<bool> EstornarRecebimentoAsync(Guid id, string motivo, Guid usuarioId, CancellationToken ct)
    {
        const string sql = """
            with old as (select to_jsonb(r.*) dados from public.recebimentos_mensalidades r where id=@id and estornado_em is null),
            changed as (update public.recebimentos_mensalidades set estornado_em=now(), motivo_estorno=@motivo, estornado_por=@usuario, atualizado_em=now() where id=@id and estornado_em is null returning to_jsonb(recebimentos_mensalidades.*) dados),
            audit as (insert into public.historico_operacoes_financeiras(entidade_tipo,entidade_id,acao,dados_anteriores,dados_novos,motivo,alterado_por) select 'recebimento',@id,'estornado',old.dados,changed.dados,@motivo,@usuario from old,changed)
            select exists(select 1 from changed);
            """;
        return await ExecuteOperationAsync(sql, id, motivo, usuarioId, ct);
    }

    public async Task<bool> AjustarMensalidadeAsync(Guid id, decimal desconto, string motivo, Guid usuarioId, CancellationToken ct)
    {
        const string sql = """
            with old as (select to_jsonb(m.*) dados from public.mensalidades m where id=@id),
            changed as (update public.mensalidades m set desconto=@valor, valor_final=valor_original-@valor, atualizado_em=now()
              where id=@id and @valor between 0 and valor_original and valor_original-@valor >= coalesce((select sum(valor_recebido) from public.recebimentos_mensalidades r where r.mensalidade_id=m.id and r.estornado_em is null),0)
              returning to_jsonb(m.*) dados),
            audit as (insert into public.historico_operacoes_financeiras(entidade_tipo,entidade_id,acao,dados_anteriores,dados_novos,motivo,alterado_por) select 'mensalidade',@id,'desconto_especial',old.dados,changed.dados,@motivo,@usuario from old,changed)
            select exists(select 1 from changed);
            """;
        await using var command = dataSource.CreateCommand(sql);
        AddOperationParameters(command, id, motivo, usuarioId); command.Parameters.AddWithValue("valor", NpgsqlDbType.Numeric, desconto);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(ct) ?? false);
    }

    public async Task<bool> CancelarMensalidadeAsync(Guid id, string motivo, Guid usuarioId, CancellationToken ct)
    {
        const string sql = """
            with old as (select to_jsonb(m.*) dados from public.mensalidades m where id=@id),
            changed as (update public.mensalidades m set status='cancelado', observacoes=concat_ws(E'\n',observacoes,@motivo), atualizado_em=now()
              where id=@id and not exists(select 1 from public.recebimentos_mensalidades r where r.mensalidade_id=m.id and r.estornado_em is null) returning to_jsonb(m.*) dados),
            audit as (insert into public.historico_operacoes_financeiras(entidade_tipo,entidade_id,acao,dados_anteriores,dados_novos,motivo,alterado_por) select 'mensalidade',@id,'cancelada',old.dados,changed.dados,@motivo,@usuario from old,changed)
            select exists(select 1 from changed);
            """;
        return await ExecuteOperationAsync(sql, id, motivo, usuarioId, ct);
    }

    public async Task<bool> AtualizarDespesaAsync(Guid id, AtualizarDespesaRequest request, Guid usuarioId, CancellationToken ct)
    {
        const string sql = """
            with old as (select to_jsonb(d.*) dados from public.despesas d where id=@id),
            changed as (update public.despesas d set descricao=@descricao,categoria=@categoria,fornecedor=@fornecedor,valor=@valor,data_vencimento=@vencimento,forma_pagamento=@forma,observacoes=@observacoes,atualizado_em=now() where id=@id and status<>'cancelada' returning to_jsonb(d.*) dados),
            audit as (insert into public.historico_operacoes_financeiras(entidade_tipo,entidade_id,acao,dados_anteriores,dados_novos,motivo,alterado_por) select 'despesa',@id,'editada',old.dados,changed.dados,@motivo,@usuario from old,changed)
            select exists(select 1 from changed);
            """;
        await using var command = dataSource.CreateCommand(sql); AddOperationParameters(command,id,request.Motivo,usuarioId);
        command.Parameters.AddWithValue("descricao",request.Descricao.Trim()); command.Parameters.AddWithValue("categoria",request.Categoria.Trim());
        command.Parameters.Add(new NpgsqlParameter("fornecedor",NpgsqlDbType.Text){Value=request.Fornecedor??(object)DBNull.Value}); command.Parameters.AddWithValue("valor",NpgsqlDbType.Numeric,request.Valor);
        command.Parameters.AddWithValue("vencimento",NpgsqlDbType.Date,request.DataVencimento); command.Parameters.Add(new NpgsqlParameter("forma",NpgsqlDbType.Text){Value=request.FormaPagamento??(object)DBNull.Value});
        command.Parameters.Add(new NpgsqlParameter("observacoes",NpgsqlDbType.Text){Value=request.Observacoes??(object)DBNull.Value});
        return Convert.ToBoolean(await command.ExecuteScalarAsync(ct)??false);
    }

    public Task<bool> CancelarDespesaAsync(Guid id,string motivo,Guid usuarioId,CancellationToken ct) => ChangeExpenseStatusAsync(id,"cancelada","cancelada",motivo,usuarioId,ct);
    public Task<bool> ReabrirDespesaAsync(Guid id,string motivo,Guid usuarioId,CancellationToken ct) => ChangeExpenseStatusAsync(id,"em_aberto","reaberta",motivo,usuarioId,ct);

    public async Task<Guid> CriarAjusteProfessoraAsync(Guid professoraId,DateOnly competencia,CriarAjusteProfessoraRequest request,Guid usuarioId,CancellationToken ct)
    {
        const string sql="""insert into public.ajustes_pagamento_professoras(professora_id,competencia,descricao,valor,criado_por) select @p,@c,@d,@v,@u where not exists(select 1 from public.fechamentos_professoras where professora_id=@p and competencia=@c and status in ('Aprovado','Pago')) returning id;""";
        await using var command=dataSource.CreateCommand(sql); command.Parameters.AddWithValue("p",professoraId); command.Parameters.AddWithValue("c",NpgsqlDbType.Date,competencia); command.Parameters.AddWithValue("d",request.Descricao); command.Parameters.AddWithValue("v",NpgsqlDbType.Numeric,request.Valor); command.Parameters.AddWithValue("u",usuarioId);
        return (Guid)(await command.ExecuteScalarAsync(ct)??throw new FinanceiroConflitoException("O fechamento aprovado ou pago não aceita ajustes."));
    }

    public async Task<bool> ExcluirAjusteProfessoraAsync(Guid id,Guid usuarioId,CancellationToken ct)
    {
        const string sql="""delete from public.ajustes_pagamento_professoras a where id=@id and not exists(select 1 from public.fechamentos_professoras f where f.professora_id=a.professora_id and f.competencia=a.competencia and f.status in ('Aprovado','Pago')) returning id;""";
        await using var command=dataSource.CreateCommand(sql); command.Parameters.AddWithValue("id",id); return await command.ExecuteScalarAsync(ct) is not null;
    }

    public async Task<bool> AprovarFechamentoAsync(Guid professoraId,DateOnly competencia,Guid usuarioId,CancellationToken ct)
    {
        var s=await ObterDemonstrativoProfessoraAsync(professoraId,competencia,ct); if(s is null)return false;
        const string sql="""insert into public.fechamentos_professoras(professora_id,competencia,quantidade_aulas_individuais,quantidade_aulas_grupo,quantidade_reposicoes,quantidade_aulas_perdidas,valor_aulas,valor_ajustes,valor_total,status,aprovado_em,aprovado_por) values(@p,@c,@i,@g,@r,@f,@a,@j,@t,'Aprovado',now(),@u) on conflict(professora_id,competencia) do update set quantidade_aulas_individuais=excluded.quantidade_aulas_individuais,quantidade_aulas_grupo=excluded.quantidade_aulas_grupo,quantidade_reposicoes=excluded.quantidade_reposicoes,quantidade_aulas_perdidas=excluded.quantidade_aulas_perdidas,valor_aulas=excluded.valor_aulas,valor_ajustes=excluded.valor_ajustes,valor_total=excluded.valor_total,status='Aprovado',aprovado_em=now(),aprovado_por=@u where fechamentos_professoras.status<>'Pago' returning id;""";
        await using var cmd=dataSource.CreateCommand(sql); cmd.Parameters.AddWithValue("p",professoraId);cmd.Parameters.AddWithValue("c",NpgsqlDbType.Date,competencia);cmd.Parameters.AddWithValue("i",s.AulasIndividuais);cmd.Parameters.AddWithValue("g",s.AulasGrupo);cmd.Parameters.AddWithValue("r",s.ReposicoesRealizadas);cmd.Parameters.AddWithValue("f",s.FaltasAluno);cmd.Parameters.AddWithValue("a",NpgsqlDbType.Numeric,s.ValorRealizado);cmd.Parameters.AddWithValue("j",NpgsqlDbType.Numeric,s.Ajustes);cmd.Parameters.AddWithValue("t",NpgsqlDbType.Numeric,s.ValorTotal);cmd.Parameters.AddWithValue("u",usuarioId); return await cmd.ExecuteScalarAsync(ct)is not null;
    }

    public async Task<bool> MarcarFechamentoPagoAsync(Guid professoraId,DateOnly competencia,MarcarFechamentoPagoRequest request,Guid usuarioId,CancellationToken ct)
    {
        const string sql="""update public.fechamentos_professoras set status='Pago',pago_em=now(),data_pagamento=@d,comprovante_url=@x,atualizado_em=now() where professora_id=@p and competencia=@c and status='Aprovado' returning id;""";
        await using var cmd=dataSource.CreateCommand(sql);cmd.Parameters.AddWithValue("p",professoraId);cmd.Parameters.AddWithValue("c",NpgsqlDbType.Date,competencia);cmd.Parameters.AddWithValue("d",NpgsqlDbType.Date,request.DataPagamento);cmd.Parameters.Add(new NpgsqlParameter("x",NpgsqlDbType.Text){Value=request.ComprovanteUrl??(object)DBNull.Value});return await cmd.ExecuteScalarAsync(ct)is not null;
    }

    public async Task<bool> ReabrirFechamentoAsync(Guid professoraId,DateOnly competencia,string motivo,Guid usuarioId,CancellationToken ct)
    {
        const string sql="""update public.fechamentos_professoras set status='Em conferência',aprovado_em=null,pago_em=null,data_pagamento=null,comprovante_url=null,atualizado_em=now() where professora_id=@p and competencia=@c returning id;""";
        await using var cmd=dataSource.CreateCommand(sql);cmd.Parameters.AddWithValue("p",professoraId);cmd.Parameters.AddWithValue("c",NpgsqlDbType.Date,competencia);return await cmd.ExecuteScalarAsync(ct)is not null;
    }

    public async Task<PoliticaPagamentoResponse?> ObterPoliticaPagamentoAsync(DateOnly data,CancellationToken ct)
    {
        const string sql="""select id,aula_aplicada_paga,aula_perdida_paga,remarcada_aluno_paga,remarcada_professora_paga,reposicao_realizada_paga,vigente_desde from public.politica_pagamento_professoras where vigente_desde<=@d order by vigente_desde desc,criado_em desc limit 1;""";
        await using var cmd=dataSource.CreateCommand(sql);cmd.Parameters.AddWithValue("d",NpgsqlDbType.Date,data);await using var r=await cmd.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))return null;return MapPolicy(r);
    }

    public async Task<PoliticaPagamentoResponse> SalvarPoliticaPagamentoAsync(SalvarPoliticaPagamentoRequest request,Guid usuarioId,CancellationToken ct)
    {
        await using var cn=await dataSource.OpenConnectionAsync(ct);await using var tx=await cn.BeginTransactionAsync(ct);try{await using(var off=new NpgsqlCommand("update public.politica_pagamento_professoras set ativo=false where ativo=true;",cn,tx))await off.ExecuteNonQueryAsync(ct);
        const string sql="""insert into public.politica_pagamento_professoras(aula_aplicada_paga,aula_perdida_paga,remarcada_aluno_paga,remarcada_professora_paga,reposicao_realizada_paga,vigente_desde,ativo,criado_por)values(@a,@p,@ra,@rp,@r,@d,true,@u)returning id,aula_aplicada_paga,aula_perdida_paga,remarcada_aluno_paga,remarcada_professora_paga,reposicao_realizada_paga,vigente_desde;""";await using var cmd=new NpgsqlCommand(sql,cn,tx);cmd.Parameters.AddWithValue("a",request.AulaAplicadaPaga);cmd.Parameters.AddWithValue("p",request.AulaPerdidaPaga);cmd.Parameters.AddWithValue("ra",request.RemarcadaAlunoPaga);cmd.Parameters.AddWithValue("rp",request.RemarcadaProfessoraPaga);cmd.Parameters.AddWithValue("r",request.ReposicaoRealizadaPaga);cmd.Parameters.AddWithValue("d",NpgsqlDbType.Date,request.VigenteDesde);cmd.Parameters.AddWithValue("u",usuarioId);await using var reader=await cmd.ExecuteReaderAsync(ct);await reader.ReadAsync(ct);var result=MapPolicy(reader);await reader.DisposeAsync();await tx.CommitAsync(ct);return result;}catch{await tx.RollbackAsync(ct);throw;}
    }

    private async Task<List<MensalidadeFinanceiroResponse>> ListarMensalidadesAsync(DateOnly competencia, CancellationToken cancellationToken)
    {
        const string sql = """
            select
                m.id,
                a.id,
                a.nome,
                c.valor_mensalidade,
                c.taxa_matricula,
                date_trunc('month', a.data_matricula)::date as competencia_matricula,
                c.percentual_desconto,
                coalesce(c.dia_vencimento, 10),
                c.forma_pagamento,
                m.descricao,
                m.valor_original,
                m.desconto,
                m.valor_final,
                m.data_vencimento,
                m.status,
                m.forma_pagamento_prevista,
                coalesce(r.valor_recebido, 0) as valor_recebido
            from public.alunos a
            join lateral (
              select * from public.condicoes_mensalidade_alunos c
              where c.aluno_id = a.id
                and c.vigente_desde <= (@competencia + interval '1 month - 1 day')::date
                and (c.vigente_ate is null or c.vigente_ate >= @competencia)
              order by c.vigente_desde desc limit 1
            ) c on true
            left join public.mensalidades m
              on m.aluno_id = a.id
             and m.competencia = @competencia
            left join lateral (
                select sum(valor_recebido) as valor_recebido
                from public.recebimentos_mensalidades
                where mensalidade_id = m.id and estornado_em is null
            ) r on true
            where a.data_matricula <= (@competencia + interval '1 month - 1 day')::date
              and (
                a.ativo = true
                or (a.data_desativacao is not null and date_trunc('month', a.data_desativacao)::date >= @competencia)
                or (m.id is not null and @competencia < date_trunc('month', current_date)::date)
              )
              and (
                c.valor_mensalidade is not null
                or (
                    c.taxa_matricula > 0
                    and date_trunc('month', a.data_matricula)::date = @competencia
                )
              )
            order by a.nome;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("competencia", NpgsqlDbType.Date, competencia);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<MensalidadeFinanceiroResponse>();
        var today = DateOnly.FromDateTime(DateTime.Today);

        while (await reader.ReadAsync(cancellationToken))
        {
            Guid? chargeId = reader.IsDBNull(0) ? null : reader.GetGuid(0);
            var alunoId = reader.GetGuid(1);
            var alunoNome = reader.GetString(2);
            var configuredMonthly = reader.IsDBNull(3) ? 0m : reader.GetDecimal(3);
            var registrationFee = reader.GetDecimal(4);
            var enrollmentCompetence = reader.GetFieldValue<DateOnly>(5);
            var feeForCompetence = competencia == enrollmentCompetence ? Math.Max(0m, registrationFee) : 0m;
            var discountPercent = reader.GetDecimal(6);
            var dueDay = reader.GetInt16(7);
            var configuredPaymentMethod = GetNullableString(reader, 8);
            var projectedDescription = configuredMonthly > 0m && feeForCompetence > 0m
                ? "Mensalidade + taxa de matrícula"
                : feeForCompetence > 0m ? "Taxa de matrícula" : "Mensalidade";
            var description = GetNullableString(reader, 9) ?? projectedDescription;
            var projectedOriginal = Math.Max(0m, configuredMonthly) + feeForCompetence;
            var projectedDiscount = Math.Round(configuredMonthly * discountPercent / 100m, 2, MidpointRounding.AwayFromZero);
            var original = reader.IsDBNull(10) ? projectedOriginal : reader.GetDecimal(10);
            var discount = reader.IsDBNull(11) ? projectedDiscount : reader.GetDecimal(11);
            var final = reader.IsDBNull(12) ? Math.Max(0m, configuredMonthly - projectedDiscount) + feeForCompetence : reader.GetDecimal(12);
            var dueDate = reader.IsDBNull(13) ? BuildDueDate(competencia, dueDay) : reader.GetFieldValue<DateOnly>(13);
            var storedStatus = GetNullableString(reader, 14);
            var paymentMethod = GetNullableString(reader, 15) ?? configuredPaymentMethod;
            var received = reader.GetDecimal(16);
            var balance = Math.Max(0, final - received);
            var status = storedStatus == "cancelado"
                ? "Cancelado"
                : received >= final - 0.01m
                    ? "Pago"
                    : received > 0
                        ? "Parcial"
                        : dueDate < today ? "Vencido" : "Em aberto";

            result.Add(new MensalidadeFinanceiroResponse(
                chargeId, alunoId, alunoNome, description, original, discount, final, received, balance, dueDate, status, paymentMethod, []));
        }
        await reader.DisposeAsync();
        var receipts = await ListarRecebimentosAsync(competencia, cancellationToken);
        return result.Select(item => item with
        {
            Recebimentos = item.MensalidadeId.HasValue && receipts.TryGetValue(item.MensalidadeId.Value, out var items)
                ? items
                : []
        }).ToList();
    }

    private async Task MaterializarMensalidadesAsync(DateOnly competencia, CancellationToken cancellationToken)
    {
        const string sql = """
            insert into public.mensalidades (
              aluno_id, competencia, descricao, valor_original, desconto, valor_final,
              data_vencimento, status, forma_pagamento_prevista, observacoes
            )
            select
              a.id,
              @competencia,
              case
                when coalesce(c.valor_mensalidade, 0) > 0
                  and date_trunc('month', a.data_matricula)::date = @competencia
                  and c.taxa_matricula > 0 then 'Mensalidade + taxa de matrícula'
                when date_trunc('month', a.data_matricula)::date = @competencia
                  and c.taxa_matricula > 0 then 'Taxa de matrícula'
                else 'Mensalidade'
              end,
              greatest(coalesce(c.valor_mensalidade, 0), 0)
                + case when date_trunc('month', a.data_matricula)::date = @competencia then greatest(c.taxa_matricula, 0) else 0 end,
              round(greatest(coalesce(c.valor_mensalidade, 0), 0) * c.percentual_desconto / 100, 2),
              greatest(coalesce(c.valor_mensalidade, 0), 0)
                - round(greatest(coalesce(c.valor_mensalidade, 0), 0) * c.percentual_desconto / 100, 2)
                + case when date_trunc('month', a.data_matricula)::date = @competencia then greatest(c.taxa_matricula, 0) else 0 end,
              make_date(
                extract(year from @competencia)::int,
                extract(month from @competencia)::int,
                least(coalesce(c.dia_vencimento, 10),
                      extract(day from (date_trunc('month', @competencia) + interval '1 month - 1 day'))::int)
              ),
              'em_aberto',
              c.forma_pagamento,
              'Cobrança materializada com as condições vigentes na competência.'
            from public.alunos a
            join lateral (
              select * from public.condicoes_mensalidade_alunos c
              where c.aluno_id = a.id
                and c.vigente_desde <= (@competencia + interval '1 month - 1 day')::date
                and (c.vigente_ate is null or c.vigente_ate >= @competencia)
              order by c.vigente_desde desc limit 1
            ) c on true
            where a.data_matricula <= (@competencia + interval '1 month - 1 day')::date
              and (
                a.ativo = true
                or (a.data_desativacao is not null and date_trunc('month', a.data_desativacao)::date >= @competencia)
              )
              and (
                coalesce(c.valor_mensalidade, 0) > 0
                or (c.taxa_matricula > 0 and date_trunc('month', a.data_matricula)::date = @competencia)
              )
            on conflict (aluno_id, competencia) do nothing;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("competencia", NpgsqlDbType.Date, competencia);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<List<DespesaFinanceiroResponse>> ListarDespesasAsync(DateOnly competencia, CancellationToken cancellationToken)
    {
        const string sql = """
            select id, descricao, categoria, fornecedor, valor, competencia, data_vencimento,
                   data_pagamento, status, recorrencia, forma_pagamento, observacoes
            from public.despesas
            where competencia = @competencia
            order by data_vencimento, descricao;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("competencia", NpgsqlDbType.Date, competencia);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<DespesaFinanceiroResponse>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(MapDespesa(reader));
        return result;
    }

    private async Task<List<PagamentoProfessoraResumoResponse>> ListarProfessorasResumoAsync(DateOnly competencia, CancellationToken cancellationToken)
    {
        const string sql = """
            select p.id
            from public.professoras p
            where p.ativo = true
               or exists (
                    select 1 from public.aulas au
                    where au.professora_id = p.id
                      and au.data_aula >= @competencia
                      and au.data_aula < (@competencia + interval '1 month')::date
               )
               or exists (
                    select 1 from public.fechamentos_professoras f
                    where f.professora_id = p.id and f.competencia = @competencia
               )
            order by p.nome;
            """;
        var ids = new List<Guid>();
        await using (var command = dataSource.CreateCommand(sql))
        {
            command.Parameters.AddWithValue("competencia", NpgsqlDbType.Date, competencia);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) ids.Add(reader.GetGuid(0));
        }

        var result = new List<PagamentoProfessoraResumoResponse>();
        foreach (var id in ids)
        {
            var statement = await ObterDemonstrativoProfessoraAsync(id, competencia, cancellationToken);
            if (statement is null) continue;
            result.Add(new PagamentoProfessoraResumoResponse(
                statement.ProfessoraId,
                statement.Nome,
                statement.Email,
                statement.DiaPagamento,
                statement.ValorAulaIndividual,
                statement.ValorAulaGrupo,
                statement.AulasPrevistas,
                statement.AulasRealizadas,
                statement.AulasAplicadas,
                statement.FaltasAluno,
                statement.ReposicoesRealizadas,
                statement.AulasRemarcadas,
                statement.AulasIndividuais,
                statement.AulasGrupo,
                statement.ValorPrevisto,
                statement.ValorRealizado,
                statement.Ajustes,
                statement.ValorTotal,
                statement.StatusFechamento,
                statement.DataPagamento));
        }
        return result;
    }

    private async Task<List<MovimentoFinanceiroResponse>> ListarMovimentosAsync(DateOnly competencia, CancellationToken cancellationToken)
    {
        var nextMonth = competencia.AddMonths(1);
        const string sql = """
            select id, data, tipo, descricao, categoria, valor
            from (
                select r.id, r.data_recebimento as data, 'Entrada'::text as tipo,
                       ('Mensalidade · ' || a.nome)::text as descricao,
                       'Mensalidades'::text as categoria,
                       r.valor_recebido as valor
                from public.recebimentos_mensalidades r
                join public.mensalidades m on m.id = r.mensalidade_id
                join public.alunos a on a.id = m.aluno_id
                where r.data_recebimento >= @inicio and r.data_recebimento < @fim

                union all

                select d.id, d.data_pagamento as data, 'Saída'::text as tipo,
                       d.descricao, d.categoria, d.valor
                from public.despesas d
                where d.status = 'paga'
                  and d.data_pagamento >= @inicio and d.data_pagamento < @fim

                union all

                select f.id, f.data_pagamento as data, 'Saída'::text as tipo,
                       ('Pagamento · ' || p.nome)::text as descricao,
                       'Professoras'::text as categoria,
                       f.valor_total as valor
                from public.fechamentos_professoras f
                join public.professoras p on p.id = f.professora_id
                where f.status = 'Pago'
                  and f.data_pagamento >= @inicio and f.data_pagamento < @fim
            ) movimentos
            where data is not null
            order by data desc, descricao;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("inicio", NpgsqlDbType.Date, competencia);
        command.Parameters.AddWithValue("fim", NpgsqlDbType.Date, nextMonth);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<MovimentoFinanceiroResponse>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new MovimentoFinanceiroResponse(
                reader.GetGuid(0),
                reader.GetFieldValue<DateOnly>(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetDecimal(5)));
        }
        return result;
    }

    private async Task<List<RateRow>> ListarRatesAsync(Guid professoraId, CancellationToken cancellationToken)
    {
        const string sql = """
            select vigente_desde, vigente_ate, valor_aula_individual, valor_aula_grupo
            from public.valores_aula_professoras
            where professora_id = @id
            order by vigente_desde;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", professoraId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<RateRow>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new RateRow(
                reader.GetFieldValue<DateOnly>(0),
                reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1),
                reader.GetDecimal(2),
                reader.GetDecimal(3)));
        return result;
    }

    private async Task<List<ScheduleRow>> ListarSchedulesAsync(
        Guid professoraId,
        DateOnly inicio,
        DateOnly fim,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select h.id, h.dia_semana, h.hora_inicio, h.hora_fim, h.data_inicio, h.data_fim,
                   a.id, a.nome
            from public.horarios_recorrentes_alunos h
            join public.alunos a on a.id = h.aluno_id
            where h.professora_id = @id
              and (h.ativo = true or @fim < current_date)
              and h.data_inicio <= @fim
              and (h.data_fim is null or h.data_fim >= @inicio)
            order by h.dia_semana, h.hora_inicio, a.nome;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", professoraId);
        command.Parameters.AddWithValue("inicio", NpgsqlDbType.Date, inicio);
        command.Parameters.AddWithValue("fim", NpgsqlDbType.Date, fim);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ScheduleRow>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new ScheduleRow(
                reader.GetGuid(0), reader.GetInt16(1), reader.GetFieldValue<TimeOnly>(2), reader.GetFieldValue<TimeOnly>(3),
                reader.GetFieldValue<DateOnly>(4), reader.IsDBNull(5) ? null : reader.GetFieldValue<DateOnly>(5),
                reader.GetGuid(6), reader.GetString(7)));
        return result;
    }

    private async Task<List<RealLessonRow>> ListarRealLessonsAsync(
        Guid professoraId,
        DateOnly inicio,
        DateOnly fim,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select au.id, au.data_aula, au.hora_inicio, au.hora_fim, au.tipo_aula, au.status,
                   au.eh_reposicao, au.elegivel_pagamento, au.valor_pagamento,
                   coalesce(array_agg(a.id order by a.nome) filter (where a.id is not null), array[]::uuid[]) as aluno_ids,
                   coalesce(array_agg(a.nome order by a.nome) filter (where a.id is not null), array[]::text[]) as alunos,
                   coalesce(array_agg(aa.status order by a.nome) filter (where aa.id is not null), array[]::text[]) as situacoes_alunos
            from public.aulas au
            left join public.aula_alunos aa on aa.aula_id = au.id
            left join public.alunos a on a.id = aa.aluno_id
            where au.professora_id = @id
              and au.data_aula between @inicio and @fim
            group by au.id
            order by au.data_aula, au.hora_inicio;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", professoraId);
        command.Parameters.AddWithValue("inicio", NpgsqlDbType.Date, inicio);
        command.Parameters.AddWithValue("fim", NpgsqlDbType.Date, fim);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<RealLessonRow>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new RealLessonRow(
                reader.GetGuid(0), reader.GetFieldValue<DateOnly>(1), reader.GetFieldValue<TimeOnly>(2), reader.GetFieldValue<TimeOnly>(3),
                reader.GetString(4), reader.GetString(5), reader.GetBoolean(6), reader.GetBoolean(7), reader.GetDecimal(8),
                reader.GetFieldValue<Guid[]>(9), reader.GetFieldValue<string[]>(10), reader.GetFieldValue<string[]>(11)));
        return result;
    }

    private async Task<Dictionary<Guid, decimal>> ListarValoresIntegraisPorAlunoAsync(
        IReadOnlyCollection<Guid> alunoIds,
        DateOnly competencia,
        DateOnly fimCompetencia,
        CancellationToken cancellationToken)
    {
        if (alunoIds.Count == 0)
            return new Dictionary<Guid, decimal>();

        const string sql = """
            with participantes as (
                select unnest(@aluno_ids::uuid[]) as aluno_id
            ),
            valores as (
                select
                    participantes.aluno_id,
                    greatest(coalesce(condicao.valor_mensalidade, 0), 0)
                      - round(
                          greatest(coalesce(condicao.valor_mensalidade, 0), 0)
                          * coalesce(condicao.percentual_desconto, 0) / 100,
                          2
                        ) as mensalidade_liquida
                from participantes
                left join lateral (
                    select c.valor_mensalidade, c.percentual_desconto
                    from public.condicoes_mensalidade_alunos c
                    where c.aluno_id = participantes.aluno_id
                      and c.vigente_desde <= @fim_competencia
                      and (c.vigente_ate is null or c.vigente_ate >= @competencia)
                    order by c.vigente_desde desc
                    limit 1
                ) condicao on true
            ),
            quantidades as (
                select
                    participantes.aluno_id,
                    count(*)::numeric as quantidade_aulas
                from participantes
                join public.horarios_recorrentes_alunos h
                  on h.aluno_id = participantes.aluno_id
                cross join lateral generate_series(@competencia::date, @fim_competencia::date, interval '1 day') as calendario(data_aula)
                where h.data_inicio <= calendario.data_aula::date
                  and (h.data_fim is null or h.data_fim >= calendario.data_aula::date)
                  and h.dia_semana = extract(dow from calendario.data_aula)::smallint
                group by participantes.aluno_id
            )
            select
                valores.aluno_id,
                case
                    when coalesce(quantidades.quantidade_aulas, 0) > 0
                      and valores.mensalidade_liquida > 0
                    then round(valores.mensalidade_liquida / quantidades.quantidade_aulas, 2)
                    else 0
                end as valor_por_aula
            from valores
            left join quantidades using (aluno_id);
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("aluno_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, alunoIds.ToArray());
        command.Parameters.AddWithValue("competencia", NpgsqlDbType.Date, competencia);
        command.Parameters.AddWithValue("fim_competencia", NpgsqlDbType.Date, fimCompetencia);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new Dictionary<Guid, decimal>();
        while (await reader.ReadAsync(cancellationToken))
            result[reader.GetGuid(0)] = reader.GetDecimal(1);
        return result;
    }

    private static bool MasterRuleApplies(bool isMaster, DateOnly? masterSince, DateOnly date) =>
        isMaster && (!masterSince.HasValue || masterSince.Value <= date);

    private static decimal GetMasterProjectedValue(
        IEnumerable<Guid> alunoIds,
        IReadOnlyDictionary<Guid, decimal> values,
        decimal fallbackRate)
    {
        var total = alunoIds
            .Distinct()
            .Sum(id => values.TryGetValue(id, out var value) ? value : 0m);
        return total > 0m ? total : fallbackRate;
    }

    private async Task<List<AjustePagamentoProfessoraResponse>> ListarAjustesAsync(Guid professoraId, DateOnly competencia, CancellationToken cancellationToken)
    {
        const string sql = "select id, descricao, valor, criado_em from public.ajustes_pagamento_professoras where professora_id = @id and competencia = @competencia order by criado_em;";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", professoraId);
        command.Parameters.AddWithValue("competencia", NpgsqlDbType.Date, competencia);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<AjustePagamentoProfessoraResponse>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetDecimal(2), reader.GetFieldValue<DateTimeOffset>(3)));
        return result;
    }

    private async Task<(string Status, DateOnly? DataPagamento, string? ComprovanteUrl, DateTimeOffset? AprovadoEm, DateTimeOffset? PagoEm, int Individuais, int Grupos, int Reposicoes, decimal ValorAulas, decimal ValorAjustes, decimal ValorTotal)> ObterClosingAsync(Guid professoraId, DateOnly competencia, CancellationToken cancellationToken)
    {
        const string sql = "select status, data_pagamento, comprovante_url, aprovado_em, pago_em, quantidade_aulas_individuais, quantidade_aulas_grupo, quantidade_reposicoes, valor_aulas, valor_ajustes, valor_total from public.fechamentos_professoras where professora_id = @id and competencia = @competencia;";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", professoraId);
        command.Parameters.AddWithValue("competencia", NpgsqlDbType.Date, competencia);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return ("Em conferência", null, null, null, null, 0, 0, 0, 0, 0, 0);
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1), GetNullableString(reader, 2),
            reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3), reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetDecimal(8), reader.GetDecimal(9), reader.GetDecimal(10));
    }

    private async Task<Dictionary<Guid, IReadOnlyList<RecebimentoMensalidadeResponse>>> ListarRecebimentosAsync(
        DateOnly competencia,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select r.mensalidade_id, r.id, r.valor_recebido, r.data_recebimento,
                   r.forma_pagamento, r.observacao, r.criado_em
            from public.recebimentos_mensalidades r
            join public.mensalidades m on m.id = r.mensalidade_id
            where m.competencia = @competencia and r.estornado_em is null
            order by r.data_recebimento, r.criado_em;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("competencia", NpgsqlDbType.Date, competencia);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var grouped = new Dictionary<Guid, List<RecebimentoMensalidadeResponse>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var mensalidadeId = reader.GetGuid(0);
            if (!grouped.TryGetValue(mensalidadeId, out var items))
                grouped[mensalidadeId] = items = [];
            items.Add(new(reader.GetGuid(1), reader.GetDecimal(2), reader.GetFieldValue<DateOnly>(3), reader.GetString(4),
                GetNullableString(reader, 5), reader.GetFieldValue<DateTimeOffset>(6)));
        }
        return grouped.ToDictionary(item => item.Key, item => (IReadOnlyList<RecebimentoMensalidadeResponse>)item.Value);
    }

    private static RateRow RateForDate(IReadOnlyList<RateRow> rates, DateOnly date)
    {
        var rate = rates.LastOrDefault(item => item.Desde <= date && (!item.Ate.HasValue || item.Ate.Value >= date));
        return rate ?? rates.LastOrDefault(item => item.Desde <= date) ?? new RateRow(date, null, 0, 0);
    }

    private static string SlotKey(DateOnly date, TimeOnly start, TimeOnly end) => $"{date:yyyyMMdd}|{start:HHmmss}|{end:HHmmss}";

    private static string ToTypeLabel(string raw, int participants) => raw == "grupo" || participants > 1 ? "Grupo" : "Individual";

    private static string ToLessonStatus(string status, bool replacement, IReadOnlyList<string> participantStatuses)
    {
        if (replacement && participantStatuses.Any(item => item == "aplicada")) return "Reposição realizada";
        if (participantStatuses.Count > 0 && participantStatuses.All(item => item == "perdida")) return "Falta do aluno";
        if (participantStatuses.Any(item => item == "aplicada") && participantStatuses.Any(item => item == "perdida"))
            return "Realizada · Falta do aluno";
        if (participantStatuses.Any(item => item == "aplicada")) return "Realizada";
        if (participantStatuses.Any(item => item == "remarcada_aluno") && participantStatuses.Any(item => item == "remarcada_professora"))
            return "Remarcada · aluno/professora";
        if (participantStatuses.Any(item => item == "remarcada_aluno")) return "Remarcada pelo aluno";
        if (participantStatuses.Any(item => item == "remarcada_professora")) return "Remarcada pela professora";
        return status switch
        {
            "realizada" => "Realizada",
            "em_andamento" => "Em andamento",
            "remarcada" => "Remarcada",
            "cancelada" => "Cancelada",
            _ => "Agendada"
        };
    }

    private static DateOnly BuildDueDate(DateOnly competence, short dueDay)
    {
        var safeDay = Math.Clamp((int)dueDay, 1, DateTime.DaysInMonth(competence.Year, competence.Month));
        return new DateOnly(competence.Year, competence.Month, safeDay);
    }

    private async Task<bool> ExecuteOperationAsync(string sql, Guid id, string motivo, Guid usuarioId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(sql);
        AddOperationParameters(command, id, motivo, usuarioId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(ct) ?? false);
    }

    private static void AddOperationParameters(NpgsqlCommand command, Guid id, string motivo, Guid usuarioId)
    {
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("motivo", motivo);
        command.Parameters.AddWithValue("usuario", usuarioId);
    }

    private async Task<bool> ChangeExpenseStatusAsync(Guid id, string status, string action, string motivo, Guid usuarioId, CancellationToken ct)
    {
        const string sql = """
            with old as (select to_jsonb(d.*) dados from public.despesas d where id=@id),
            changed as (update public.despesas d set status=@status,data_pagamento=null,atualizado_em=now() where id=@id returning to_jsonb(d.*) dados),
            audit as (insert into public.historico_operacoes_financeiras(entidade_tipo,entidade_id,acao,dados_anteriores,dados_novos,motivo,alterado_por) select 'despesa',@id,@acao,old.dados,changed.dados,@motivo,@usuario from old,changed)
            select exists(select 1 from changed);
            """;
        await using var command=dataSource.CreateCommand(sql);AddOperationParameters(command,id,motivo,usuarioId);command.Parameters.AddWithValue("status",status);command.Parameters.AddWithValue("acao",action);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(ct)??false);
    }

    private static PoliticaPagamentoResponse MapPolicy(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetBoolean(1), reader.GetBoolean(2), reader.GetBoolean(3),
        reader.GetBoolean(4), reader.GetBoolean(5), reader.GetFieldValue<DateOnly>(6));

    private static DespesaFinanceiroResponse MapDespesa(NpgsqlDataReader reader)
    {
        var rawStatus = reader.GetString(8);
        var status = rawStatus switch
        {
            "paga" => "Paga",
            "agendada" => "Agendada",
            "vencida" => "Vencida",
            "cancelada" => "Cancelada",
            _ => reader.GetFieldValue<DateOnly>(6) < DateOnly.FromDateTime(DateTime.Today) ? "Vencida" : "Em aberto"
        };
        return new DespesaFinanceiroResponse(
            reader.GetGuid(0), reader.GetString(1), reader.GetString(2), GetNullableString(reader, 3), reader.GetDecimal(4),
            reader.GetFieldValue<DateOnly>(5), reader.GetFieldValue<DateOnly>(6),
            reader.IsDBNull(7) ? null : reader.GetFieldValue<DateOnly>(7), status, reader.GetString(9),
            GetNullableString(reader, 10), GetNullableString(reader, 11));
    }

    private static string? GetNullableString(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
