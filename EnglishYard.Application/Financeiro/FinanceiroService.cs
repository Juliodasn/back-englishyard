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

    private static DateOnly PrimeiroDia(DateOnly value) => new(value.Year, value.Month, 1);

    private static string NormalizarFormaPagamento(string value)
    {
        var trimmed = value.Trim();
        return FormasPagamento.First(item => item.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizarOpcional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class FinanceiroValidationException(string message) : Exception(message);
public sealed class FinanceiroNotFoundException(string message) : Exception(message);
public sealed class FinanceiroConflitoException(string message) : Exception(message);
