using EnglishYard.Application.Financeiro;

namespace EnglishYard.Tests;

public sealed class PagamentosParciaisRegrasTests
{
    [Fact]
    public void CreditoNovo_PosPagamentoAntigo_EntraNoNovoLote()
    {
        var pago = new DateTimeOffset(2026, 11, 16, 15, 17, 0, TimeSpan.Zero);
        Assert.True(PagamentosParciaisRegras.CreditoNovoAposPagamentoLegado(
            pago.AddHours(1), pago));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void CreditoHistoricoSemComprovacao_NaoEhAutomaticamenteQuitadoNemReaprovado(
        bool temCredito, bool temPagamento)
    {
        var pago = new DateTimeOffset(2026, 11, 16, 15, 17, 0, TimeSpan.Zero);
        DateTimeOffset? credito = temCredito ? pago.AddSeconds(-1) : null;
        DateTimeOffset? pagamento = temPagamento ? pago : null;
        Assert.False(PagamentosParciaisRegras.CreditoNovoAposPagamentoLegado(credito, pagamento));
    }

    [Fact]
    public void TotalIncluiValoresPagosAprovadosEPendentes_SemDuplicar()
    {
        Assert.Equal(330m, PagamentosParciaisRegras.SaldoTotal(240m, 0m, 90m));
        Assert.Equal(330m, PagamentosParciaisRegras.SaldoTotal(240m, 90m, 0m));
        Assert.Equal(330m, PagamentosParciaisRegras.SaldoTotal(330m, 0m, 0m));
    }
}
