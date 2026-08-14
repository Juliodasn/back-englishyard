namespace EnglishYard.Application.Imagens;

public static class ImagemPerfilValidator
{
    public const long TamanhoMaximoBytes = 5 * 1024 * 1024;

    private static readonly HashSet<string> TiposPermitidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    public static string? ObterErro(string? contentType, long tamanho)
    {
        if (tamanho <= 0)
            return "Selecione uma imagem válida.";

        if (tamanho > TamanhoMaximoBytes)
            return "A foto deve ter no máximo 5 MB.";

        if (string.IsNullOrWhiteSpace(contentType) || !TiposPermitidos.Contains(contentType))
            return "A foto deve estar no formato JPG, PNG ou WEBP.";

        return null;
    }
}
