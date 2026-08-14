namespace EnglishYard.Domain.Entities;

public sealed class Aluno
{
    public Guid Id { get; init; }
    public required string Nome { get; init; }
    public DateOnly? DataNascimento { get; init; }
    public string? Genero { get; init; }
    public string? Email { get; init; }
    public string? Telefone { get; init; }
    public string? ResponsavelNome { get; init; }
    public string? ResponsavelTelefone { get; init; }
    public required string Status { get; init; }
    public Guid? ProfessoraId { get; init; }
    public string? ProfessoraNome { get; init; }
    public string? ProfessoraFotoUrl { get; init; }
    public decimal? ValorMensalidade { get; init; }
    public short? DiaVencimento { get; init; }
    public string? FormaPagamento { get; init; }
    public decimal TaxaMatricula { get; init; }
    public decimal PercentualDesconto { get; init; }
    public string? Observacoes { get; init; }
    public string? FotoUrl { get; init; }
    public DateOnly DataMatricula { get; init; }
    public bool Ativo { get; init; }
    public DateTimeOffset CriadoEm { get; init; }
    public DateTimeOffset AtualizadoEm { get; init; }
}
