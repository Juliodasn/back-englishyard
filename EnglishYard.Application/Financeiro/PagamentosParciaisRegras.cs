namespace EnglishYard.Application.Financeiro;

/// <summary>
/// Compatibilidade segura com os fechamentos mensais anteriores à migração 27.
/// O legado não tinha itens por aula. Não presumimos que uma aula anterior esteja
/// pendente: só créditos gerados após o pagamento podem entrar automaticamente.
/// </summary>
public static class PagamentosParciaisRegras
{
    public static bool CreditoNovoAposPagamentoLegado(
        DateTimeOffset? creditoGeradoEm, DateTimeOffset? pagoLegadoEm) =>
        pagoLegadoEm.HasValue && creditoGeradoEm.HasValue && creditoGeradoEm.Value > pagoLegadoEm.Value;

    public static decimal SaldoTotal(decimal pago, decimal aprovado, decimal pendente) =>
        pago + aprovado + pendente;
}
