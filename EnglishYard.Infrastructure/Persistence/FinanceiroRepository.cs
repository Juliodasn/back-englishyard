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
        string[] Alunos,
        string[] SituacoesAlunos);

    public async Task<FinanceiroResumoResponse> ObterResumoAsync(DateOnly competencia, CancellationToken cancellationToken)
    {
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
                   p.tipo_chave_pix, p.chave_pix, p.banco
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
        }

        var rates = await ListarRatesAsync(professoraId, cancellationToken);
        var latestRate = RateForDate(rates, fimCompetencia);
        var schedules = await ListarSchedulesAsync(professoraId, competencia, fimCompetencia, cancellationToken);
        var realLessons = await ListarRealLessonsAsync(professoraId, competencia, fimCompetencia, cancellationToken);
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
                var projected = type == "Grupo" ? rate.Grupo : rate.Individual;
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
            var baseRate = type == "Grupo" ? rate.Grupo : rate.Individual;
            var projected = real.Reposicao ? 0m : baseRate;
            var realized = real.Elegivel ? (real.ValorPagamento > 0 ? real.ValorPagamento : baseRate) : 0m;
            entries.Add(new AulaPagamentoProfessoraResponse(
                real.Id.ToString(), real.Data, real.Inicio, real.Fim, type, real.Alunos,
                ToLessonStatus(real.Status, real.Reposicao, real.SituacoesAlunos), true, projected, realized));
        }

        entries = entries.OrderBy(item => item.Data).ThenBy(item => item.HoraInicio).ToList();
        var adjustments = await ObterAjustesAsync(professoraId, competencia, cancellationToken);
        var closing = await ObterClosingAsync(professoraId, competencia, cancellationToken);
        var realizedTotal = entries.Sum(item => item.ValorRealizado);

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
            entries.Count(item => item.Status == "Reposição realizada"),
            entries.Count(item => item.Status.StartsWith("Remarcada", StringComparison.OrdinalIgnoreCase)),
            entries.Count(item => item.Tipo == "Individual"),
            entries.Count(item => item.Tipo == "Grupo"),
            entries.Sum(item => item.ValorPrevisto),
            realizedTotal,
            adjustments,
            realizedTotal + adjustments,
            closing.Status,
            closing.DataPagamento,
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
                select valor_mensalidade, taxa_matricula,
                       date_trunc('month', criado_em at time zone 'America/Sao_Paulo')::date as competencia_matricula,
                       percentual_desconto, coalesce(dia_vencimento, 10), forma_pagamento
                from public.alunos
                where id = @id and ativo = true;
                """;
            await using (var studentCommand = new NpgsqlCommand(studentSql, connection, transaction))
            {
                studentCommand.Parameters.AddWithValue("id", alunoId);
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
            const string chargeSql = """
                select id, valor_final, data_vencimento
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
            }

            decimal alreadyReceived;
            const string receivedSql = "select coalesce(sum(valor_recebido), 0) from public.recebimentos_mensalidades where mensalidade_id = @id;";
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

    private async Task<List<MensalidadeFinanceiroResponse>> ListarMensalidadesAsync(DateOnly competencia, CancellationToken cancellationToken)
    {
        const string sql = """
            select
                m.id,
                a.id,
                a.nome,
                a.valor_mensalidade,
                a.taxa_matricula,
                date_trunc('month', a.criado_em at time zone 'America/Sao_Paulo')::date as competencia_matricula,
                a.percentual_desconto,
                coalesce(a.dia_vencimento, 10),
                a.forma_pagamento,
                m.descricao,
                m.valor_original,
                m.desconto,
                m.valor_final,
                m.data_vencimento,
                m.status,
                m.forma_pagamento_prevista,
                coalesce(r.valor_recebido, 0) as valor_recebido
            from public.alunos a
            left join public.mensalidades m
              on m.aluno_id = a.id
             and m.competencia = @competencia
            left join lateral (
                select sum(valor_recebido) as valor_recebido
                from public.recebimentos_mensalidades
                where mensalidade_id = m.id
            ) r on true
            where a.ativo = true
              and (
                a.valor_mensalidade is not null
                or (
                    a.taxa_matricula > 0
                    and date_trunc('month', a.criado_em at time zone 'America/Sao_Paulo')::date = @competencia
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
                chargeId, alunoId, alunoNome, description, original, discount, final, received, balance, dueDate, status, paymentMethod));
        }

        return result;
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
        const string sql = "select id from public.professoras where ativo = true order by nome;";
        var ids = new List<Guid>();
        await using (var command = dataSource.CreateCommand(sql))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) ids.Add(reader.GetGuid(0));

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
            join public.alunos a on a.id = h.aluno_id and a.ativo = true
            where h.professora_id = @id
              and h.ativo = true
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
                reader.GetFieldValue<string[]>(9), reader.GetFieldValue<string[]>(10)));
        return result;
    }

    private async Task<decimal> ObterAjustesAsync(Guid professoraId, DateOnly competencia, CancellationToken cancellationToken)
    {
        const string sql = "select coalesce(sum(valor), 0) from public.ajustes_pagamento_professoras where professora_id = @id and competencia = @competencia;";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", professoraId);
        command.Parameters.AddWithValue("competencia", NpgsqlDbType.Date, competencia);
        return (decimal)(await command.ExecuteScalarAsync(cancellationToken) ?? 0m);
    }

    private async Task<(string Status, DateOnly? DataPagamento)> ObterClosingAsync(Guid professoraId, DateOnly competencia, CancellationToken cancellationToken)
    {
        const string sql = "select status, data_pagamento from public.fechamentos_professoras where professora_id = @id and competencia = @competencia;";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", professoraId);
        command.Parameters.AddWithValue("competencia", NpgsqlDbType.Date, competencia);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return ("Em conferência", null);
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1));
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
