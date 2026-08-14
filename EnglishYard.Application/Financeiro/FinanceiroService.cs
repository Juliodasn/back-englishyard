namespace EnglishYard.Application.Financeiro;

public sealed class FinanceiroService(IFinanceiroRepository repository)
{
    private static readonly HashSet<string> FormasPagamento = new(StringComparer.OrdinalIgnoreCase)
    {
        "PIX", "Cartão", "Boleto", "Dinheiro", "Transferência", "Outro"
    };

    private static readonly HashSet<string> Recorrencias = new(StringComparer.OrdinalIgnoreCase)
    {
        "Mensal", "Pontual"
    };

    public Task<FinanceiroResumoResponse> ObterResumoAsync(DateOnly competencia, CancellationToken cancellationToken) =>
        repository.ObterResumoAsync(PrimeiroDia(competencia), cancellationToken);

    public Task<DemonstrativoProfessoraResponse?> ObterDemonstrativoProfessoraAsync(
        Guid professoraId,
        DateOnly competencia,
        CancellationToken cancellationToken) =>
        repository.ObterDemonstrativoProfessoraAsync(professoraId, PrimeiroDia(competencia), cancellationToken);

    public async Task RegistrarRecebimentoAsync(
        Guid alunoId,
        DateOnly competencia,
        RegistrarRecebimentoRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Valor <= 0)
            throw new FinanceiroValidationException("Informe um valor de recebimento maior que zero.");

        if (string.IsNullOrWhiteSpace(request.FormaPagamento) || !FormasPagamento.Contains(request.FormaPagamento.Trim()))
            throw new FinanceiroValidationException("Selecione uma forma de pagamento válida.");

        await repository.RegistrarRecebimentoAsync(
            alunoId,
            PrimeiroDia(competencia),
            request with { FormaPagamento = NormalizarFormaPagamento(request.FormaPagamento) },
            cancellationToken);
    }

    public Task<DespesaFinanceiroResponse> CadastrarDespesaAsync(
        DateOnly competencia,
        CadastrarDespesaRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Descricao))
            throw new FinanceiroValidationException("Informe a descrição da despesa.");
        if (string.IsNullOrWhiteSpace(request.Categoria))
            throw new FinanceiroValidationException("Informe a categoria da despesa.");
        if (request.Valor <= 0)
            throw new FinanceiroValidationException("O valor da despesa deve ser maior que zero.");
        if (string.IsNullOrWhiteSpace(request.Recorrencia) || !Recorrencias.Contains(request.Recorrencia.Trim()))
            throw new FinanceiroValidationException("Selecione uma recorrência válida.");

        var recorrencia = request.Recorrencia.Trim().Equals("Mensal", StringComparison.OrdinalIgnoreCase) ? "Mensal" : "Pontual";
        var quantidadeMeses = recorrencia == "Mensal" ? request.QuantidadeMeses ?? 12 : (int?)null;
        if (recorrencia == "Mensal" && (quantidadeMeses is < 2 or > 60))
            throw new FinanceiroValidationException("Para uma despesa mensal, informe uma duração entre 2 e 60 meses.");

        return repository.CadastrarDespesaAsync(
            PrimeiroDia(competencia),
            request with
            {
                Descricao = request.Descricao.Trim(),
                Categoria = request.Categoria.Trim(),
                Fornecedor = NormalizarOpcional(request.Fornecedor),
                Recorrencia = recorrencia,
                QuantidadeMeses = quantidadeMeses,
                FormaPagamento = NormalizarOpcional(request.FormaPagamento),
                Observacoes = NormalizarOpcional(request.Observacoes)
            },
            cancellationToken);
    }

    public Task<bool> MarcarDespesaPagaAsync(Guid despesaId, MarcarDespesaPagaRequest request, CancellationToken cancellationToken) =>
        repository.MarcarDespesaPagaAsync(despesaId, request, cancellationToken);

    public Task<bool> EstornarRecebimentoAsync(Guid id, string motivo, Guid usuarioId, CancellationToken ct) =>
        repository.EstornarRecebimentoAsync(id, ExigirMotivo(motivo), usuarioId, ct);

    public Task<bool> AjustarMensalidadeAsync(Guid id, AjustarMensalidadeRequest request, Guid usuarioId, CancellationToken ct)
    {
        if (request.Desconto < 0) throw new FinanceiroValidationException("O desconto não pode ser negativo.");
        return repository.AjustarMensalidadeAsync(id, request.Desconto, ExigirMotivo(request.Motivo), usuarioId, ct);
    }

    public Task<bool> CancelarMensalidadeAsync(Guid id, string motivo, Guid usuarioId, CancellationToken ct) =>
        repository.CancelarMensalidadeAsync(id, ExigirMotivo(motivo), usuarioId, ct);

    public Task<bool> AtualizarDespesaAsync(Guid id, AtualizarDespesaRequest request, Guid usuarioId, CancellationToken ct)
    {
        if (request.Valor <= 0) throw new FinanceiroValidationException("O valor da despesa deve ser maior que zero.");
        return repository.AtualizarDespesaAsync(id, request with { Motivo = ExigirMotivo(request.Motivo) }, usuarioId, ct);
    }

    public Task<bool> CancelarDespesaAsync(Guid id, string motivo, Guid usuarioId, CancellationToken ct) =>
        repository.CancelarDespesaAsync(id, ExigirMotivo(motivo), usuarioId, ct);

    public Task<bool> ReabrirDespesaAsync(Guid id, string motivo, Guid usuarioId, CancellationToken ct) =>
        repository.ReabrirDespesaAsync(id, ExigirMotivo(motivo), usuarioId, ct);

    public Task<Guid> CriarAjusteProfessoraAsync(Guid professoraId, DateOnly competencia, CriarAjusteProfessoraRequest request, Guid usuarioId, CancellationToken ct)
    {
        if (request.Valor == 0 || string.IsNullOrWhiteSpace(request.Descricao))
            throw new FinanceiroValidationException("Informe a descrição e um valor de ajuste diferente de zero.");
        return repository.CriarAjusteProfessoraAsync(professoraId, PrimeiroDia(competencia), request with { Descricao = request.Descricao.Trim() }, usuarioId, ct);
    }

    public Task<bool> ExcluirAjusteProfessoraAsync(Guid id, Guid usuarioId, CancellationToken ct) => repository.ExcluirAjusteProfessoraAsync(id, usuarioId, ct);
    public Task<bool> AprovarFechamentoAsync(Guid professoraId, DateOnly competencia, Guid usuarioId, CancellationToken ct) => repository.AprovarFechamentoAsync(professoraId, PrimeiroDia(competencia), usuarioId, ct);
    public Task<bool> MarcarFechamentoPagoAsync(Guid professoraId, DateOnly competencia, MarcarFechamentoPagoRequest request, Guid usuarioId, CancellationToken ct) => repository.MarcarFechamentoPagoAsync(professoraId, PrimeiroDia(competencia), request, usuarioId, ct);
    public Task<bool> ReabrirFechamentoAsync(Guid professoraId, DateOnly competencia, string motivo, Guid usuarioId, CancellationToken ct) => repository.ReabrirFechamentoAsync(professoraId, PrimeiroDia(competencia), ExigirMotivo(motivo), usuarioId, ct);
    public Task<PoliticaPagamentoResponse?> ObterPoliticaPagamentoAsync(DateOnly data, CancellationToken ct) => repository.ObterPoliticaPagamentoAsync(data, ct);
    public Task<PoliticaPagamentoResponse> SalvarPoliticaPagamentoAsync(SalvarPoliticaPagamentoRequest request, Guid usuarioId, CancellationToken ct) => repository.SalvarPoliticaPagamentoAsync(request, usuarioId, ct);

    private static DateOnly PrimeiroDia(DateOnly value) => new(value.Year, value.Month, 1);

    private static string NormalizarFormaPagamento(string value)
    {
        var trimmed = value.Trim();
        return FormasPagamento.First(item => item.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizarOpcional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string ExigirMotivo(string? value) => string.IsNullOrWhiteSpace(value)
        ? throw new FinanceiroValidationException("Informe o motivo da alteração para manter a trilha de auditoria.")
        : value.Trim();
}

public sealed class FinanceiroValidationException(string message) : Exception(message);
public sealed class FinanceiroNotFoundException(string message) : Exception(message);
public sealed class FinanceiroConflitoException(string message) : Exception(message);
