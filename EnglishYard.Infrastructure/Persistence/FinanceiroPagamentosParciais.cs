using EnglishYard.Application.Financeiro;
using Npgsql;
using NpgsqlTypes;

namespace EnglishYard.Infrastructure.Persistence;

public sealed partial class FinanceiroRepository
{
    private sealed record LoteAtual(Guid Id, string Status, decimal ValorAulas,
        decimal ValorAjustes, decimal ValorTotal, DateTimeOffset AprovadoEm,
        DateOnly? DataPagamento, DateTimeOffset? PagoEm, string? ComprovanteUrl);

    private async Task<List<LoteAtual>> ListarLotesAsync(Guid professoraId, DateOnly competencia, CancellationToken ct)
    {
        const string sql = """
            select id, status, valor_aulas, valor_ajustes, valor_total, aprovado_em,
                   data_pagamento, pago_em, comprovante_url
            from public.lotes_pagamento_professoras
            where professora_id=@p and competencia=@c and status in ('Aprovado','Pago')
            order by aprovado_em, id;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("p", professoraId);
        command.Parameters.AddWithValue("c", NpgsqlDbType.Date, competencia);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<LoteAtual>();
        while (await reader.ReadAsync(ct))
            rows.Add(new LoteAtual(reader.GetGuid(0), reader.GetString(1), reader.GetDecimal(2),
                reader.GetDecimal(3), reader.GetDecimal(4), reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateOnly>(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        return rows;
    }

    private async Task<(Dictionary<Guid, string> Aulas, Dictionary<Guid, string> Ajustes)> ListarItensLancadosAsync(
        Guid professoraId, DateOnly competencia, CancellationToken ct)
    {
        const string sql = """
            select i.aula_id, i.ajuste_id, l.status
            from public.itens_lote_pagamento_professoras i
            join public.lotes_pagamento_professoras l on l.id=i.lote_id
            where l.professora_id=@p and l.competencia=@c
              and l.status in ('Aprovado','Pago') and i.estornado_em is null;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("p", professoraId);
        command.Parameters.AddWithValue("c", NpgsqlDbType.Date, competencia);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var aulas = new Dictionary<Guid, string>();
        var ajustes = new Dictionary<Guid, string>();
        while (await reader.ReadAsync(ct))
        {
            if (!reader.IsDBNull(0)) aulas.Add(reader.GetGuid(0), reader.GetString(2));
            if (!reader.IsDBNull(1)) ajustes.Add(reader.GetGuid(1), reader.GetString(2));
        }
        return (aulas, ajustes);
    }

    private async Task<Dictionary<Guid, DateTimeOffset?>> ListarCreditosGeradosAsync(
        IReadOnlyList<Guid> aulaIds, CancellationToken ct)
    {
        var result = new Dictionary<Guid, DateTimeOffset?>();
        if (aulaIds.Count == 0) return result;
        const string sql = "select id, credito_pagamento_gerado_em from public.aulas where id=any(@ids);";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, aulaIds.ToArray());
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1));
        return result;
    }

    // Um fechamento antigo tinha somente o valor agregado, sem vínculo a aulas.
    // Por segurança, na existência de um legado Pago, somente créditos surgidos
    // DEPOIS daquele pagamento entram automaticamente no próximo lote.
    private async Task<DemonstrativoProfessoraResponse> CompletarDemonstrativoLotesAsync(
        DemonstrativoProfessoraResponse statement,
        string statusLegado, decimal valorLegado, DateTimeOffset? legadoPagoEm,
        CancellationToken ct)
    {
        var competencia = DateOnly.ParseExact(statement.Competencia + "-01", "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture);
        var lotes = await ListarLotesAsync(statement.ProfessoraId, competencia, ct);
        var lançados = await ListarItensLancadosAsync(statement.ProfessoraId, competencia, ct);
        var ids = statement.Aulas.Select(a => Guid.TryParse(a.Id, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty).Distinct().ToArray();
        var creditos = await ListarCreditosGeradosAsync(ids, ct);
        var entradas = new List<AulaPagamentoProfessoraResponse>(statement.Aulas.Count);
        decimal saldoAulas = 0m;
        foreach (var aula in statement.Aulas)
        {
            if (!Guid.TryParse(aula.Id, out var id) || aula.ValorRealizado <= 0m)
            {
                entradas.Add(aula with { StatusQuitacao = "Sem valor pendente" });
                continue;
            }
            var quitada = lançados.Aulas.TryGetValue(id, out var statusLote);
            creditos.TryGetValue(id, out var geradoEm);
            var legado = statusLegado == "Pago" &&
                !PagamentosParciaisRegras.CreditoNovoAposPagamentoLegado(geradoEm, legadoPagoEm);
            var statusQuitacao = quitada ? statusLote! : legado ? "Legado — verificar" : "Pendente";
            entradas.Add(aula with { StatusQuitacao = statusQuitacao });
            if (statusQuitacao == "Pendente") saldoAulas += aula.ValorRealizado;
        }
        decimal saldoAjustes = 0m;
        foreach (var ajuste in statement.AjustesDetalhes)
        {
            if (lançados.Ajustes.ContainsKey(ajuste.Id)) continue;
            if (statusLegado == "Pago" && legadoPagoEm.HasValue && ajuste.CriadoEm <= legadoPagoEm.Value) continue;
            saldoAjustes += ajuste.Valor;
        }
        var pago = (statusLegado == "Pago" ? valorLegado : 0m)
                   + lotes.Where(l => l.Status == "Pago").Sum(l => l.ValorTotal);
        var aprovado = (statusLegado == "Aprovado" ? valorLegado : 0m)
                       + lotes.Where(l => l.Status == "Aprovado").Sum(l => l.ValorTotal);
        var pendente = saldoAulas + saldoAjustes;
        var statusDemonstrativo = aprovado != 0 ? "Aprovado" : pago > 0 && pendente != 0
            ? "Pago com pendências" : pago > 0 ? "Pago" : "Em conferência";
        return statement with
        {
            Aulas = entradas,
            ValorPago = pago,
            ValorAprovado = aprovado,
            ValorPendente = pendente,
            ValorTotal = PagamentosParciaisRegras.SaldoTotal(pago, aprovado, pendente),
            StatusFechamento = statusDemonstrativo,
            PossuiFechamentoLegado = statusLegado is "Aprovado" or "Pago",
            ValorPagoLegado = statusLegado == "Pago" ? valorLegado : 0m,
            LotesPagamento = lotes.Select(l => new LotePagamentoProfessoraResponse(
                l.Id, l.Status, l.ValorAulas, l.ValorAjustes, l.ValorTotal,
                l.AprovadoEm, l.DataPagamento, l.PagoEm, l.ComprovanteUrl)).ToArray()
        };
    }

    private static async Task TravarPagamentoAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid professoraId, DateOnly competencia, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "select pg_advisory_xact_lock(hashtextextended(@chave, 0));", connection, transaction);
        cmd.Parameters.AddWithValue("chave", $"pagamento:{professoraId:N}:{competencia:yyyyMM}");
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<bool> AprovarLoteAsync(Guid professoraId, DateOnly competencia,
        Guid usuarioId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await TravarPagamentoAsync(connection, transaction, professoraId, competencia, ct);

        var demonstrativo = await ObterDemonstrativoProfessoraAsync(professoraId, competencia, ct);
        if (demonstrativo is null) return false;
        // Fechamentos antigos apenas aprovados precisam ser reabertos primeiro.
        const string antigoSql = """
            select exists(select 1 from public.fechamentos_professoras
             where professora_id=@p and competencia=@c and status='Aprovado');
            """;
        await using (var antigo = new NpgsqlCommand(antigoSql, connection, transaction))
        {
            antigo.Parameters.AddWithValue("p", professoraId);
            antigo.Parameters.AddWithValue("c", NpgsqlDbType.Date, competencia);
            if (Convert.ToBoolean(await antigo.ExecuteScalarAsync(ct)))
                throw new FinanceiroConflitoException("Existe um fechamento antigo aprovado. Reabra-o antes de aprovar um novo lote.");
        }
        if (demonstrativo.LotesPagamento.Any(l => l.Status == "Aprovado"))
            throw new FinanceiroConflitoException("Já existe um lote aprovado aguardando pagamento ou cancelamento.");
        var aulas = demonstrativo.Aulas.Where(a => a.StatusQuitacao == "Pendente" && a.ValorRealizado > 0m)
            .Select(a => (Id: Guid.Parse(a.Id), Valor: a.ValorRealizado)).DistinctBy(a => a.Id).ToList();
        var lançados = await ListarItensLancadosAsync(professoraId, competencia, ct);
        var legacy = await ObterClosingAsync(professoraId, competencia, ct);
        var ajustes = demonstrativo.AjustesDetalhes
            .Where(a => !lançados.Ajustes.ContainsKey(a.Id)
                && (legacy.Status != "Pago" || (legacy.PagoEm.HasValue && a.CriadoEm > legacy.PagoEm.Value)))
            .ToArray();
        var totalAulas = aulas.Sum(a => a.Valor);
        var totalAjustes = ajustes.Sum(a => a.Valor);
        if (totalAulas + totalAjustes <= 0m)
            throw new FinanceiroConflitoException("Não há saldo positivo novo para aprovar. Verifique as aulas e o histórico legado.");

        // Bloqueia as aulas antes de definir o lote, para não aceitar alterações
        // simultâneas enquanto seus valores são fotografados.
        foreach (var aula in aulas.OrderBy(a => a.Id))
        {
            const string lockSql = """
                select elegivel_pagamento, status_pagamento, valor_pagamento
                from public.aulas where id=@id and professora_id=@p
                   and data_aula>=@c and data_aula<(@c + interval '1 month')::date
                for update;
                """;
            await using var lockRow = new NpgsqlCommand(lockSql, connection, transaction);
            lockRow.Parameters.AddWithValue("id", aula.Id);
            lockRow.Parameters.AddWithValue("p", professoraId);
            lockRow.Parameters.AddWithValue("c", NpgsqlDbType.Date, competencia);
            await using var reader = await lockRow.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct) || !reader.GetBoolean(0)
                || reader.GetString(1) is "paid" or "approved"
                || (reader.GetDecimal(2) > 0m && reader.GetDecimal(2) != aula.Valor))
                throw new FinanceiroConflitoException("Uma aula mudou durante a conferência. Atualize o demonstrativo.");
        }

        const string loteSql = """
            insert into public.lotes_pagamento_professoras
              (professora_id,competencia,status,valor_aulas,valor_ajustes,valor_total,aprovado_por)
            values (@p,@c,'Aprovado',@a,@j,@t,@u) returning id;
            """;
        Guid loteId;
        await using (var cmd = new NpgsqlCommand(loteSql, connection, transaction))
        {
            cmd.Parameters.AddWithValue("p", professoraId);
            cmd.Parameters.AddWithValue("c", NpgsqlDbType.Date, competencia);
            cmd.Parameters.AddWithValue("a", NpgsqlDbType.Numeric, totalAulas);
            cmd.Parameters.AddWithValue("j", NpgsqlDbType.Numeric, totalAjustes);
            cmd.Parameters.AddWithValue("t", NpgsqlDbType.Numeric, totalAulas + totalAjustes);
            cmd.Parameters.AddWithValue("u", usuarioId);
            loteId = (Guid)(await cmd.ExecuteScalarAsync(ct) ?? throw new FinanceiroConflitoException("Falha ao criar lote."));
        }
        const string itemSql = """
            insert into public.itens_lote_pagamento_professoras(lote_id,aula_id,ajuste_id,valor)
            values(@l,@a,@j,@v);
            """;
        foreach (var aula in aulas)
            await InserirItemLoteAsync(connection, transaction, itemSql, loteId, aula.Id, null, aula.Valor, ct);
        foreach (var ajuste in ajustes)
            await InserirItemLoteAsync(connection, transaction, itemSql, loteId, null, ajuste.Id, ajuste.Valor, ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    private static async Task InserirItemLoteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string sql, Guid lote, Guid? aula, Guid? ajuste, decimal valor, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("l", lote);
        command.Parameters.Add(new NpgsqlParameter("a", NpgsqlDbType.Uuid) { Value = aula.HasValue ? aula.Value : DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("j", NpgsqlDbType.Uuid) { Value = ajuste.HasValue ? ajuste.Value : DBNull.Value });
        command.Parameters.AddWithValue("v", NpgsqlDbType.Numeric, valor);
        try { await command.ExecuteNonQueryAsync(ct); }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new FinanceiroConflitoException("Aula ou ajuste já foi incluído em outro pagamento. Atualize o demonstrativo.");
        }
    }

    private async Task<bool> PagarLoteAsync(Guid professoraId, DateOnly competencia,
        MarcarFechamentoPagoRequest request, Guid usuarioId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await TravarPagamentoAsync(connection, transaction, professoraId, competencia, ct);
        // Compatibilidade: um fechamento mensal aprovado antes da migração 27
        // ainda pode ser quitado como legado, sem converter seu histórico.
        const string legacySql = """
            update public.fechamentos_professoras set
                status='Pago', pago_em=now(), data_pagamento=@d,
                comprovante_url=@url, atualizado_em=now()
            where professora_id=@p and competencia=@c and status='Aprovado'
              and not exists (
                select 1 from public.lotes_pagamento_professoras
                where professora_id=@p and competencia=@c and status='Aprovado'
              ) returning id;
            """;
        await using (var legacy = new NpgsqlCommand(legacySql, connection, transaction))
        {
            legacy.Parameters.AddWithValue("p", professoraId);
            legacy.Parameters.AddWithValue("c", NpgsqlDbType.Date, competencia);
            legacy.Parameters.AddWithValue("d", NpgsqlDbType.Date, request.DataPagamento);
            legacy.Parameters.Add(new NpgsqlParameter("url", NpgsqlDbType.Text)
                { Value = request.ComprovanteUrl ?? (object)DBNull.Value });
            if (await legacy.ExecuteScalarAsync(ct) is not null)
            {
                await transaction.CommitAsync(ct);
                return true;
            }
        }
        const string sql = """
            update public.lotes_pagamento_professoras
            set status='Pago', data_pagamento=@d, comprovante_url=@url, pago_por=@u, pago_em=now()
            where professora_id=@p and competencia=@c and status='Aprovado'
            returning id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("p", professoraId);
        command.Parameters.AddWithValue("c", NpgsqlDbType.Date, competencia);
        command.Parameters.AddWithValue("d", NpgsqlDbType.Date, request.DataPagamento);
        command.Parameters.Add(new NpgsqlParameter("url", NpgsqlDbType.Text) { Value = request.ComprovanteUrl ?? (object)DBNull.Value });
        command.Parameters.AddWithValue("u", usuarioId);
        var result = await command.ExecuteScalarAsync(ct) is not null;
        if (result) await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<bool> ReverterLotePagamentoAsync(Guid loteId, string motivo,
        bool confirmoPagamentoNaoRealizado, Guid usuarioId, CancellationToken ct)
    {
        if (!confirmoPagamentoNaoRealizado || motivo.Trim().Length < 10)
            throw new FinanceiroConflitoException("Somente reverta uma marcação incorreta sem transferência, informando o motivo.");
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        const string lockSql = """
            select professora_id, competencia, status from public.lotes_pagamento_professoras
            where id=@l;
            """;
        Guid professoraId;
        DateOnly competencia;
        string anterior;
        await using (var lockRow = new NpgsqlCommand(lockSql, connection, transaction))
        {
            lockRow.Parameters.AddWithValue("l", loteId);
            await using var reader = await lockRow.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return false;
            professoraId = reader.GetGuid(0);
            competencia = reader.GetFieldValue<DateOnly>(1);
            anterior = reader.GetString(2);
        }
        if (anterior is not ("Pago" or "Aprovado"))
            throw new FinanceiroConflitoException("O lote já foi revertido ou cancelado.");
        await TravarPagamentoAsync(connection, transaction, professoraId, competencia, ct);
        // Validar novamente o estado sob a mesma trava do pagamento.
        await using (var verify = new NpgsqlCommand("""
            select status from public.lotes_pagamento_professoras where id=@l for update;
            """, connection, transaction))
        {
            verify.Parameters.AddWithValue("l", loteId);
            var current = (string?)await verify.ExecuteScalarAsync(ct);
            if (current != anterior)
                throw new FinanceiroConflitoException("O lote foi alterado por outra operação. Recarregue.");
        }
        const string sql = """
            update public.lotes_pagamento_professoras set
             status=@novo, revertido_em=now(), revertido_por=@u,
             motivo_reversao=@motivo where id=@l;
            update public.itens_lote_pagamento_professoras
             set estornado_em=now() where lote_id=@l and estornado_em is null;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("l", loteId);
        command.Parameters.AddWithValue("u", usuarioId);
        command.Parameters.AddWithValue("motivo", motivo.Trim());
        command.Parameters.AddWithValue("novo", anterior == "Pago" ? "Revertido" : "Cancelado");
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }
}
