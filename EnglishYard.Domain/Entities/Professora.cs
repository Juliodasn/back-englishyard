namespace EnglishYard.Domain.Entities;

public sealed class Professora
{
    public Guid Id { get; init; }
    public required string Nome { get; init; }
    public string? NomeProfissional { get; init; }
    public DateOnly? DataNascimento { get; init; }
    public string? DocumentoIdentidade { get; init; }
    public required string Email { get; init; }
    public string? Telefone { get; init; }
    public required string Status { get; init; }
    public required string ModeloPagamento { get; init; }
    public short? DiaPagamento { get; init; }
    public string? TipoChavePix { get; init; }
    public string? ChavePix { get; init; }
    public string? Banco { get; init; }
    public string? Observacoes { get; init; }
    public string? FotoUrl { get; init; }
    public bool Ativo { get; init; }
    public decimal ValorAulaIndividual { get; init; }
    public decimal ValorAulaGrupo { get; init; }
    public DateOnly? VigenteDesde { get; init; }
    public int QuantidadeAlunos { get; init; }
    public int QuantidadeAulas { get; init; }
    public DateOnly? ProximaAulaData { get; init; }
    public TimeOnly? ProximaAulaHora { get; init; }
    public bool PossuiAcesso { get; init; }
    public DateTimeOffset CriadoEm { get; init; }
    public DateTimeOffset AtualizadoEm { get; init; }
}
